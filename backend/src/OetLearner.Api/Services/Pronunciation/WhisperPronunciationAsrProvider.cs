using System.Net.Http.Headers;
using System.Text;
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

        // ── Step 3: Grounded AI phoneme-level refinement ─────────────────────
        //
        // Whisper does not expose native phoneme scores. Rather than fabricate
        // them, we send the aligned reference/heard transcript through the
        // grounded gateway with Kind=Pronunciation + Task=ScorePronunciationAttempt
        // and parse the rule-cited phoneme scores from the reply. If the call
        // fails or returns unusable data, we keep the single-phoneme summary
        // above — never silently emit fake per-phoneme scores.
        var refined = await TryRefineViaGroundedAiAsync(
            request, transcript, refWords, heard, accuracy, fluency, completeness, prosody, overall, ct);
        if (refined is not null)
        {
            return refined with
            {
                FluencyMarkers = markers,
                ProviderResponseSummary = $"whisper+grounded-ai: transcript='{Truncate(transcript, 80)}', {matched}/{refLower.Count} words matched"
            };
        }

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

    private async Task<AsrResult?> TryRefineViaGroundedAiAsync(
        AsrRequest request,
        string transcript,
        IReadOnlyList<string> refWords,
        IReadOnlyList<string> heardWords,
        double accuracy,
        double fluency,
        double completeness,
        double prosody,
        double overall,
        CancellationToken ct)
    {
        try
        {
            var profession = ParseProfession(request.RulebookProfession);
            var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Pronunciation,
                Profession = profession,
                Task = AiTaskMode.ScorePronunciationAttempt,
            });

            var user = new StringBuilder();
            user.AppendLine($"Target phoneme: /{request.TargetPhoneme}/");
            if (!string.IsNullOrWhiteSpace(request.TargetRuleId))
                user.AppendLine($"Primary rule: {request.TargetRuleId}");
            user.AppendLine();
            user.AppendLine("Reference text:");
            user.AppendLine(request.ReferenceText);
            user.AppendLine();
            user.AppendLine($"Heard transcript: {transcript}");
            user.AppendLine($"Reference words: {string.Join(' ', refWords)}");
            user.AppendLine($"Heard words: {string.Join(' ', heardWords)}");
            user.AppendLine();
            user.AppendLine("Whisper alignment (baseline — use these as a floor, not a ceiling):");
            user.AppendLine($"accuracy={accuracy} fluency={fluency} completeness={completeness} prosody={prosody} overall={overall}");
            user.AppendLine();
            user.AppendLine("Cite pronunciation rule IDs (P01.1 etc.) for every finding. Do not invent rules.");

            var result = await aiGateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = user.ToString(),
                Model = "auto",
                Temperature = 0.2,
                MaxTokens = 900,
                UserId = null,
                FeatureCode = AiFeatureCodes.PronunciationScore,
                PromptTemplateId = "pronunciation.whisper.score.v1",
            }, ct);

            var parsed = ParseScoredJson(result.Completion);
            if (parsed is null) return null;
            return parsed;
        }
        catch (PromptNotGroundedException)
        {
            // Propagate — this indicates a coding bug that must surface.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Whisper grounded-AI phoneme inference failed — falling back to Whisper-only scoring.");
            return null;
        }
    }

    private static AsrResult? ParseScoredJson(string completion)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;
        int start = completion.IndexOf('{');
        int end = completion.LastIndexOf('}');
        if (start < 0 || end < 0 || end <= start) return null;
        var json = completion.Substring(start, end - start + 1);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            double Accu = D(root, "accuracyScore");
            double Flu = D(root, "fluencyScore");
            double Com = D(root, "completenessScore");
            double Pro = D(root, "prosodyScore");
            double Ove = D(root, "overallScore");
            if (Ove <= 0.01 && (Accu + Flu + Com + Pro) > 0)
                Ove = Math.Round((Accu + Flu + Com + Pro) / 4.0, 1);

            var words = new List<WordScore>();
            if (root.TryGetProperty("wordScores", out var wordsEl) && wordsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var w in wordsEl.EnumerateArray())
                {
                    if (w.ValueKind != JsonValueKind.Object) continue;
                    var word = S(w, "word") ?? "";
                    var score = D(w, "accuracyScore");
                    var err = S(w, "errorType") ?? "None";
                    if (!string.IsNullOrEmpty(word))
                        words.Add(new WordScore(word, Math.Round(score, 1), err));
                }
            }

            var phonemes = new List<PhonemeScore>();
            if (root.TryGetProperty("problematicPhonemes", out var phEl) && phEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in phEl.EnumerateArray())
                {
                    if (p.ValueKind != JsonValueKind.Object) continue;
                    var phoneme = S(p, "phoneme") ?? "";
                    if (string.IsNullOrEmpty(phoneme)) continue;
                    var ph = D(p, "score");
                    var occ = I(p, "occurrences");
                    var rule = S(p, "ruleId");
                    phonemes.Add(new PhonemeScore(phoneme, Math.Round(ph, 1), Math.Max(1, occ), rule));
                }
            }

            if (words.Count == 0 && phonemes.Count == 0) return null;

            var speechRate = 140;
            var pauses = 0;
            var avgPause = 400;
            if (root.TryGetProperty("fluencyMarkers", out var fmEl) && fmEl.ValueKind == JsonValueKind.Object)
            {
                speechRate = (int)Math.Max(0, D(fmEl, "speechRateWpm"));
                if (speechRate == 0) speechRate = 140;
                pauses = I(fmEl, "pauseCount");
                avgPause = I(fmEl, "averagePauseDurationMs");
                if (avgPause == 0) avgPause = 400;
            }

            return new AsrResult(
                AccuracyScore: Math.Round(Accu, 1),
                FluencyScore: Math.Round(Flu, 1),
                CompletenessScore: Math.Round(Com, 1),
                ProsodyScore: Math.Round(Pro, 1),
                OverallScore: Math.Round(Ove, 1),
                WordScores: words,
                ProblematicPhonemes: phonemes,
                FluencyMarkers: new FluencyMarkers(speechRate, pauses, avgPause),
                ProviderName: "whisper",
                ProviderResponseSummary: "whisper+grounded-ai (refined)");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double D(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return 0;
        if (!el.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    }

    private static int I(JsonElement el, string name) => (int)Math.Round(D(el, name));

    private static string? S(JsonElement el, string name)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static ExamProfession ParseProfession(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "all", StringComparison.OrdinalIgnoreCase))
            return ExamProfession.Medicine;
        var norm = raw!.Replace("-", "_").ToLowerInvariant();
        foreach (var v in Enum.GetValues<ExamProfession>())
            if (string.Equals(v.ToString(), norm, StringComparison.OrdinalIgnoreCase)) return v;
        if (norm.Replace("_", "") == "occupationaltherapy") return ExamProfession.OccupationalTherapy;
        if (norm.Replace("_", "") == "speechpathology") return ExamProfession.SpeechPathology;
        return ExamProfession.Medicine;
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
