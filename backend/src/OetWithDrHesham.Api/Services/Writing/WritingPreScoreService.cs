using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Writing;

/// <summary>
/// Result of a Writing pre-score pass. Pre-fill only — NOT
/// authoritative; the expert reviewer remains the source of truth and
/// must explicitly accept, adjust, or override these signals before
/// they enter the final rubric.
/// </summary>
/// <param name="Purpose">Heuristic band for Purpose (0-3).</param>
/// <param name="Content">Heuristic band for Content (0-7).</param>
/// <param name="ConcisenessAndClarity">Heuristic band for Conciseness &amp;
/// Clarity (0-7).</param>
/// <param name="GenreAndStyle">Heuristic band for Genre &amp; Style (0-7).</param>
/// <param name="OrganisationAndLayout">Heuristic band for Organisation &amp;
/// Layout (0-7).</param>
/// <param name="Language">Heuristic band for Language (0-7).</param>
/// <param name="Rationale">Per-criterion plain-English justification the
/// reviewer can use as a pre-fill hint. Never persisted as final feedback.</param>
public sealed record WritingPreScoreResult(
    int Purpose,
    int Content,
    int ConcisenessAndClarity,
    int GenreAndStyle,
    int OrganisationAndLayout,
    int Language,
    IReadOnlyDictionary<string, string> Rationale);

/// <summary>
/// Pre-scores a Writing submission and emits preliminary per-criterion
/// bands the expert reviewer can use as a starting point.
///
/// IMPORTANT: pre-fill only, not authoritative. The heuristics here are
/// length / structure proxies and shallow lexical flags — they exist to
/// avoid a blank rubric on first load, NOT to score the candidate. Real
/// scoring stays in <c>WritingEvaluationPipeline</c> and the expert
/// review flow.
/// </summary>
public interface IWritingPreScoreService
{
    /// <summary>
    /// Run pre-scoring for the given Writing attempt. If a linked
    /// <see cref="ReviewRequest"/> can be located, the result is also
    /// pushed into the matching <see cref="ExpertReviewDraft.RubricEntriesJson"/>
    /// as a pre-fill — but ONLY when the draft is still in
    /// <c>editing</c> state and the rubric is empty/unchanged.
    /// </summary>
    Task<WritingPreScoreResult> ScoreAsync(string writingAttemptId, CancellationToken ct);
}

public sealed class WritingPreScoreService : IWritingPreScoreService
{
    private const string SubtestCode = "writing";
    private const int IdealMin = 180;
    private const int IdealMax = 200;

    // Lexical filler/hedging markers used as a (very approximate)
    // conciseness-and-clarity proxy.
    private static readonly string[] FillerPhrases =
    {
        "in order to",
        "due to the fact that",
        "at this point in time",
        "for the purpose of",
        "the fact that",
        "it should be noted that",
        "as a matter of fact",
        "in spite of the fact that",
    };

    // Letter / referral genre markers — presence raises the
    // genre-and-style proxy.
    private static readonly string[] GenreMarkers =
    {
        "dear",
        "yours sincerely",
        "yours faithfully",
        "thank you for your attention",
        "i am writing to refer",
        "i am writing to request",
        "kind regards",
    };

    // Paragraph-shape markers used as a layout / organisation proxy.
    private static readonly Regex ParagraphSplit = new(@"\n\s*\n", RegexOptions.Compiled);

    private readonly LearnerDbContext _db;
    private readonly ILogger<WritingPreScoreService> _logger;

    public WritingPreScoreService(LearnerDbContext db, ILogger<WritingPreScoreService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<WritingPreScoreResult> ScoreAsync(string writingAttemptId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(writingAttemptId);

        var attempt = await _db.Attempts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == writingAttemptId, ct);

        if (attempt is null)
        {
            _logger.LogInformation("Writing pre-score skipped: attempt {AttemptId} not found.", writingAttemptId);
            return EmptyResult("Writing attempt not found; nothing to pre-score.");
        }

        if (!string.Equals(attempt.SubtestCode, SubtestCode, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Writing pre-score skipped: attempt {AttemptId} is subtest {Subtest}, not writing.",
                writingAttemptId,
                attempt.SubtestCode);
            return EmptyResult($"Attempt is for subtest '{attempt.SubtestCode}', not writing.");
        }

        var text = ExtractSubmissionText(attempt);

        if (string.IsNullOrWhiteSpace(text))
        {
            // Fall back to typed asset extracts (OCR'd handwritten pages).
            text = await ExtractAssetTextAsync(writingAttemptId, ct);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogInformation(
                "Writing pre-score skipped: no extractable text for attempt {AttemptId}.",
                writingAttemptId);
            return EmptyResult("No submission text available yet — pre-score will run once extraction completes.");
        }

        var result = ComputeBands(text);

        try
        {
            await TryPrefillDraftAsync(writingAttemptId, result, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Writing pre-score could not pre-fill expert draft for attempt {AttemptId}.",
                writingAttemptId);
        }

        return result;
    }

