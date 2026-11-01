using System.Globalization;
using System.Text;
using MonteCarlo.Application.Abstractions;
using MonteCarlo.Application.Common;
using MonteCarlo.Application.Dtos;
using MonteCarlo.Domain.Common;
using MonteCarlo.Domain.Entities;

namespace MonteCarlo.Application.Services;

/// <summary>"Why was this decided?" search over decisions, actions, blockers and transcripts. Every answer cites its source.</summary>
public sealed class SearchService(IKnowledgeStore store)
{
    public async Task<SearchResultDto> SearchAsync(Guid? projectId, string query, int limit = 25, CancellationToken ct = default)
    {
        var project = await store.ResolveProjectAsync(projectId, ct);
        query = (query ?? "").Trim();
        if (query.Length == 0) return new SearchResultDto(query, "Type a question, e.g. \"why did we drop Excel export?\"", []);

        var decisions = await store.ListAsync(store.Decisions.Where(d => d.ProjectId == project.Id), ct);
        var actions = await store.ListAsync(store.ActionItems.Where(a => a.ProjectId == project.Id), ct);
        var blockers = await store.ListAsync(store.Blockers.Where(b => b.ProjectId == project.Id), ct);
        var meetingIds = (await store.ListAsync(store.Meetings.Where(m => m.ProjectId == project.Id).Select(m => new { m.Id, m.StartedAt }), ct))
            .ToDictionary(m => m.Id, m => m.StartedAt);
        var ids = meetingIds.Keys.ToList();
        var turns = await store.ListAsync(store.SpeakerTurns.Where(t => ids.Contains(t.MeetingId)), ct);

        var lookups = await Lookups.LoadAsync(store, project.Id,
            decisions.Select(d => d.EvidenceId).Concat(actions.Select(a => a.EvidenceId)).Concat(blockers.Select(b => b.EvidenceId)), ct);

        double Score(string text, double boost)
        {
            var s = TextSimilarity.Score(query, text);
            if (text.Contains(query, StringComparison.OrdinalIgnoreCase)) s += 0.5;
            return s * boost;
        }

        var hits = new List<SearchHitDto>();
        foreach (var d in decisions)
        {
            var s = Score($"{d.Statement} {d.Rationale} {lookups.Ref(d.AffectedWorkItemId)}", 1.3);
            if (s > 0.15) hits.Add(new SearchHitDto("Decision", d.Id, d.Statement, s, d.DecidedAt, d.DecidedByName, lookups.Ref(d.AffectedWorkItemId), lookups.ToDto(d.EvidenceId)));
        }
        foreach (var a in actions)
        {
            var s = Score(a.Description, 1.0);
            if (s > 0.15) hits.Add(new SearchHitDto("Action", a.Id, a.Description, s, a.CreatedAt, lookups.PersonName(a.OwnerId) ?? a.OwnerName, lookups.Ref(a.LinkedWorkItemId), lookups.ToDto(a.EvidenceId)));
        }
        foreach (var b in blockers)
        {
            var s = Score(b.Description, 1.0);
            if (s > 0.15) hits.Add(new SearchHitDto("Blocker", b.Id, b.Description, s, b.RaisedAt, b.RaisedByName, lookups.Ref(b.BlockingWorkItemId), lookups.ToDto(b.EvidenceId)));
        }
        foreach (var t in turns)
        {
            var s = Score(t.Text, 0.7);
            if (s <= 0.25) continue;
            var at = meetingIds[t.MeetingId].AddSeconds(t.OffsetSeconds);
            var ev = new EvidenceDto(t.Id, SourceType.Meeting, t.MeetingId.ToString(), t.MeetingId, lookups.MeetingTitles.GetValueOrDefault(t.MeetingId),
                t.OffsetSeconds, TranscriptParser.Format(t.OffsetSeconds), t.SpeakerName, t.Text, 1);
            hits.Add(new SearchHitDto("Transcript", t.Id, t.Text, s, at, t.SpeakerName, null, ev));
        }

        var top = hits.OrderByDescending(h => h.Score).ThenByDescending(h => h.At).Take(limit).ToList();
        return new SearchResultDto(query, Answer(top), top);
    }

