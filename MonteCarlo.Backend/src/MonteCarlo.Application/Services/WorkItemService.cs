using MonteCarlo.Application.Abstractions;
using MonteCarlo.Application.Common;
using MonteCarlo.Application.Dtos;
using MonteCarlo.Domain.Common;
using MonteCarlo.Domain.Entities;

namespace MonteCarlo.Application.Services;

public sealed class WorkItemService(IKnowledgeStore store, ITicketSystem tickets)
{
    public async Task<IReadOnlyList<WorkItemDto>> ListAsync(Guid? projectId, CancellationToken ct = default)
    {
        var project = await store.ResolveProjectAsync(projectId, ct);
        var lookups = await Lookups.LoadAsync(store, project.Id, null, ct);
        return lookups.WorkItems.Values.OrderBy(w => w.Status).ThenBy(w => w.Priority).ThenBy(w => w.ExternalRef)
            .Select(lookups.ToDto).ToList();
    }

    public async Task<WorkItemDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var w = await store.FirstOrDefaultAsync(store.WorkItems.Where(x => x.Id == id), ct) ?? throw new NotFoundException("Work item not found");
        var decisions = await store.ListAsync(store.Decisions.Where(d => d.AffectedWorkItemId == id), ct);
        var actions = await store.ListAsync(store.ActionItems.Where(a => a.LinkedWorkItemId == id), ct);
        var blockers = await store.ListAsync(store.Blockers.Where(b => b.BlockingWorkItemId == id), ct);
        var suggestions = await store.ListAsync(store.Suggestions.Where(s => s.WorkItemId == id), ct);
        var code = (await store.ListAsync(store.CodeEvents.Where(c => c.ProjectId == w.ProjectId && c.TicketRefs.Contains(w.ExternalRef)), ct))
            .Where(c => c.Refs().Contains(w.ExternalRef, StringComparer.OrdinalIgnoreCase)).ToList();

        var lookups = await Lookups.LoadAsync(store, w.ProjectId,
            decisions.Select(d => d.EvidenceId).Concat(actions.Select(a => a.EvidenceId)).Concat(blockers.Select(b => b.EvidenceId))
                .Concat(suggestions.Select(s => s.EvidenceId)), ct);

        var timeline = new List<TimelineEntryDto>();
        timeline.AddRange(decisions.Select(d => new TimelineEntryDto(d.DecidedAt, "Decision", d.Statement, d.DecidedByName, lookups.ToDto(d.EvidenceId))));
        timeline.AddRange(actions.Select(a => new TimelineEntryDto(a.CreatedAt, "Action", a.Description, lookups.PersonName(a.OwnerId) ?? a.OwnerName, lookups.ToDto(a.EvidenceId))));
        timeline.AddRange(blockers.Select(b => new TimelineEntryDto(b.RaisedAt, b.ResolvedAt is null ? "Blocker" : "Blocker (resolved)", b.Description, b.RaisedByName, lookups.ToDto(b.EvidenceId))));
        timeline.AddRange(code.Select(c => new TimelineEntryDto(c.OccurredAt, c.Type.ToString(), c.Title, c.Author, null)));
        timeline.AddRange(suggestions.Select(s => new TimelineEntryDto(s.ReviewedAt ?? s.CreatedAt, $"Suggestion ({s.Outcome})", s.Title, s.ReviewedBy, lookups.ToDto(s.EvidenceId))));

