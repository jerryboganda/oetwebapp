using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Result of a Speaking pre-analysis pass. Pre-fill only — NOT
/// authoritative; the expert reviewer remains the source of truth and
/// must explicitly accept, adjust, or override these signals before
/// they enter the final rubric.
/// </summary>
/// <param name="FluencyScore">Heuristic fluency band (0-6) — derived from
/// filler/hesitation density on the latest transcript.</param>
/// <param name="IntelligibilityScore">Heuristic intelligibility band (0-6) —
/// derived from ASR mean-confidence and word-count proxies.</param>
/// <param name="AppropriatenessScore">Heuristic appropriateness band (0-6) —
/// presence of greeting/sign-off/professional register markers.</param>
/// <param name="PatientPerspectiveScore">Heuristic patient-perspective
/// band (0-3) — empathy/check-back phrase density.</param>
/// <param name="Notes">Human-readable rationale shown to the expert
/// reviewer as a pre-fill hint. Never persisted as final feedback.</param>
public sealed record SpeakingPreAnalysisResult(
    int FluencyScore,
    int IntelligibilityScore,
    int AppropriatenessScore,
    int PatientPerspectiveScore,
    string Notes);

/// <summary>
/// Pre-analyses a Speaking session (transcripts + lightweight recording
/// metadata) and emits a draft set of band signals the expert reviewer
/// can use as a starting point.
///
/// IMPORTANT: pre-fill only, not authoritative. The heuristics here are
/// intentionally simple stubs — they exist to give the reviewer
/// something better than a blank rubric on first load, NOT to score the
/// candidate. Real scoring stays in <c>SpeakingEvaluationPipeline</c>
/// and the expert review flow.
/// </summary>
public interface ISpeakingPreAnalysisService
{
    /// <summary>
    /// Run pre-analysis for the given Speaking session. If a linked
    /// <see cref="ReviewRequest"/> can be located (via the session's
    /// wrapped <c>AttemptId</c>), the result is also pushed into the
    /// matching <see cref="ExpertReviewDraft.RubricEntriesJson"/> as a
    /// pre-fill — but ONLY when the draft is still in <c>editing</c>
    /// state and the rubric is empty/unchanged.
    /// </summary>
    Task<SpeakingPreAnalysisResult> AnalyseAsync(string speakingSessionId, CancellationToken ct);
}

public sealed class SpeakingPreAnalysisService : ISpeakingPreAnalysisService
{
    private readonly LearnerDbContext _db;
    private readonly ILogger<SpeakingPreAnalysisService> _logger;

    // Common English fillers + hesitation markers. Intentionally short —
    // expand once we have ASR ground-truth corpora to validate against.
    private static readonly string[] FillerTokens =
    {
        "um", "uh", "er", "erm", "ah", "hmm", "like", "you know", "i mean", "kind of", "sort of"
    };

    // Clinical-empathy / patient-perspective phrase fragments. Lower-cased
    // and matched on word boundaries. Pre-fill heuristic only.
    private static readonly string[] EmpathyMarkers =
    {
        "how are you feeling",
        "i understand",
        "that must be",
        "i'm sorry",
        "let me check",
        "would you like",
        "do you have any questions",
        "is there anything else",
        "i'd like to explain",
        "what's been worrying you",
        "let me know if",
        "are you comfortable",
        "i can imagine",
        "thank you for",
    };

    // Register / sign-off markers used for the appropriateness heuristic.
    private static readonly string[] RegisterMarkers =
    {
        "good morning",
        "good afternoon",
        "my name is",
        "thank you",
        "if you have any questions",
        "take care",
        "have a good day",
    };

    public SpeakingPreAnalysisService(
        LearnerDbContext db,
        ILogger<SpeakingPreAnalysisService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<SpeakingPreAnalysisResult> AnalyseAsync(string speakingSessionId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speakingSessionId);

        var session = await _db.SpeakingSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == speakingSessionId, ct);

        if (session is null)
        {
            _logger.LogInformation("Speaking pre-analysis skipped: session {SessionId} not found.", speakingSessionId);
            return EmptyResult("Speaking session not found; nothing to pre-analyse.");
        }

