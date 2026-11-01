using System.Text.RegularExpressions;

namespace MonteCarlo.Domain.Common;

/// <summary>Lightweight lexical similarity used to match spoken items to tickets.</summary>
public static partial class TextSimilarity
{
    private static readonly HashSet<string> StopWords =
    [
        "the", "a", "an", "and", "or", "to", "of", "for", "in", "on", "by", "with", "is", "it", "this", "that",
        "we", "i", "ill", "will", "be", "can", "you", "me", "my", "our", "us", "so", "do", "let", "lets",
        "fix", "add", "make", "take", "look", "at", "up", "from", "into", "should", "need", "needs", "just",
        "friday", "monday", "tuesday", "wednesday", "thursday", "today", "tomorrow", "week", "next", "end",
        "eod", "please", "also", "going", "gonna", "get", "got", "all", "are", "was", "has", "have", "its"
    ];

    [GeneratedRegex(@"\b[A-Z][A-Z0-9]+-\d+\b")]
    private static partial Regex TicketRefRegex();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex SplitRegex();

    public static IReadOnlyList<string> FindTicketRefs(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : TicketRefRegex().Matches(text).Select(m => m.Value).Distinct().ToList();

    public static HashSet<string> Tokens(string? text)
    {
        var set = new HashSet<string>();
        if (string.IsNullOrWhiteSpace(text)) return set;
        foreach (var raw in SplitRegex().Split(text.ToLowerInvariant()))
        {
            if (raw.Length < 2 || StopWords.Contains(raw)) continue;
            set.Add(Stem(raw));
        }
        return set;
    }

    private static string Stem(string w)
    {
        if (w.Length > 5 && w.EndsWith("ing")) return w[..^3];
        if (w.Length > 4 && w.EndsWith("es")) return w[..^2];
        if (w.Length > 3 && w.EndsWith('s')) return w[..^1];
        if (w.Length > 4 && w.EndsWith("ed")) return w[..^2];
        return w;
    }

    /// <summary>Overlap coefficient: shared tokens / smaller set. 0..1.</summary>
    public static double Score(string? a, string? b)
    {
        var ta = Tokens(a);
        var tb = Tokens(b);
        if (ta.Count == 0 || tb.Count == 0) return 0;
        var shared = ta.Count(tb.Contains);
        return (double)shared / Math.Min(ta.Count, tb.Count);
    }
}
