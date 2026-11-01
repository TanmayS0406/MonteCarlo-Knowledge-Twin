using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonteCarlo.Application.Abstractions;
using MonteCarlo.Domain.Common;
using MonteCarlo.Infrastructure.Persistence;

namespace MonteCarlo.Infrastructure.Integrations;

/// <summary>The built-in board: the database is the ticket system. Only generates keys.</summary>
public sealed class LocalTicketSystem(AppDbContext db) : ITicketSystem
{
    public string Name => "Local board";
    public bool IsExternal => false;

    public Task<IReadOnlyList<ExternalTicket>> GetOpenItemsAsync(string projectKey, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ExternalTicket>>([]);

    public async Task<string> CreateAsync(WorkItemDraft draft, CancellationToken ct)
    {
        var prefix = draft.ProjectKey + "-";
        var refs = await db.WorkItemSet.Where(w => w.ExternalRef.StartsWith(prefix)).Select(w => w.ExternalRef).ToListAsync(ct);
        var pendingRefs = db.ChangeTracker.Entries<Domain.Entities.WorkItem>().Select(e => e.Entity.ExternalRef).Where(r => r.StartsWith(prefix));
        var max = refs.Concat(pendingRefs).Select(r => int.TryParse(r[prefix.Length..], out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return $"{prefix}{max + 1}";
    }

    public Task UpdateStatusAsync(string externalId, WorkItemStatus status, CancellationToken ct) => Task.CompletedTask;
    public Task UpdateFieldsAsync(string externalId, TicketFieldUpdate update, CancellationToken ct) => Task.CompletedTask;
}

public sealed class JiraOptions
{
    public string BaseUrl { get; set; } = "";
    public string Email { get; set; } = "";
    public string ApiToken { get; set; } = "";
    public string IssueType { get; set; } = "Task";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiToken);
}

/// <summary>Jira Cloud adapter (REST v3). Accepted suggestions are written back here.</summary>
public sealed class JiraTicketSystem(HttpClient http, IOptions<JiraOptions> options, ILogger<JiraTicketSystem> log) : ITicketSystem
{
    private readonly JiraOptions _o = options.Value;
    public string Name => "Jira";
    public bool IsExternal => true;

    private HttpRequestMessage Req(HttpMethod m, string path, object? body = null)
    {
        var r = new HttpRequestMessage(m, $"{_o.BaseUrl.TrimEnd('/')}{path}");
        r.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_o.Email}:{_o.ApiToken}")));
        r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) r.Content = JsonContent.Create(body);
        return r;
    }

