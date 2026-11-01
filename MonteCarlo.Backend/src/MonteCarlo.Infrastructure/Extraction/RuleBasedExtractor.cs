using System.Globalization;
using System.Text.RegularExpressions;
using MonteCarlo.Application.Abstractions;
using MonteCarlo.Domain.Common;
using MonteCarlo.Domain.Entities;

namespace MonteCarlo.Infrastructure.Extraction;

/// <summary>
/// Offline extractor based on language patterns. Works with no API key, so the product runs locally out of the box.
/// An LLM extractor (OpenAI / Azure OpenAI) replaces it when configured.
/// </summary>
public sealed partial class RuleBasedExtractor : ILlmExtractor
{
    public const string Version = "rules-1.2";
    public string Name => "Rule-based (offline)";

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z""'])")]
    private static partial Regex SentenceSplit();

    [GeneratedRegex(@",\s*(?=(?:and\s+)?(?:i'll|i will|i can|i'm going to|we'll|we will)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex ClauseSplit();

    [GeneratedRegex(@"\b(we('ve| have)? decided|decision is|we agreed|we('re| are) going with|we('ll| will) go with|let'?s (drop|defer|move|go with|use|keep|cut|postpone|skip|switch|ship|park|descope|stick with|not do)|we('ll| will) (drop|defer|postpone|cut|skip|park)|final call)\b", RegexOptions.IgnoreCase)]
    private static partial Regex StrongDecision();

    [GeneratedRegex(@"^(agreed|ok(ay)?,? (let's|we'll)|sounds good,? (let's|we'll)|fine,? (let's|we'll))\b", RegexOptions.IgnoreCase)]
    private static partial Regex WeakDecision();

    [GeneratedRegex(@"\b(?<lead>i'll|i will|i can|i'm going to|i am going to|let me|i'll take|i can take)\s+(?<rest>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SelfCommit();

    [GeneratedRegex(@"^(?<name>[A-Z][a-z]+),?\s+(can you|could you|please)\s+(?<rest>.+?)\??$")]
    private static partial Regex Request();

    [GeneratedRegex(@"\b(?<name>[A-Z][a-z]+)\s+(will|is going to|to)\s+(?<rest>(fix|build|write|update|take|own|handle|finish|review|deploy|create|add|look into|investigate|send|prepare|check)\b.+)$")]
    private static partial Regex ThirdPartyCommit();

    [GeneratedRegex(@"\b(blocked|blocker|waiting (for|on)|stuck|can'?t (proceed|continue|move forward)|cannot (proceed|continue)|held up|no access|dependency on)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BlockerPattern();

    [GeneratedRegex(@"\b(because|since|as)\s+(?<why>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex Rationale();

    [GeneratedRegex(@"\b(by|before|until|on)\s+(?<when>(end of (the )?(day|week))|eod|eow|today|tomorrow|next week|monday|tuesday|wednesday|thursday|friday|saturday|sunday|\d{1,2}(st|nd|rd|th)?\s+[A-Za-z]{3,9}|[A-Za-z]{3,9}\s+\d{1,2}(st|nd|rd|th)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DuePattern();

    public Task<ExtractionResult> ExtractAsync(IReadOnlyList<SpeakerTurn> turns, ExtractionContext ctx, CancellationToken ct)
    {
        var decisions = new List<ExtractedDecision>();
        var actions = new List<ExtractedAction>();
        var blockers = new List<ExtractedBlocker>();

        foreach (var turn in turns)
        {
            foreach (var sentence in SentenceSplit().Split(turn.Text).Select(s => s.Trim()).Where(s => s.Length > 3))
            {
                var refs = TextSimilarity.FindTicketRefs(sentence);
                var ticketRef = refs.FirstOrDefault();

                // Decisions
                if (StrongDecision().IsMatch(sentence) || WeakDecision().IsMatch(sentence))
                {
                    var strong = StrongDecision().IsMatch(sentence);
                    var why = Rationale().Match(sentence);
                    decisions.Add(new ExtractedDecision(
                        Clean(sentence), turn.SpeakerName, why.Success ? why.Groups["why"].Value.TrimEnd('.') : null, ticketRef,
                        sentence, turn.Sequence, strong ? (sentence.Contains("decided", StringComparison.OrdinalIgnoreCase) ? 0.9 : 0.85) : 0.5));
                    continue;
                }

                // Blockers
                if (BlockerPattern().IsMatch(sentence) && !sentence.TrimEnd().EndsWith('?'))
                {
                    blockers.Add(new ExtractedBlocker(Clean(sentence), turn.SpeakerName, ticketRef, sentence, turn.Sequence, 0.75));
                    continue;
                }

                // Action items (a sentence can contain several commitments)
                foreach (var clause in ClauseSplit().Split(sentence).Where(c => c.Length > 3))
                {
                    string? owner = null;
                    string? rest = null;
                    double conf = 0;
                    Match m;
                    if ((m = SelfCommit().Match(clause)).Success)
                    {
                        owner = turn.SpeakerName;
                        rest = m.Groups["rest"].Value;
                        if (m.Groups["lead"].Value.Contains("take", StringComparison.OrdinalIgnoreCase)) rest = "Take " + rest;
                        conf = 0.75;
                    }
                    else if ((m = Request().Match(clause)).Success && ctx.KnownPeople.Any(p => p.StartsWith(m.Groups["name"].Value, StringComparison.OrdinalIgnoreCase)))
                    {
                        owner = ctx.KnownPeople.First(p => p.StartsWith(m.Groups["name"].Value, StringComparison.OrdinalIgnoreCase));
                        rest = m.Groups["rest"].Value;
                        conf = 0.6;
                    }
                    else if ((m = ThirdPartyCommit().Match(clause)).Success && ctx.KnownPeople.Any(p => p.StartsWith(m.Groups["name"].Value, StringComparison.OrdinalIgnoreCase)))
                    {
                        owner = ctx.KnownPeople.First(p => p.StartsWith(m.Groups["name"].Value, StringComparison.OrdinalIgnoreCase));
                        rest = m.Groups["rest"].Value;
                        conf = 0.7;
                    }
                    if (rest is null) continue;

                    rest = rest.Trim().TrimEnd('.', '!', '?', ',');
                    if (TextSimilarity.Tokens(rest).Count < 2) continue; // too vague ("start on it")
                    if (Regex.IsMatch(rest, @"^(think|guess|believe|hope|need|want|see|check if|try to remember)\b", RegexOptions.IgnoreCase)) continue;

                    var due = ParseDue(clause, ctx.MeetingDate);
                    if (due is not null) conf += 0.15;
                    actions.Add(new ExtractedAction(char.ToUpperInvariant(rest[0]) + rest[1..], owner, due,
                        TextSimilarity.FindTicketRefs(clause).FirstOrDefault() ?? ticketRef, clause, turn.Sequence, Math.Min(0.95, conf)));
                }
            }
        }

        return Task.FromResult(new ExtractionResult(decisions, actions, blockers, "pattern-matcher", Version));
    }

    private static string Clean(string s) => s.Trim().TrimEnd('.', '!');

    public static DateOnly? ParseDue(string text, DateOnly meetingDate)
    {
        var m = DuePattern().Match(text);
        if (!m.Success) return null;
        var when = m.Groups["when"].Value.ToLowerInvariant().Trim();

        DateOnly Next(DayOfWeek dow, bool allowToday = false)
        {
            var d = allowToday ? meetingDate : meetingDate.AddDays(1);
            while (d.DayOfWeek != dow) d = d.AddDays(1);
            return d;
        }

        switch (when)
        {
            case "today" or "eod" or "end of day" or "end of the day": return meetingDate;
            case "tomorrow": return meetingDate.AddDays(1);
            case "eow" or "end of week" or "end of the week": return Next(DayOfWeek.Friday, true);
            case "next week": return Next(DayOfWeek.Monday).AddDays(4);
        }
        if (Enum.TryParse<DayOfWeek>(when, true, out var dow)) return Next(dow);

        var cleaned = Regex.Replace(when, @"(\d)(st|nd|rd|th)", "$1");
        string[] formats = ["d MMM", "d MMMM", "MMM d", "MMMM d"];
        if (DateTime.TryParseExact(cleaned, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt))
        {
            var d = new DateOnly(meetingDate.Year, dt.Month, dt.Day);
            return d < meetingDate.AddDays(-7) ? d.AddYears(1) : d;
        }
        return null;
    }
}