    private static string Answer(IReadOnlyList<SearchHitDto> hits)
    {
        var d = hits.FirstOrDefault(h => h.Kind == "Decision") ?? hits.FirstOrDefault();
        if (d is null) return "Nothing in the knowledge graph matches that yet.";
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"On {d.At:dd MMM yyyy}, {(string.IsNullOrEmpty(d.Who) ? "the team" : d.Who)} ");
        sb.Append(d.Kind switch
        {
            "Decision" => $"decided: \"{d.Text}\"",
            "Action" => $"took an action: \"{d.Text}\"",
            "Blocker" => $"raised a blocker: \"{d.Text}\"",
            _ => $"said: \"{d.Text}\""
        });
        if (d.Evidence is { } e && !string.IsNullOrEmpty(e.MeetingTitle))
            sb.Append(CultureInfo.InvariantCulture, $" Source: {e.MeetingTitle} at {e.Timestamp} ({e.SpeakerName}: \"{e.QuotedText}\").");
        if (!string.IsNullOrEmpty(d.WorkItemRef)) sb.Append(CultureInfo.InvariantCulture, $" Related ticket: {d.WorkItemRef}.");
        return sb.ToString();
    }
}

public sealed class DashboardService(IKnowledgeStore store)
{
    public async Task<DashboardDto> GetAsync(Guid? projectId, CancellationToken ct = default)
    {
        var project = await store.ResolveProjectAsync(projectId, ct);
        var now = DateTimeOffset.UtcNow;
        var since = now.AddDays(-7);

        var decisions = await store.ListAsync(store.Decisions.Where(d => d.ProjectId == project.Id).OrderByDescending(d => d.DecidedAt).Take(8), ct);
        var blockers = await store.ListAsync(store.Blockers.Where(b => b.ProjectId == project.Id && b.ResolvedAt == null).OrderByDescending(b => b.RaisedAt), ct);
        var lookups = await Lookups.LoadAsync(store, project.Id, decisions.Select(d => d.EvidenceId).Concat(blockers.Select(b => b.EvidenceId)), ct);
        var items = lookups.WorkItems.Values.ToList();
        var dtos = items.Select(lookups.ToDto).ToList();
        var open = items.Where(w => w.Status.IsOpen()).ToList();

        var outcomes = await store.ListAsync(store.Suggestions.Where(s => s.ProjectId == project.Id).Select(s => s.Outcome), ct);
        var accepted = outcomes.Count(o => o is SuggestionOutcome.Accepted or SuggestionOutcome.Edited);
        var rejected = outcomes.Count(o => o == SuggestionOutcome.Rejected);

        var latest = await store.FirstOrDefaultAsync(store.SimulationRuns.Where(r => r.ProjectId == project.Id).OrderByDescending(r => r.CreatedAt), ct);

        return new DashboardDto(
            ProjectService.ToDto(project),
            open.Count,
            items.Count(w => w.Status == WorkItemStatus.Done),
            items.Count(w => w.Status == WorkItemStatus.Blocked),
            outcomes.Count(o => o == SuggestionOutcome.Pending),
            open.Count == 0 ? 1 : Math.Round(open.Count(w => w.LastActivityAt >= since) / (double)open.Count, 3),
            accepted + rejected == 0 ? null : Math.Round(accepted / (double)(accepted + rejected), 3),
            await store.CountAsync(store.Meetings.Where(m => m.ProjectId == project.Id && m.Processed), ct),
            await store.CountAsync(store.Decisions.Where(d => d.ProjectId == project.Id), ct),
            await store.CountAsync(store.ActionItems.Where(a => a.ProjectId == project.Id), ct),
            blockers.Count,
            Enum.GetValues<WorkItemStatus>().Select(s => new StatusCountDto(s, items.Count(w => w.Status == s))).ToList(),
            dtos.Where(d => d.IsStale).OrderBy(d => d.LastActivityAt).Take(10).ToList(),
            dtos.Where(d => d.Status.IsOpen() && d.QualityScore < 60).OrderBy(d => d.QualityScore).Take(10).ToList(),
            decisions.Select(lookups.ToDto).ToList(),
            blockers.Select(lookups.ToDto).ToList(),
            latest is null ? null : SimulationService.ToSummary(latest));
    }