    private WritingPreScoreResult ComputeBands(string text)
    {
        var wordCount = CountWords(text);
        var lower = text.ToLowerInvariant();
        var paragraphCount = ParagraphSplit.Split(text.Trim()).Count(p => !string.IsNullOrWhiteSpace(p));

        // --- Purpose (0-3): does the opening declare a purpose? ---
        var openingHas = lower.Contains("i am writing", StringComparison.Ordinal)
                          || lower.Contains("i am referring", StringComparison.Ordinal)
                          || lower.Contains("re:", StringComparison.Ordinal);
        var purpose = wordCount switch
        {
            < 40 => 0,
            < 100 => openingHas ? 2 : 1,
            < 160 => openingHas ? 3 : 2,
            _ => openingHas ? 3 : 2,
        };

        // --- Content (0-7): length-based proxy bands ---
        var content = wordCount switch
        {
            < 60 => 1,
            < 100 => 2,
            < 140 => 3,
            < 170 => 4,
            < IdealMin => 5,
            <= IdealMax => 6,
            < 240 => 6,
            < 280 => 5,
            _ => 4,
        };

        // --- Conciseness & Clarity (0-7): filler-phrase density ---
        var fillerCount = CountPhraseOccurrences(lower, FillerPhrases);
        var fillerPer100 = wordCount > 0 ? fillerCount * 100.0 / wordCount : 0.0;
        var conciseness = fillerPer100 switch
        {
            <= 0.5 => 7,
            <= 1.0 => 6,
            <= 2.0 => 5,
            <= 3.5 => 4,
            <= 5.0 => 3,
            <= 7.0 => 2,
            _ => 1,
        };

        // --- Genre & Style (0-7): letter/referral markers present? ---
        var genreHits = CountPhraseOccurrences(lower, GenreMarkers);
        var genre = genreHits switch
        {
            >= 4 => 7,
            3 => 6,
            2 => 5,
            1 => 3,
            _ => 1,
        };

        // --- Organisation & Layout (0-7): paragraph count proxy ---
        var organisation = paragraphCount switch
        {
            >= 5 => 7,
            4 => 6,
            3 => 5,
            2 => 3,
            1 => 2,
            _ => 1,
        };

        // --- Language (0-7): coarse sentence-length + length proxy ---
        var sentences = SplitSentences(text);
        var avgSentenceLength = sentences.Count > 0 ? (double)wordCount / sentences.Count : 0.0;
        var languageBaseline = wordCount switch
        {
            < 60 => 1,
            < 120 => 2,
            < 170 => 3,
            _ => 4,
        };
        var languageBoost = avgSentenceLength switch
        {
            < 6 => -1,   // choppy
            < 12 => 0,   // ok
            < 22 => 1,   // varied
            < 32 => 0,   // long
            _ => -1,     // run-on
        };
        var language = Clamp(languageBaseline + languageBoost, 0, 7);

        var rationale = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["purpose"] = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Pre-fill (advisory): opening purpose marker {0}, {1} words.",
                openingHas ? "present" : "missing",
                wordCount),
            ["content"] = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Pre-fill (advisory): {0} words; target band is {1}-{2}. Reviewer to assess relevance and selection.",
                wordCount,
                IdealMin,
                IdealMax),
            ["concisenessAndClarity"] = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Pre-fill (advisory): {0} filler phrase(s) detected ({1:F1} / 100 words).",
                fillerCount,
                fillerPer100),
            ["genreAndStyle"] = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Pre-fill (advisory): {0} letter/referral convention marker(s) found.",
                genreHits),
            ["organisationAndLayout"] = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Pre-fill (advisory): {0} paragraph(s) detected.",
                paragraphCount),
            ["language"] = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Pre-fill (advisory): {0} sentence(s), avg {1:F1} words per sentence.",
                sentences.Count,
                avgSentenceLength),
        };

        return new WritingPreScoreResult(
            Purpose: Clamp(purpose, 0, 3),
            Content: Clamp(content, 0, 7),
            ConcisenessAndClarity: Clamp(conciseness, 0, 7),
            GenreAndStyle: Clamp(genre, 0, 7),
            OrganisationAndLayout: Clamp(organisation, 0, 7),
            Language: Clamp(language, 0, 7),
            Rationale: rationale);
    }

    /// <summary>
    /// Locate the ExpertReviewDraft tied to this Writing attempt and
    /// stamp the pre-score into <see cref="ExpertReviewDraft.RubricEntriesJson"/>
    /// only if the draft is still in <c>editing</c> state and the
    /// rubric has not yet been authored. Never overwrites human edits.
    /// </summary>
    private async Task TryPrefillDraftAsync(string writingAttemptId, WritingPreScoreResult result, CancellationToken ct)
    {
        var reviewRequest = await _db.ReviewRequests
            .AsNoTracking()
            .Where(r => r.AttemptId == writingAttemptId && r.SubtestCode == SubtestCode)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (reviewRequest is null)
        {
            _logger.LogDebug(
                "No writing ReviewRequest found for attempt {AttemptId}; skipping draft pre-fill.",
                writingAttemptId);
            return;
        }

        var draft = await _db.ExpertReviewDrafts
            .Where(d => d.ReviewRequestId == reviewRequest.Id && d.State == "editing")
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync(ct);

        if (draft is null)
        {
            _logger.LogDebug(
                "No editable ExpertReviewDraft for ReviewRequest {ReviewRequestId}; skipping pre-fill.",
                reviewRequest.Id);
            return;
        }

        if (!IsBlankRubricJson(draft.RubricEntriesJson))
        {
            _logger.LogDebug(
                "ExpertReviewDraft {DraftId} already has rubric content; skipping writing pre-fill.",
                draft.Id);
            return;
        }

        var payload = new
        {
            source = "writing_pre_score_v1",
            authoritative = false,
            generatedAt = DateTimeOffset.UtcNow,
            bands = new
            {
                purpose = result.Purpose,
                content = result.Content,
                concisenessAndClarity = result.ConcisenessAndClarity,
                genreAndStyle = result.GenreAndStyle,
                organisationAndLayout = result.OrganisationAndLayout,
                language = result.Language,
            },
            rationale = result.Rationale,
        };

        draft.RubricEntriesJson = JsonSerializer.Serialize(payload);
        draft.DraftSavedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Writing pre-score pre-filled draft {DraftId} (request {ReviewRequestId}).",
            draft.Id,
            reviewRequest.Id);
    }

    private static WritingPreScoreResult EmptyResult(string note)
    {
        var rationale = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["purpose"] = note,
            ["content"] = note,
            ["concisenessAndClarity"] = note,
            ["genreAndStyle"] = note,
            ["organisationAndLayout"] = note,
            ["language"] = note,
        };
        return new WritingPreScoreResult(0, 0, 0, 0, 0, 0, rationale);
    }

    private static string ExtractSubmissionText(Attempt attempt)
    {
        // Writing submissions land in DraftContent (typed paths) or in
        // AnswersJson (mock-set / structured paths).
        if (!string.IsNullOrWhiteSpace(attempt.DraftContent))
        {
            return attempt.DraftContent;
        }

        if (!string.IsNullOrWhiteSpace(attempt.AnswersJson) && attempt.AnswersJson != "{}")
        {
            try
            {
                using var doc = JsonDocument.Parse(attempt.AnswersJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            var s = prop.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(s) && s.Length > 40)
                            {
                                return s;
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Fall through to asset extraction.
            }
        }

        return string.Empty;
    }

    private async Task<string> ExtractAssetTextAsync(string attemptId, CancellationToken ct)
    {
        var pages = await _db.WritingAttemptAssets
            .AsNoTracking()
            .Where(p => p.AttemptId == attemptId
                        && p.ExtractionState == "extracted"
                        && p.ExtractedText != null
                        && p.ExtractedText != "")
            .OrderBy(p => p.PageNumber)
            .Select(p => p.ExtractedText)
            .ToListAsync(ct);

        if (pages.Count == 0) return string.Empty;
        return string.Join("\n\n", pages);
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return Regex.Matches(text, "\\b[\\w']+\\b").Count;
    }

    private static List<string> SplitSentences(string text)
    {
        var raw = Regex.Split(text, @"(?<=[\.!?])\s+");
        return raw.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }

    private static int CountPhraseOccurrences(string lowerText, IReadOnlyList<string> phrases)
    {
        if (string.IsNullOrEmpty(lowerText)) return 0;

        var total = 0;
        foreach (var phrase in phrases)
        {
            if (string.IsNullOrEmpty(phrase)) continue;
            var index = 0;
            while ((index = lowerText.IndexOf(phrase, index, StringComparison.Ordinal)) >= 0)
            {
                total++;
                index += phrase.Length;
            }
        }
        return total;
    }

    private static int Clamp(int value, int min, int max) =>
        value < min ? min : value > max ? max : value;

    private static bool IsBlankRubricJson(string? rubricJson)
    {
        if (string.IsNullOrWhiteSpace(rubricJson)) return true;
        var trimmed = rubricJson.Trim();
        if (trimmed == "{}" || trimmed == "[]" || trimmed == "null") return true;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var hasContent = false;
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.NameEquals("source")) continue;
                    if (prop.NameEquals("authoritative")) continue;
                    if (prop.NameEquals("generatedAt")) continue;
                    hasContent = true;
                    break;
                }
                return !hasContent;
            }
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
