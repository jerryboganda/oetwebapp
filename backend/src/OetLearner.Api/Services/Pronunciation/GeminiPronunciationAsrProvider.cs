using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Pronunciation;

/// <summary>
/// Gemini native-audio pronunciation provider. The raw recording is sent to
/// Gemini through the grounded AI gateway so scoring remains rulebook-bound,
/// audited, and accounted like the rest of the AI subsystem.
/// </summary>
public sealed class GeminiPronunciationAsrProvider(
    IOptions<PronunciationOptions> options,
    IAiGatewayService aiGateway,
    IPronunciationCredentialResolver credentialResolver,
    ILogger<GeminiPronunciationAsrProvider> logger) : IPronunciationAsrProvider
{
    private const string ProviderCode = "gemini-pronunciation-audio";
    private readonly PronunciationOptions _options = options.Value;

    public string Name => "gemini";

    public bool IsConfigured =>
        credentialResolver.IsRegistryConfigured(ProviderCode) ||
        (!string.IsNullOrWhiteSpace(_options.GeminiApiKey) &&
         !string.IsNullOrWhiteSpace(_options.GeminiBaseUrl) &&
         AiProviderConnectionTester.GetUnsafeBaseUrlReason(_options.GeminiBaseUrl) is null);

    public async Task<AsrResult> AnalyzeAsync(AsrRequest request, CancellationToken ct)
    {
        var registry = await credentialResolver.ResolveAsync(ProviderCode, ct);
        var model = registry?.DefaultModel ?? _options.GeminiModel;
        if (string.IsNullOrWhiteSpace(model)) model = "gemini-3.5-flash";

        var profession = ParseProfession(request.RulebookProfession);
        var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Pronunciation,
            Profession = profession,
            Task = AiTaskMode.ScorePronunciationAttempt,
        });

        var audio = await ReadAudioAsync(request.Audio, request.AudioBytes, ct);
        if (audio.Length == 0)
            throw new PronunciationAsrException("gemini_empty_audio", "Gemini pronunciation scoring requires a non-empty audio recording.");

        var user = BuildUserPrompt(request);
        try
        {
            var result = await aiGateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = user,
                Provider = ProviderCode,
                Model = model,
                Temperature = 0.1,
                MaxTokens = 1100,
                UserId = request.UserId,
                FeatureCode = AiFeatureCodes.PronunciationLinguisticScore,
                PromptTemplateId = "pronunciation.gemini.native-audio.score.v1",
                AudioAttachments = new[]
                {
                    new AiProviderAudioAttachment
                    {
                        MimeType = string.IsNullOrWhiteSpace(request.AudioMimeType) ? "audio/webm" : request.AudioMimeType,
                        Data = audio,
                    },
                },
            }, ct);

            var parsed = ParseScoredJson(result.Completion, request.TargetPhoneme, request.TargetRuleId);
            if (parsed is null)
            {
                throw new PronunciationAsrException(
                    "gemini_invalid_response",
                    "Gemini pronunciation scoring returned an unusable response.");
            }

            return parsed with
            {
                ProviderName = Name,
                ProviderResponseSummary = "gemini-native-audio: grounded pronunciation scoring",
            };
        }
        catch (PromptNotGroundedException)
        {
            throw;
        }
        catch (AiQuotaDeniedException)
        {
            throw;
        }
        catch (PronunciationAsrException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Gemini native-audio pronunciation provider is not available.");
            throw new PronunciationAsrUnavailableException(
                "gemini_unavailable",
                "Gemini pronunciation scoring is temporarily unavailable.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Gemini native-audio pronunciation scoring failed.");
            throw new PronunciationAsrException("gemini_error", "Gemini pronunciation scoring failed.");
        }
    }

    private static string BuildUserPrompt(AsrRequest request)
    {
        var user = new StringBuilder();
        user.AppendLine("Score the attached learner audio against the pronunciation rulebook and the reference text.");
        user.AppendLine("Return only JSON with this exact shape:");
        user.AppendLine("{\"accuracyScore\":0," +
                        "\"fluencyScore\":0," +
                        "\"completenessScore\":0," +
                        "\"prosodyScore\":0," +
                        "\"overallScore\":0," +
                        "\"wordScores\":[{\"word\":\"text\",\"accuracyScore\":0,\"errorType\":\"None\"}]," +
                        "\"problematicPhonemes\":[{\"phoneme\":\"t\",\"score\":0,\"occurrences\":1,\"ruleId\":\"P01.1\"}]," +
                        "\"fluencyMarkers\":{\"speechRateWpm\":140,\"pauseCount\":0,\"averagePauseDurationMs\":400}}");
        user.AppendLine();
        user.AppendLine($"Target phoneme: /{request.TargetPhoneme}/");
        if (!string.IsNullOrWhiteSpace(request.TargetRuleId))
            user.AppendLine($"Primary rule: {request.TargetRuleId}");
        user.AppendLine($"Locale: {request.Locale}");
        user.AppendLine();
        user.AppendLine("Reference text:");
        user.AppendLine(request.ReferenceText);
        user.AppendLine();
        user.AppendLine("Cite only pronunciation rule IDs that exist in the provided rulebook. Do not invent rules or official OET scores.");
        return user.ToString();
    }

    private static async Task<byte[]> ReadAudioAsync(Stream source, long? expectedBytes, CancellationToken ct)
    {
        if (source.CanSeek) source.Position = 0;
        var initialCapacity = expectedBytes is > 0 and <= int.MaxValue ? (int)expectedBytes.Value : 0;
        using var buffer = initialCapacity > 0 ? new MemoryStream(initialCapacity) : new MemoryStream();
        await source.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private static AsrResult? ParseScoredJson(string completion, string targetPhoneme, string? targetRuleId)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;
        var start = completion.IndexOf('{');
        var end = completion.LastIndexOf('}');
        if (start < 0 || end < 0 || end <= start) return null;
        var json = completion.Substring(start, end - start + 1);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var accuracy = D(root, "accuracyScore");
            var fluency = D(root, "fluencyScore");
            var completeness = D(root, "completenessScore");
            var prosody = D(root, "prosodyScore");
            var overall = D(root, "overallScore");
            if (overall <= 0.01 && accuracy + fluency + completeness + prosody > 0)
                overall = Math.Round((accuracy + fluency + completeness + prosody) / 4.0, 1);

            var words = new List<WordScore>();
            if (root.TryGetProperty("wordScores", out var wordsEl) && wordsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var w in wordsEl.EnumerateArray())
                {
                    if (w.ValueKind != JsonValueKind.Object) continue;
                    var word = S(w, "word") ?? "";
                    if (string.IsNullOrWhiteSpace(word)) continue;
                    words.Add(new WordScore(word, Math.Round(D(w, "accuracyScore"), 1), S(w, "errorType") ?? "None"));
                }
            }

            var phonemes = new List<PhonemeScore>();
            if (root.TryGetProperty("problematicPhonemes", out var phEl) && phEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in phEl.EnumerateArray())
                {
                    if (p.ValueKind != JsonValueKind.Object) continue;
                    var phoneme = S(p, "phoneme") ?? "";
                    if (string.IsNullOrWhiteSpace(phoneme)) continue;
                    phonemes.Add(new PhonemeScore(
                        phoneme,
                        Math.Round(D(p, "score"), 1),
                        Math.Max(1, I(p, "occurrences")),
                        S(p, "ruleId")));
                }
            }

            if (words.Count == 0) return null;
            if (phonemes.Count == 0 && !string.IsNullOrWhiteSpace(targetPhoneme))
            {
                phonemes.Add(new PhonemeScore(
                    targetPhoneme,
                    Math.Round(accuracy > 0 ? accuracy : overall, 1),
                    Occurrences: 1,
                    RuleId: string.IsNullOrWhiteSpace(targetRuleId) ? null : targetRuleId));
            }
            if (phonemes.Count == 0) return null;

            var markers = new FluencyMarkers(140, 0, 400);
            if (root.TryGetProperty("fluencyMarkers", out var fmEl) && fmEl.ValueKind == JsonValueKind.Object)
            {
                var speechRate = D(fmEl, "speechRateWpm");
                if (speechRate <= 0) speechRate = 140;
                var averagePause = I(fmEl, "averagePauseDurationMs");
                if (averagePause <= 0) averagePause = 400;
                markers = new FluencyMarkers(speechRate, Math.Max(0, I(fmEl, "pauseCount")), averagePause);
            }

            return new AsrResult(
                Math.Round(accuracy, 1),
                Math.Round(fluency, 1),
                Math.Round(completeness, 1),
                Math.Round(prosody, 1),
                Math.Round(overall, 1),
                words,
                phonemes,
                markers,
                ProviderName: "gemini",
                ProviderResponseSummary: "gemini-native-audio (grounded)");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double D(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return 0;
        if (!el.TryGetProperty(name, out var value)) return 0;
        return value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
    }

    private static int I(JsonElement el, string name) => (int)Math.Round(D(el, name));

    private static string? S(JsonElement el, string name)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static ExamProfession ParseProfession(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "all", StringComparison.OrdinalIgnoreCase))
            return ExamProfession.Medicine;
        var norm = raw!.Replace("-", "_").ToLowerInvariant();
        foreach (var value in Enum.GetValues<ExamProfession>())
            if (string.Equals(value.ToString(), norm, StringComparison.OrdinalIgnoreCase)) return value;
        if (norm.Replace("_", "") == "occupationaltherapy") return ExamProfession.OccupationalTherapy;
        if (norm.Replace("_", "") == "speechpathology") return ExamProfession.SpeechPathology;
        return ExamProfession.Medicine;
    }
}