    /// <summary>Weekly digest: stale tickets, pending suggestions and the week's decisions.</summary>
    public async Task<DigestDto> GetDigestAsync(Guid? projectId, CancellationToken ct = default)
    {
        var project = await store.ResolveProjectAsync(projectId, ct);
        var since = DateTimeOffset.UtcNow.AddDays(-7);
        var decisions = await store.ListAsync(store.Decisions.Where(d => d.ProjectId == project.Id && d.DecidedAt >= since).OrderByDescending(d => d.DecidedAt), ct);
        var pending = await store.ListAsync(store.Suggestions.Where(s => s.ProjectId == project.Id && s.Outcome == SuggestionOutcome.Pending).OrderByDescending(s => s.CreatedAt), ct);
        var lookups = await Lookups.LoadAsync(store, project.Id, decisions.Select(d => d.EvidenceId).Concat(pending.Select(p => p.EvidenceId)), ct);
        var stale = lookups.WorkItems.Values.Select(lookups.ToDto).Where(d => d.IsStale).ToList();

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Weekly digest for {project.Name}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- {decisions.Count} decisions captured this week");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- {pending.Count} suggestions waiting for review");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- {stale.Count} stale tickets: {string.Join(", ", stale.Select(s => s.ExternalRef))}");

        var monday = DateOnly.FromDateTime(DateTime.UtcNow);
        while (monday.DayOfWeek != DayOfWeek.Monday) monday = monday.AddDays(-1);
        return new DigestDto(monday, sb.ToString(), stale, pending.Select(lookups.ToDto).ToList(), decisions.Select(lookups.ToDto).ToList());
    }
}

public sealed class SettingsService(IKnowledgeStore store, ILlmExtractor extractor, ITicketSystem tickets, ICodeActivitySource code, ITranscriptSource graph)
{
    public async Task<SettingsDto> GetAsync(CancellationToken ct = default) => ToDto(await store.GetSettingsAsync(ct));

    public async Task<SettingsDto> UpdateAsync(UpdateSettingsRequest req, CancellationToken ct = default)
    {
        var s = await store.GetSettingsAsync(ct);
        if (req.TeamName is { } tn) s.TeamName = tn.Trim();
        if (req.RetentionDays is { } rd) s.RetentionDays = Math.Clamp(rd, 1, 3650);
        if (req.CaptureEnabledByDefault is { } c) s.CaptureEnabledByDefault = c;
        if (req.DataRegion is { } dr) s.DataRegion = dr.Trim();
        if (req.StaleAfterDays is { } sd) s.StaleAfterDays = Math.Clamp(sd, 1, 90);
        if (req.MinConfidence is { } mc) s.MinConfidence = Math.Clamp(mc, 0, 1);
        store.Audit("admin", "UpdateSettings", "Settings", s.Id, $"retention={s.RetentionDays} region={s.DataRegion} stale={s.StaleAfterDays}");
        await store.SaveChangesAsync(ct);
        return ToDto(s);
    }

    /// <summary>Delete raw transcripts older than the retention period. Extracted facts and short quotes stay.</summary>
    public async Task<int> PurgeRawTranscriptsAsync(CancellationToken ct = default)
    {
        var s = await store.GetSettingsAsync(ct);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-s.RetentionDays);
        var meetings = await store.ListAsync(store.Meetings.Where(m => m.StartedAt < cutoff && m.RawDeletedAt == null), ct);
        foreach (var m in meetings)
        {
            foreach (var t in await store.ListAsync(store.SpeakerTurns.Where(t => t.MeetingId == m.Id), ct)) store.Remove(t);
            m.RawTranscript = null;
            m.RawDeletedAt = DateTimeOffset.UtcNow;
        }
        if (meetings.Count > 0) store.Audit("system", "Retention", "Meeting", "*", $"Deleted raw transcripts of {meetings.Count} meetings older than {s.RetentionDays} days");
        await store.SaveChangesAsync(ct);
        return meetings.Count;
    }

    public async Task<IReadOnlyList<AuditEntryDto>> AuditAsync(int take, CancellationToken ct = default) =>
        (await store.ListAsync(store.AuditEntries.OrderByDescending(a => a.CreatedAt).Take(Math.Clamp(take, 1, 500)), ct))
        .Select(a => new AuditEntryDto(a.CreatedAt, a.Actor, a.Action, a.EntityType, a.EntityId, a.Details)).ToList();

    private SettingsDto ToDto(TenantSettings s) => new(s.TeamName, s.RetentionDays, s.CaptureEnabledByDefault, s.DataRegion, s.StaleAfterDays,
        s.MinConfidence, extractor.Name, tickets.Name, code.IsConfigured ? code.Name : "Manual / webhook", graph.IsConfigured);
}
