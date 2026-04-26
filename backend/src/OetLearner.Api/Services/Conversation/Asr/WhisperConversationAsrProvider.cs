using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Conversation.Asr;

public sealed class WhisperConversationAsrProvider(
    IHttpClientFactory httpClientFactory,
    IConversationOptionsProvider optionsProvider,
    ILogger<WhisperConversationAsrProvider> logger) : IConversationAsrProvider
{
    private ConversationOptions ReadOptions() => optionsProvider.Snapshot();

    public string Name => "whisper";
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ReadOptions().WhisperApiKey) &&
        !string.IsNullOrWhiteSpace(ReadOptions().WhisperBaseUrl);

    public async Task<ConversationAsrResult> TranscribeAsync(ConversationAsrRequest request, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("Whisper provider is not configured.");

        var client = httpClientFactory.CreateClient("ConversationWhisperClient");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ReadOptions().WhisperApiKey);
        var url = $"{ReadOptions().WhisperBaseUrl.TrimEnd('/')}/audio/transcriptions";

        using var form = new MultipartFormDataContent();
        var audioContent = new StreamContent(request.Audio);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue(request.AudioMimeType);
        form.Add(audioContent, "file", GuessFileName(request.AudioMimeType));
        form.Add(new StringContent(ReadOptions().WhisperModel), "model");
        var lang = (request.Locale ?? "en-GB").Length >= 2 ? request.Locale.Substring(0, 2) : "en";
        form.Add(new StringContent(lang), "language");
        form.Add(new StringContent("verbose_json"), "response_format");

        using var response = await client.PostAsync(url, form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Whisper ASR {Status}: {Body}", (int)response.StatusCode, body);
            throw new ConversationAsrException("whisper_error", $"Whisper returned {(int)response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var text = root.TryGetProperty("text", out var t) ? (t.GetString() ?? "").Trim() : "";
        double? duration = root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : null;
        double confidence = 0.85;
        if (root.TryGetProperty("segments", out var segs) && segs.ValueKind == JsonValueKind.Array)
        {
            var probs = new List<double>();
            foreach (var s in segs.EnumerateArray())
                if (s.TryGetProperty("avg_logprob", out var lp) && lp.ValueKind == JsonValueKind.Number)
                    probs.Add(Math.Exp(lp.GetDouble()));
            if (probs.Count > 0) confidence = Math.Clamp(probs.Average(), 0.0, 1.0);
        }
        return new ConversationAsrResult(
            text, confidence, duration.HasValue ? (int)(duration.Value * 1000) : 0,
            lang, Name, $"whisper {text.Length} chars");
    }

    private static string GuessFileName(string mime) => mime switch
    {
        "audio/wav" or "audio/x-wav" => "audio.wav",
        "audio/webm" => "audio.webm",
        "audio/ogg" => "audio.ogg",
        "audio/mpeg" => "audio.mp3",
        "audio/mp4" => "audio.m4a",
        _ => "audio.bin",
    };
}