        return new WorkItemDetailDto(lookups.ToDto(w), timeline.OrderByDescending(t => t.At).ToList());
    }

    public async Task<WorkItemDto> CreateAsync(UpsertWorkItemRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) throw new ValidationException("Title is required.");
        var project = await store.ResolveProjectAsync(req.ProjectId, ct);
        var owner = req.OwnerId is { } oid ? await store.FirstOrDefaultAsync(store.People.Where(p => p.Id == oid), ct) : null;
        var key = string.IsNullOrWhiteSpace(req.ExternalRef)
            ? await tickets.CreateAsync(new WorkItemDraft(project.Key, req.Title, req.Description ?? "", req.Role, owner?.Email, null,
                req.Release ?? project.CurrentRelease, req.EstimateDays), ct)
            : req.ExternalRef.Trim();

        var item = new WorkItem { ProjectId = project.Id, ExternalRef = key };
        Apply(item, req);
        store.Add(item);
        store.Audit("user", "CreateWorkItem", "WorkItem", item.Id, $"{key} {item.Title}");
        await store.SaveChangesAsync(ct);
        return (await GetAsync(item.Id, ct)).Item;
    }

    public async Task<WorkItemDto> UpdateAsync(Guid id, UpsertWorkItemRequest req, CancellationToken ct = default)
    {
        var item = await store.FirstOrDefaultAsync(store.WorkItems.Where(x => x.Id == id), ct) ?? throw new NotFoundException("Work item not found");
        var oldStatus = item.Status;
        Apply(item, req);
        if (item.Status != oldStatus)
        {
            var newStatus = item.Status;
            item.Status = oldStatus;
            item.ChangeStatus(newStatus, DateTimeOffset.UtcNow);
            await tickets.UpdateStatusAsync(item.ExternalRef, newStatus, ct);
        }
        store.Audit("user", "UpdateWorkItem", "WorkItem", item.Id, item.ExternalRef);
        await store.SaveChangesAsync(ct);
        return (await GetAsync(item.Id, ct)).Item;
    }

    public async Task<WorkItemDto> ChangeStatusAsync(Guid id, ChangeStatusRequest req, CancellationToken ct = default)
    {
        var item = await store.FirstOrDefaultAsync(store.WorkItems.Where(x => x.Id == id), ct) ?? throw new NotFoundException("Work item not found");
        await tickets.UpdateStatusAsync(item.ExternalRef, req.Status, ct);
        item.ChangeStatus(req.Status, DateTimeOffset.UtcNow);
        store.Audit(req.Actor ?? "user", "ChangeStatus", "WorkItem", item.Id, $"{item.ExternalRef} -> {req.Status}");
        await store.SaveChangesAsync(ct);
        return (await GetAsync(item.Id, ct)).Item;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var item = await store.FirstOrDefaultAsync(store.WorkItems.Where(x => x.Id == id), ct) ?? throw new NotFoundException("Work item not found");
        foreach (var d in await store.ListAsync(store.Dependencies.Where(d => d.DependsOnId == id || d.WorkItemId == id), ct)) store.Remove(d);
        foreach (var s in await store.ListAsync(store.Suggestions.Where(s => s.WorkItemId == id && s.Outcome == SuggestionOutcome.Pending), ct)) store.Remove(s);
        store.Remove(item);
        store.Audit("user", "DeleteWorkItem", "WorkItem", id, item.ExternalRef);
        await store.SaveChangesAsync(ct);
    }

    /// <summary>Option B1: pull open tickets from an external ticket system into the shared model.</summary>
    public async Task<int> SyncAsync(Guid? projectId, CancellationToken ct = default)
    {
        if (!tickets.IsExternal) return 0;
        var project = await store.ResolveProjectAsync(projectId, ct);
        var people = await store.ListAsync(store.People, ct);
        var existing = (await store.ListAsync(store.WorkItems.Where(w => w.ProjectId == project.Id), ct))
            .ToDictionary(w => w.ExternalRef, StringComparer.OrdinalIgnoreCase);
        var changed = 0;
        foreach (var t in await tickets.GetOpenItemsAsync(project.Key, ct))
        {
            if (!existing.TryGetValue(t.ExternalRef, out var w))
            {
                w = new WorkItem { ProjectId = project.Id, ExternalRef = t.ExternalRef, Release = t.Release ?? project.CurrentRelease };
                store.Add(w);
            }
            w.Title = t.Title;
            w.Description = t.Description;
            w.Status = t.Status;
            w.EstimateDays = t.EstimateDays ?? w.EstimateDays;
            w.OwnerId = people.FirstOrDefault(p => !string.IsNullOrEmpty(t.AssigneeEmail) && p.Email.Equals(t.AssigneeEmail, StringComparison.OrdinalIgnoreCase))?.Id ?? w.OwnerId;
            if (t.UpdatedAt > w.LastActivityAt) w.LastActivityAt = t.UpdatedAt;
            changed++;
        }
        store.Audit("system", "TicketSync", "Project", project.Id, $"{tickets.Name}: {changed} tickets");
        await store.SaveChangesAsync(ct);
        return changed;
    }

    private static void Apply(WorkItem item, UpsertWorkItemRequest req)
    {
        item.Title = req.Title.Trim();
        item.Description = req.Description ?? item.Description;
        item.AcceptanceCriteria = req.AcceptanceCriteria ?? item.AcceptanceCriteria;
        if (req.Status is { } st) item.Status = st;
        item.Role = req.Role;
        item.EstimateDays = req.EstimateDays;
        item.OwnerId = req.OwnerId;
        item.Release = string.IsNullOrWhiteSpace(req.Release) ? item.Release : req.Release.Trim();
        item.Priority = Math.Clamp(req.Priority ?? item.Priority, 1, 5);
        item.LastActivityAt = DateTimeOffset.UtcNow;
        if (req.DependsOn is not null)
        {
            item.Dependencies.RemoveAll(d => !req.DependsOn.Contains(d.DependsOnId));
            foreach (var dep in req.DependsOn.Where(d => d != item.Id && item.Dependencies.All(x => x.DependsOnId != d)))
                item.Dependencies.Add(new WorkItemDependency { WorkItemId = item.Id, DependsOnId = dep });
        }
    }
}

