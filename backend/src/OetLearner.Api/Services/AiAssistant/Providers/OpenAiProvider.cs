using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OetLearner.Api.Services.AiAssistant.Providers;

/// <summary>
/// OpenAI chat completions provider with Server-Sent-Events streaming.
///
/// Credentials are resolved from configuration in this order:
///   1. <c>AiAssistant:Providers:OpenAi:ApiKey</c>
///   2. <c>OpenAI:ApiKey</c>
///   3. <c>OPENAI_API_KEY</c> environment variable.
/// Endpoint defaults to <c>https://api.openai.com</c>; override via
/// <c>AiAssistant:Providers:OpenAi:BaseUrl</c>.
///
/// NOTE: This provider is the chatbot-only path. The canonical OET grading
/// pipeline continues to flow through <c>IAiGatewayService</c>; the chatbot
/// will migrate to the gateway in Phase 1.5 once <c>RuleKind.Chatbot</c> is
/// added to the rulebook prompt builder. Until then, the per-call audit
/// trail lives in <c>AiUsageLog</c> (chatbot-specific) rather than
/// <c>AiUsageRecord</c> (gateway-managed).
/// </summary>
public sealed class OpenAiProvider : ILlmProvider
{
    public string ProviderKindKey => "OpenAi";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<OpenAiProvider> _logger;

    public OpenAiProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<OpenAiProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
    {
        // V1: return the curated short-list. The full GET /v1/models call
        // costs an API round-trip per page load; defer until the admin
        // model-picker UI demands it.
        IReadOnlyList<string> models = new[]
        {
            "gpt-4o-mini",
            "gpt-4o",
            "gpt-4.1-mini",
            "gpt-4.1",
            "o4-mini",
        };
        return Task.FromResult(models);
    }

    public async IAsyncEnumerable<ChatStreamDelta> StreamChatAsync(
        LlmChatRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OpenAI API key not configured. Set AiAssistant:Providers:OpenAi:ApiKey or OPENAI_API_KEY.");
        }

        var baseUrl = _config["AiAssistant:Providers:OpenAi:BaseUrl"] ?? "https://api.openai.com";
        var endpoint = baseUrl.TrimEnd('/') + "/v1/chat/completions";

        var payload = BuildPayload(request);
        var json = JsonSerializer.Serialize(payload);

        using var http = _httpClientFactory.CreateClient("AiAssistant.OpenAi");
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"OpenAI returned {(int)response.StatusCode}: {Truncate(body, 512)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        int promptTokens = 0;
        int completionTokens = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (line.Length == 0) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].TrimStart();
            if (data.Length == 0) continue;
            if (data == "[DONE]")
            {
                yield return new ChatStreamDelta
                {
                    IsFinal = true,
                    PromptTokens = promptTokens > 0 ? promptTokens : null,
                    CompletionTokens = completionTokens > 0 ? completionTokens : null,
                };
                yield break;
            }

            JsonDocument? doc = null;
            try { doc = JsonDocument.Parse(data); }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "OpenAI SSE non-JSON frame skipped: {Data}", Truncate(data, 128));
                continue;
            }
            using (doc)
            {
                if (doc.RootElement.TryGetProperty("usage", out var usage))
                {
                    if (usage.TryGetProperty("prompt_tokens", out var p)) promptTokens = p.GetInt32();
                    if (usage.TryGetProperty("completion_tokens", out var c)) completionTokens = c.GetInt32();
                }
                if (!doc.RootElement.TryGetProperty("choices", out var choices)
                    || choices.ValueKind != JsonValueKind.Array
                    || choices.GetArrayLength() == 0)
                {
                    continue;
                }
                var choice0 = choices[0];
                if (!choice0.TryGetProperty("delta", out var delta)) continue;
                if (delta.TryGetProperty("content", out var contentEl)
                    && contentEl.ValueKind == JsonValueKind.String)
                {
                    var text = contentEl.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        yield return new ChatStreamDelta { TextDelta = text };
                    }
                }
            }
        }

        yield return new ChatStreamDelta
        {
            IsFinal = true,
            PromptTokens = promptTokens > 0 ? promptTokens : null,
            CompletionTokens = completionTokens > 0 ? completionTokens : null,
        };
    }

    private object BuildPayload(LlmChatRequest request)
    {
        var msgs = new List<object>(request.Messages.Count);
        foreach (var m in request.Messages)
        {
            msgs.Add(new { role = m.Role, content = m.Content });
        }
        var dict = new Dictionary<string, object>
        {
            ["model"] = string.IsNullOrWhiteSpace(request.Model) ? "gpt-4o-mini" : request.Model,
            ["messages"] = msgs,
            ["stream"] = true,
            ["stream_options"] = new { include_usage = true },
        };
        if (request.Temperature is not null) dict["temperature"] = request.Temperature.Value;
        if (request.MaxTokens is not null) dict["max_tokens"] = request.MaxTokens.Value;
        return dict;
    }

    private string? ResolveApiKey()
    {
        return _config["AiAssistant:Providers:OpenAi:ApiKey"]
            ?? _config["OpenAI:ApiKey"]
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
