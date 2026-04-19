using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Pronunciation;

/// <summary>
/// Azure Speech SDK Pronunciation Assessment provider. Uses the REST API
/// (simpler deploy than the native SDK and avoids a ~50 MB package).
///
/// Endpoint:
///   https://{region}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1
///
/// With Pronunciation Assessment headers:
///   Pronunciation-Assessment: {referenceText, gradingSystem=HundredMark,
///                              granularity=Phoneme, enableMiscue=true} (base64 JSON)
///
/// See docs/PRONUNCIATION.md §4 for provider comparison.
///
/// When <see cref="IsConfigured"/> is false (no key configured) the provider
/// returns false and the selector falls through to the next provider.
/// </summary>
public sealed class AzurePronunciationAsrProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<PronunciationOptions> options,
    ILogger<AzurePronunciationAsrProvider> logger) : IPronunciationAsrProvider
{
    private readonly PronunciationOptions _options = options.Value;

    public string Name => "azure";
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.AzureSpeechKey) &&
        !string.IsNullOrWhiteSpace(_options.AzureSpeechRegion);

    public async Task<AsrResult> AnalyzeAsync(AsrRequest request, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Azure Speech provider is not configured.");

        var region = _options.AzureSpeechRegion;
        var locale = string.IsNullOrWhiteSpace(request.Locale) ? _options.AzureLocale : request.Locale;
        var endpoint =
            $"https://{region}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1" +
            $"?language={Uri.EscapeDataString(locale)}&format=detailed";

        var pronConfig = JsonSerializer.Serialize(new
        {
            ReferenceText = request.ReferenceText,
            GradingSystem = "HundredMark",
            Granularity = "Phoneme",
            Dimension = "Comprehensive",
            EnableMiscue = true,
        });
        var pronHeader = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(pronConfig));

        var client = httpClientFactory.CreateClient("PronunciationAzureClient");
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint);
        requestMessage.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", _options.AzureSpeechKey);
        requestMessage.Headers.TryAddWithoutValidation("Pronunciation-Assessment", pronHeader);
        requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Azure prefers 16kHz 16-bit PCM or specific container types. We stream
        // whatever the learner uploaded; the service supports WAV/WebM/OGG.
        var streamContent = new StreamContent(request.Audio);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(request.AudioMimeType);
        requestMessage.Content = streamContent;

        using var response = await client.SendAsync(requestMessage, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Azure ASR returned {Status}: {Body}", (int)response.StatusCode, body);
            throw new PronunciationAsrException(
                "azure_error",
                $"Azure Speech returned {(int)response.StatusCode}: {Truncate(body, 300)}");
        }

        return ParseAzureResponse(body, request);
    }

    internal static AsrResult ParseAzureResponse(string body, AsrRequest request)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Azure returns an NBest array with NBest[0] being the best alternative.
        if (!root.TryGetProperty("NBest", out var nbest) || nbest.GetArrayLength() == 0)
        {
            throw new PronunciationAsrException("azure_no_match", "Azure Speech returned no recognised alternatives.");
        }

        var best = nbest[0];
        var assessment = best.TryGetProperty("PronunciationAssessment", out var pa) ? pa : default;

        double accuracy = SafeDouble(assessment, "AccuracyScore");
        double fluency = SafeDouble(assessment, "FluencyScore");
        double completeness = SafeDouble(assessment, "CompletenessScore");
        double prosody = SafeDouble(assessment, "ProsodyScore");
        double overall = SafeDouble(assessment, "PronScore");
        if (overall <= 0.01)
            overall = Math.Round((accuracy + fluency + completeness + prosody) / 4.0, 1);

        var wordScores = new List<WordScore>();
        var phonemeAgg = new Dictionary<string, (double scoreSum, int count)>();
        if (best.TryGetProperty("Words", out var words))
        {
            foreach (var w in words.EnumerateArray())
            {
                var word = w.TryGetProperty("Word", out var ws) ? ws.GetString() ?? "" : "";
                var wpa = w.TryGetProperty("PronunciationAssessment", out var p) ? p : default;
                var wAccuracy = SafeDouble(wpa, "AccuracyScore");
                var errorType = wpa.ValueKind == JsonValueKind.Object && wpa.TryGetProperty("ErrorType", out var et)
                    ? et.GetString() ?? "None" : "None";

                var phonemes = new List<PhonemeScore>();
                if (w.TryGetProperty("Phonemes", out var phs))
                {
                    foreach (var ph in phs.EnumerateArray())
                    {
                        var sym = ph.TryGetProperty("Phoneme", out var pps) ? pps.GetString() ?? "" : "";
                        var ppa = ph.TryGetProperty("PronunciationAssessment", out var ppap) ? ppap : default;
                        var phScore = SafeDouble(ppa, "AccuracyScore");
                        phonemes.Add(new PhonemeScore(sym, Math.Round(phScore, 1), 1));
                        if (!phonemeAgg.TryGetValue(sym, out var pair)) pair = (0, 0);
                        phonemeAgg[sym] = (pair.scoreSum + phScore, pair.count + 1);
                    }
                }
                wordScores.Add(new WordScore(word, Math.Round(wAccuracy, 1), errorType, phonemes));
            }
        }

        // Pick worst phonemes for problematic list (bottom 5).
        var problematic = phonemeAgg
            .Select(kv => new PhonemeScore(
                kv.Key,
                Math.Round(kv.Value.scoreSum / Math.Max(1, kv.Value.count), 1),
                kv.Value.count,
                RuleId: PhonemeToRuleId(kv.Key, request.TargetRuleId)))
            .OrderBy(p => p.Score)
            .Take(5)
            .ToList();
        if (problematic.Count == 0)
        {
            problematic.Add(new PhonemeScore(request.TargetPhoneme, Math.Round(accuracy, 1), 1, request.TargetRuleId));
        }

        var markers = new FluencyMarkers(
            SpeechRateWpm: (int)Math.Round(SafeDouble(assessment, "SpeechRate") is var sr and > 0
                ? sr * 60.0
                : 140.0),
            PauseCount: (int)SafeDouble(assessment, "PauseCount"),
            AveragePauseDurationMs: (int)SafeDouble(assessment, "AveragePauseDuration"));

        return new AsrResult(
            AccuracyScore: Math.Round(accuracy, 1),
            FluencyScore: Math.Round(fluency, 1),
            CompletenessScore: Math.Round(completeness, 1),
            ProsodyScore: Math.Round(prosody, 1),
            OverallScore: Math.Round(overall, 1),
            WordScores: wordScores,
            ProblematicPhonemes: problematic,
            FluencyMarkers: markers,
            ProviderName: "azure",
            ProviderResponseSummary: $"azure: {wordScores.Count} words, {phonemeAgg.Count} phonemes");
    }

    private static double SafeDouble(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return 0;
        if (!el.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    }

    private static string? PhonemeToRuleId(string phoneme, string? fallback)
    {
        // Basic mapping for medicine rulebook. Extend as rulebook grows.
        return phoneme switch
        {
            "θ" or "th" => "P01.1",
            "ð" => "P01.2",
            "v" => "P01.3",
            "w" => "P01.4",
            "r" or "l" => "P01.5",
            "ɪ" => "P02.1",
            "iː" => "P02.2",
            "æ" => "P02.3",
            "ɜː" => "P02.4",
            "ʌ" => "P02.5",
            "ə" => "P02.6",
            "ŋ" => "P01.8",
            _ => fallback
        };
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

public sealed class PronunciationAsrException(string code, string message)
    : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