        var transcript = await _db.SpeakingTranscripts
            .AsNoTracking()
            .Where(t => t.SpeakingSessionId == speakingSessionId && t.IsLatest)
            .OrderByDescending(t => t.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        if (transcript is null || string.IsNullOrWhiteSpace(transcript.SegmentsJson) || transcript.SegmentsJson == "[]")
        {
            _logger.LogInformation(
                "Speaking pre-analysis skipped: no latest transcript for session {SessionId}.",
                speakingSessionId);
            return EmptyResult("No transcript available yet — pre-analysis will run once ASR completes.");
        }

        var candidateText = ExtractCandidateText(transcript.SegmentsJson);
        var wordCount = transcript.WordCount > 0
            ? transcript.WordCount
            : CountWords(candidateText);
        var meanConfidence = transcript.MeanConfidence;

        var result = ComputeBands(candidateText, wordCount, meanConfidence);

        // Best-effort persistence: pre-fill the expert review draft (if one
        // exists and is still in editing state). Failures here MUST NOT
        // surface to the caller — pre-analysis is advisory only.
        if (!string.IsNullOrWhiteSpace(session.AttemptId))
        {
            try
            {
                await TryPrefillDraftAsync(session.AttemptId!, result, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Speaking pre-analysis could not pre-fill expert draft for session {SessionId}.",
                    speakingSessionId);
            }
        }

        return result;
    }

    private SpeakingPreAnalysisResult ComputeBands(string candidateText, int wordCount, double meanConfidence)
    {
        if (wordCount < 20)
        {
            return new SpeakingPreAnalysisResult(
                FluencyScore: 0,
                IntelligibilityScore: 0,
                AppropriatenessScore: 0,
                PatientPerspectiveScore: 0,
                Notes: $"Transcript is too short ({wordCount} words) for reliable pre-analysis. Reviewer to assess manually.");
        }

        var lower = candidateText.ToLowerInvariant();

        // --- Fluency: filler density → band (0-6) ---
        var fillerCount = CountPhraseOccurrences(lower, FillerTokens);
        var fillerPer100 = wordCount > 0 ? fillerCount * 100.0 / wordCount : 0.0;
        var fluencyScore = fillerPer100 switch
        {
            < 1.0 => 6,
            < 2.0 => 5,
            < 3.5 => 4,
            < 5.0 => 3,
            < 7.5 => 2,
            < 10.0 => 1,
            _ => 0,
        };

        // --- Intelligibility: ASR mean-confidence proxy → band (0-6) ---
        // Mean confidence sits in [0,1]; treat <0.5 as unintelligible-ish.
        var intelligibilityScore = meanConfidence switch
        {
            >= 0.95 => 6,
            >= 0.90 => 5,
            >= 0.80 => 4,
            >= 0.70 => 3,
            >= 0.60 => 2,
            >= 0.50 => 1,
            _ => 0,
        };

        // --- Appropriateness: register/sign-off markers → band (0-6) ---
        var registerHits = CountPhraseOccurrences(lower, RegisterMarkers);
        var appropriatenessScore = registerHits switch
        {
            >= 4 => 6,
            3 => 5,
            2 => 4,
            1 => 3,
            _ => 2,
        };

        // --- Patient perspective: empathy markers → band (0-3) ---
        var empathyHits = CountPhraseOccurrences(lower, EmpathyMarkers);
        var patientPerspectiveScore = empathyHits switch
        {
            >= 5 => 3,
            >= 3 => 2,
            >= 1 => 1,
            _ => 0,
        };

        var notes = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "Pre-fill heuristic (advisory only): {0} words, mean ASR confidence {1:F2}, " +
            "{2} fillers ({3:F1} / 100 words), {4} register markers, {5} empathy markers. " +
            "Reviewer to confirm or override before submitting.",
            wordCount,
            meanConfidence,
            fillerCount,
            fillerPer100,
            registerHits,
            empathyHits);

