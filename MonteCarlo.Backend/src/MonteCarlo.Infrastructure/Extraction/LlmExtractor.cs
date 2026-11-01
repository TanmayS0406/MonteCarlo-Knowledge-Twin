using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using MonteCarlo.Application.Abstractions;
using MonteCarlo.Domain.Entities;

namespace MonteCarlo.Infrastructure.Extraction;

/// <summary>LLM extraction with a strict JSON schema. Falls back to the rule-based extractor on any failure.</summary>
public sealed class LlmExtractor(LlmClient client, RuleBasedExtractor fallback, ILogger<LlmExtractor> log) : ILlmExtractor
{
    public const string PromptVersion = "extract-v3";
    public string Name => client.IsConfigured ? $"LLM ({client.ModelName})" : fallback.Name;

    private const string System = """
        You extract project knowledge from a software team's meeting transcript.
        Return ONLY items that were actually said. Never invent owners, dates or tickets.
        - decisions: something the team agreed or decided (scope, approach, priority). Not opinions.
        - actions: a concrete task someone committed to or was asked to do. owner = the person who will do it
          ("I'll" means the speaker). dueDate as yyyy-MM-dd if a date or weekday was said, resolved against the meeting date, else null.
        - blockers: something stopping work (waiting for, blocked by, no access).
        quote = the exact words from the transcript. turn = the [number] of the turn it came from.
        ticketRef = a ticket key like ABC-123 if mentioned or clearly matching a known open ticket, else null.
        confidence = 0..1, how sure you are this is a real item.
        """;

    public async Task<ExtractionResult> ExtractAsync(IReadOnlyList<SpeakerTurn> turns, ExtractionContext ctx, CancellationToken ct)
    {
        if (!client.IsConfigured) return await fallback.ExtractAsync(turns, ctx, ct);
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"Meeting date: {ctx.MeetingDate:yyyy-MM-dd} ({ctx.MeetingDate.DayOfWeek}). Project key: {ctx.ProjectKey}.");
            sb.AppendLine("Team: " + string.Join(", ", ctx.KnownPeople));
            sb.AppendLine("Open tickets:");
            foreach (var t in ctx.OpenTickets.Take(80)) sb.AppendLine($"- {t.Ref}: {t.Title}");
            sb.AppendLine().AppendLine("Transcript:");
            foreach (var t in turns) sb.AppendLine(CultureInfo.InvariantCulture, $"[{t.Sequence}] ({TimeSpan.FromSeconds(t.OffsetSeconds):hh\\:mm\\:ss}) {t.SpeakerName}: {t.Text}");

            var json = await client.CompleteAsync(System, sb.ToString(), Schema(), "meeting_extraction", ct);
            var root = JsonNode.Parse(json)!.AsObject();

            static string? S(JsonNode? n) => n is null || n.GetValueKind() == JsonValueKind.Null ? null : n.GetValue<string>();
            static double D(JsonNode? n) => n is null ? 0.5 : Math.Clamp(n.GetValue<double>(), 0, 1);
            static int I(JsonNode? n) => n is null ? 0 : n.GetValue<int>();
            static DateOnly? Date(JsonNode? n) => DateOnly.TryParseExact(S(n), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

            var decisions = root["decisions"]!.AsArray().Select(x => new ExtractedDecision(
                S(x!["statement"]) ?? "", S(x["decidedBy"]), S(x["rationale"]), S(x["ticketRef"]), S(x["quote"]) ?? "", I(x["turn"]), D(x["confidence"]))).ToList();
            var actions = root["actions"]!.AsArray().Select(x => new ExtractedAction(
                S(x!["description"]) ?? "", S(x["owner"]), Date(x["dueDate"]), S(x["ticketRef"]), S(x["quote"]) ?? "", I(x["turn"]), D(x["confidence"]))).ToList();
            var blockers = root["blockers"]!.AsArray().Select(x => new ExtractedBlocker(
                S(x!["description"]) ?? "", S(x["raisedBy"]), S(x["ticketRef"]), S(x["quote"]) ?? "", I(x["turn"]), D(x["confidence"]))).ToList();

            return new ExtractionResult(decisions, actions, blockers, client.ModelName, PromptVersion);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "LLM extraction failed, falling back to rule-based extraction");
            var r = await fallback.ExtractAsync(turns, ctx, ct);
            return r with { Model = $"fallback:{r.Model}" };
        }
    }

    private static JsonObject Schema()
    {
        JsonObject Str(bool nullable = false) => nullable
            ? new JsonObject { ["type"] = new JsonArray("string", "null") }
            : new JsonObject { ["type"] = "string" };
        JsonObject Obj(params (string Name, JsonObject Type)[] props)
        {
            var p = new JsonObject();
            foreach (var (n, t) in props) p[n] = t;
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = p,
                ["required"] = new JsonArray(props.Select(x => (JsonNode)JsonValue.Create(x.Name)!).ToArray()),
                ["additionalProperties"] = false
            };
        }
        JsonObject Arr(JsonObject item) => new() { ["type"] = "array", ["items"] = item };
        JsonObject Num() => new() { ["type"] = "number" };
        JsonObject Int() => new() { ["type"] = "integer" };

        return Obj(
            ("decisions", Arr(Obj(("statement", Str()), ("decidedBy", Str(true)), ("rationale", Str(true)), ("ticketRef", Str(true)),
                ("quote", Str()), ("turn", Int()), ("confidence", Num())))),
            ("actions", Arr(Obj(("description", Str()), ("owner", Str(true)), ("dueDate", Str(true)), ("ticketRef", Str(true)),
                ("quote", Str()), ("turn", Int()), ("confidence", Num())))),
            ("blockers", Arr(Obj(("description", Str()), ("raisedBy", Str(true)), ("ticketRef", Str(true)),
                ("quote", Str()), ("turn", Int()), ("confidence", Num())))));
    }
}
