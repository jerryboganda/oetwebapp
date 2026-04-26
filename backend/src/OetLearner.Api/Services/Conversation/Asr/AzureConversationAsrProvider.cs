using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OetLearner.Api.Services.Conversation.Asr;

public sealed class AzureConversationAsrProvider(
    IHttpClientFactory httpClientFactory,
    IConversationOptionsProvider optionsProvider,
    ILogger<AzureConversationAsrProvider> logger) : IConversationAsrProvider
{
    public string Name => "azure";
    public bool IsConfigured
    {
        get
        {
            var o = optionsProvider.Snapshot();
            return !string.IsNullOrWhiteSpace(o.AzureSpeechKey) && !string.IsNullOrWhiteSpace(o.AzureSpeechRegion);
        }
    }

    public async Task<ConversationAsrResult> TranscribeAsync(ConversationAsrRequest request, CancellationToken ct)
    {
        var options = await optionsProvider.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(options.AzureSpeechKey) || string.IsNullOrWhiteSpace(options.AzureSpeechRegion))
            throw new InvalidOperationException("Azure Speech not configured.");

        var client = httpClientFactory.CreateClient("ConversationAzureClient");
        var locale = request.Locale ?? options.AzureLocale ?? "en-GB";
        var url = $"https://{options.AzureSpeechRegion}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1?language={Uri.EscapeDataString(locale)}&format=detailed";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("Ocp-Apim-Subscription-Key", options.AzureSpeechKey);
        req.Headers.Add("Accept", "application/json");
        var content = new StreamContent(request.Audio);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(MapContentType(request.AudioMimeType));
        req.Content = content;

        using var response = await client.SendAsync(req, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Azure ASR {Status}: {Body}", (int)response.StatusCode, body);
            throw new ConversationAsrException("azure_error", $"Azure STT returned {(int)response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var status = root.TryGetProperty("RecognitionStatus", out var rs) ? rs.GetString() : "Unknown";
        if (!string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase))
            return new ConversationAsrResult("", 0.0, 0, locale, Name, $"azure status={status}");

        var text = root.TryGetProperty("DisplayText", out var dt) ? (dt.GetString() ?? "") : "";
        double confidence = 0.85;
        int durationMs = 0;
        if (root.TryGetProperty("NBest", out var nb) && nb.ValueKind == JsonValueKind.Array && nb.GetArrayLength() > 0)
        {
            var top = nb[0];
            if (top.TryGetProperty("Confidence", out var c) && c.ValueKind == JsonValueKind.Number) confidence = c.GetDouble();
            if (string.IsNullOrEmpty(text) && top.TryGetProperty("Display", out var dp)) text = dp.GetString() ?? "";
        }
        if (root.TryGetProperty("Duration", out var dur) && dur.ValueKind == JsonValueKind.Number)
            durationMs = (int)(dur.GetInt64() / 10000);

        return new ConversationAsrResult(text.Trim(), confidence, durationMs, locale, Name, $"azure {text.Length} chars");
    }

    private static string MapContentType(string mime) => mime switch
    {
        "audio/wav" or "audio/x-wav" => "audio/wav; codecs=audio/pcm; samplerate=16000",
        "audio/ogg" => "audio/ogg; codecs=opus",
        "audio/webm" => "audio/webm; codecs=opus",
        _ => mime,
    };
}
