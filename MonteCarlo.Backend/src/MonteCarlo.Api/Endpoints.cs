using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using MonteCarlo.Application.Abstractions;
using MonteCarlo.Application.Dtos;
using MonteCarlo.Application.Services;
using MonteCarlo.Domain.Common;
using MonteCarlo.Infrastructure.Integrations;
using MonteCarlo.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace MonteCarlo.Api;

public static partial class Endpoints
{
    public static void MapApiEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/health", () => Results.Ok(new { status = "ok", at = DateTimeOffset.UtcNow }));

        // ---- Projects ----
        var projects = api.MapGroup("/projects").WithTags("Projects");
        projects.MapGet("/", (ProjectService s, CancellationToken ct) => s.ListAsync(ct));
        projects.MapPost("/", (UpsertProjectRequest r, ProjectService s, CancellationToken ct) => s.CreateAsync(r, ct));
        projects.MapPut("/{id:guid}", (Guid id, UpsertProjectRequest r, ProjectService s, CancellationToken ct) => s.UpdateAsync(id, r, ct));

        // ---- Dashboard & digest ----
        api.MapGet("/dashboard", (Guid? projectId, DashboardService s, CancellationToken ct) => s.GetAsync(projectId, ct)).WithTags("Dashboard");
        api.MapGet("/digest", (Guid? projectId, DashboardService s, CancellationToken ct) => s.GetDigestAsync(projectId, ct)).WithTags("Dashboard");

        // ---- People ----
        var people = api.MapGroup("/people").WithTags("People");
        people.MapGet("/", (PeopleService s, CancellationToken ct) => s.ListAsync(ct));
        people.MapPost("/", (UpsertPersonRequest r, PeopleService s, CancellationToken ct) => s.CreateAsync(r, ct));
        people.MapPut("/{id:guid}", (Guid id, UpsertPersonRequest r, PeopleService s, CancellationToken ct) => s.UpdateAsync(id, r, ct));
        people.MapDelete("/{id:guid}", async (Guid id, PeopleService s, CancellationToken ct) => { await s.DeleteAsync(id, ct); return Results.NoContent(); });

        // ---- Work items ----
        var items = api.MapGroup("/workitems").WithTags("Work items");
        items.MapGet("/", (Guid? projectId, WorkItemService s, CancellationToken ct) => s.ListAsync(projectId, ct));
        items.MapGet("/{id:guid}", (Guid id, WorkItemService s, CancellationToken ct) => s.GetAsync(id, ct));
        items.MapPost("/", (UpsertWorkItemRequest r, WorkItemService s, CancellationToken ct) => s.CreateAsync(r, ct));
        items.MapPut("/{id:guid}", (Guid id, UpsertWorkItemRequest r, WorkItemService s, CancellationToken ct) => s.UpdateAsync(id, r, ct));
        items.MapPost("/{id:guid}/status", (Guid id, ChangeStatusRequest r, WorkItemService s, CancellationToken ct) => s.ChangeStatusAsync(id, r, ct));
        items.MapDelete("/{id:guid}", async (Guid id, WorkItemService s, CancellationToken ct) => { await s.DeleteAsync(id, ct); return Results.NoContent(); });
        items.MapPost("/sync", async (Guid? projectId, WorkItemService s, CancellationToken ct) => Results.Ok(new { synced = await s.SyncAsync(projectId, ct) }));

        // ---- Meetings (capture) ----
        var meetings = api.MapGroup("/meetings").WithTags("Meetings");
        meetings.MapGet("/", (Guid? projectId, MeetingService s, CancellationToken ct) => s.ListAsync(projectId, ct));
        meetings.MapGet("/{id:guid}", (Guid id, MeetingService s, CancellationToken ct) => s.GetAsync(id, ct));
        meetings.MapPost("/", (IngestTranscriptRequest r, MeetingService s, CancellationToken ct) => s.IngestAsync(r, ct));
        meetings.MapPost("/{id:guid}/reprocess", (Guid id, MeetingService s, CancellationToken ct) => s.ReprocessAsync(id, ct));
        meetings.MapDelete("/{id:guid}", async (Guid id, MeetingService s, CancellationToken ct) => { await s.DeleteAsync(id, ct); return Results.NoContent(); });
        meetings.MapGet("/sample", () => Results.Text(DataSeeder.SampleTranscript, "text/plain"));

