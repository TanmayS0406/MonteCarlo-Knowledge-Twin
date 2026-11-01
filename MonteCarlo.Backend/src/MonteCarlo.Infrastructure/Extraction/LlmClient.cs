using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace MonteCarlo.Infrastructure.Extraction;

public sealed class LlmOptions
{
    /// <summary>RuleBased (default, offline), OpenAI (or any OpenAI-compatible endpoint), AzureOpenAI.</summary>
    public string Provider { get; set; } = "RuleBased";
    public string Endpoint { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o-mini";
    public string AzureDeployment { get; set; } = "";
    public string ApiVersion { get; set; } = "2024-10-21";
    public double Temperature { get; set; } = 0;

    public bool IsLlm => !Provider.Equals("RuleBased", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>Minimal chat-completions client for OpenAI and Azure OpenAI with strict JSON schema output.</summary>
public sealed class LlmClient(HttpClient http, IOptions<LlmOptions> options)
{
    private readonly LlmOptions _o = options.Value;

    public bool IsConfigured => _o.IsLlm;
    public string ModelName => _o.Provider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase) ? _o.AzureDeployment : _o.Model;

    public async Task<string> CompleteAsync(string system, string user, JsonObject? jsonSchema, string schemaName, CancellationToken ct)
    {
        var azure = _o.Provider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase);
        var url = azure
            ? $"{_o.Endpoint.TrimEnd('/')}/openai/deployments/{_o.AzureDeployment}/chat/completions?api-version={_o.ApiVersion}"
            : $"{_o.Endpoint.TrimEnd('/')}/chat/completions";

        var body = new JsonObject
        {
            ["temperature"] = _o.Temperature,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user }
            }
        };
        if (!azure) body["model"] = _o.Model;
        if (jsonSchema is not null)
        {
            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = schemaName, ["strict"] = true, ["schema"] = jsonSchema }
            };
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (azure) req.Headers.Add("api-key", _o.ApiKey);
        else req.Headers.Authorization = new("Bearer", _o.ApiKey);

        using var res = await http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"LLM call failed ({(int)res.StatusCode}): {text[..Math.Min(400, text.Length)]}");

        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }
}