    private async Task<JsonNode?> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var res = await http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"Jira {(int)res.StatusCode}: {text[..Math.Min(300, text.Length)]}");
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    public async Task<IReadOnlyList<ExternalTicket>> GetOpenItemsAsync(string projectKey, CancellationToken ct)
    {
        var jql = Uri.EscapeDataString($"project = {projectKey} AND statusCategory != Done ORDER BY updated DESC");
        var node = await SendAsync(Req(HttpMethod.Get,
            $"/rest/api/3/search/jql?jql={jql}&maxResults=200&fields=summary,description,status,assignee,updated,fixVersions,timeoriginalestimate"), ct);
        var list = new List<ExternalTicket>();
        foreach (var issue in node?["issues"]?.AsArray() ?? [])
        {
            var f = issue!["fields"]!;
            var est = f["timeoriginalestimate"]?.GetValue<double?>();
            list.Add(new ExternalTicket(
                issue["key"]!.GetValue<string>(),
                f["summary"]?.GetValue<string>() ?? "",
                f["description"]?.ToJsonString() is { } d && d.Length < 4000 ? AdfToText(f["description"]) : "",
                MapStatus(f["status"]?["name"]?.GetValue<string>(), f["status"]?["statusCategory"]?["key"]?.GetValue<string>()),
                f["assignee"]?["emailAddress"]?.GetValue<string>(),
                est is > 0 ? est / 3600 / 8 : null,
                f["fixVersions"]?.AsArray().FirstOrDefault()?["name"]?.GetValue<string>(),
                DateTimeOffset.TryParse(f["updated"]?.GetValue<string>(), out var u) ? u : DateTimeOffset.UtcNow));
        }
        return list;
    }

    public async Task<string> CreateAsync(WorkItemDraft draft, CancellationToken ct)
    {
        var fields = new JsonObject
        {
            ["project"] = new JsonObject { ["key"] = draft.ProjectKey },
            ["summary"] = draft.Title,
            ["issuetype"] = new JsonObject { ["name"] = _o.IssueType },
            ["description"] = Adf(draft.Description)
        };
        if (draft.DueDate is { } due) fields["duedate"] = due.ToString("yyyy-MM-dd");
        if (draft.EstimateDays is { } e) fields["timetracking"] = new JsonObject { ["originalEstimate"] = $"{e}d" };
        if (await AccountIdAsync(draft.AssigneeEmail, ct) is { } acc) fields["assignee"] = new JsonObject { ["accountId"] = acc };
        var node = await SendAsync(Req(HttpMethod.Post, "/rest/api/3/issue", new JsonObject { ["fields"] = fields }), ct);
        return node!["key"]!.GetValue<string>();
    }

    public async Task UpdateStatusAsync(string externalId, WorkItemStatus status, CancellationToken ct)
    {
        var node = await SendAsync(Req(HttpMethod.Get, $"/rest/api/3/issue/{externalId}/transitions"), ct);
        var wanted = status switch
        {
            WorkItemStatus.Done => new[] { "done", "closed", "resolved" },
            WorkItemStatus.InProgress => ["in progress", "start progress"],
            WorkItemStatus.InReview => ["in review", "review"],
            WorkItemStatus.Blocked => ["blocked", "on hold"],
            WorkItemStatus.Cancelled => ["cancelled", "won't do", "rejected"],
            _ => ["to do", "backlog", "open"]
        };
        var t = node?["transitions"]?.AsArray().FirstOrDefault(x =>
            wanted.Contains((x!["to"]?["name"]?.GetValue<string>() ?? "").ToLowerInvariant()) ||
            wanted.Contains((x["name"]?.GetValue<string>() ?? "").ToLowerInvariant()));
        if (t is null) { log.LogWarning("No Jira transition to {Status} for {Key}", status, externalId); return; }
        await SendAsync(Req(HttpMethod.Post, $"/rest/api/3/issue/{externalId}/transitions",
            new JsonObject { ["transition"] = new JsonObject { ["id"] = t["id"]!.GetValue<string>() } }), ct);
    }

    public async Task UpdateFieldsAsync(string externalId, TicketFieldUpdate update, CancellationToken ct)
    {
        var fields = new JsonObject();
        if (update.Release is { } r) fields["fixVersions"] = new JsonArray(new JsonObject { ["name"] = r });
        if (update.EstimateDays is { } e) fields["timetracking"] = new JsonObject { ["originalEstimate"] = $"{e}d" };
        if (await AccountIdAsync(update.AssigneeEmail, ct) is { } acc) fields["assignee"] = new JsonObject { ["accountId"] = acc };
        if (fields.Count == 0) return;
        await SendAsync(Req(HttpMethod.Put, $"/rest/api/3/issue/{externalId}", new JsonObject { ["fields"] = fields }), ct);
    }

    private async Task<string?> AccountIdAsync(string? email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var node = await SendAsync(Req(HttpMethod.Get, $"/rest/api/3/user/search?query={Uri.EscapeDataString(email)}"), ct);
        return node?.AsArray().FirstOrDefault()?["accountId"]?.GetValue<string>();
    }

    private static WorkItemStatus MapStatus(string? name, string? category) => (name ?? "").ToLowerInvariant() switch
    {
        "blocked" or "on hold" => WorkItemStatus.Blocked,
        "in review" or "code review" or "review" => WorkItemStatus.InReview,
        "backlog" => WorkItemStatus.Backlog,
        _ => category switch { "indeterminate" => WorkItemStatus.InProgress, "done" => WorkItemStatus.Done, _ => WorkItemStatus.Todo }
    };

    private static JsonObject Adf(string text) => new()
    {
        ["type"] = "doc",
        ["version"] = 1,
        ["content"] = new JsonArray(new JsonObject
        {
            ["type"] = "paragraph",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = string.IsNullOrWhiteSpace(text) ? "-" : text })
        })
    };

    private static string AdfToText(JsonNode? node)
    {
        if (node is null) return "";
        var sb = new StringBuilder();
        void Walk(JsonNode n)
        {
            if (n is JsonObject o)
            {
                if (o["text"] is JsonValue v && v.GetValueKind() == JsonValueKind.String) sb.Append(v.GetValue<string>()).Append(' ');
                if (o["content"] is JsonArray a) foreach (var c in a) if (c is not null) Walk(c);
            }
        }
        Walk(node);
        return sb.ToString().Trim();
    }
}
