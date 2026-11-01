using System.Diagnostics;
using System.Text.Json;
using MonteCarlo.Application.Abstractions;
using MonteCarlo.Application.Common;
using MonteCarlo.Application.Dtos;
using MonteCarlo.Domain.Common;
using MonteCarlo.Domain.Entities;

namespace MonteCarlo.Application.Services;

/// <summary>Use cases: IngestTranscript and ExtractInsights.</summary>
public sealed class MeetingService(
    IKnowledgeStore store,
    ILlmExtractor extractor,
    ITranscriptSource transcriptSource,
    ReconciliationService reconciliation)
{
    private const int ChunkSeconds = 300;
    private const int ChunkTurns = 40;

    public async Task<IReadOnlyList<MeetingSummaryDto>> ListAsync(Guid? projectId, CancellationToken ct = default)
    {
        var project = await store.ResolveProjectAsync(projectId, ct);
        var meetings = await store.ListAsync(store.Meetings.Where(m => m.ProjectId == project.Id).OrderByDescending(m => m.StartedAt), ct);
        var result = new List<MeetingSummaryDto>();
        foreach (var m in meetings) result.Add(await SummaryAsync(m, ct));
        return result;
    }

    public async Task<MeetingDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var m = await store.FirstOrDefaultAsync(store.Meetings.Where(x => x.Id == id), ct) ?? throw new NotFoundException("Meeting not found");
        var turns = await store.ListAsync(store.SpeakerTurns.Where(t => t.MeetingId == id).OrderBy(t => t.Sequence), ct);
        var decisions = await store.ListAsync(store.Decisions.Where(d => d.MeetingId == id).OrderBy(d => d.DecidedAt), ct);
        var actions = await store.ListAsync(store.ActionItems.Where(a => a.MeetingId == id).OrderBy(a => a.CreatedAt), ct);
        var blockers = await store.ListAsync(store.Blockers.Where(b => b.MeetingId == id).OrderBy(b => b.RaisedAt), ct);
        var evidenceIds = decisions.Select(d => d.EvidenceId).Concat(actions.Select(a => a.EvidenceId)).Concat(blockers.Select(b => b.EvidenceId)).ToList();
        var actionIds = actions.Select(a => (Guid?)a.Id).ToList();
        var decisionIds = decisions.Select(d => (Guid?)d.Id).ToList();
        var blockerIds = blockers.Select(b => (Guid?)b.Id).ToList();
        var suggestions = await store.ListAsync(store.Suggestions.Where(s =>
            actionIds.Contains(s.ActionItemId) || decisionIds.Contains(s.DecisionId) || blockerIds.Contains(s.BlockerId)), ct);
        var logs = await store.ListAsync(store.ExtractionLogs.Where(l => l.MeetingId == id).OrderByDescending(l => l.CreatedAt), ct);

        var lookups = await Lookups.LoadAsync(store, m.ProjectId, evidenceIds.Concat(suggestions.Select(s => s.EvidenceId)), ct);
        return new MeetingDetailDto(
            await SummaryAsync(m, ct),
            turns.Select(t => new SpeakerTurnDto(t.Sequence, t.OffsetSeconds, TranscriptParser.Format(t.OffsetSeconds), t.SpeakerName, t.PersonId, t.Text)).ToList(),
            decisions.Select(lookups.ToDto).ToList(),
            actions.Select(lookups.ToDto).ToList(),
            blockers.Select(lookups.ToDto).ToList(),
            suggestions.OrderByDescending(s => s.CreatedAt).Select(lookups.ToDto).ToList(),
            logs.Select(l => new ExtractionLogDto(l.Extractor, l.Model, l.PromptVersion, l.InputChars, l.DurationMs, l.CreatedAt)).ToList());
    }

    /// <summary>Manual upload / paste of a transcript (also the fallback when Graph permissions are refused).</summary>
    public async Task<MeetingDetailDto> IngestAsync(IngestTranscriptRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Content)) throw new ValidationException("Transcript content is empty.");
        var project = await store.ResolveProjectAsync(req.ProjectId, ct);
        var meeting = await CreateMeetingAsync(project.Id, string.IsNullOrWhiteSpace(req.Title) ? "Untitled meeting" : req.Title.Trim(),
            req.StartedAt ?? DateTimeOffset.UtcNow, req.Content, MeetingSource.Upload, null, req.OptOut, ct);

        if (!meeting.OptedOut) await ExtractAsync(meeting.Id, ct);
        return await GetAsync(meeting.Id, ct);
    }

    /// <summary>Called by the queue worker after a Graph "transcript created" notification.</summary>
    public async Task<Guid?> IngestFromTeamsAsync(string externalMeetingId, string transcriptId, CancellationToken ct = default)
    {
        if (!transcriptSource.IsConfigured) return null;
        var externalId = $"{externalMeetingId}/{transcriptId}";
        if (await store.FirstOrDefaultAsync(store.Meetings.Where(m => m.ExternalId == externalId), ct) is { } existing) return existing.Id;

        var transcript = await transcriptSource.GetAsync(externalMeetingId, transcriptId, ct);
        var project = await store.ResolveProjectAsync(null, ct);
        var settings = await store.GetSettingsAsync(ct);
        var meeting = await CreateMeetingAsync(project.Id, transcript.Title, transcript.StartedAt, transcript.Content,
            MeetingSource.TeamsTranscript, externalId, !settings.CaptureEnabledByDefault, ct);
        if (!meeting.OptedOut) await ExtractAsync(meeting.Id, ct);
        return meeting.Id;
    }

    private async Task<Meeting> CreateMeetingAsync(Guid projectId, string title, DateTimeOffset startedAt, string content,
        MeetingSource source, string? externalId, bool optOut, CancellationToken ct)
    {
        var parsed = TranscriptParser.Parse(content);
        if (parsed.Count == 0) throw new ValidationException("No speaker turns found in the transcript.");

        var people = await store.ListAsync(store.People, ct);
        var meeting = new Meeting
        {
            ProjectId = projectId,
            Title = title,
            StartedAt = startedAt,
            Source = source,
            ExternalId = externalId,
            RawTranscript = content,
            OptedOut = optOut
        };
        store.Add(meeting);
        foreach (var t in parsed)
        {
            store.Add(new SpeakerTurn
            {
                MeetingId = meeting.Id,
                Sequence = t.Sequence,
                OffsetSeconds = t.OffsetSeconds,
                SpeakerName = t.Speaker,
                PersonId = people.FirstOrDefault(p => p.MatchesName(t.Speaker) || (p.ExternalUserId != null && p.ExternalUserId == t.Speaker))?.Id,
                Text = t.Text
            });
        }
        store.Audit("system", "IngestTranscript", "Meeting", meeting.Id, $"{parsed.Count} turns from {source}");
        await store.SaveChangesAsync(ct);
        return meeting;
    }

    /// <summary>Re-run extraction (e.g. after a prompt change). Removes previously extracted items that are still pending.</summary>
    public async Task<MeetingDetailDto> ReprocessAsync(Guid id, CancellationToken ct = default)
    {
        var m = await store.FirstOrDefaultAsync(store.Meetings.Where(x => x.Id == id), ct) ?? throw new NotFoundException("Meeting not found");
        foreach (var d in await store.ListAsync(store.Decisions.Where(x => x.MeetingId == id), ct)) store.Remove(d);
        foreach (var a in await store.ListAsync(store.ActionItems.Where(x => x.MeetingId == id && x.State == ActionItemState.Proposed), ct)) store.Remove(a);
        foreach (var b in await store.ListAsync(store.Blockers.Where(x => x.MeetingId == id && x.ResolvedAt == null), ct)) store.Remove(b);
        m.OptedOut = false;
        await store.SaveChangesAsync(ct);
        await ExtractAsync(id, ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var m = await store.FirstOrDefaultAsync(store.Meetings.Where(x => x.Id == id), ct) ?? throw new NotFoundException("Meeting not found");
        foreach (var t in await store.ListAsync(store.SpeakerTurns.Where(x => x.MeetingId == id), ct)) store.Remove(t);
        foreach (var d in await store.ListAsync(store.Decisions.Where(x => x.MeetingId == id), ct)) store.Remove(d);
        foreach (var a in await store.ListAsync(store.ActionItems.Where(x => x.MeetingId == id), ct)) store.Remove(a);
        foreach (var b in await store.ListAsync(store.Blockers.Where(x => x.MeetingId == id), ct)) store.Remove(b);
        foreach (var e in await store.ListAsync(store.Evidence.Where(x => x.MeetingId == id), ct)) store.Remove(e);
        store.Remove(m);
        store.Audit("user", "DeleteMeeting", "Meeting", id, m.Title);
        await store.SaveChangesAsync(ct);
    }

    /// <summary>Extract decisions, action items and blockers, resolve them to tickets, then reconcile.</summary>
    public async Task ExtractAsync(Guid meetingId, CancellationToken ct = default)
    {
        var meeting = await store.FirstOrDefaultAsync(store.Meetings.Where(m => m.Id == meetingId), ct) ?? throw new NotFoundException("Meeting not found");
        var project = await store.ResolveProjectAsync(meeting.ProjectId, ct);
        var settings = await store.GetSettingsAsync(ct);
        var turns = await store.ListAsync(store.SpeakerTurns.Where(t => t.MeetingId == meetingId).OrderBy(t => t.Sequence), ct);
        var people = await store.ListAsync(store.People, ct);
        var openItems = (await store.ListAsync(store.WorkItems.Where(w => w.ProjectId == project.Id), ct)).Where(w => w.Status.IsOpen()).ToList();

        var ctx = new ExtractionContext(project.Key, DateOnly.FromDateTime(meeting.StartedAt.UtcDateTime),
            people.Select(p => p.Name).ToList(), openItems.Select(w => new KnownTicket(w.ExternalRef, w.Title)).ToList());

        var sw = Stopwatch.StartNew();
        var decisions = new List<ExtractedDecision>();
        var actions = new List<ExtractedAction>();
        var blockers = new List<ExtractedBlocker>();
        string model = "", promptVersion = "";
        foreach (var chunk in Chunk(turns))
        {
            var r = await extractor.ExtractAsync(chunk, ctx, ct);
            decisions.AddRange(r.Decisions);
            actions.AddRange(r.Actions);
            blockers.AddRange(r.Blockers);
            model = r.Model;
            promptVersion = r.PromptVersion;
        }
        sw.Stop();

        var bySeq = turns.ToDictionary(t => t.Sequence);
        Person? FindPerson(string? name) => people.FirstOrDefault(p => p.MatchesName(name));

        Evidence MakeEvidence(int seq, string quote, double confidence)
        {
            bySeq.TryGetValue(seq, out var turn);
            var e = new Evidence
            {
                SourceType = SourceType.Meeting,
                SourceId = meeting.ExternalId ?? meeting.Id.ToString(),
                MeetingId = meeting.Id,
                OffsetSeconds = turn?.OffsetSeconds,
                SpeakerName = turn?.SpeakerName ?? "",
                QuotedText = Truncate(string.IsNullOrWhiteSpace(quote) ? turn?.Text ?? "" : quote, 500),
                Confidence = Math.Round(confidence, 2)
            };
            store.Add(e);
            return e;
        }

        DateTimeOffset At(int seq) => meeting.StartedAt.AddSeconds(bySeq.TryGetValue(seq, out var t) ? t.OffsetSeconds : 0);

        WorkItem? Resolve(string? ticketRef, string text, double threshold)
        {
            if (!string.IsNullOrWhiteSpace(ticketRef))
            {
                var exact = openItems.FirstOrDefault(w => w.ExternalRef.Equals(ticketRef, StringComparison.OrdinalIgnoreCase));
                if (exact is not null) return exact;
            }
            foreach (var r in TextSimilarity.FindTicketRefs(text))
            {
                var exact = openItems.FirstOrDefault(w => w.ExternalRef.Equals(r, StringComparison.OrdinalIgnoreCase));
                if (exact is not null) return exact;
            }
            var best = openItems.Select(w => (w, s: TextSimilarity.Score(text, w.Title))).OrderByDescending(x => x.s).FirstOrDefault();
            return best.w is not null && best.s >= threshold ? best.w : null;
        }

        int kept = 0;
        foreach (var d in decisions.Where(d => d.Confidence >= settings.MinConfidence))
        {
            var ev = MakeEvidence(d.TurnSequence, d.Quote, d.Confidence);
            var who = FindPerson(d.DecidedBy) ?? FindPerson(ev.SpeakerName);
            store.Add(new Decision
            {
                ProjectId = project.Id,
                MeetingId = meeting.Id,
                Statement = Truncate(d.Statement, 1000),
                DecidedById = who?.Id,
                DecidedByName = who?.Name ?? d.DecidedBy ?? ev.SpeakerName,
                DecidedAt = At(d.TurnSequence),
                Rationale = Truncate(d.Rationale ?? "", 1000),
                AffectedWorkItemId = Resolve(d.TicketRef, d.Statement, 0.5)?.Id,
                EvidenceId = ev.Id
            });
            kept++;
        }

        foreach (var a in actions.Where(a => a.Confidence >= settings.MinConfidence))
        {
            var ev = MakeEvidence(a.TurnSequence, a.Quote, a.Confidence);
            var owner = FindPerson(a.Owner) ?? (string.IsNullOrWhiteSpace(a.Owner) ? FindPerson(ev.SpeakerName) : null);
            var linked = Resolve(a.TicketRef, a.Description, 0.75);
            store.Add(new ActionItem
            {
                ProjectId = project.Id,
                MeetingId = meeting.Id,
                Description = Truncate(a.Description, 1000),
                OwnerId = owner?.Id,
                OwnerName = owner?.Name ?? a.Owner ?? "",
                DueDate = a.DueDate,
                LinkedWorkItemId = linked?.Id,
                EvidenceId = ev.Id
            });
            linked?.Touch(DateTimeOffset.UtcNow);
            kept++;
        }

        foreach (var b in blockers.Where(b => b.Confidence >= settings.MinConfidence))
        {
            var ev = MakeEvidence(b.TurnSequence, b.Quote, b.Confidence);
            var who = FindPerson(b.RaisedBy) ?? FindPerson(ev.SpeakerName);
            store.Add(new Blocker
            {
                ProjectId = project.Id,
                MeetingId = meeting.Id,
                Description = Truncate(b.Description, 1000),
                BlockingWorkItemId = Resolve(b.TicketRef, b.Description, 0.5)?.Id,
                RaisedById = who?.Id,
                RaisedByName = who?.Name ?? b.RaisedBy ?? ev.SpeakerName,
                RaisedAt = At(b.TurnSequence),
                EvidenceId = ev.Id
            });
            kept++;
        }

        store.Add(new ExtractionLog
        {
            MeetingId = meeting.Id,
            Extractor = extractor.Name,
            Model = model,
            PromptVersion = promptVersion,
            InputChars = turns.Sum(t => t.Text.Length),
            DecisionsFound = decisions.Count,
            ActionsFound = actions.Count,
            BlockersFound = blockers.Count,
            DurationMs = sw.ElapsedMilliseconds,
            OutputJson = JsonSerializer.Serialize(new { decisions, actions, blockers }, Json.Options)
        });
        meeting.Processed = true;
        meeting.ProcessedAt = DateTimeOffset.UtcNow;
        store.Audit("system", "ExtractInsights", "Meeting", meeting.Id, $"{extractor.Name}: kept {kept} items");
        await store.SaveChangesAsync(ct);

        await reconciliation.RunAsync(project.Id, ct);
    }

    /// <summary>Groups turns into topics of roughly 2-5 minutes so each LLM call stays small.</summary>
    private static IEnumerable<List<SpeakerTurn>> Chunk(List<SpeakerTurn> turns)
    {
        var current = new List<SpeakerTurn>();
        foreach (var t in turns)
        {
            if (current.Count > 0 && (t.OffsetSeconds - current[0].OffsetSeconds > ChunkSeconds || current.Count >= ChunkTurns))
            {
                yield return current;
                current = [];
            }
            current.Add(t);
        }
        if (current.Count > 0) yield return current;
    }

    private async Task<MeetingSummaryDto> SummaryAsync(Meeting m, CancellationToken ct) => new(
        m.Id, m.Title, m.StartedAt, m.Source, m.Processed, m.OptedOut, m.RawDeletedAt != null,
        await store.CountAsync(store.SpeakerTurns.Where(t => t.MeetingId == m.Id), ct),
        await store.CountAsync(store.Decisions.Where(d => d.MeetingId == m.Id), ct),
        await store.CountAsync(store.ActionItems.Where(a => a.MeetingId == m.Id), ct),
        await store.CountAsync(store.Blockers.Where(b => b.MeetingId == m.Id), ct));

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 3)] + "...";
}
