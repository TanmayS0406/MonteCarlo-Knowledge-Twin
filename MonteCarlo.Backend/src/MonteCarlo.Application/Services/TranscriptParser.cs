using System.Globalization;
using System.Text.RegularExpressions;

namespace MonteCarlo.Application.Services;

public sealed record ParsedTurn(int Sequence, int OffsetSeconds, string Speaker, string Text);

/// <summary>
/// Normalises a transcript into speaker turns. Supports Teams WebVTT (&lt;v Name&gt;text&lt;/v&gt;),
/// "[00:12:30] Name: text", "00:12:30 Name: text", "Name (00:12): text" and plain "Name: text".
/// </summary>
public static partial class TranscriptParser
{
    [GeneratedRegex(@"^(?<start>\d{1,2}:\d{2}(?::\d{2})?(?:[.,]\d+)?)\s*-->\s*\S+")]
    private static partial Regex VttCue();

    [GeneratedRegex(@"^<v\s+(?<name>[^>]+)>(?<text>.*?)(?:</v>)?$")]
    private static partial Regex VttVoice();

    [GeneratedRegex(@"^\[?(?<ts>\d{1,2}:\d{2}(?::\d{2})?)\]?\s*[-–]?\s*(?<name>[^:]{1,60}?):\s*(?<text>.+)$")]
    private static partial Regex TimestampFirst();

    [GeneratedRegex(@"^(?<name>[^:(\[]{1,60}?)\s*[\(\[](?<ts>\d{1,2}:\d{2}(?::\d{2})?)[\)\]]\s*:?\s*(?<text>.+)$")]
    private static partial Regex NameThenTimestamp();

    [GeneratedRegex(@"^(?<name>[A-Za-z][A-Za-z .'\-]{0,59}):\s*(?<text>.+)$")]
    private static partial Regex NameOnly();

    public static List<ParsedTurn> Parse(string raw)
    {
        var lines = (raw ?? "").Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).ToList();
        var turns = lines.Any(l => l.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase) || VttCue().IsMatch(l))
            ? ParseVtt(lines)
            : ParsePlain(lines);
        return Merge(turns);
    }

    private static List<ParsedTurn> ParseVtt(List<string> lines)
    {
        var result = new List<ParsedTurn>();
        var offset = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0 || line.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase) || line.StartsWith("NOTE")) continue;
            var cue = VttCue().Match(line);
            if (cue.Success) { offset = ToSeconds(cue.Groups["start"].Value); continue; }
            if (Guid.TryParse(line, out _) || Regex.IsMatch(line, @"^(\d+|[0-9a-f]{8}-[0-9a-f\-/]+)$")) continue; // cue ids

            var v = VttVoice().Match(line);
            if (v.Success)
            {
                result.Add(new ParsedTurn(0, offset, v.Groups["name"].Value.Trim(), StripTags(v.Groups["text"].Value)));
                continue;
            }
            var n = NameOnly().Match(line);
            if (n.Success) result.Add(new ParsedTurn(0, offset, n.Groups["name"].Value.Trim(), StripTags(n.Groups["text"].Value)));
            else if (result.Count > 0)
            {
                var last = result[^1];
                result[^1] = last with { Text = (last.Text + " " + StripTags(line)).Trim() };
            }
        }
        return result;
    }

    private static List<ParsedTurn> ParsePlain(List<string> lines)
    {
        var result = new List<ParsedTurn>();
        var lastOffset = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0) continue;
            Match m;
            if ((m = TimestampFirst().Match(line)).Success || (m = NameThenTimestamp().Match(line)).Success)
            {
                lastOffset = ToSeconds(m.Groups["ts"].Value);
                result.Add(new ParsedTurn(0, lastOffset, m.Groups["name"].Value.Trim(), m.Groups["text"].Value.Trim()));
            }
            else if ((m = NameOnly().Match(line)).Success)
            {
                lastOffset = result.Count == 0 ? 0 : lastOffset + 20; // no timestamps: assume ~20s per turn
                result.Add(new ParsedTurn(0, lastOffset, m.Groups["name"].Value.Trim(), m.Groups["text"].Value.Trim()));
            }
            else if (result.Count > 0)
            {
                var last = result[^1];
                result[^1] = last with { Text = (last.Text + " " + line).Trim() };
            }
            else
            {
                result.Add(new ParsedTurn(0, 0, "Unknown", line));
            }
        }
        return result;
    }

    /// <summary>Joins consecutive lines from the same speaker within 30 seconds and numbers turns.</summary>
    private static List<ParsedTurn> Merge(List<ParsedTurn> turns)
    {
        var merged = new List<ParsedTurn>();
        foreach (var t in turns.Where(t => !string.IsNullOrWhiteSpace(t.Text)))
        {
            if (merged.Count > 0 && merged[^1].Speaker == t.Speaker && t.OffsetSeconds - merged[^1].OffsetSeconds <= 30 && merged[^1].Text.Length < 600)
                merged[^1] = merged[^1] with { Text = merged[^1].Text + " " + t.Text };
            else
                merged.Add(t);
        }
        return merged.Select((t, i) => t with { Sequence = i + 1 }).ToList();
    }

    private static string StripTags(string s) => Regex.Replace(s, "<[^>]+>", "").Trim();

    public static int ToSeconds(string ts)
    {
        ts = ts.Replace(',', '.');
        var parts = ts.Split(':');
        double total = 0;
        foreach (var p in parts) total = total * 60 + double.Parse(p, CultureInfo.InvariantCulture);
        return (int)total;
    }

    public static string Format(int? seconds)
    {
        if (seconds is null) return "";
        var t = TimeSpan.FromSeconds(seconds.Value);
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }
}
