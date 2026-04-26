using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Conversation.Asr;

public sealed class DeepgramConversationAsrProvider(
    IHttpClientFactory httpClientFactory,
    IConversationOptionsProvider optionsProvider,
    ILogger<DeepgramConversationAsrProvider> logger) : IConversationAsrProvider
{
    private ConversationOptions ReadOptions() => optionsProvider.GetAsync().GetAwaiter().GetResult();

    public string Name => "deepgram";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ReadOptions().DeepgramApiKey);

    public async Task<ConversationAsrResult> TranscribeAsync(ConversationAsrRequest request, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("Deepgram not configured.");

        var client = httpClientFactory.CreateClient("ConversationDeepgramClient");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", ReadOptions().DeepgramApiKey);

        var lang = string.IsNullOrWhiteSpace(ReadOptions().DeepgramLanguage) ? request.Locale : ReadOptions().DeepgramLanguage;
        var model = string.IsNullOrWhiteSpace(ReadOptions().DeepgramModel) ? "nova-2" : ReadOptions().DeepgramModel;
        var diarization = request.EnableDiarization ? "&diarize=true&utterances=true" : "";
        var url = $"https://api.deepgram.com/v1/listen?model={Uri.EscapeDataString(model)}&language={Uri.EscapeDataString(lang)}&smart_format=true&punctuate=true{diarization}";

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

        var speakerSegments = request.EnableDiarization
            ? ParseUtterances(root)
            : null;

        return new ConversationAsrResult(text, confidence, durationMs, lang, Name, $"deepgram {text.Length} chars", speakerSegments);
    }

    private static IReadOnlyList<ConversationSpeakerSegment> ParseUtterances(JsonElement root)
    {
        var segments = new List<ConversationSpeakerSegment>();
        if (!root.TryGetProperty("results", out var results) ||
            !results.TryGetProperty("utterances", out var utterances) ||
            utterances.ValueKind != JsonValueKind.Array)
            return segments;

        foreach (var utterance in utterances.EnumerateArray())
        {
            var text = utterance.TryGetProperty("transcript", out var transcript)
                ? (transcript.GetString() ?? "").Trim()
                : "";
            if (string.IsNullOrWhiteSpace(text)) continue;
            var speaker = utterance.TryGetProperty("speaker", out var sp)
                ? $"speaker-{sp.GetRawText().Trim('\"')}"
                : "speaker-unknown";
            var startMs = utterance.TryGetProperty("start", out var start) && start.ValueKind == JsonValueKind.Number
                ? (int)(start.GetDouble() * 1000)
                : 0;
            var endMs = utterance.TryGetProperty("end", out var end) && end.ValueKind == JsonValueKind.Number
                ? (int)(end.GetDouble() * 1000)
                : startMs;
            var confidence = utterance.TryGetProperty("confidence", out var conf) && conf.ValueKind == JsonValueKind.Number
                ? conf.GetDouble()
                : (double?)null;
            segments.Add(new ConversationSpeakerSegment(speaker, text, startMs, endMs, confidence));
        }
        return segments;
    }
}
