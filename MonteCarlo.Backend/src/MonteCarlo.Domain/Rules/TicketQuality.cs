using MonteCarlo.Domain.Common;
using MonteCarlo.Domain.Entities;

namespace MonteCarlo.Domain.Rules;

public sealed record QualityReport(int Score, IReadOnlyList<string> Issues);

/// <summary>Scores a ticket for missing estimate, acceptance criteria, owner and freshness (option B6).</summary>
public static class TicketQuality
{
    public static QualityReport Evaluate(WorkItem item, DateTimeOffset now, int staleAfterDays)
    {
        var issues = new List<string>();
        var score = 100;

        if (item.EstimateDays is null or <= 0) { score -= 25; issues.Add("Missing estimate"); }
        if (item.OwnerId is null && item.Status.IsOpen()) { score -= 25; issues.Add("No owner"); }
        if (string.IsNullOrWhiteSpace(item.AcceptanceCriteria)) { score -= 20; issues.Add("No acceptance criteria"); }
        if (string.IsNullOrWhiteSpace(item.Description) || item.Description.Length < 20) { score -= 10; issues.Add("Thin description"); }
        if (item.Status is WorkItemStatus.InProgress or WorkItemStatus.InReview &&
            (now - item.LastActivityAt).TotalDays > staleAfterDays)
        {
            score -= 20;
            issues.Add($"No activity for {(int)(now - item.LastActivityAt).TotalDays} days");
        }

        return new QualityReport(Math.Max(0, score), issues);
    }

    public static bool IsStale(WorkItem item, DateTimeOffset now, int staleAfterDays) =>
        item.Status.IsOpen() && item.Status != WorkItemStatus.Backlog &&
        (now - item.LastActivityAt).TotalDays > staleAfterDays;
}
