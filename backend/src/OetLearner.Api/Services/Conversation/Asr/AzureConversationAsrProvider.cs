using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Conversation.Asr;

/// <summary>
/// Azure Speech-to-Text (REST short audio API) for conversation turns. Uses
/// the "recognize once" endpoint which suits push-to-talk turn chunks up to
/// ~60 s. Longer-form should migrate to the Azure Speech SDK (streaming).
/// </summary>
public sealed class AzureConversationAsrProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<ConversationOptions> options,
    ILogger<AzureConversationAsrProvider> logger) : IConversationAsrProvider
{
    private readonly ConversationOptions _options = options.Value;

    public string Name => "azure";
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.AzureSpeechKey) &&
        !string.IsNullOrWhiteSpace(_options.AzureSpeechRegion);

    public async Task<ConversationAsrResult> TranscribeAsync(ConversationAsrRequest request, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Azure Speech provider is not configured.");

        var client = httpClientFactory.CreateClient("ConversationAzureClient");
        var locale = request.Locale ?? _options.AzureLocale ?? "en-GB";
        var url = $"https://{_options.AzureSpeechRegion}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1?language={Uri.EscapeDataString(locale)}&format=detailed";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("Ocp-Apim-Subscription-Key", _options.AzureSpeechKey);
        req.Headers.Add("Accept", "application/json");
        var content = new StreamContent(request.Audio);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(MapContentType(request.AudioMimeType));
        req.Content = content;

        using var response = await client.SendAsync(req, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Azure ASR returned {Status}: {Body}", (int)response.StatusCode, body);
            throw new ConversationAsrException("azure_error", $"Azure STT returned {(int)response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var status = root.TryGetProperty("RecognitionStatus", out var rs) ? rs.GetString() : "Unknown";
        if (!string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase))
        {
            return new ConversationAsrResult(
                Text: "",
                Confidence: 0.0,
                DurationMs: 0,
                Language: locale,
                ProviderName: Name,
                ProviderResponseSummary: $"azure: status={status}");
        }

        var text = root.TryGetProperty("DisplayText", out var dt) ? (dt.GetString() ?? "") : "";
        double confidence = 0.85;
        int durationMs = 0;
        if (root.TryGetProperty("NBest", out var nb) && nb.ValueKind == JsonValueKind.Array && nb.GetArrayLength() > 0)
        {
            var top = nb[0];
            if (top.TryGetProperty("Confidence", out var c) && c.ValueKind == JsonValueKind.Number)
                confidence = c.GetDouble();
            if (string.IsNullOrEmpty(text) && top.TryGetProperty("Display", out var dp))
                text = dp.GetString() ?? "";
        }
        if (root.TryGetProperty("Duration", out var dur) && dur.ValueKind == JsonValueKind.Number)
        {
            // Azure returns duration in 100-ns ticks.
            durationMs = (int)(dur.GetInt64() / 10000);
        }

        return new ConversationAsrResult(
            Text: text.Trim(),
            Confidence: confidence,
            DurationMs: durationMs,
            Language: locale,
            ProviderName: Name,
            ProviderResponseSummary: $"azure: {text.Length} chars, status={status}");
    }

    private static string MapContentType(string mime) => mime switch
    {
        "audio/wav" or "audio/x-wav" => "audio/wav; codecs=audio/pcm; samplerate=16000",
        "audio/ogg" => "audio/ogg; codecs=opus",
        "audio/webm" => "audio/webm; codecs=opus",
        _ => mime,
    };
}