        return new SpeakingPreAnalysisResult(
            FluencyScore: fluencyScore,
            IntelligibilityScore: intelligibilityScore,
            AppropriatenessScore: appropriatenessScore,
            PatientPerspectiveScore: patientPerspectiveScore,
            Notes: notes);
    }

    /// <summary>
    /// Locate the ExpertReviewDraft tied to this Speaking session (via
    /// Attempt → ReviewRequest → Draft) and stamp the pre-analysis bands
    /// into <see cref="ExpertReviewDraft.RubricEntriesJson"/> only if the
    /// draft is still in <c>editing</c> state and rubric entries have not
    /// yet been authored. We never overwrite human edits.
    /// </summary>
    private async Task TryPrefillDraftAsync(string attemptId, SpeakingPreAnalysisResult result, CancellationToken ct)
    {
        var reviewRequest = await _db.ReviewRequests
            .AsNoTracking()
            .Where(r => r.AttemptId == attemptId && r.SubtestCode == "speaking")
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (reviewRequest is null)
        {
            _logger.LogDebug(
                "No speaking ReviewRequest found for attempt {AttemptId}; skipping draft pre-fill.",
                attemptId);
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

        // Refuse to overwrite reviewer edits: only pre-fill when the rubric
        // is empty/default-shaped.
        if (!IsBlankRubricJson(draft.RubricEntriesJson))
        {
            _logger.LogDebug(
                "ExpertReviewDraft {DraftId} already has rubric content; skipping speaking pre-fill.",
                draft.Id);
            return;
        }

        var payload = new
        {
            source = "speaking_pre_analysis_v1",
            authoritative = false,
            generatedAt = DateTimeOffset.UtcNow,
            bands = new
            {
                fluency = result.FluencyScore,
                intelligibility = result.IntelligibilityScore,
                appropriateness = result.AppropriatenessScore,
                patientPerspective = result.PatientPerspectiveScore,
            },
            notes = result.Notes,
        };

        draft.RubricEntriesJson = JsonSerializer.Serialize(payload);
        draft.DraftSavedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Speaking pre-analysis pre-filled draft {DraftId} (request {ReviewRequestId}).",
            draft.Id,
            reviewRequest.Id);
    }

    private static SpeakingPreAnalysisResult EmptyResult(string note) =>
        new(FluencyScore: 0,
            IntelligibilityScore: 0,
            AppropriatenessScore: 0,
            PatientPerspectiveScore: 0,
            Notes: note);

    private static string ExtractCandidateText(string segmentsJson)
    {
        // Segments shape: [{speaker, startMs, endMs, text, confidence, ...}, ...].
        // We pull every segment's `text`, skipping non-candidate speakers
        // (anything that looks like "interlocutor"/"examiner"/"patient").
        try
        {
            using var doc = JsonDocument.Parse(segmentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var speaker = element.TryGetProperty("speaker", out var sp)
                    ? sp.GetString()?.ToLowerInvariant() ?? string.Empty
                    : string.Empty;

                if (IsNonCandidateSpeaker(speaker))
                {
                    continue;
                }

                if (element.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                {
                    var text = textProp.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        parts.Add(text);
                    }
                }
            }

            return string.Join(' ', parts);
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static bool IsNonCandidateSpeaker(string speaker)
    {
        if (string.IsNullOrEmpty(speaker)) return false;
        return speaker.Contains("interlocutor", StringComparison.Ordinal)
            || speaker.Contains("examiner", StringComparison.Ordinal)
            || speaker.Contains("patient", StringComparison.Ordinal)
            || speaker.Contains("tutor", StringComparison.Ordinal)
            || speaker.Contains("ai", StringComparison.Ordinal);
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return Regex.Matches(text, "\\b[\\w']+\\b").Count;
    }

    private static int CountPhraseOccurrences(string lowerText, IReadOnlyList<string> phrases)
    {
        if (string.IsNullOrEmpty(lowerText)) return 0;

        var total = 0;
        foreach (var phrase in phrases)
        {
            if (string.IsNullOrEmpty(phrase)) continue;

            // For single-word fillers, require word boundaries to avoid
            // matching substrings ("um" inside "umbrella").
            if (!phrase.Contains(' '))
            {
                var pattern = "\\b" + Regex.Escape(phrase) + "\\b";
                total += Regex.Matches(lowerText, pattern).Count;
            }
            else
            {
                var index = 0;
                while ((index = lowerText.IndexOf(phrase, index, StringComparison.Ordinal)) >= 0)
                {
                    total++;
                    index += phrase.Length;
                }
            }
        }

        return total;
    }

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
                // Treat as blank if there are zero properties or only a
                // previous pre-fill marker we authored.
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
