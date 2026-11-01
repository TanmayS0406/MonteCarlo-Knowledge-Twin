using System.Globalization;
using System.Text.RegularExpressions;
using MonteCarlo.Domain.Common;
using MonteCarlo.Domain.Entities;

namespace MonteCarlo.Domain.Rules;

public sealed record ReconciliationSnapshot(
    Guid ProjectId,
    DateTimeOffset Now,
    string CurrentRelease,
    int StaleAfterDays,
    IReadOnlyList<WorkItem> WorkItems,
    IReadOnlyList<Person> People,
    IReadOnlyList<ActionItem> ActionItems,
    IReadOnlyList<Decision> Decisions,
    IReadOnlyList<Blocker> Blockers,
    IReadOnlyList<CodeEvent> CodeEvents,
    IReadOnlySet<string> ExistingFingerprints);

/// <summary>
/// Compares what was said (decisions, actions, blockers), what was planned (tickets) and what was done (code)
/// and proposes a Suggestion only where they differ. Pure: no I/O.
/// </summary>
public static partial class ReconciliationPolicy
{
    public const double LinkThreshold = 0.6;

    [GeneratedRegex(@"\b(drop|defer|descope|de-scope|postpone|push|move|cut|park|out of)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DeferRegex();

    [GeneratedRegex(@"\b(cancel|scrap|kill|abandon|won't do|not doing)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CancelRegex();

    [GeneratedRegex(@"\bv(\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReleaseRegex();

    public static List<Suggestion> Evaluate(ReconciliationSnapshot s)
    {
        var result = new List<Suggestion>();
        var seen = new HashSet<string>(s.ExistingFingerprints);
        var byId = s.WorkItems.ToDictionary(w => w.Id);
        var byRef = s.WorkItems
            .Where(w => !string.IsNullOrEmpty(w.ExternalRef))
            .GroupBy(w => w.ExternalRef, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var peopleById = s.People.ToDictionary(p => p.Id);

        void Add(Suggestion sg)
        {
            if (!seen.Add(sg.Fingerprint)) return;
            sg.ProjectId = s.ProjectId;
            sg.CreatedAt = s.Now;
            result.Add(sg);
        }

        // 1. Verbal tasks with no ticket -> link to a similar ticket, or draft a new one (B2).
        foreach (var a in s.ActionItems.Where(a => a.State is ActionItemState.Proposed or ActionItemState.Accepted && a.LinkedWorkItemId is null))
        {
            var best = s.WorkItems
                .Where(w => w.Status.IsOpen())
                .Select(w => (w, score: TextSimilarity.Score(a.Description, w.Title)))
                .OrderByDescending(x => x.score)
                .FirstOrDefault();

            if (best.w is not null && best.score >= LinkThreshold)
            {
                Add(new Suggestion
                {
                    Type = SuggestionType.LinkConversation,
                    Fingerprint = $"link:{a.Id}:{best.w.Id}",
                    Title = $"Link \"{Trim(a.Description)}\" to {best.w.ExternalRef}?",
                    Detail = $"The action item sounds like ticket {best.w.ExternalRef} \"{best.w.Title}\" (match {best.score * 100:0}%).",
                    WorkItemId = best.w.Id,
                    ActionItemId = a.Id,
                    ProposedOwnerId = best.w.OwnerId is null ? a.OwnerId : null,
                    ProposedDueDate = a.DueDate,
                    EvidenceId = a.EvidenceId,
                    Confidence = Math.Round(best.score, 2)
                });
                continue;
            }

            var owner = a.OwnerId is { } oid && peopleById.TryGetValue(oid, out var p) ? p : null;
            var due = a.DueDate is { } d ? $", due {d.ToString("ddd d MMM", CultureInfo.InvariantCulture)}" : "";
            Add(new Suggestion
            {
                Type = SuggestionType.CreateTicket,
                Fingerprint = $"create:{a.Id}",
                Title = $"Create ticket: {Trim(a.Description)}",
                Detail = $"Promised verbally with no ticket. Owner {(owner?.Name ?? (a.OwnerName.Length > 0 ? a.OwnerName : "unassigned"))}{due}.",
                ActionItemId = a.Id,
                ProposedTitle = CleanTitle(a.Description),
                ProposedOwnerId = a.OwnerId,
                ProposedDueDate = a.DueDate,
                ProposedRole = owner?.Role,
                ProposedRelease = s.CurrentRelease,
                EvidenceId = a.EvidenceId,
                Confidence = 0.8
            });
        }

        // 2. Decisions that contradict ticket scope (drop / defer / cancel).
        foreach (var dec in s.Decisions)
        {
            if (dec.AffectedWorkItemId is not { } wid || !byId.TryGetValue(wid, out var w) || !w.Status.IsOpen()) continue;

            if (CancelRegex().IsMatch(dec.Statement))
            {
                Add(new Suggestion
                {
                    Type = SuggestionType.UpdateStatus,
                    Fingerprint = $"cancel:{dec.Id}:{w.Id}",
                    Title = $"Cancel {w.ExternalRef}?",
                    Detail = $"Decided: \"{dec.Statement}\"",
                    WorkItemId = w.Id,
                    DecisionId = dec.Id,
                    ProposedStatus = WorkItemStatus.Cancelled,
                    EvidenceId = dec.EvidenceId,
                    Confidence = 0.75
                });
            }
            else if (DeferRegex().IsMatch(dec.Statement) && string.Equals(w.Release, s.CurrentRelease, StringComparison.OrdinalIgnoreCase))
            {
                var target = NextRelease(dec.Statement, w.Release);
                Add(new Suggestion
                {
                    Type = SuggestionType.MoveRelease,
                    Fingerprint = $"move:{dec.Id}:{w.Id}",
                    Title = $"Move {w.ExternalRef} to {target}?",
                    Detail = $"Decided {dec.DecidedAt:dd MMM HH:mm}: \"{dec.Statement}\" but the ticket is still in {w.Release}.",
                    WorkItemId = w.Id,
                    DecisionId = dec.Id,
                    ProposedRelease = target,
                    EvidenceId = dec.EvidenceId,
                    Confidence = 0.8
                });
            }
        }

        // 3. Code says otherwise (C1/C2): merged PR -> close; commits on a not-started ticket -> in progress.
        foreach (var ev in s.CodeEvents.OrderBy(e => e.OccurredAt))
        {
            foreach (var r in ev.Refs())
            {
                if (!byRef.TryGetValue(r, out var w) || !w.Status.IsOpen()) continue;

                if (ev.Type == CodeEventType.PullRequestMerged || ev.Type == CodeEventType.Deployed)
                {
                    Add(new Suggestion
                    {
                        Type = SuggestionType.CloseTicket,
                        Fingerprint = $"close:{w.Id}:{ev.Id}",
                        Title = $"Close {w.ExternalRef}?",
                        Detail = $"Ticket still says {w.Status}, but \"{ev.Title}\" was {(ev.Type == CodeEventType.Deployed ? "deployed" : "merged")} {Ago(s.Now, ev.OccurredAt)} by {ev.Author}.",
                        WorkItemId = w.Id,
                        CodeEventId = ev.Id,
                        ProposedStatus = WorkItemStatus.Done,
                        Confidence = 0.9
                    });
                }
                else if (ev.Type is CodeEventType.Commit or CodeEventType.PullRequestOpened &&
                         w.Status is WorkItemStatus.Todo or WorkItemStatus.Backlog)
                {
                    Add(new Suggestion
                    {
                        Type = SuggestionType.UpdateStatus,
                        Fingerprint = $"start:{w.Id}",
                        Title = $"Move {w.ExternalRef} to In progress?",
                        Detail = $"Code activity \"{ev.Title}\" by {ev.Author} {Ago(s.Now, ev.OccurredAt)}, but the ticket says {w.Status}.",
                        WorkItemId = w.Id,
                        CodeEventId = ev.Id,
                        ProposedStatus = WorkItemStatus.InProgress,
                        Confidence = 0.85
                    });
                }
            }
        }

        // 4. Blockers raised in conversation but the board does not show them.
        foreach (var b in s.Blockers.Where(b => b.ResolvedAt is null && b.BlockingWorkItemId is not null))
        {
            if (!byId.TryGetValue(b.BlockingWorkItemId!.Value, out var w) || !w.Status.IsOpen() || w.Status == WorkItemStatus.Blocked) continue;
            Add(new Suggestion
            {
                Type = SuggestionType.UpdateStatus,
                Fingerprint = $"block:{b.Id}",
                Title = $"Mark {w.ExternalRef} as Blocked?",
                Detail = $"{(b.RaisedByName.Length > 0 ? b.RaisedByName : "Someone")} said: \"{Trim(b.Description)}\"",
                WorkItemId = w.Id,
                BlockerId = b.Id,
                ProposedStatus = WorkItemStatus.Blocked,
                EvidenceId = b.EvidenceId,
                Confidence = 0.7
            });
        }

        // 5. Stale tickets (B3) - re-flagged once per week.
        var week = ISOWeek.GetWeekOfYear(s.Now.UtcDateTime);
        foreach (var w in s.WorkItems.Where(w => w.Status is WorkItemStatus.InProgress or WorkItemStatus.InReview or WorkItemStatus.Blocked))
        {
            if (!TicketQuality.IsStale(w, s.Now, s.StaleAfterDays)) continue;
            var recentCode = s.CodeEvents.Any(e => e.Refs().Contains(w.ExternalRef, StringComparer.OrdinalIgnoreCase) &&
                                                   (s.Now - e.OccurredAt).TotalDays <= s.StaleAfterDays);
            if (recentCode) continue;
            var days = (int)(s.Now - w.LastActivityAt).TotalDays;
            var owner = w.OwnerId is { } o && peopleById.TryGetValue(o, out var op) ? op.Name : "nobody";
            Add(new Suggestion
            {
                Type = SuggestionType.StaleTicket,
                Fingerprint = $"stale:{w.Id}:{s.Now.Year}-{week}",
                Title = $"{w.ExternalRef} says {w.Status} but nothing happened for {days} days",
                Detail = $"Owner: {owner}. Is it done, abandoned or blocked? Pick a status to keep the board honest.",
                WorkItemId = w.Id,
                ProposedStatus = w.Status == WorkItemStatus.Blocked ? WorkItemStatus.Backlog : WorkItemStatus.Blocked,
                Confidence = 0.6
            });
        }

        // 6. Unowned tickets where the conversation names an owner.
        foreach (var a in s.ActionItems.Where(a => a.LinkedWorkItemId is not null && a.OwnerId is not null))
        {
            if (!byId.TryGetValue(a.LinkedWorkItemId!.Value, out var w) || w.OwnerId is not null || !w.Status.IsOpen()) continue;
            var name = peopleById.TryGetValue(a.OwnerId!.Value, out var p) ? p.Name : a.OwnerName;
            Add(new Suggestion
            {
                Type = SuggestionType.AssignOwner,
                Fingerprint = $"assign:{w.Id}:{a.OwnerId}",
                Title = $"Assign {w.ExternalRef} to {name}?",
                Detail = $"{name} took it on in a meeting: \"{Trim(a.Description)}\"",
                WorkItemId = w.Id,
                ActionItemId = a.Id,
                ProposedOwnerId = a.OwnerId,
                EvidenceId = a.EvidenceId,
                Confidence = 0.75
            });
        }

        // 7. Missing estimates - propose the team's historic median for that role.
        var medians = s.WorkItems
            .Where(w => w.Status == WorkItemStatus.Done && w.ActualDays > 0)
            .GroupBy(w => w.Role)
            .ToDictionary(g => g.Key, g => Median(g.Select(w => w.ActualDays!.Value)));
        foreach (var w in s.WorkItems.Where(w => w.Status is WorkItemStatus.Todo or WorkItemStatus.InProgress && w.EstimateDays is null))
        {
            var est = medians.TryGetValue(w.Role, out var m) ? Math.Round(m * 2) / 2 : 3;
            Add(new Suggestion
            {
                Type = SuggestionType.AddEstimate,
                Fingerprint = $"estimate:{w.Id}",
                Title = $"{w.ExternalRef} has no estimate. Use {est} days?",
                Detail = $"Median actual for past {w.Role} tickets is {est} days. Forecasts treat missing estimates as a guess.",
                WorkItemId = w.Id,
                ProposedEstimateDays = est,
                Confidence = 0.5
            });
        }

        return result;
    }

    private static string NextRelease(string statement, string current)
    {
        var mentioned = ReleaseRegex().Matches(statement).Select(m => int.Parse(m.Groups[1].Value)).ToList();
        var cur = ReleaseRegex().Match(current) is { Success: true } c ? int.Parse(c.Groups[1].Value) : 1;
        var higher = mentioned.Where(v => v > cur).DefaultIfEmpty(cur + 1).Min();
        return $"v{higher}";
    }

    private static double Median(IEnumerable<double> values)
    {
        var a = values.OrderBy(v => v).ToArray();
        if (a.Length == 0) return 0;
        return a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2;
    }

    private static string Trim(string s) => s.Length <= 90 ? s : s[..87] + "...";

    private static string CleanTitle(string s)
    {
        var t = Regex.Replace(s, @"^(i'?ll|i will|we'?ll|we will|i can|let me|i'm going to|i am going to)\s+", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"\s+(by|before|until)\s+(eod|end of day|tomorrow|today|next week|monday|tuesday|wednesday|thursday|friday)\b.*$", "", RegexOptions.IgnoreCase);
        t = t.TrimEnd('.', '!', ' ');
        return t.Length == 0 ? s : char.ToUpperInvariant(t[0]) + t[1..];
    }

    private static string Ago(DateTimeOffset now, DateTimeOffset at)
    {
        var d = now - at;
        if (d.TotalHours < 1) return "just now";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours}h ago";
        if (d.TotalDays < 2) return "yesterday";
        return $"{(int)d.TotalDays} days ago";
    }
}
