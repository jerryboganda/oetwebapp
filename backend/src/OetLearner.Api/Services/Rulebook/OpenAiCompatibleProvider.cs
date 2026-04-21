using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Generic OpenAI-compatible provider. Intended for DigitalOcean Serverless
/// Inference and other vendors that expose `/chat/completions` semantics.
///
/// This provider does NOT know anything about OET rulebooks or scoring. It
/// only transmits already-grounded prompts assembled by AiGatewayService.
/// Grounding is enforced one layer above.
/// </summary>
public sealed class OpenAiCompatibleProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<AiProviderOptions> options) : IAiModelProvider
{
    private readonly AiProviderOptions _options = options.Value;

    public string Name => string.IsNullOrWhiteSpace(_options.ProviderId) ? "openai-compatible" : _options.ProviderId;

    public async Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
    {
        var baseUrl = string.IsNullOrWhiteSpace(request.BaseUrlOverride)
            ? _options.BaseUrl : request.BaseUrlOverride;
        var apiKey = string.IsNullOrWhiteSpace(request.ApiKeyOverride)
            ? _options.ApiKey : request.ApiKeyOverride;

        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException($"{AiProviderOptions.SectionName}:BaseUrl is not configured.");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"{AiProviderOptions.SectionName}:ApiKey is not configured.");

        var client = httpClientFactory.CreateClient("AiOpenAiCompatible");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var model = string.IsNullOrWhiteSpace(request.Model) ? _options.DefaultModel : request.Model;
        var maxTokens = request.MaxTokens ?? _options.DefaultMaxTokens;
        var reasoningEffort = (_options.ReasoningEffort ?? "high").Trim().ToLowerInvariant();
        var sendReasoning = IsReasoningCapable(model) && !string.IsNullOrWhiteSpace(reasoningEffort);

        object payload = sendReasoning
            ? new
            {
                model,
                messages = new object[]
                {
                    new { role = "system", content = request.SystemPrompt },
                    new { role = "user", content = request.UserPrompt },
                },
                temperature = request.Temperature,
                max_tokens = maxTokens,
                reasoning_effort = reasoningEffort,
                stream = false,
            }
            : new
            {
                model,
                messages = new object[]
                {
                    new { role = "system", content = request.SystemPrompt },
                    new { role = "user", content = request.UserPrompt },
                },
                temperature = request.Temperature,
                max_tokens = maxTokens,
                stream = false,
            };

        using var response = await client.PostAsync(
            "chat/completions",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"AI provider call failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var choice = root.GetProperty("choices")[0];
        var message = choice.GetProperty("message");
        var text = ReadMessageContent(message);

        var usage = root.TryGetProperty("usage", out var usageEl)
            ? new AiUsage
            {
                PromptTokens = usageEl.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0,
                CompletionTokens = usageEl.TryGetProperty("completion_tokens", out var ctEl) ? ctEl.GetInt32() : 0,
            }
            : null;

        return new AiProviderCompletion { Text = text, Usage = usage };
    }

    private static string ReadMessageContent(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content)) return "";

        if (content.ValueKind == JsonValueKind.String) return content.GetString() ?? "";

        if (content.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var item in content.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    parts.Add(item.GetString() ?? "");
                    continue;
                }
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("text", out var textEl))
                {
                    parts.Add(textEl.GetString() ?? "");
                }
            }
            return string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        return content.ToString();
    }

    /// <summary>
    /// Returns true for models that accept the <c>reasoning_effort</c>
    /// parameter (Anthropic Claude 4+ extended thinking and OpenAI o-series).
    /// Unknown models fall through as false so we don't send an unsupported
    /// parameter that would 400.
    /// </summary>
    private static bool IsReasoningCapable(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) return false;
        var m = model.ToLowerInvariant();
        if (m.Contains("claude-opus") || m.Contains("claude-4") || m.Contains("claude-5")) return true;
        if (m.Contains("opus-4")) return true;
        if (m.Contains("openai-o1") || m.Contains("openai-o3") || m.Contains("openai-o4")) return true;
        if (m.StartsWith("o1") || m.StartsWith("o3") || m.StartsWith("o4")) return true;
        if (m.Contains("gpt-5")) return true;
        if (m.Contains("thinking")) return true;
        return false;
    }
}
