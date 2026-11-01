using MonteCarlo.Application.Abstractions;
using MonteCarlo.Application.Dtos;
using MonteCarlo.Domain.Common;
using MonteCarlo.Domain.Entities;
using MonteCarlo.Domain.Rules;

namespace MonteCarlo.Application.Services;

/// <summary>Loads the reference data needed to turn entities into DTOs in a few queries.</summary>
public sealed class Lookups
{
    public Dictionary<Guid, Person> People { get; private init; } = [];
    public Dictionary<Guid, WorkItem> WorkItems { get; private init; } = [];
    public Dictionary<Guid, string> MeetingTitles { get; private init; } = [];
    public Dictionary<Guid, Evidence> Evidence { get; private set; } = [];
    public Dictionary<Guid, int> PendingByWorkItem { get; private init; } = [];
    public int StaleAfterDays { get; private init; } = 7;

    public static async Task<Lookups> LoadAsync(IKnowledgeStore store, Guid projectId, IEnumerable<Guid?>? evidenceIds = null, CancellationToken ct = default)
    {
        var settings = await store.GetSettingsAsyncSafe(ct);
        var people = await store.ListAsync(store.People, ct);
        var items = await store.ListAsync(store.WorkItems.Where(w => w.ProjectId == projectId), ct);
        var meetings = await store.ListAsync(store.Meetings.Where(m => m.ProjectId == projectId).Select(m => new { m.Id, m.Title }), ct);
        var pending = await store.ListAsync(store.Suggestions
            .Where(s => s.ProjectId == projectId && s.Outcome == SuggestionOutcome.Pending && s.WorkItemId != null)
            .GroupBy(s => s.WorkItemId!.Value)
            .Select(g => new { g.Key, Count = g.Count() }), ct);

        var l = new Lookups
        {
            People = people.ToDictionary(p => p.Id),
            WorkItems = items.ToDictionary(w => w.Id),
            MeetingTitles = meetings.ToDictionary(m => m.Id, m => m.Title),
            PendingByWorkItem = pending.ToDictionary(p => p.Key, p => p.Count),
            StaleAfterDays = settings?.StaleAfterDays ?? 7
        };
        if (evidenceIds is not null) await l.LoadEvidenceAsync(store, evidenceIds, ct);
        return l;
    }

    public async Task LoadEvidenceAsync(IKnowledgeStore store, IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var wanted = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().Where(i => !Evidence.ContainsKey(i)).ToList();
        foreach (var chunk in wanted.Chunk(500))
        {
            var rows = await store.ListAsync(store.Evidence.Where(e => chunk.Contains(e.Id)), ct);
            foreach (var r in rows) Evidence[r.Id] = r;
        }
    }

    public string? PersonName(Guid? id) => id is { } v && People.TryGetValue(v, out var p) ? p.Name : null;
    public string? Ref(Guid? id) => id is { } v && WorkItems.TryGetValue(v, out var w) ? w.ExternalRef : null;

    public EvidenceDto? ToDto(Guid? evidenceId)
    {
        if (evidenceId is not { } id || !Evidence.TryGetValue(id, out var e)) return null;
        return new EvidenceDto(e.Id, e.SourceType, e.SourceId, e.MeetingId,
            e.MeetingId is { } m && MeetingTitles.TryGetValue(m, out var title) ? title : null,
            e.OffsetSeconds, TranscriptParser.Format(e.OffsetSeconds), e.SpeakerName, e.QuotedText, e.Confidence);
    }

    public WorkItemDto ToDto(WorkItem w)
    {
        var now = DateTimeOffset.UtcNow;
        var q = TicketQuality.Evaluate(w, now, StaleAfterDays);
        var deps = w.Dependencies.Select(d => d.DependsOnId).ToList();
        return new WorkItemDto(w.Id, w.ExternalRef, w.Title, w.Description, w.AcceptanceCriteria, w.Status, w.Role,
            w.EstimateDays, w.ActualDays, w.OwnerId, PersonName(w.OwnerId), w.Release, w.Priority, w.LastActivityAt, w.StartedAt, w.CompletedAt,
            deps, deps.Select(d => Ref(d) ?? "?").ToList(), q.Score, q.Issues,
            TicketQuality.IsStale(w, now, StaleAfterDays) && w.Status is WorkItemStatus.InProgress or WorkItemStatus.InReview or WorkItemStatus.Blocked,
            PendingByWorkItem.GetValueOrDefault(w.Id));
    }

    public DecisionDto ToDto(Decision d) => new(d.Id, d.Statement, d.DecidedByName, d.DecidedAt, d.Rationale,
        d.AffectedWorkItemId, Ref(d.AffectedWorkItemId), d.MeetingId, ToDto(d.EvidenceId));

    public ActionItemDto ToDto(ActionItem a) => new(a.Id, a.Description, a.OwnerId, PersonName(a.OwnerId) ?? a.OwnerName, a.DueDate,
        a.LinkedWorkItemId, Ref(a.LinkedWorkItemId), a.State, a.MeetingId, ToDto(a.EvidenceId));

    public BlockerDto ToDto(Blocker b) => new(b.Id, b.Description, b.BlockingWorkItemId, Ref(b.BlockingWorkItemId), b.RaisedByName,
        b.RaisedAt, b.ResolvedAt, b.MeetingId, ToDto(b.EvidenceId));

    public SuggestionDto ToDto(Suggestion s)
    {
        WorkItem? w = s.WorkItemId is { } id && WorkItems.TryGetValue(id, out var wi) ? wi : null;
        return new SuggestionDto(s.Id, s.Type, s.Title, s.Detail, s.Confidence, s.Outcome, s.CreatedAt,
            s.WorkItemId, w?.ExternalRef, w?.Title, w?.Status,
            s.ProposedTitle, s.ProposedStatus, s.ProposedOwnerId, PersonName(s.ProposedOwnerId), s.ProposedDueDate,
            s.ProposedRelease, s.ProposedEstimateDays, s.ProposedRole, s.ReviewedBy, s.ReviewedAt, s.ReviewNote, ToDto(s.EvidenceId));
    }
}

internal static class LookupStoreExtensions
{
    public static async Task<TenantSettings?> GetSettingsAsyncSafe(this IKnowledgeStore store, CancellationToken ct) =>
        await store.FirstOrDefaultAsync(store.Settings, ct);
}
