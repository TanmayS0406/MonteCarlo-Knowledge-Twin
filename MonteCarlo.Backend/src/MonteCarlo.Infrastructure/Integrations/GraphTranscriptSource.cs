using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonteCarlo.Application.Abstractions;

namespace MonteCarlo.Infrastructure.Integrations;

public sealed class GraphOptions
{
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    /// <summary>Organizer user id used for /users/{id}/onlineMeetings (application access policy).</summary>
    public string UserId { get; set; } = "";
    /// <summary>Public HTTPS URL of /api/webhooks/graph. Needed to create the transcript subscription.</summary>
    public string NotificationUrl { get; set; } = "";
    public string ClientState { get; set; } = "monte-carlo-twin";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId) &&
                                !string.IsNullOrWhiteSpace(ClientSecret) && !string.IsNullOrWhiteSpace(UserId);
}

public sealed class GraphAccessException(string message) : Exception(message);

/// <summary>
/// Microsoft Graph client for meeting transcripts (OnlineMeetingTranscript.Read.All or RSC OnlineMeetingTranscript.Read.Chat).
/// </summary>
public sealed class GraphTranscriptSource(HttpClient http, IOptions<GraphOptions> options, ILogger<GraphTranscriptSource> log) : ITranscriptSource
{
    private readonly GraphOptions _o = options.Value;
    private string? _token;
    private DateTimeOffset _tokenExpires;
    private string? _subscriptionId;
    private DateTimeOffset _subscriptionExpires;

    public bool IsConfigured => _o.IsConfigured;

    private async Task<string> TokenAsync(CancellationToken ct)
    {
        if (_token is not null && _tokenExpires > DateTimeOffset.UtcNow.AddMinutes(2)) return _token;
        using var res = await http.PostAsync($"https://login.microsoftonline.com/{_o.TenantId}/oauth2/v2.0/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _o.ClientId,
                ["client_secret"] = _o.ClientSecret,
                ["scope"] = "https://graph.microsoft.com/.default",
                ["grant_type"] = "client_credentials"
            }), ct);
        res.EnsureSuccessStatusCode();
        var node = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct))!;
        _token = node["access_token"]!.GetValue<string>();
        _tokenExpires = DateTimeOffset.UtcNow.AddSeconds(node["expires_in"]!.GetValue<int>());
        return _token;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        var req = new HttpRequestMessage(method, url.StartsWith("http") ? url : $"https://graph.microsoft.com/v1.0{url}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(ct));
        if (body is not null) req.Content = JsonContent.Create(body);
        var res = await http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.Forbidden)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            if (text.Contains("GraphAccessToTranscriptsDisabled", StringComparison.OrdinalIgnoreCase))
                throw new GraphAccessException("The tenant admin has disabled Graph access to transcripts. Use manual transcript upload instead.");
            throw new GraphAccessException($"Graph refused access (403). Check admin consent / RSC permissions. {text[..Math.Min(200, text.Length)]}");
        }
        return res;
    }

    public async Task<Transcript> GetAsync(string meetingId, string transcriptId, CancellationToken ct)
    {
        var title = "Teams meeting";
        var started = DateTimeOffset.UtcNow;
        using (var meta = await SendAsync(HttpMethod.Get, $"/users/{_o.UserId}/onlineMeetings/{meetingId}", null, ct))
        {
            if (meta.IsSuccessStatusCode)
            {
                var node = JsonNode.Parse(await meta.Content.ReadAsStringAsync(ct));
                title = node?["subject"]?.GetValue<string>() ?? title;
                if (DateTimeOffset.TryParse(node?["startDateTime"]?.GetValue<string>(), out var s)) started = s;
            }
        }

        using var res = await SendAsync(HttpMethod.Get,
            $"/users/{_o.UserId}/onlineMeetings/{meetingId}/transcripts/{transcriptId}/content?$format=text/vtt", null, ct);
        res.EnsureSuccessStatusCode();
        return new Transcript(meetingId, transcriptId, title, started, await res.Content.ReadAsStringAsync(ct));
    }

    /// <summary>
    /// Creates or renews the tenant-wide transcript subscription. Notifications only arrive if the subscription
    /// exists before transcription starts, and subscriptions over 1 hour need a lifecycle URL, so renew regularly.
    /// </summary>
    public async Task EnsureSubscriptionAsync(CancellationToken ct)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(_o.NotificationUrl)) return;
        var expires = DateTimeOffset.UtcNow.AddHours(23);
        if (_subscriptionId is not null && _subscriptionExpires > DateTimeOffset.UtcNow.AddHours(2)) return;

        if (_subscriptionId is not null)
        {
            using var renew = await SendAsync(HttpMethod.Patch, $"/subscriptions/{_subscriptionId}",
                new { expirationDateTime = expires.UtcDateTime.ToString("o") }, ct);
            if (renew.IsSuccessStatusCode) { _subscriptionExpires = expires; return; }
            _subscriptionId = null;
        }

        using var res = await SendAsync(HttpMethod.Post, "/subscriptions", new
        {
            changeType = "created",
            notificationUrl = _o.NotificationUrl,
            lifecycleNotificationUrl = _o.NotificationUrl,
            resource = "communications/onlineMeetings/getAllTranscripts",
            includeResourceData = false,
            expirationDateTime = expires.UtcDateTime.ToString("o"),
            clientState = _o.ClientState
        }, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) { log.LogWarning("Graph subscription failed: {Body}", text); return; }
        _subscriptionId = JsonNode.Parse(text)?["id"]?.GetValue<string>();
        _subscriptionExpires = expires;
        log.LogInformation("Graph transcript subscription {Id} active until {Expires}", _subscriptionId, expires);
    }
}