        // ---- Suggestions (review) ----
        var sugg = api.MapGroup("/suggestions").WithTags("Suggestions");
        sugg.MapGet("/", (Guid? projectId, SuggestionOutcome? outcome, SuggestionService s, CancellationToken ct) => s.ListAsync(projectId, outcome, ct));
        sugg.MapPost("/{id:guid}/accept", (Guid id, ReviewRequest r, SuggestionService s, CancellationToken ct) => s.AcceptAsync(id, r, ct));
        sugg.MapPost("/{id:guid}/reject", (Guid id, ReviewRequest r, SuggestionService s, CancellationToken ct) => s.RejectAsync(id, r, ct));
        api.MapPost("/reconcile", (Guid? projectId, ReconciliationService s, CancellationToken ct) => s.RunAsync(projectId, ct)).WithTags("Suggestions");

        // ---- Code activity ----
        var code = api.MapGroup("/code-events").WithTags("Code activity");
        code.MapGet("/", (Guid? projectId, CodeActivityService s, CancellationToken ct) => s.ListAsync(projectId, ct));
        code.MapPost("/", (CodeEventRequest r, CodeActivityService s, CancellationToken ct) => s.IngestAsync(r, true, ct));
        code.MapPost("/pull", async (Guid? projectId, CodeActivityService s, CancellationToken ct) => Results.Ok(new { added = await s.PullAsync(projectId, ct) }));

        // ---- Search ----
        api.MapGet("/search", (string q, Guid? projectId, SearchService s, CancellationToken ct) => s.SearchAsync(projectId, q, 25, ct)).WithTags("Search");

        // ---- Simulation ----
        var sim = api.MapGroup("/simulation").WithTags("Simulation");
        sim.MapGet("/model", (Guid? projectId, bool? allReleases, SimulationService s, CancellationToken ct) => s.GetModelAsync(projectId, allReleases ?? false, ct));
        sim.MapPost("/run", (RunSimulationRequest r, SimulationService s, CancellationToken ct) => s.RunAsync(r, ct));
        sim.MapPost("/montecarlo", (MonteCarloTraceRequest r, SimulationService s, CancellationToken ct) => s.TraceAsync(r, ct));
        sim.MapPost("/landscape", (LandscapeRequest r, SimulationService s, CancellationToken ct) => s.LandscapeAsync(r, ct));
        sim.MapGet("/runs", (Guid? projectId, SimulationService s, CancellationToken ct) => s.ListRunsAsync(projectId, ct));
        sim.MapGet("/runs/{id:guid}", (Guid id, SimulationService s, CancellationToken ct) => s.GetRunAsync(id, ct));
        sim.MapDelete("/runs/{id:guid}", async (Guid id, SimulationService s, CancellationToken ct) => { await s.DeleteRunAsync(id, ct); return Results.NoContent(); });

        // ---- Settings, privacy, audit ----
        var settings = api.MapGroup("/settings").WithTags("Settings");
        settings.MapGet("/", (SettingsService s, CancellationToken ct) => s.GetAsync(ct));
        settings.MapPut("/", (UpdateSettingsRequest r, SettingsService s, CancellationToken ct) => s.UpdateAsync(r, ct));
        settings.MapPost("/purge", async (SettingsService s, CancellationToken ct) => Results.Ok(new { purged = await s.PurgeRawTranscriptsAsync(ct) }));
        api.MapGet("/audit", (int? take, SettingsService s, CancellationToken ct) => s.AuditAsync(take ?? 100, ct)).WithTags("Settings");

