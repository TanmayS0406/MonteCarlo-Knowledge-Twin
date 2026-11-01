using MonteCarlo.Application.Abstractions;
using MonteCarlo.Application.Common;
using MonteCarlo.Application.Dtos;
using MonteCarlo.Domain.Common;
using MonteCarlo.Domain.Entities;

namespace MonteCarlo.Application.Services;

/// <summary>Use case: ReviewSuggestion. One tap to accept (optionally edited) or reject; accepted ones are written back to the ticket system.</summary>
public sealed class SuggestionService(IKnowledgeStore store, ITicketSystem tickets)
{
    public async Task<IReadOnlyList<SuggestionDto>> ListAsync(Guid? projectId, SuggestionOutcome? outcome, CancellationToken ct = default)
    {
        var project = await store.ResolveProjectAsync(projectId, ct);
        var q = store.Suggestions.Where(s => s.ProjectId == project.Id);
        if (outcome is { } o) q = q.Where(s => s.Outcome == o);
        var list = await store.ListAsync(q.OrderByDescending(s => s.CreatedAt).Take(500), ct);
        var lookups = await Lookups.LoadAsync(store, project.Id, list.Select(s => s.EvidenceId), ct);
        return list.Select(lookups.ToDto).ToList();
    }

    public async Task<SuggestionDto> AcceptAsync(Guid id, ReviewRequest req, CancellationToken ct = default)
    {
        var s = await store.FirstOrDefaultAsync(store.Suggestions.Where(x => x.Id == id), ct) ?? throw new NotFoundException("Suggestion not found");
        if (s.Outcome != SuggestionOutcome.Pending) throw new ValidationException($"Suggestion already {s.Outcome}.");
        var project = await store.ResolveProjectAsync(s.ProjectId, ct);
        var now = DateTimeOffset.UtcNow;

        // Apply edits made by the reviewer.
        var edited = false;
        void Set<T>(T? value, Func<T?> get, Action<T?> set)
        {
            if (value is null || EqualityComparer<T?>.Default.Equals(value, get())) return;
            set(value);
            edited = true;
        }
        Set(req.ProposedTitle, () => s.ProposedTitle, v => s.ProposedTitle = v);
        Set(req.ProposedStatus, () => s.ProposedStatus, v => s.ProposedStatus = v);
        Set(req.ProposedOwnerId, () => s.ProposedOwnerId, v => s.ProposedOwnerId = v);
        Set(req.ProposedDueDate, () => s.ProposedDueDate, v => s.ProposedDueDate = v);
        Set(req.ProposedRelease, () => s.ProposedRelease, v => s.ProposedRelease = v);
        Set(req.ProposedEstimateDays, () => s.ProposedEstimateDays, v => s.ProposedEstimateDays = v);
        Set(req.ProposedRole, () => s.ProposedRole, v => s.ProposedRole = v);

        var item = s.WorkItemId is { } wid ? await store.FirstOrDefaultAsync(store.WorkItems.Where(w => w.Id == wid), ct) : null;
        var action = s.ActionItemId is { } aid ? await store.FirstOrDefaultAsync(store.ActionItems.Where(a => a.Id == aid), ct) : null;
        Person? owner = s.ProposedOwnerId is { } oid ? await store.FirstOrDefaultAsync(store.People.Where(p => p.Id == oid), ct) : null;

        switch (s.Type)
        {
            case SuggestionType.CreateTicket:
            {
                var title = string.IsNullOrWhiteSpace(s.ProposedTitle) ? action?.Description ?? s.Title : s.ProposedTitle!;
                var role = s.ProposedRole ?? owner?.Role ?? WorkRole.Backend;
                var description = action is null ? s.Detail : $"Captured from a meeting: \"{action.Description}\"";
                var draft = new WorkItemDraft(project.Key, title, description, role, owner?.Email, s.ProposedDueDate,
                    s.ProposedRelease ?? project.CurrentRelease, s.ProposedEstimateDays);
                var key = await tickets.CreateAsync(draft, ct);
                item = new WorkItem
                {
                    ProjectId = project.Id,
                    ExternalRef = key,
                    Title = title,
                    Description = description,
                    Role = role,
                    OwnerId = owner?.Id,
                    Release = draft.Release,
                    EstimateDays = s.ProposedEstimateDays,
                    Status = WorkItemStatus.Todo,
                    Priority = 2,
                    LastActivityAt = now
                };
                store.Add(item);
                s.WorkItemId = item.Id;
                if (action is not null) { action.LinkedWorkItemId = item.Id; action.State = ActionItemState.Accepted; }
                break;
            }
            case SuggestionType.LinkConversation:
                Require(item);
                if (action is not null) { action.LinkedWorkItemId = item!.Id; action.State = ActionItemState.Accepted; }
                if (item!.OwnerId is null && owner is not null) await AssignAsync(item, owner, ct);
                item.Touch(now);
                break;
            case SuggestionType.UpdateStatus:
            case SuggestionType.CloseTicket:
            case SuggestionType.StaleTicket:
                Require(item);
                var status = s.ProposedStatus ?? (s.Type == SuggestionType.CloseTicket ? WorkItemStatus.Done : item!.Status);
                await tickets.UpdateStatusAsync(item!.ExternalRef, status, ct);
                item.ChangeStatus(status, now);
                if (status == WorkItemStatus.Done)
                {
                    foreach (var a in await store.ListAsync(store.ActionItems.Where(a => a.LinkedWorkItemId == item.Id && a.State != ActionItemState.Rejected), ct))
                        a.State = ActionItemState.Done;
                }
                if (status != WorkItemStatus.Blocked)
                {
                    foreach (var b in await store.ListAsync(store.Blockers.Where(b => b.BlockingWorkItemId == item.Id && b.ResolvedAt == null), ct))
                        b.ResolvedAt = now;
                }
                break;
            case SuggestionType.MoveRelease:
                Require(item);
                var release = s.ProposedRelease ?? "v2";
                await tickets.UpdateFieldsAsync(item!.ExternalRef, new TicketFieldUpdate(Release: release), ct);
                item.Release = release;
                item.Touch(now);
                break;
            case SuggestionType.AssignOwner:
                Require(item);
                if (owner is null) throw new ValidationException("Pick an owner.");
                await AssignAsync(item!, owner, ct);
                break;
            case SuggestionType.AddEstimate:
                Require(item);
                var est = s.ProposedEstimateDays ?? throw new ValidationException("Enter an estimate.");
                await tickets.UpdateFieldsAsync(item!.ExternalRef, new TicketFieldUpdate(EstimateDays: est), ct);
                item.EstimateDays = est;
                item.Touch(now);
                break;
        }

        s.Outcome = edited ? SuggestionOutcome.Edited : SuggestionOutcome.Accepted;
        s.ReviewedBy = req.Reviewer ?? "reviewer";
        s.ReviewedAt = now;
        s.ReviewNote = req.Note;
        store.Audit(s.ReviewedBy, $"Accept{s.Type}", "Suggestion", s.Id, $"{s.Title}{(edited ? " (edited)" : "")} -> {item?.ExternalRef}");
        await store.SaveChangesAsync(ct);

        var lookups = await Lookups.LoadAsync(store, project.Id, [s.EvidenceId], ct);
        return lookups.ToDto(s);
    }

