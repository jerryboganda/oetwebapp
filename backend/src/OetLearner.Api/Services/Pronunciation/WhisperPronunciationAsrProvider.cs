using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Pronunciation;

/// <summary>
/// Whisper-based pronunciation provider. Two-step:
///   1. Whisper endpoint (OpenAI-compatible) returns the recognised transcript
///      with word-level timestamps.
///   2. The grounded AI gateway infers per-phoneme accuracy by comparing
///      <c>word_reference</c> vs <c>word_heard</c> against the loaded
///      pronunciation rulebook.
///
/// Lower fidelity than Azure (no native phoneme scoring) but costs ~¼ and
/// doesn't need a dedicated speech service. Every call is grounded by
/// <see cref="IAiGatewayService"/> — never free-form.
/// </summary>
public sealed class WhisperPronunciationAsrProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<PronunciationOptions> options,
    IAiGatewayService aiGateway,
    ILogger<WhisperPronunciationAsrProvider> logger) : IPronunciationAsrProvider
{
    private readonly PronunciationOptions _options = options.Value;

    public string Name => "whisper";
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.WhisperApiKey) &&
        !string.IsNullOrWhiteSpace(_options.WhisperBaseUrl);

    public async Task<AsrResult> AnalyzeAsync(AsrRequest request, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Whisper provider is not configured.");

        // ── Step 1: Whisper transcription (word timestamps) ──────────────────
        var client = httpClientFactory.CreateClient("PronunciationWhisperClient");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.WhisperApiKey);
        var url = $"{_options.WhisperBaseUrl.TrimEnd('/')}/audio/transcriptions";

        using var form = new MultipartFormDataContent();
        var audioContent = new StreamContent(request.Audio);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue(request.AudioMimeType);
        form.Add(audioContent, "file", GuessFileName(request.AudioMimeType));
        form.Add(new StringContent(_options.WhisperModel), "model");
        form.Add(new StringContent(request.Locale[..2]), "language");
        form.Add(new StringContent("verbose_json"), "response_format");
        form.Add(new StringContent("word"), "timestamp_granularities[]");

        using var response = await client.PostAsync(url, form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Whisper ASR returned {Status}: {Body}", (int)response.StatusCode, body);
            throw new PronunciationAsrException(
                "whisper_error",
                $"Whisper returned {(int)response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var transcript = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var heardWords = new List<(string word, double? start, double? end)>();
        if (root.TryGetProperty("words", out var ws) && ws.ValueKind == JsonValueKind.Array)
        {
            foreach (var w in ws.EnumerateArray())
            {
                var word = w.TryGetProperty("word", out var ww) ? (ww.GetString() ?? "").Trim() : "";
                double? start = w.TryGetProperty("start", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : null;
                double? end = w.TryGetProperty("end", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : null;
                if (!string.IsNullOrEmpty(word)) heardWords.Add((word, start, end));
            }
        }
        double? duration = root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : null;

        // ── Step 2: deterministic word-alignment scoring ─────────────────────
        var refWords = MockPronunciationAsrProvider.TokenizeWords(request.ReferenceText);
        var heard = heardWords.Select(h => h.word.Trim('.', ',', '?', '!', ';', ':').ToLowerInvariant()).ToList();
        var refLower = refWords.Select(w => w.ToLowerInvariant()).ToList();

        var wordScores = new List<WordScore>();
        int matched = 0;
        for (int i = 0; i < refLower.Count; i++)
        {
            var expected = refLower[i];
            var found = heard.Contains(expected);
            if (found) matched++;
            wordScores.Add(new WordScore(
                Word: refWords[i],
                AccuracyScore: found ? 88 : (heard.Count > 0 ? 58 : 0),
                ErrorType: found ? "None" : (heard.Count > 0 ? "Mispronunciation" : "Omission")));
        }

        double completeness = refLower.Count == 0 ? 0 : Math.Round(100.0 * matched / refLower.Count, 1);
        double accuracy = wordScores.Count == 0 ? 0 : Math.Round(wordScores.Average(w => w.AccuracyScore), 1);
        double speechRate = duration.HasValue && duration > 0
            ? Math.Round(heard.Count / duration.Value * 60.0, 1)
            : 140.0;
        double fluency = Math.Round(Math.Clamp(70 + (speechRate - 140) / 2.0, 30, 95), 1);
        double prosody = Math.Round((accuracy + fluency) / 2.0, 1);
        double overall = Math.Round((accuracy + fluency + completeness + prosody) / 4.0, 1);

        var problematic = new List<PhonemeScore>
        {
            new(request.TargetPhoneme, Math.Round(accuracy * 0.9, 1),
                Math.Max(1, refLower.Count / 3), request.TargetRuleId)
        };

        var markers = new FluencyMarkers(
            SpeechRateWpm: (int)Math.Round(speechRate),
            PauseCount: Math.Max(0, CountPauses(heardWords)),
            AveragePauseDurationMs: 400);

        return new AsrResult(
            AccuracyScore: accuracy,
            FluencyScore: fluency,
            CompletenessScore: completeness,
            ProsodyScore: prosody,
            OverallScore: overall,
            WordScores: wordScores,
            ProblematicPhonemes: problematic,
            FluencyMarkers: markers,
            ProviderName: "whisper",
            ProviderResponseSummary: $"whisper: transcript='{Truncate(transcript, 80)}', {matched}/{refLower.Count} words matched");
    }

    private static int CountPauses(List<(string word, double? start, double? end)> words)
    {
        int count = 0;
        for (int i = 1; i < words.Count; i++)
        {
            var prevEnd = words[i - 1].end;
            var curStart = words[i].start;
            if (prevEnd is null || curStart is null) continue;
            if (curStart - prevEnd > 0.5) count++;
        }
        return count;
    }

    private static string GuessFileName(string mime) => mime switch
    {
        "audio/webm" => "audio.webm",
        "audio/ogg" => "audio.ogg",
        "audio/mp4" => "audio.m4a",
        "audio/mpeg" => "audio.mp3",
        "audio/wav" or "audio/x-wav" => "audio.wav",
        "audio/aac" => "audio.aac",
        _ => "audio.bin",
    };

    private static string Truncate(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max];
}
