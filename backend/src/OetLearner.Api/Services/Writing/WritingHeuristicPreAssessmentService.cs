using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Deterministic, offline-safe pre-assessment of a Writing submission (spec §13.2).
///
/// The heuristic path is PURE — no network, no Random, no DateTime in the scoring logic — so it
/// produces identical output for identical input and works with zero external dependencies in
/// Docker-local. When an AI gateway is configured AND <c>Writing:PreAssessmentLlmEnabled</c> is
/// true (it defaults to <c>false</c>, so tests and Docker-local stay on the heuristic), the result
/// MAY be enriched by an LLM. Any failure — missing keys, no provider, ungrounded-prompt refusal,
/// or any exception — silently degrades to the heuristic (source stays <c>"heuristic"</c>).
/// </summary>
public interface IWritingHeuristicPreAssessmentService
{
    Task<WritingPreAssessmentResult> AssessAsync(WritingPreAssessmentRequest request, CancellationToken ct);
}

public sealed record WritingPreAssessmentRequest(
    Guid SubmissionId,
    string LetterText,
    WritingScenario Scenario,
    IReadOnlyList<WritingContentChecklistItem> ChecklistItems,
    string? ModelAnswerText);

public sealed record WritingPreAssessmentResult(
    Guid SubmissionId,
    int WordCount,
    bool WithinWordGuide,
    int KeyContentCoveragePercent,
    IReadOnlyList<string> MissingKeyContent,
    IReadOnlyList<string> DetectedIrrelevantContent,
    IReadOnlyList<string> LanguageNotes,
    WritingCriteriaScores EstimatedBands,
    int EstimatedRawTotal,
    string EstimatedBandLabel,
    string Confidence,
    string Source,
    IReadOnlyDictionary<string, string> SuggestedCriterionFeedback);

/// <summary>Plain six-criterion score holder (spec §12.2; max 38).</summary>
public sealed record WritingCriteriaScores(
    int C1Purpose,
    int C2Content,
    int C3Conciseness,
    int C4Genre,
    int C5Organisation,
    int C6Language)
{
    public int RawTotal => C1Purpose + C2Content + C3Conciseness + C4Genre + C5Organisation + C6Language;
}