        // ---- Webhooks ----
        var hooks = api.MapGroup("/webhooks").WithTags("Webhooks");
        hooks.MapPost("/graph", GraphWebhook);
        hooks.MapPost("/github", GitHubWebhook);
    }

    [GeneratedRegex(@"onlineMeetings\('?(?<meeting>[^'\)/]+)'?\)/transcripts\('?(?<transcript>[^'\)/]+)'?\)", RegexOptions.IgnoreCase)]
    private static partial Regex TranscriptResource();

    /// <summary>
    /// Microsoft Graph change notifications. Answers the validation handshake, then only queues work,
    /// so Graph gets a fast 202 and slow LLM calls happen in the background.
    /// </summary>
    private static async Task<IResult> GraphWebhook(HttpRequest req, [FromQuery] string? validationToken, IIngestionQueue queue, IOptions<GraphOptions> graph, ILogger<Program> log)
    {
        if (!string.IsNullOrEmpty(validationToken)) return Results.Text(validationToken, "text/plain");

        var body = await JsonNode.ParseAsync(req.Body);
        foreach (var n in body?["value"]?.AsArray() ?? [])
        {
            if (n?["clientState"]?.GetValue<string>() is { } state && state != graph.Value.ClientState)
            {
                log.LogWarning("Ignoring Graph notification with unexpected clientState");
                continue;
            }
            if (n?["lifecycleEvent"] is not null) continue; // renewal is handled by MaintenanceWorker
            var resource = n?["resource"]?.GetValue<string>() ?? "";
            var m = TranscriptResource().Match(resource);
            if (m.Success)
                await queue.EnqueueAsync(new IngestionJob("TeamsTranscript", m.Groups["meeting"].Value, m.Groups["transcript"].Value));
        }
        return Results.Accepted();
    }

    /// <summary>GitHub "push" and "pull_request" webhooks.</summary>
    private static async Task<IResult> GitHubWebhook(HttpRequest req, CodeActivityService code, CancellationToken ct)
    {
        var evt = req.Headers["X-GitHub-Event"].ToString();
        var body = await JsonNode.ParseAsync(req.Body, cancellationToken: ct);
        if (body is null) return Results.BadRequest();
        var added = 0;

        if (evt == "push")
        {
            var branch = (body["ref"]?.GetValue<string>() ?? "").Replace("refs/heads/", "");
            foreach (var c in body["commits"]?.AsArray() ?? [])
            {
                await code.IngestAsync(new CodeEventRequest(null, CodeEventType.Commit, c!["id"]?.GetValue<string>(),
                    (c["message"]?.GetValue<string>() ?? "").Split('\n')[0], c["author"]?["name"]?.GetValue<string>(), branch,
                    DateTimeOffset.TryParse(c["timestamp"]?.GetValue<string>(), out var t) ? t : null, c["url"]?.GetValue<string>()), false, ct);
                added++;
            }
        }
        else if (evt == "pull_request" && body["pull_request"] is { } pr)
        {
            var action = body["action"]?.GetValue<string>();
            var merged = pr["merged"]?.GetValue<bool>() == true;
            CodeEventType? type = action == "closed" && merged ? CodeEventType.PullRequestMerged : action == "opened" ? CodeEventType.PullRequestOpened : null;
            if (type is { } ty)
            {
                await code.IngestAsync(new CodeEventRequest(null, ty, $"pr-{pr["number"]}-{action}", pr["title"]?.GetValue<string>() ?? "",
                    pr["user"]?["login"]?.GetValue<string>(), pr["head"]?["ref"]?.GetValue<string>(), DateTimeOffset.UtcNow,
                    pr["html_url"]?.GetValue<string>()), false, ct);
                added++;
            }
        }

        if (added > 0)
        {
            var rec = req.HttpContext.RequestServices.GetRequiredService<ReconciliationService>();
            await rec.RunAsync(null, ct);
        }
        return Results.Ok(new { added });
    }
}
