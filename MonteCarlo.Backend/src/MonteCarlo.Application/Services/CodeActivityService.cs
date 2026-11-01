using MonteCarlo.Application.Abstractions;
using MonteCarlo.Application.Common;
using MonteCarlo.Application.Dtos;
using MonteCarlo.Domain.Common;
using MonteCarlo.Domain.Entities;

namespace MonteCarlo.Application.Services;

/// <summary>Options C1/C2: commits, PRs, builds and deployments that mention ticket ids reveal the real status.</summary>
public sealed class CodeActivityService(IKnowledgeStore store, ICodeActivitySource source, ReconciliationService reconciliation)
{
    public async Task<IReadOnlyList<CodeEventDto>> ListAsync(Guid? projectId, CancellationToken ct = default)
    {
        var project = await store.ResolveProjectAsync(projectId, ct);
        var rows = await store.ListAsync(store.CodeEvents.Where(c => c.ProjectId == project.Id).OrderByDescending(c => c.OccurredAt).Take(200), ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<CodeEventDto> IngestAsync(CodeEventRequest req, bool reconcile = true, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) throw new ValidationException("Title is required.");
        var project = await store.ResolveProjectAsync(req.ProjectId, ct);
        var ev = await AddAsync(project.Id, new CodeEventData(req.Type, req.ExternalId ?? Guid.NewGuid().ToString("N")[..10], req.Title,
            req.Author ?? "", req.Branch ?? "", req.OccurredAt ?? DateTimeOffset.UtcNow, req.Url ?? ""), ct);
        await store.SaveChangesAsync(ct);
        if (reconcile) await reconciliation.RunAsync(project.Id, ct);
        return ToDto(ev);
    }

    /// <summary>Pull new events from the configured source (e.g. GitHub) and reconcile.</summary>
    public async Task<int> PullAsync(Guid? projectId, CancellationToken ct = default)
    {
        if (!source.IsConfigured) return 0;
        var project = await store.ResolveProjectAsync(projectId, ct);
        var last = await store.FirstOrDefaultAsync(store.CodeEvents.Where(c => c.ProjectId == project.Id).OrderByDescending(c => c.OccurredAt), ct);
        var since = last?.OccurredAt ?? DateTimeOffset.UtcNow.AddDays(-30);
        var events = await source.GetSinceAsync(since, ct);
        var known = (await store.ListAsync(store.CodeEvents.Where(c => c.ProjectId == project.Id && c.OccurredAt >= since).Select(c => c.ExternalId), ct)).ToHashSet();
        var added = 0;
        foreach (var e in events.Where(e => !known.Contains(e.ExternalId)))
        {
            await AddAsync(project.Id, e, ct);
            added++;
        }
        await store.SaveChangesAsync(ct);
        if (added > 0) await reconciliation.RunAsync(project.Id, ct);
        return added;
    }

    private async Task<CodeEvent> AddAsync(Guid projectId, CodeEventData e, CancellationToken ct)
    {
        var refs = TextSimilarity.FindTicketRefs($"{e.Title} {e.Branch}");
        var ev = new CodeEvent
        {
            ProjectId = projectId,
            Type = e.Type,
            ExternalId = e.ExternalId,
            Title = e.Title,
            Author = e.Author,
            Branch = e.Branch,
            TicketRefs = string.Join(",", refs),
            OccurredAt = e.OccurredAt,
            Url = e.Url
        };
        store.Add(ev);

        // Code activity is real activity: the ticket is not stale, even if the board was not updated.
        if (refs.Count > 0)
        {
            var items = await store.ListAsync(store.WorkItems.Where(w => w.ProjectId == projectId && refs.Contains(w.ExternalRef)), ct);
            foreach (var w in items.Where(w => w.LastActivityAt < e.OccurredAt)) w.LastActivityAt = e.OccurredAt;
        }
        return ev;
    }

    private static CodeEventDto ToDto(CodeEvent c) =>
        new(c.Id, c.Type, c.ExternalId, c.Title, c.Author, c.Branch, c.Refs().ToList(), c.OccurredAt, c.Url);
}