public sealed class WritingHeuristicPreAssessmentService(
    IAiGatewayService aiGateway,
    IConfiguration configuration,
    ILogger<WritingHeuristicPreAssessmentService> logger) : IWritingHeuristicPreAssessmentService
{
    // Criterion maxima (spec §12.2): c1 Purpose 0–3; c2..c6 0–7.
    private const int MaxC1 = 3;
    private const int MaxOther = 7;

    // Significant-token heuristic: ignore very short / stop words so keyword overlap is meaningful.
    private const int MinTokenLength = 4;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "that", "this", "from", "have", "has", "had",
        "was", "were", "are", "will", "would", "should", "could", "your", "you",
        "their", "they", "them", "her", "his", "she", "him", "who", "whom", "which",
        "into", "onto", "than", "then", "there", "these", "those", "been", "being",
        "also", "about", "after", "before", "patient", "please",
    };

    private static readonly Regex WordRegex = new(@"[A-Za-z']+", RegexOptions.Compiled);
    private static readonly Regex SentenceSplit = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);
    private static readonly Regex ContractionRegex =
        new(@"\b\w+'(t|s|re|ve|ll|d|m)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<WritingPreAssessmentResult> AssessAsync(WritingPreAssessmentRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1) Always compute the deterministic heuristic first (offline-safe fallback).
        var heuristic = BuildHeuristic(request);

        // 2) Only attempt LLM enrichment when explicitly enabled. Defaults to false so the whole
        //    thing works with zero external dependencies in Docker-local and in tests/mocks.
        var llmEnabled = configuration.GetValue("Writing:PreAssessmentLlmEnabled", false);
        if (!llmEnabled)
        {
            return heuristic;
        }

        try
        {
            var grounded = aiGateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                LetterType = NormalizeLetterTypeForGrounding(request.Scenario.LetterType),
                Task = AiTaskMode.Score,
            });

            var result = await aiGateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = grounded,
                UserInput = BuildUserInput(request, heuristic),
                Temperature = 0.0,
                FeatureCode = "writing.pre_assessment",
            }, ct);

            if (string.IsNullOrWhiteSpace(result?.Completion))
            {
                return heuristic;
            }

            return MergeLlm(heuristic, result.Completion);
        }
        catch (Exception ex)
        {
            // Missing keys / no provider / PromptNotGroundedException / transport errors all land here.
            logger.LogWarning(
                ex,
                "Writing pre-assessment LLM enrichment failed for submission {SubmissionId}; using heuristic.",
                request.SubmissionId);
            return heuristic;
        }
    }

    // ── Deterministic heuristic ────────────────────────────────────────────────────────────────

    private static WritingPreAssessmentResult BuildHeuristic(WritingPreAssessmentRequest request)
    {
        var scenario = request.Scenario;
        var letter = request.LetterText ?? string.Empty;
        var letterTokens = Tokenize(letter);
        var letterTokenSet = new HashSet<string>(letterTokens, StringComparer.OrdinalIgnoreCase);

        // wordCount = body words.
        var wordCount = letterTokens.Count;

        // withinWordGuide vs scenario WordGuideMin/Max (a bound of 0 means "unbounded").
        var minOk = scenario.WordGuideMin <= 0 || wordCount >= scenario.WordGuideMin;
        var maxOk = scenario.WordGuideMax <= 0 || wordCount <= scenario.WordGuideMax;
        var withinWordGuide = minOk && maxOk;

        // Key content coverage: a required item is "covered" when at least half of its significant
        // tokens (from ItemText + ExpectedRepresentation) appear in the letter (>= 50% overlap).
        var requiredItems = request.ChecklistItems
            .Where(i => string.Equals(i.RequiredStatus, "required", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var missingKeyContent = new List<string>();
        var coveredCount = 0;
        foreach (var item in requiredItems)
        {
            if (IsItemCovered(item, letterTokenSet))
            {
                coveredCount++;
            }
            else
            {
                missingKeyContent.Add(item.ItemText);
            }
        }

        var keyContentCoveragePercent = requiredItems.Count == 0
            ? 100
            : (int)Math.Round(coveredCount * 100.0 / requiredItems.Count, MidpointRounding.AwayFromZero);

        // Detected irrelevant content: an "irrelevant" distractor whose tokens DO appear in the letter.
        var detectedIrrelevantContent = request.ChecklistItems
            .Where(i => string.Equals(i.RequiredStatus, "irrelevant", StringComparison.OrdinalIgnoreCase))
            .Where(i => IsItemCovered(i, letterTokenSet))
            .Select(i => i.ItemText)
            .ToList();

        // Language notes — simple deterministic surface checks.
        var languageNotes = BuildLanguageNotes(letter);

        // Estimated bands from coverage + length + language signals.
        var estimatedBands = EstimateBands(
            keyContentCoveragePercent,
            withinWordGuide,
            wordCount,
            scenario,
            detectedIrrelevantContent.Count,
            languageNotes);

        var rawTotal = estimatedBands.RawTotal;
        var bandLabel = ComputeBandLabel(rawTotal);

        // Confidence: "low" by default; "medium" only when every signal is unambiguous.
        var confidence = (keyContentCoveragePercent == 100
                          && withinWordGuide
                          && detectedIrrelevantContent.Count == 0
                          && languageNotes.Count == 0)
            ? "medium"
            : "low";

        var suggested = BuildSuggestedFeedback(
            keyContentCoveragePercent,
            withinWordGuide,
            wordCount,
            scenario,
            missingKeyContent,
            detectedIrrelevantContent,
            languageNotes);

        return new WritingPreAssessmentResult(
            request.SubmissionId,
            wordCount,
            withinWordGuide,
            keyContentCoveragePercent,
            missingKeyContent,
            detectedIrrelevantContent,
            languageNotes,
            estimatedBands,
            rawTotal,
            bandLabel,
            confidence,
            "heuristic",
            suggested);
    }

    /// <summary>
    /// An item counts as covered when at least half of its significant tokens are present in the
    /// letter token set (case-insensitive). Items with no significant tokens are treated as covered
    /// so empty/short checklist text never forces a false "missing".
    /// </summary>
    private static bool IsItemCovered(WritingContentChecklistItem item, HashSet<string> letterTokens)
    {
        var itemTokens = Tokenize($"{item.ItemText} {item.ExpectedRepresentation}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (itemTokens.Count == 0)
        {
            return true;
        }

        var hits = itemTokens.Count(t => letterTokens.Contains(t));
        // Ceiling of half → single-keyword items still require that keyword.
        var threshold = (itemTokens.Count + 1) / 2;
        return hits >= threshold;
    }

    private static List<string> BuildLanguageNotes(string letter)
    {
        var notes = new List<string>();
        if (string.IsNullOrWhiteSpace(letter))
        {
            notes.Add("Letter body is empty.");
            return notes;
        }

        if (!letter.Contains("Dear", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add("No greeting detected (e.g. 'Dear ...').");
        }

        if (!HasSignOff(letter))
        {
            notes.Add("No sign-off detected (e.g. 'Yours sincerely', 'Regards').");
        }

        if (ContractionRegex.IsMatch(letter))
        {
            notes.Add("Contractions detected (e.g. don't/can't); prefer full forms in a formal letter.");
        }

        var longSentences = SentenceSplit.Split(letter).Count(s => CountWords(s) > 40);
        if (longSentences > 0)
        {
            notes.Add($"Very long sentence(s) detected ({longSentences} over 40 words); consider splitting.");
        }

        // No paragraph breaks → a single block of text.
        if (!letter.Contains("\n\n", StringComparison.Ordinal)
            && !letter.Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            notes.Add("No paragraph breaks detected; structure the letter into paragraphs.");
        }

        return notes;
    }

    private static bool HasSignOff(string letter)
    {
        string[] signOffs =
        {
            "yours sincerely", "yours faithfully", "kind regards", "best regards", "regards", "sincerely",
        };
        return signOffs.Any(s => letter.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Conservative deterministic mapping of signals to per-criterion bands.
    /// Formula (documented so it can be unit-tested):
    ///   coverageFactor = keyContentCoveragePercent / 100  (0..1)
    ///   c2 Content      = round(7 * coverageFactor) − 1 per detected irrelevant item.
    ///   c1 Purpose      = 3 if coverage ≥ 50% and a greeting exists, else 1.
    ///   c3 Conciseness  = 7; −2 if over the word-guide max, −1 if under the min.
    ///   c4 Genre        = 7 − 1 per genre flag (missing greeting / missing sign-off / contractions).
    ///   c5 Organisation = 7 − 2 if "no paragraph breaks", −1 if a long-sentence flag exists.
    ///   c6 Language     = 7 − 1 per language flag (contractions / long sentence).
    /// All criteria clamp to their [floor, max] range; an empty body forces all-zero.
    /// </summary>
    private static WritingCriteriaScores EstimateBands(
        int keyContentCoveragePercent,
        bool withinWordGuide,
        int wordCount,
        WritingScenario scenario,
        int irrelevantCount,
        IReadOnlyList<string> languageNotes)
    {
        if (wordCount == 0)
        {
            return new WritingCriteriaScores(0, 0, 0, 0, 0, 0);
        }

        var coverageFactor = keyContentCoveragePercent / 100.0;
        var hasGreeting = !languageNotes.Any(n => n.StartsWith("No greeting", StringComparison.OrdinalIgnoreCase));
        var hasSignOff = !languageNotes.Any(n => n.StartsWith("No sign-off", StringComparison.OrdinalIgnoreCase));
        var hasContractions = languageNotes.Any(n => n.StartsWith("Contractions", StringComparison.OrdinalIgnoreCase));
        var hasLongSentence = languageNotes.Any(n => n.StartsWith("Very long sentence", StringComparison.OrdinalIgnoreCase));
        var noParagraphs = languageNotes.Any(n => n.StartsWith("No paragraph breaks", StringComparison.OrdinalIgnoreCase));

        // c2 Content — driven by coverage, penalised by distractors.
        var c2 = (int)Math.Round(MaxOther * coverageFactor, MidpointRounding.AwayFromZero) - irrelevantCount;

        // c1 Purpose — captured when enough content is present and the letter opens correctly.
        var c1 = (coverageFactor >= 0.5 && hasGreeting) ? MaxC1 : 1;

        // c3 Conciseness — length discipline.
        var c3 = MaxOther;
        if (!withinWordGuide)
        {
            var overMax = scenario.WordGuideMax > 0 && wordCount > scenario.WordGuideMax;
            c3 -= overMax ? 2 : 1;
        }

        // c4 Genre — register / format conventions.
        var genrePenalty = (hasGreeting ? 0 : 1) + (hasSignOff ? 0 : 1) + (hasContractions ? 1 : 0);
        var c4 = MaxOther - genrePenalty;

        // c5 Organisation — paragraphing / sentence control.
        var c5 = MaxOther - (noParagraphs ? 2 : 0) - (hasLongSentence ? 1 : 0);

        // c6 Language — surface language flags.
        var c6 = MaxOther - (hasContractions ? 1 : 0) - (hasLongSentence ? 1 : 0);

        return new WritingCriteriaScores(
            Clamp(c1, 0, MaxC1),
            Clamp(c2, 0, MaxOther),
            Clamp(c3, 1, MaxOther),
            Clamp(c4, 2, MaxOther),
            Clamp(c5, 2, MaxOther),
            Clamp(c6, 2, MaxOther));
    }

    private static Dictionary<string, string> BuildSuggestedFeedback(
        int keyContentCoveragePercent,
        bool withinWordGuide,
        int wordCount,
        WritingScenario scenario,
        IReadOnlyList<string> missingKeyContent,
        IReadOnlyList<string> detectedIrrelevantContent,
        IReadOnlyList<string> languageNotes)
    {
        var feedback = new Dictionary<string, string>(StringComparer.Ordinal);

        if (missingKeyContent.Count > 0)
        {
            feedback["c2"] = $"Coverage {keyContentCoveragePercent}%. Missing key content: {string.Join("; ", missingKeyContent)}.";
        }
        if (detectedIrrelevantContent.Count > 0)
        {
            var existing = feedback.TryGetValue("c2", out var v) ? v + " " : string.Empty;
            feedback["c2"] = existing + $"Irrelevant content included: {string.Join("; ", detectedIrrelevantContent)}.";
        }
        if (!withinWordGuide)
        {
            feedback["c3"] = $"Word count {wordCount} is outside the guide ({scenario.WordGuideMin}-{scenario.WordGuideMax}).";
        }

        var genreNotes = languageNotes.Where(n =>
            n.StartsWith("No greeting", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("No sign-off", StringComparison.OrdinalIgnoreCase)).ToList();
        if (genreNotes.Count > 0)
        {
            feedback["c4"] = string.Join(" ", genreNotes);
        }

        var orgNotes = languageNotes.Where(n =>
            n.StartsWith("No paragraph breaks", StringComparison.OrdinalIgnoreCase)).ToList();
        if (orgNotes.Count > 0)
        {
            feedback["c5"] = string.Join(" ", orgNotes);
        }

        var langNotes = languageNotes.Where(n =>
            n.StartsWith("Contractions", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("Very long sentence", StringComparison.OrdinalIgnoreCase)).ToList();
        if (langNotes.Count > 0)
        {
            feedback["c6"] = string.Join(" ", langNotes);
        }

        return feedback;
    }

    // Band thresholds mirror WritingTutorReviewService.RawBandLabel (spec §12.2 convention).
    private static string ComputeBandLabel(int rawTotal)
    {
        if (rawTotal >= 38) return "A";
        if (rawTotal >= 34) return "B+";
        if (rawTotal >= 30) return "B";
        if (rawTotal >= 24) return "C+";
        if (rawTotal >= 18) return "C";
        if (rawTotal >= 12) return "D";
        return "E";
    }

    // ── LLM enrichment (optional; degrades to heuristic) ─────────────────────────────────────────

    private static string BuildUserInput(WritingPreAssessmentRequest request, WritingPreAssessmentResult heuristic)
    {
        var checklist = string.Join(
            "\n",
            request.ChecklistItems.Select(i => $"- [{i.RequiredStatus}] {i.ItemText}"));

        return
            "Score this OET letter on six criteria and respond ONLY with compact JSON of the form " +
            "{\"c1\":n,\"c2\":n,\"c3\":n,\"c4\":n,\"c5\":n,\"c6\":n} " +
            "(c1 Purpose 0-3; c2 Content 0-7; c3 Conciseness 0-7; c4 Genre 0-7; c5 Organisation 0-7; c6 Language 0-7).\n\n" +
            $"Task: {request.Scenario.Title}\n" +
            $"Word guide: {request.Scenario.WordGuideMin}-{request.Scenario.WordGuideMax}\n" +
            $"Checklist:\n{checklist}\n\n" +
            $"Heuristic reference: raw {heuristic.EstimatedRawTotal} band {heuristic.EstimatedBandLabel}.\n\n" +
            $"Letter:\n{request.LetterText}";
    }

    /// <summary>
    /// When the LLM returns parseable per-criterion scores, adopt them (clamped) and mark
    /// source="llm" with confidence bumped to "medium". Anything unparseable falls through to the
    /// heuristic unchanged. The heuristic's coverage/length/language analysis is preserved.
    /// </summary>
    private static WritingPreAssessmentResult MergeLlm(WritingPreAssessmentResult heuristic, string completion)
    {
        var bands = TryParseBands(completion);
        if (bands is null)
        {
            return heuristic;
        }

        var rawTotal = bands.RawTotal;
        return heuristic with
        {
            EstimatedBands = bands,
            EstimatedRawTotal = rawTotal,
            EstimatedBandLabel = ComputeBandLabel(rawTotal),
            Confidence = "medium",
            Source = "llm",
        };
    }

    private static WritingCriteriaScores? TryParseBands(string completion)
    {
        try
        {
            var start = completion.IndexOf('{');
            var end = completion.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                return null;
            }

            var json = completion.Substring(start, end - start + 1);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            int Read(string shortKey, string longKey, int max)
            {
                if ((root.TryGetProperty(shortKey, out var el) || root.TryGetProperty(longKey, out el))
                    && el.TryGetInt32(out var v))
                {
                    return Clamp(v, 0, max);
                }
                return 0;
            }

            return new WritingCriteriaScores(
                Read("c1", "c1Purpose", MaxC1),
                Read("c2", "c2Content", MaxOther),
                Read("c3", "c3Conciseness", MaxOther),
                Read("c4", "c4Genre", MaxOther),
                Read("c5", "c5Organisation", MaxOther),
                Read("c6", "c6Language", MaxOther));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── Shared helpers ───────────────────────────────────────────────────────────────────────────

    private static List<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        return WordRegex.Matches(text)
            .Select(m => m.Value)
            .Where(IsSignificant)
            .Select(t => t.ToLower(CultureInfo.InvariantCulture))
            .ToList();
    }

    private static bool IsSignificant(string token)
        => token.Length >= MinTokenLength && !StopWords.Contains(token);

    private static int CountWords(string? text)
        => string.IsNullOrWhiteSpace(text) ? 0 : WordRegex.Matches(text).Count;

    private static int Clamp(int value, int min, int max)
        => value < min ? min : (value > max ? max : value);

    // The grounding builder expects a rulebook letter-type token; fall back to a routine referral
    // when the scenario uses the internal LT-xx code set (the grounding layer only needs a hint).
    private static string NormalizeLetterTypeForGrounding(string? letterType)
        => string.IsNullOrWhiteSpace(letterType) ? "routine_referral" : letterType!;
}
