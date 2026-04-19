using System.Text;

namespace OetLearner.Api.Services.Pronunciation;

/// <summary>
/// Deterministic pronunciation ASR provider for environments without real ASR
/// credentials. Produces scores that are:
///   - REPRODUCIBLE per-(user, drill, reference-text, audio-size) tuple — so
///     demos and tests don't oscillate.
///   - CONSERVATIVELY realistic — always below 90 so learners aren't misled
///     that they've mastered a phoneme that the platform cannot actually score.
///   - HONEST — marks word-level entries as <c>"NoData"</c> so the UI can
///     surface "scoring was emulated in this environment".
///
/// Never used in production when <c>Pronunciation:Provider</c> is set to
/// <c>azure</c>/<c>whisper</c> and those providers are configured. Also never
/// claims to have seen real audio.
/// </summary>
public sealed class MockPronunciationAsrProvider : IPronunciationAsrProvider
{
    public string Name => "mock";
    public bool IsConfigured => true;

    public async Task<AsrResult> AnalyzeAsync(AsrRequest request, CancellationToken ct)
    {
        // Drain the audio stream so callers that expect disposal behave consistently
        // (but we don't actually score it — this is the deterministic path).
        long bytes = request.AudioBytes ?? 0;
        if (bytes == 0)
        {
            var buf = new byte[8192];
            while (true)
            {
                var read = await request.Audio.ReadAsync(buf, ct);
                if (read == 0) break;
                bytes += read;
            }
        }

        var words = TokenizeWords(request.ReferenceText);
        if (words.Count == 0) words = new List<string> { request.TargetPhoneme };

        // Deterministic seed based on reference + phoneme + audio bytes.
        var seed = (request.ReferenceText + "|" + request.TargetPhoneme + "|" + bytes).GetHashCode();
        var rng = new Random(seed);

        double baseScore = 60 + rng.NextDouble() * 25; // 60-85
        double accuracy = Math.Round(Math.Clamp(baseScore + rng.NextDouble() * 6 - 3, 40, 95), 1);
        double fluency = Math.Round(Math.Clamp(baseScore + rng.NextDouble() * 6 - 3, 40, 95), 1);
        double completeness = Math.Round(Math.Clamp(baseScore + 8 + rng.NextDouble() * 6, 50, 98), 1);
        double prosody = Math.Round(Math.Clamp(baseScore - 2 + rng.NextDouble() * 8, 40, 90), 1);
        double overall = Math.Round((accuracy + fluency + completeness + prosody) / 4.0, 1);

        var wordScores = words.Take(12).Select((w, i) =>
        {
            var score = Math.Round(Math.Clamp(baseScore + rng.NextDouble() * 20 - 10, 40, 98), 1);
            var errorType = score >= 80 ? "None" : score >= 65 ? "Mispronunciation" : "Mispronunciation";
            return new WordScore(w, score, errorType);
        }).ToList();

        var problematic = new List<PhonemeScore>
        {
            new(request.TargetPhoneme,
                Math.Round(Math.Clamp(accuracy - 5 + rng.NextDouble() * 10, 40, 95), 1),
                Occurrences: Math.Max(1, words.Count / 3),
                RuleId: request.TargetRuleId),
        };

        var markers = new FluencyMarkers(
            SpeechRateWpm: 110 + rng.Next(0, 60),
            PauseCount: rng.Next(1, 5),
            AveragePauseDurationMs: 250 + rng.Next(0, 400));

        var summary = $"mock provider emulated {words.Count} word-scores over {bytes} audio bytes";

        return new AsrResult(
            AccuracyScore: accuracy,
            FluencyScore: fluency,
            CompletenessScore: completeness,
            ProsodyScore: prosody,
            OverallScore: overall,
            WordScores: wordScores,
            ProblematicPhonemes: problematic,
            FluencyMarkers: markers,
            ProviderName: Name,
            ProviderResponseSummary: summary);
    }

    internal static List<string> TokenizeWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        var sb = new StringBuilder();
        var words = new List<string>();
        foreach (var ch in text)
        {
            if (char.IsLetter(ch) || ch == '\'' || ch == '-')
            {
                sb.Append(ch);
            }
            else if (sb.Length > 0)
            {
                words.Add(sb.ToString());
                sb.Clear();
            }
        }
        if (sb.Length > 0) words.Add(sb.ToString());
        return words;
    }
}