    public async Task<SuggestionDto> RejectAsync(Guid id, ReviewRequest req, CancellationToken ct = default)
    {
        var s = await store.FirstOrDefaultAsync(store.Suggestions.Where(x => x.Id == id), ct) ?? throw new NotFoundException("Suggestion not found");
        if (s.Outcome != SuggestionOutcome.Pending) throw new ValidationException($"Suggestion already {s.Outcome}.");
        s.Outcome = SuggestionOutcome.Rejected;
        s.ReviewedBy = req.Reviewer ?? "reviewer";
        s.ReviewedAt = DateTimeOffset.UtcNow;
        s.ReviewNote = req.Note;

        // A rejected "create ticket" means the extracted action was wrong; remember it so it is not proposed again.
        if (s.Type == SuggestionType.CreateTicket && s.ActionItemId is { } aid &&
            await store.FirstOrDefaultAsync(store.ActionItems.Where(a => a.Id == aid), ct) is { } action)
            action.State = ActionItemState.Rejected;

        store.Audit(s.ReviewedBy, $"Reject{s.Type}", "Suggestion", s.Id, $"{s.Title}. {req.Note}");
        await store.SaveChangesAsync(ct);
        var lookups = await Lookups.LoadAsync(store, s.ProjectId, [s.EvidenceId], ct);
        return lookups.ToDto(s);
    }

    private async Task AssignAsync(WorkItem item, Person owner, CancellationToken ct)
    {
        await tickets.UpdateFieldsAsync(item.ExternalRef, new TicketFieldUpdate(AssigneeEmail: owner.Email), ct);
        item.OwnerId = owner.Id;
        item.Touch(DateTimeOffset.UtcNow);
    }

    private static void Require(WorkItem? item)
    {
        if (item is null) throw new ValidationException("The ticket for this suggestion no longer exists.");
    }
}
