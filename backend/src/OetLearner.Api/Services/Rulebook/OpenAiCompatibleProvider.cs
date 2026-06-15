using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Settings;

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
    IOptions<AiProviderOptions> options,
    IRuntimeSettingsProvider settingsProvider) : IAiModelProvider
{
    private readonly AiProviderOptions _options = options.Value;

    public string Name => string.IsNullOrWhiteSpace(_options.ProviderId) ? "openai-compatible" : _options.ProviderId;

    public async Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
    {
        // DB-over-env gateway knobs (admin-configurable). The provider API key
        // stays env/registry-managed via AiProviderOptions — never sourced from
        // RuntimeSettings.
        var gateway = (await settingsProvider.GetAsync(ct)).AiGateway;

        var baseUrl = string.IsNullOrWhiteSpace(request.BaseUrlOverride)
            ? gateway.BaseUrl : request.BaseUrlOverride;
        var apiKey = string.IsNullOrWhiteSpace(request.ApiKeyOverride)
            ? _options.ApiKey : request.ApiKeyOverride;

        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException($"{AiProviderOptions.SectionName}:BaseUrl is not configured.");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"{AiProviderOptions.SectionName}:ApiKey is not configured.");
        var unsafeBaseUrlReason = AiProviderConnectionTester.GetUnsafeBaseUrlReason(baseUrl);
        if (unsafeBaseUrlReason is not null)
            throw new InvalidOperationException(unsafeBaseUrlReason);

        var client = httpClientFactory.CreateClient("AiOpenAiCompatible");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var model = string.IsNullOrWhiteSpace(request.Model) ? gateway.DefaultModel : request.Model;
        var maxTokens = request.MaxTokens ?? gateway.DefaultMaxTokens;
        if (request.AudioAttachments is { Count: > 0 } && IsTranscriptionModel(model))
        {
            return await TranscribeAudioAsync(client, model, request, ct);
        }

        // Empty ReasoningEffort means "non-reasoning model" — honour it rather
        // than forcing "high" so glm-5 etc. don't get an unsupported parameter.
        var reasoningEffort = (gateway.ReasoningEffort ?? string.Empty).Trim().ToLowerInvariant();
        var sendReasoning = IsReasoningCapable(model) && !string.IsNullOrWhiteSpace(reasoningEffort);

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = AiProviderPayloadBuilder.BuildOpenAiMessages(request),
            ["temperature"] = request.Temperature,
            ["max_tokens"] = maxTokens,
            ["stream"] = false,
        };
        if (sendReasoning)
        {
            payload["reasoning_effort"] = reasoningEffort;
        }
        var tools = AiProviderPayloadBuilder.BuildOpenAiTools(request.Tools);
        if (tools.Count > 0)
        {
            payload["tools"] = tools;
            if (!string.IsNullOrWhiteSpace(request.ToolChoice)) payload["tool_choice"] = request.ToolChoice;
        }

        using var response = await client.PostAsync(
            "chat/completions",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(AiProviderErrorMessages.HttpFailure("AI provider", (int)response.StatusCode, response.ReasonPhrase));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        AiProviderPayloadBuilder.ReadOpenAiChoiceMessage(root, "AI provider", out var choice, out var message);
        var text = AiProviderPayloadBuilder.ReadOpenAiMessageContent(message);
        var toolCalls = AiProviderPayloadBuilder.ReadOpenAiToolCalls(message);

        var usage = root.TryGetProperty("usage", out var usageEl)
            ? new AiUsage
            {
                PromptTokens = usageEl.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0,
                CompletionTokens = usageEl.TryGetProperty("completion_tokens", out var ctEl) ? ctEl.GetInt32() : 0,
            }
            : null;

        var finishReason = choice.TryGetProperty("finish_reason", out var finish) ? finish.GetString() : null;
        return new AiProviderCompletion { Text = text, Usage = usage, ToolCalls = toolCalls, FinishReason = finishReason };
    }

    private static async Task<AiProviderCompletion> TranscribeAudioAsync(HttpClient client, string model, AiProviderRequest request, CancellationToken ct)
    {
        var audio = request.AudioAttachments?.FirstOrDefault(attachment => attachment.Data.Length > 0);
        if (audio is null)
            throw new InvalidOperationException("OpenAI audio transcription requires a non-empty audio attachment.");

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(model), "model");
        if (!string.IsNullOrWhiteSpace(request.UserPrompt))
        {
            content.Add(new StringContent(request.UserPrompt), "prompt");
        }
        content.Add(new StringContent("text"), "response_format");
        content.Add(new StringContent(request.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)), "temperature");

        var fileContent = new ByteArrayContent(audio.Data);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(audio.MimeType) ? "application/octet-stream" : audio.MimeType);
        content.Add(fileContent, "file", FileNameForMimeType(audio.MimeType));

        using var response = await client.PostAsync("audio/transcriptions", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(AiProviderErrorMessages.HttpFailure("AI transcription provider", (int)response.StatusCode, response.ReasonPhrase));

        var text = body.Trim();
        if (text.StartsWith('{'))
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("text", out var textElement))
            {
                text = textElement.GetString()?.Trim() ?? string.Empty;
            }
        }

        return new AiProviderCompletion { Text = text };
    }

    private static bool IsTranscriptionModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) return false;
        var normalized = model.Trim().ToLowerInvariant();
        return normalized.StartsWith("whisper", StringComparison.Ordinal)
            || normalized.Contains("transcribe", StringComparison.Ordinal);
    }

    private static string FileNameForMimeType(string mimeType)
        => mimeType.ToLowerInvariant() switch
        {
            "audio/mpeg" => "recording.mp3",
            "audio/mp4" => "recording.m4a",
            "video/mp4" => "recording.mp4",
            "audio/ogg" => "recording.ogg",
            "audio/wav" => "recording.wav",
            "audio/webm" => "recording.webm",
            _ => "recording.bin",
        };

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
