using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using MonteCarlo.Application.Abstractions;

namespace MonteCarlo.Infrastructure.Extraction;

/// <summary>
/// Turns a baseline-vs-scenario comparison into plain language, always citing the numbers it used.
/// Uses the LLM when configured; the template version is always produced first and used as the fallback.
/// </summary>
public sealed class Narrator(LlmClient client, ILogger<Narrator> log) : IInsightNarrator
{
    public async Task<string> ExplainAsync(NarrativeInput input, CancellationToken ct)
    {
        var template = Template(input);
        if (!client.IsConfigured) return template;
        try
        {
            const string system = "You explain project forecast results to a team lead in 3-5 short sentences. " +
                                  "Use only the numbers given. Cite them exactly. No hype, no new facts. End with the best option if one is given.";
            var text = await client.CompleteAsync(system, template, null, "", ct);
            return string.IsNullOrWhiteSpace(text) ? template : text.Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "LLM narration failed, using template");
            return template;
        }
    }

    private static string Pct(double v) => $"{Math.Round(v * 100):0}%";

    public static string Template(NarrativeInput input)
    {
        var c = input.Comparison;
        var b = c.Baseline;
        var s = c.Scenario;
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        if (input.Changes.Count == 0)
        {
            sb.Append(inv, $"Baseline forecast: 50% chance to finish by {b.P50:dd MMM}, 80% by {b.P80:dd MMM}, 95% by {b.P95:dd MMM}. ");
            if (c.Deadline is { } dl) sb.Append(inv, $"Chance of meeting the {dl:dd MMM} deadline is {Pct(b.ProbabilityOnTime)}. ");
        }
        else
        {
            sb.Append(inv, $"Scenario \"{input.ScenarioName}\": {string.Join("; ", input.Changes)}. ");
            var dir = c.DeltaP80Days > 0 ? "later" : "earlier";
            if (c.DeltaP80Days == 0)
                sb.Append(inv, $"The P80 finish date stays at {s.P80:dd MMM}. ");
            else
                sb.Append(inv, $"The P80 finish moves {Math.Abs(c.DeltaP80Days)} working days {dir}, from {b.P80:dd MMM} to {s.P80:dd MMM} (P50 {b.P50:dd MMM} -> {s.P50:dd MMM}). ");
            if (c.Deadline is { } dl)
                sb.Append(inv, $"Chance of meeting the {dl:dd MMM} deadline goes from {Pct(b.ProbabilityOnTime)} to {Pct(s.ProbabilityOnTime)}. ");
            sb.Append(inv, $"With the same luck in every run, the scenario changes the finish by {c.MeanPairedDeltaDays:+0.0;-0.0;0} days on average. ");
        }

        sb.Append(inv, $"The bottleneck is {c.BottleneckRole} work. ");
        var critical = s.Tasks.Where(t => t.OnCriticalPath).OrderBy(t => t.P50Day).Select(t => t.Key).ToList();
        if (critical.Count > 0) sb.Append(inv, $"Critical path: {string.Join(" -> ", critical)}. ");
        if (s.UnfinishedRuns > 0) sb.Append(inv, $"Warning: {s.UnfinishedRuns} runs never finished (missing skills or blocked work). ");

        if (input.Mitigation is { } m)
        {
            sb.Append(m.RecoveredDays > 0
                ? string.Create(inv, $"Option: {m.Description} recovers {m.RecoveredDays} of the {m.LostDays} lost days (P80 {m.P80:dd MMM}, {Pct(m.ProbabilityOnTime)} on time).")
                : string.Create(inv, $"Hiring does not help here ({m.Description} recovers 0 days); consider cutting scope instead."));
        }
        return sb.ToString().Trim();
    }
}