public sealed class PeopleService(IKnowledgeStore store)
{
    public async Task<IReadOnlyList<PersonDto>> ListAsync(CancellationToken ct = default)
    {
        var people = await store.ListAsync(store.People.OrderBy(p => p.Name), ct);
        var open = (await store.ListAsync(store.WorkItems.Where(w => w.OwnerId != null && w.Status != WorkItemStatus.Done && w.Status != WorkItemStatus.Cancelled)
            .Select(w => w.OwnerId!.Value), ct)).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return people.Select(p => ToDto(p, open.GetValueOrDefault(p.Id), today)).ToList();
    }

    public async Task<PersonDto> CreateAsync(UpsertPersonRequest req, CancellationToken ct = default)
    {
        Validate(req);
        var p = new Person();
        Apply(p, req);
        store.Add(p);
        store.Audit("user", "CreatePerson", "Person", p.Id, p.Name);
        await store.SaveChangesAsync(ct);
        return ToDto(p, 0, DateOnly.FromDateTime(DateTime.UtcNow));
    }

    public async Task<PersonDto> UpdateAsync(Guid id, UpsertPersonRequest req, CancellationToken ct = default)
    {
        Validate(req);
        var p = await store.FirstOrDefaultAsync(store.People.Where(x => x.Id == id), ct) ?? throw new NotFoundException("Person not found");
        Apply(p, req);
        store.Audit("user", "UpdatePerson", "Person", p.Id, p.Name);
        await store.SaveChangesAsync(ct);
        return ToDto(p, 0, DateOnly.FromDateTime(DateTime.UtcNow));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var p = await store.FirstOrDefaultAsync(store.People.Where(x => x.Id == id), ct) ?? throw new NotFoundException("Person not found");
        foreach (var w in await store.ListAsync(store.WorkItems.Where(w => w.OwnerId == id), ct)) w.OwnerId = null;
        store.Remove(p);
        store.Audit("user", "DeletePerson", "Person", id, p.Name);
        await store.SaveChangesAsync(ct);
    }

    private static void Validate(UpsertPersonRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) throw new ValidationException("Name is required.");
        if (req.Capacity is <= 0 or > 1) throw new ValidationException("Capacity must be between 0 and 1.");
    }

    private static void Apply(Person p, UpsertPersonRequest req)
    {
        p.Name = req.Name.Trim();
        p.Email = req.Email?.Trim() ?? "";
        p.ExternalUserId = string.IsNullOrWhiteSpace(req.ExternalUserId) ? null : req.ExternalUserId.Trim();
        p.Role = req.Role;
        p.Skills = req.Skills ?? "";
        p.Capacity = req.Capacity;
        p.StartDate = req.StartDate ?? (p.StartDate == default ? DateOnly.FromDateTime(DateTime.UtcNow) : p.StartDate);
        p.LeaveDate = req.LeaveDate;
    }

    private static PersonDto ToDto(Person p, int open, DateOnly today) =>
        new(p.Id, p.Name, p.Email, p.ExternalUserId, p.Role, p.Skills, p.Capacity, p.StartDate, p.LeaveDate, open, p.IsActiveOn(today) || p.StartDate > today);
}

public sealed class ProjectService(IKnowledgeStore store)
{
    public static ProjectDto ToDto(Project p) => new(p.Id, p.Key, p.Name, p.Description, p.StartDate, p.Deadline, p.CurrentRelease);

    public async Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken ct = default) =>
        (await store.ListAsync(store.Projects.OrderBy(p => p.CreatedAt), ct)).Select(ToDto).ToList();

    public async Task<ProjectDto> CreateAsync(UpsertProjectRequest req, CancellationToken ct = default)
    {
        var p = new Project();
        Apply(p, req);
        store.Add(p);
        await store.SaveChangesAsync(ct);
        return ToDto(p);
    }

    public async Task<ProjectDto> UpdateAsync(Guid id, UpsertProjectRequest req, CancellationToken ct = default)
    {
        var p = await store.FirstOrDefaultAsync(store.Projects.Where(x => x.Id == id), ct) ?? throw new NotFoundException("Project not found");
        Apply(p, req);
        store.Audit("user", "UpdateProject", "Project", p.Id, $"{p.Name} deadline {p.Deadline}");
        await store.SaveChangesAsync(ct);
        return ToDto(p);
    }

    private static void Apply(Project p, UpsertProjectRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Key) || string.IsNullOrWhiteSpace(req.Name)) throw new ValidationException("Key and name are required.");
        if (req.Deadline < req.StartDate) throw new ValidationException("Deadline must be after the start date.");
        p.Key = req.Key.Trim().ToUpperInvariant();
        p.Name = req.Name.Trim();
        p.Description = req.Description ?? "";
        p.StartDate = req.StartDate;
        p.Deadline = req.Deadline;
        p.CurrentRelease = string.IsNullOrWhiteSpace(req.CurrentRelease) ? "v1" : req.CurrentRelease.Trim();
    }
}
