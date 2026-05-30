using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Backs <c>GET /v1/writing/evaluations/{evaluationId}/dual-assessment</c>.
/// Returns the AI track (from <see cref="Domain.Evaluation"/>) and — when an
/// expert has submitted a review — the Tutor track (from the latest
/// <see cref="Domain.ExpertReviewDraft"/> with <c>State='submitted'</c>).
/// Either track may be null and the learner UI handles all four combinations.
/// </summary>
public sealed class WritingDualAssessmentService(LearnerDbContext db)
{
    private static readonly IReadOnlyList<(string Code, int Max)> Criteria = new[]
    {
        ("purpose", 3),
        ("content", 7),
        ("conciseness", 7),
        ("genre", 7),
        ("organization", 7),
        ("language", 7),
    };

    public async Task<WritingDualAssessmentDto?> GetAsync(
        string userId, string evaluationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(evaluationId))
            return null;

        // 1. Evaluation + IDOR check.
        var ev = await db.Evaluations
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == evaluationId && e.SubtestCode == "writing", ct);
        if (ev is null) return null;

        var owned = await db.Attempts
            .AsNoTracking()
            .AnyAsync(a => a.Id == ev.AttemptId && a.UserId == userId, ct);
        if (!owned) return null;

        // 2. Submitted tutor draft (if any).
        var review = await db.ReviewRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.AttemptId == ev.AttemptId && r.SubtestCode == "writing", ct);

        Domain.ExpertReviewDraft? submittedDraft = null;
        string? tutorName = null;
        if (review is not null)
        {
            submittedDraft = await db.ExpertReviewDrafts
                .AsNoTracking()
                .Where(d => d.ReviewRequestId == review.Id && d.State == "submitted")
                .OrderByDescending(d => d.DraftSavedAt)
                .FirstOrDefaultAsync(ct);
            if (submittedDraft is not null)
            {
                tutorName = await db.ExpertUsers
                    .AsNoTracking()
                    .Where(u => u.Id == submittedDraft.ReviewerId)
                    .Select(u => u.DisplayName)
                    .FirstOrDefaultAsync(ct);
            }
        }

        // 3. Parse AI scores from CriterionScoresJson.
        var aiScores = ParseAiCriteria(ev.CriterionScoresJson);
        var aiTrack = new WritingAiTrackDto(
            AssessmentId: ev.Id,
            GeneratedAt: ev.GeneratedAt ?? ev.CreatedAt,
            ConfidenceBand: ev.ConfidenceBand.ToString().ToLowerInvariant(),
            ScoreRange: ev.ScoreRange,
            GradeRange: ev.GradeRange,
            CriterionScores: aiScores,
            IsAdvisory: true);

        // 4. Tutor track when available.
        WritingTutorTrackDto? tutorTrack = null;
        Dictionary<string, int>? tutorScores = null;
        if (submittedDraft is not null)
        {
            tutorScores = ParseTutorRubric(submittedDraft.RubricEntriesJson);
            tutorTrack = new WritingTutorTrackDto(
                AssessmentId: submittedDraft.Id,
                TutorId: submittedDraft.ReviewerId,
                TutorName: tutorName ?? string.Empty,
                CriterionScores: Criteria.ToDictionary(
                    c => c.Code,
                    c => new WritingCriterionScoreDto(
                        tutorScores.TryGetValue(c.Code, out var v) ? v : 0,
                        c.Max,
                        null,
                        null)),
                OverallFeedback: submittedDraft.FinalCommentDraft,
                IsFinal: true,
                SubmittedAt: submittedDraft.DraftSavedAt);
        }

        // 5. Divergence (only when both tracks present).
        WritingDivergenceDto? divergence = null;
        if (tutorScores is not null)
        {
            divergence = ComputeDivergence(aiScores, tutorScores);
        }

        return new WritingDualAssessmentDto(
            EvaluationId: ev.Id,
            AttemptId: ev.AttemptId,
            SubtestCode: "writing",
            Ai: aiTrack,
            Tutor: tutorTrack,
            Divergence: divergence);
    }

    private static Dictionary<string, WritingCriterionScoreDto> ParseAiCriteria(string? json)
    {
        var result = new Dictionary<string, WritingCriterionScoreDto>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                var code = TryReadString(entry, "criterionCode") ?? TryReadString(entry, "code");
                if (string.IsNullOrWhiteSpace(code)) continue;
                var score = TryReadInt(entry, "score") ?? 0;
                var max = TryReadInt(entry, "maxScore") ?? DefaultMaxFor(code);
                var rationale = TryReadString(entry, "rationale");
                var evidence = TryReadStringArray(entry, "evidenceQuotes");
                result[code.ToLowerInvariant()] = new WritingCriterionScoreDto(score, max, rationale, evidence);
            }
        }
        catch (JsonException) { /* malformed JSON — return what we parsed so far */ }

        // Fill any missing canonical criteria with zero so the UI always has 6 rows.
        foreach (var (code, max) in Criteria)
        {
            if (!result.ContainsKey(code))
            {
                result[code] = new WritingCriterionScoreDto(0, max, null, null);
            }
        }
        return result;
    }

    private static Dictionary<string, int> ParseTutorRubric(string? json)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var direct))
                {
                    result[prop.Name.ToLowerInvariant()] = direct;
                    continue;
                }
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    var score = TryReadInt(prop.Value, "score");
                    if (score.HasValue) result[prop.Name.ToLowerInvariant()] = score.Value;
                }
            }
        }
        catch (JsonException) { /* fall through */ }
        return result;
    }

    private static WritingDivergenceDto ComputeDivergence(
        IReadOnlyDictionary<string, WritingCriterionScoreDto> ai,
        IReadOnlyDictionary<string, int> tutor)
    {
        var perCriterion = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var totalAi = 0;
        var totalTutor = 0;
        foreach (var (code, _) in Criteria)
        {
            var aiScore = ai.TryGetValue(code, out var a) ? a.Score : 0;
            var tutorScore = tutor.TryGetValue(code, out var t) ? t : 0;
            perCriterion[code] = tutorScore - aiScore;
            totalAi += aiScore;
            totalTutor += tutorScore;
        }
        var scaledDelta = totalTutor - totalAi;
        var maxDelta = perCriterion.Values.Select(Math.Abs).DefaultIfEmpty(0).Max();
        // Mirror Speaking thresholds: ≤1 close, ≤2 moderate, otherwise wide.
        var band = maxDelta <= 1 ? "close" : maxDelta <= 2 ? "moderate" : "wide";
        return new WritingDivergenceDto(perCriterion, scaledDelta, band);
    }

    private static int DefaultMaxFor(string code) =>
        string.Equals(code, "purpose", StringComparison.OrdinalIgnoreCase) ? 3 : 7;

    private static string? TryReadString(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    private static int? TryReadInt(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var prop)) return null;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var v)) return v;
        return null;
    }

    private static IReadOnlyList<string>? TryReadStringArray(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var prop)) return null;
        if (prop.ValueKind != JsonValueKind.Array) return null;
        return prop.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    /// <summary>
    /// Parse a tutor score-override JSON blob (as persisted on
    /// <c>WritingTutorReview.ScoreOverrideJson</c>) into a canonical
    /// <c>c1</c>..<c>c6</c> → score map. Tolerates short (<c>c1</c>..<c>c6</c>),
    /// long (<c>c1Purpose</c>..<c>c6Language</c>) and criterion-name
    /// (<c>purpose</c>..<c>language</c>) key forms, and either a numeric value or
    /// an object carrying a <c>score</c> property. Returns an empty map for
    /// null/blank/malformed input or when no recognised criterion is present.
    /// </summary>
    public static Dictionary<string, int> ParseOverride(string? json)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var canonical = CanonicalCriterionKey(prop.Name);
                if (canonical is null) continue;
                int? score = prop.Value.ValueKind switch
                {
                    JsonValueKind.Number when prop.Value.TryGetInt32(out var n) => n,
                    JsonValueKind.Object => TryReadInt(prop.Value, "score"),
                    _ => null,
                };
                if (score.HasValue) result[canonical] = score.Value;
            }
        }
        catch (JsonException) { /* malformed — return what parsed so far */ }
        return result;
    }

    /// <summary>Sum of the canonical c1..c6 override scores (0 when none present).</summary>
    public static int SumOverride(string? json) => ParseOverride(json).Values.Sum();

    private static string? CanonicalCriterionKey(string key) => key.Trim().ToLowerInvariant() switch
    {
        "c1" or "c1purpose" or "purpose" => "c1",
        "c2" or "c2content" or "content" => "c2",
        "c3" or "c3conciseness" or "conciseness" => "c3",
        "c4" or "c4genre" or "genre" => "c4",
        "c5" or "c5organisation" or "c5organization" or "organisation" or "organization" => "c5",
        "c6" or "c6language" or "language" => "c6",
        _ => null,
    };
}

public sealed record WritingDualAssessmentDto(
    string EvaluationId,
    string AttemptId,
    string SubtestCode,
    WritingAiTrackDto Ai,
    WritingTutorTrackDto? Tutor,
    WritingDivergenceDto? Divergence);

public sealed record WritingAiTrackDto(
    string AssessmentId,
    DateTimeOffset GeneratedAt,
    string ConfidenceBand,
    string ScoreRange,
    string? GradeRange,
    IReadOnlyDictionary<string, WritingCriterionScoreDto> CriterionScores,
    bool IsAdvisory);

public sealed record WritingTutorTrackDto(
    string AssessmentId,
    string TutorId,
    string TutorName,
    IReadOnlyDictionary<string, WritingCriterionScoreDto> CriterionScores,
    string? OverallFeedback,
    bool IsFinal,
    DateTimeOffset SubmittedAt);

public sealed record WritingCriterionScoreDto(
    int Score,
    int MaxScore,
    string? Rationale,
    IReadOnlyList<string>? EvidenceQuotes);

public sealed record WritingDivergenceDto(
    IReadOnlyDictionary<string, int> PerCriterion,
    int ScaledDelta,
    string AgreementBand);
