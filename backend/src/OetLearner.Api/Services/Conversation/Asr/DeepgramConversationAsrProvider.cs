using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Conversation.Asr;

public sealed class DeepgramConversationAsrProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<ConversationOptions> options,
    ILogger<DeepgramConversationAsrProvider> logger) : IConversationAsrProvider
{
    private readonly ConversationOptions _options = options.Value;

    public string Name => "deepgram";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.DeepgramApiKey);

    public async Task<ConversationAsrResult> TranscribeAsync(ConversationAsrRequest request, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("Deepgram not configured.");

        var client = httpClientFactory.CreateClient("ConversationDeepgramClient");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", _options.DeepgramApiKey);

        var lang = string.IsNullOrWhiteSpace(_options.DeepgramLanguage) ? request.Locale : _options.DeepgramLanguage;
        var model = string.IsNullOrWhiteSpace(_options.DeepgramModel) ? "nova-2" : _options.DeepgramModel;
        var url = $"https://api.deepgram.com/v1/listen?model={Uri.EscapeDataString(model)}&language={Uri.EscapeDataString(lang)}&smart_format=true&punctuate=true";

        var content = new StreamContent(request.Audio);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(request.AudioMimeType);
        using var response = await client.PostAsync(url, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Deepgram ASR {Status}: {Body}", (int)response.StatusCode, body);
            throw new ConversationAsrException("deepgram_error", $"Deepgram returned {(int)response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var text = "";
        double confidence = 0.85;
        int durationMs = 0;
        if (root.TryGetProperty("results", out var results) &&
            results.TryGetProperty("channels", out var channels) &&
            channels.ValueKind == JsonValueKind.Array && channels.GetArrayLength() > 0)
        {
            var top = channels[0];
            if (top.TryGetProperty("alternatives", out var alts) && alts.ValueKind == JsonValueKind.Array && alts.GetArrayLength() > 0)
            {
                var best = alts[0];
                text = best.TryGetProperty("transcript", out var tr) ? (tr.GetString() ?? "").Trim() : "";
                if (best.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number)
                    confidence = c.GetDouble();
            }
        }
        if (root.TryGetProperty("metadata", out var meta) &&
            meta.TryGetProperty("duration", out var dur) && dur.ValueKind == JsonValueKind.Number)
            durationMs = (int)(dur.GetDouble() * 1000);

        return new ConversationAsrResult(text, confidence, durationMs, lang, Name, $"deepgram {text.Length} chars");
    }
}
