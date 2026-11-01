using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using MonteCarlo.Application.Abstractions;
using MonteCarlo.Domain.Common;

namespace MonteCarlo.Infrastructure.Integrations;

public sealed class GitHubOptions
{
    public string Owner { get; set; } = "";
    public string Repo { get; set; } = "";
    public string Token { get; set; } = "";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Owner) && !string.IsNullOrWhiteSpace(Repo);
}

/// <summary>Reads commits and pull requests. Ticket keys in titles/branches link them to work items.</summary>
public sealed class GitHubCodeActivitySource(HttpClient http, IOptions<GitHubOptions> options) : ICodeActivitySource
{
    private readonly GitHubOptions _o = options.Value;
    public string Name => $"GitHub {_o.Owner}/{_o.Repo}";
    public bool IsConfigured => _o.IsConfigured;

    private async Task<JsonArray> GetAsync(string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_o.Owner}/{_o.Repo}{path}");
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("MonteCarloKnowledgeTwin", "1.0"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrWhiteSpace(_o.Token)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _o.Token);
        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return JsonNode.Parse(await res.Content.ReadAsStringAsync(ct))?.AsArray() ?? [];
    }

    public async Task<IReadOnlyList<CodeEventData>> GetSinceAsync(DateTimeOffset since, CancellationToken ct)
    {
        if (!IsConfigured) return [];
        var events = new List<CodeEventData>();

        foreach (var c in await GetAsync($"/commits?since={Uri.EscapeDataString(since.UtcDateTime.ToString("o"))}&per_page=100", ct))
        {
            var msg = c!["commit"]?["message"]?.GetValue<string>() ?? "";
            events.Add(new CodeEventData(CodeEventType.Commit, c["sha"]!.GetValue<string>(), msg.Split('\n')[0],
                c["commit"]?["author"]?["name"]?.GetValue<string>() ?? "", "",
                DateTimeOffset.TryParse(c["commit"]?["author"]?["date"]?.GetValue<string>(), out var d) ? d : DateTimeOffset.UtcNow,
                c["html_url"]?.GetValue<string>() ?? ""));
        }

        foreach (var pr in await GetAsync("/pulls?state=all&sort=updated&direction=desc&per_page=50", ct))
        {
            var number = pr!["number"]!.GetValue<int>();
            var title = pr["title"]?.GetValue<string>() ?? "";
            var branch = pr["head"]?["ref"]?.GetValue<string>() ?? "";
            var user = pr["user"]?["login"]?.GetValue<string>() ?? "";
            var url = pr["html_url"]?.GetValue<string>() ?? "";
            if (DateTimeOffset.TryParse(pr["merged_at"]?.GetValue<string>(), out var merged) && merged >= since)
                events.Add(new CodeEventData(CodeEventType.PullRequestMerged, $"pr-{number}-merged", title, user, branch, merged, url));
            else if (DateTimeOffset.TryParse(pr["created_at"]?.GetValue<string>(), out var created) && created >= since)
                events.Add(new CodeEventData(CodeEventType.PullRequestOpened, $"pr-{number}-opened", title, user, branch, created, url));
        }
        return events;
    }
}

public sealed class NullCodeActivitySource : ICodeActivitySource
{
    public string Name => "None";
    public bool IsConfigured => false;
    public Task<IReadOnlyList<CodeEventData>> GetSinceAsync(DateTimeOffset since, CancellationToken ct) => Task.FromResult<IReadOnlyList<CodeEventData>>([]);
}
