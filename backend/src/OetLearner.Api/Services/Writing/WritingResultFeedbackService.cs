using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Services;

namespace OetLearner.Api.Services.Writing;

// ─────────────────────────────────────────────────────────────────────────────
// Learner-facing gated feedback + rewrite comparison (spec §15.2 / §15.3, WS-B4
// Section D). The result-visibility CONFIG resolver/upsert lives in
// WritingResultVisibilityService; THIS service composes the gated learner bundle
// (owner-checked) and the original↔rewrite delta. Every learner-visible field is
// populated only when the corresponding visibility flag allows it.
//
// All response DTO record types (WritingResultVisibilityDto, WritingGradeDto,
// WritingTutorReviewDto, WritingFeedbackAnnotationDto, WritingContentChecklistItemDto,
// WritingSubmissionSummaryDto, WritingCriteriaScoresDto) are reused from the
// already-built marking/authoring code to avoid duplicate declarations.
// ─────────────────────────────────────────────────────────────────────────────

public interface IWritingResultFeedbackService
{
    /// <summary>Owner-only gated feedback bundle for a submission.</summary>
    Task<WritingSubmissionFeedbackDto> GetFeedbackAsync(string userId, Guid submissionId, CancellationToken ct);

    /// <summary>Owner-only original↔rewrite comparison; the submission must be a revision.</summary>
    Task<WritingRewriteComparisonDto> GetRewriteComparisonAsync(string userId, Guid rewriteSubmissionId, CancellationToken ct);
}

public sealed class WritingResultFeedbackService(
    LearnerDbContext db,
    IWritingResultVisibilityService visibility,
    IWritingHeuristicPreAssessmentService preAssessment,
    ILogger<WritingResultFeedbackService> logger) : IWritingResultFeedbackService
{
    public async Task<WritingSubmissionFeedbackDto> GetFeedbackAsync(string userId, Guid submissionId, CancellationToken ct)
    {
        var submission = await db.WritingSubmissions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == submissionId, ct)
            ?? throw ApiException.NotFound("writing_submission_not_found", "Writing submission was not found.");
        if (!string.Equals(submission.UserId, userId, StringComparison.Ordinal))
        {
            throw ApiException.Forbidden("writing_submission_forbidden", "This submission belongs to another learner.");
        }

        var vis = await visibility.ResolveAsync(submission.ScenarioId, ct);
        var visDto = ToVisibilityDto(vis);

        // Authoritative grade for the submission (latest tutor-attached, else latest AI).
        var grade = await db.WritingGrades
            .AsNoTracking()
            .Where(g => g.SubmissionId == submissionId)
            .OrderByDescending(g => g.TutorReviewId != null)
            .ThenByDescending(g => g.GradedAt)
            .FirstOrDefaultAsync(ct);

        // A submitted tutor review (any marker) — used both for the status and the gated review payload.
        var review = await db.WritingTutorReviews
            .AsNoTracking()
            .Where(r => r.SubmissionId == submissionId && r.Status == "submitted")
            .OrderByDescending(r => r.SubmittedAt)
            .FirstOrDefaultAsync(ct);

        var moderation = await db.WritingModerations
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.SubmissionId == submissionId, ct);

        var tutorFinalised = review is not null
            || (moderation is not null && string.Equals(moderation.Status, "finalized", StringComparison.Ordinal));

        // status: tutor_reviewed > ai_estimated (only if visible) > submitted_awaiting_review.
        string status;
        if (tutorFinalised)
        {
            status = "tutor_reviewed";
        }
        else if (grade is not null && vis.ShowAiEstimate)
        {
            status = "ai_estimated";
        }
        else
        {
            status = "submitted_awaiting_review";
        }

        // ── Gated grade ──────────────────────────────────────────────────────────
        // Tutor score gated by ShowTutorScore; AI estimate gated by ShowAiEstimate.
        WritingGradeDto? gradeDto = null;
        if (grade is not null)
        {
            var isTutorGrade = grade.TutorReviewId is not null || tutorFinalised;
            var gradeVisible = isTutorGrade ? vis.ShowTutorScore : vis.ShowAiEstimate;
            if (gradeVisible)
            {
                gradeDto = MapGrade(grade, vis.ShowFullCriteria);
            }
        }

        // ── Gated tutor review ───────────────────────────────────────────────────
        WritingTutorReviewDto? reviewDto = null;
        if (review is not null && vis.ShowTutorScore)
        {
            reviewDto = MapReview(review);
        }

        // ── Gated annotations ────────────────────────────────────────────────────
        var annotations = new List<WritingFeedbackAnnotationDto>();
        if (vis.ShowAnnotatedResponse)
        {
            var rows = await db.WritingFeedbackAnnotations
                .AsNoTracking()
                .Where(a => a.SubmissionId == submissionId)
                .OrderBy(a => a.StartOffset)
                .ThenBy(a => a.CreatedAt)
                .ToListAsync(ct);
            annotations = rows.Select(MapAnnotation).ToList();
        }

        // ── Missing / irrelevant content (from checklist verdict + pre-assessment) ─
        var missingContent = new List<WritingContentChecklistItemDto>();
        var irrelevantContent = new List<WritingContentChecklistItemDto>();
        if (vis.ShowMissingContent || vis.ShowContentChecklist)
        {
            var checklist = await db.WritingContentChecklistItems
                .AsNoTracking()
                .Where(c => c.ScenarioId == submission.ScenarioId)
                .OrderBy(c => c.Ordinal)
                .ToListAsync(ct);

            var (missingIds, irrelevantIds) = await ResolveContentVerdictAsync(submission, checklist, review, ct);

            missingContent = checklist
                .Where(c => missingIds.Contains(c.Id))
                .Select(MapChecklistItem)
                .ToList();
            irrelevantContent = checklist
                .Where(c => irrelevantIds.Contains(c.Id))
                .Select(MapChecklistItem)
                .ToList();
        }

        // ── Model answer ─────────────────────────────────────────────────────────
        string? modelAnswerText = null;
        if (vis.ShowModelAnswer)
        {
            var scenario = await db.WritingScenarios
                .AsNoTracking()
                .Where(s => s.Id == submission.ScenarioId)
                .Select(s => new { s.ModelAnswerExemplarId })
                .FirstOrDefaultAsync(ct);
            if (scenario?.ModelAnswerExemplarId is { } exemplarId)
            {
                modelAnswerText = await db.WritingExemplars
                    .AsNoTracking()
                    .Where(e => e.Id == exemplarId)
                    .Select(e => e.LetterContent)
                    .FirstOrDefaultAsync(ct);
            }
        }

        // ── Next steps (weakest criteria → short prompts) ─────────────────────────
        var nextSteps = BuildNextSteps(grade, missingContent, irrelevantContent, vis);

        return new WritingSubmissionFeedbackDto(
            MapSubmission(submission),
            visDto,
            status,
            gradeDto,
            reviewDto,
            annotations,
            missingContent,
            irrelevantContent,
            modelAnswerText,
            nextSteps);
    }

    public async Task<WritingRewriteComparisonDto> GetRewriteComparisonAsync(string userId, Guid rewriteSubmissionId, CancellationToken ct)
    {
        var rewrite = await db.WritingSubmissions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == rewriteSubmissionId, ct)
            ?? throw ApiException.NotFound("writing_submission_not_found", "Writing submission was not found.");
        if (!string.Equals(rewrite.UserId, userId, StringComparison.Ordinal))
        {
            throw ApiException.Forbidden("writing_submission_forbidden", "This submission belongs to another learner.");
        }
        if (!rewrite.IsRevision || rewrite.OriginalSubmissionId is not { } originalId)
        {
            throw ApiException.Validation("writing_submission_not_a_revision", "This submission is not a rewrite of an earlier attempt.");
        }

        var original = await db.WritingSubmissions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == originalId, ct)
            ?? throw ApiException.NotFound("writing_original_submission_not_found", "The original submission was not found.");
        if (!string.Equals(original.UserId, userId, StringComparison.Ordinal))
        {
            throw ApiException.Forbidden("writing_submission_forbidden", "The original submission belongs to another learner.");
        }

        var originalGrade = await LatestGradeAsync(original.Id, ct);
        var rewriteGrade = await LatestGradeAsync(rewrite.Id, ct);

        // Per-criterion delta = rewrite − original, only where both grades exist.
        var delta = new Dictionary<string, int>(StringComparer.Ordinal);
        if (originalGrade is not null && rewriteGrade is not null)
        {
            delta["c1"] = rewriteGrade.C1Purpose - originalGrade.C1Purpose;
            delta["c2"] = rewriteGrade.C2Content - originalGrade.C2Content;
            delta["c3"] = rewriteGrade.C3Conciseness - originalGrade.C3Conciseness;
            delta["c4"] = rewriteGrade.C4Genre - originalGrade.C4Genre;
            delta["c5"] = rewriteGrade.C5Organisation - originalGrade.C5Organisation;
            delta["c6"] = rewriteGrade.C6Language - originalGrade.C6Language;
        }

        return new WritingRewriteComparisonDto(
            new WritingRewriteSideDto(
                original.Id.ToString(),
                original.LetterContent,
                originalGrade is null ? null : MapGrade(originalGrade, includeCriteria: true)),
            new WritingRewriteSideDto(
                rewrite.Id.ToString(),
                rewrite.LetterContent,
                rewriteGrade is null ? null : MapGrade(rewriteGrade, includeCriteria: true)),
            delta);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<WritingGrade?> LatestGradeAsync(Guid submissionId, CancellationToken ct)
        => await db.WritingGrades
            .AsNoTracking()
            .Where(g => g.SubmissionId == submissionId)
            .OrderByDescending(g => g.TutorReviewId != null)
            .ThenByDescending(g => g.GradedAt)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Resolves which checklist item ids are "missing" and which are "irrelevant" for the
    /// learner view. The tutor's content-checklist verdict (verdict per item id) is
    /// authoritative when present; otherwise we fall back to the deterministic
    /// pre-assessment (missing-by-text / detected-irrelevant-by-text).
    /// </summary>
    private async Task<(HashSet<Guid> Missing, HashSet<Guid> Irrelevant)> ResolveContentVerdictAsync(
        WritingSubmission submission,
        IReadOnlyList<WritingContentChecklistItem> checklist,
        WritingTutorReview? review,
        CancellationToken ct)
    {
        var missing = new HashSet<Guid>();
        var irrelevant = new HashSet<Guid>();

        var verdict = review is null ? null : DeserializeStringMap(review.ContentChecklistVerdictJson);
        if (verdict is { Count: > 0 })
        {
            foreach (var item in checklist)
            {
                if (!verdict.TryGetValue(item.Id.ToString(), out var v)) continue;
                switch (v?.Trim().ToLowerInvariant())
                {
                    case "missing":
                    case "inaccurate":
                        missing.Add(item.Id);
                        break;
                    case "irrelevant":
                        irrelevant.Add(item.Id);
                        break;
                }
            }
            return (missing, irrelevant);
        }

        // Fallback: deterministic pre-assessment (text-keyed). Map item text back to ids.
        var scenario = await db.WritingScenarios
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == submission.ScenarioId, ct);
        if (scenario is null)
        {
            return (missing, irrelevant);
        }

        var pre = await preAssessment.AssessAsync(
            new WritingPreAssessmentRequest(submission.Id, submission.LetterContent, scenario, checklist, null),
            ct);

        var missingText = new HashSet<string>(pre.MissingKeyContent, StringComparer.OrdinalIgnoreCase);
        var irrelevantText = new HashSet<string>(pre.DetectedIrrelevantContent, StringComparer.OrdinalIgnoreCase);
        foreach (var item in checklist)
        {
            if (missingText.Contains(item.ItemText)) missing.Add(item.Id);
            if (irrelevantText.Contains(item.ItemText)) irrelevant.Add(item.Id);
        }

        return (missing, irrelevant);
    }

    private static List<string> BuildNextSteps(
        WritingGrade? grade,
        IReadOnlyList<WritingContentChecklistItemDto> missingContent,
        IReadOnlyList<WritingContentChecklistItemDto> irrelevantContent,
        WritingResultVisibilityConfig vis)
    {
        var steps = new List<string>();

        if (vis.ShowMissingContent && missingContent.Count > 0)
        {
            steps.Add($"Add the missing key content: {string.Join("; ", missingContent.Take(3).Select(c => c.ItemText))}.");
        }
        if (vis.ShowMissingContent && irrelevantContent.Count > 0)
        {
            steps.Add($"Remove irrelevant content: {string.Join("; ", irrelevantContent.Take(3).Select(c => c.ItemText))}.");
        }

        if (grade is not null && vis.ShowFullCriteria)
        {
            // Weakest criteria first, normalised against each criterion's max (c1=3, others=7).
            var ranked = new (string Code, string Label, double Ratio)[]
            {
                ("c1", "Purpose", grade.C1Purpose / 3.0),
                ("c2", "Content", grade.C2Content / 7.0),
                ("c3", "Conciseness", grade.C3Conciseness / 7.0),
                ("c4", "Genre/format", grade.C4Genre / 7.0),
                ("c5", "Organisation", grade.C5Organisation / 7.0),
                ("c6", "Language", grade.C6Language / 7.0),
            }
            .OrderBy(x => x.Ratio)
            .ToArray();

            foreach (var weak in ranked.Where(x => x.Ratio < 0.75).Take(2))
            {
                steps.Add($"Focus on {weak.Label} ({weak.Code.ToUpperInvariant()}), your weakest criterion this attempt.");
            }
        }

        if (steps.Count == 0)
        {
            steps.Add("Strong attempt — review the model answer and refine your phrasing.");
        }

        return steps;
    }

    // ── Mapping (mirrors WritingMarkingEndpoints camelCase DTOs) ────────────────

    private static WritingSubmissionSummaryDto MapSubmission(WritingSubmission s)
        => new(
            s.Id.ToString(),
            s.ScenarioId.ToString(),
            s.UserId,
            s.LetterContent,
            s.WordCount,
            s.Status,
            s.SubmittedAt.ToString("o"));

    private static WritingGradeDto MapGrade(WritingGrade g, bool includeCriteria)
        => new(
            g.Id.ToString(),
            g.SubmissionId.ToString(),
            g.C1Purpose,
            g.C2Content,
            g.C3Conciseness,
            g.C4Genre,
            g.C5Organisation,
            g.C6Language,
            g.RawTotal,
            g.EstimatedBand.ToString(),
            g.BandLabel,
            includeCriteria ? DeserializeStringMap(g.PerCriterionFeedbackJson) : new Dictionary<string, string>());

    private static WritingTutorReviewDto MapReview(WritingTutorReview r)
        => new(
            r.Id.ToString(),
            r.SubmissionId.ToString(),
            r.TutorId,
            r.Status,
            r.FreeTextFeedback,
            DeserializeNullableStringMap(r.PerCriterionCommentsJson),
            DeserializeNullableScores(r.ScoreOverrideJson),
            r.MarkerSequence,
            r.IsContentChecklistMarked,
            DeserializeStringMap(r.ContentChecklistVerdictJson),
            null,
            r.CreatedAt.ToString("o"),
            r.SubmittedAt?.ToString("o"));

    private static WritingFeedbackAnnotationDto MapAnnotation(WritingFeedbackAnnotation a)
        => new(
            a.Id.ToString(),
            a.SubmissionId.ToString(),
            a.ReviewId?.ToString(),
            a.TutorId,
            a.Criterion,
            a.HighlightedText,
            a.StartOffset,
            a.EndOffset,
            a.Severity,
            a.Suggestion,
            a.FeedbackText,
            a.CreatedAt.ToString("o"));

    private static WritingContentChecklistItemDto MapChecklistItem(WritingContentChecklistItem c)
        => new()
        {
            Id = c.Id,
            ItemText = c.ItemText,
            Category = c.Category,
            Importance = c.Importance,
            RequiredStatus = c.RequiredStatus,
            LinkedCaseNoteSection = c.LinkedCaseNoteSection,
            ExpectedRepresentation = c.ExpectedRepresentation,
            CommonError = c.CommonError,
            Ordinal = c.Ordinal,
        };

    private static WritingResultVisibilityDto ToVisibilityDto(WritingResultVisibilityConfig c)
        => new(
            c.ScenarioId,
            c.ShowSubmissionReceived,
            c.ShowAiEstimate,
            c.ShowTutorScore,
            c.ShowFullCriteria,
            c.ShowAnnotatedResponse,
            c.ShowMissingContent,
            c.ShowModelAnswer,
            c.ShowContentChecklist,
            c.AllowRewrite,
            c.UpdatedAt);

    // ── JSON helpers (mirror WritingMarkingEndpoints) ───────────────────────────

    private static Dictionary<string, string> DeserializeStringMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static Dictionary<string, string>? DeserializeNullableStringMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Endpoints.WritingCriteriaScoresDto? DeserializeNullableScores(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            int Read(params string[] names)
            {
                foreach (var name in names)
                {
                    if (root.TryGetProperty(name, out var el) && el.TryGetInt32(out var v)) return v;
                }
                return 0;
            }

            return new Endpoints.WritingCriteriaScoresDto(
                Read("c1Purpose", "C1Purpose", "c1"),
                Read("c2Content", "C2Content", "c2"),
                Read("c3Conciseness", "C3Conciseness", "c3"),
                Read("c4Genre", "C4Genre", "c4"),
                Read("c5Organisation", "C5Organisation", "c5"),
                Read("c6Language", "C6Language", "c6"));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Learner gated-feedback + rewrite-comparison DTOs (mirror lib/writing/types.ts).
// WritingResultVisibilityDto / WritingGradeDto / WritingTutorReviewDto /
// WritingFeedbackAnnotationDto / WritingContentChecklistItemDto /
// WritingSubmissionSummaryDto are reused from existing code.
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingSubmissionFeedbackDto(
    WritingSubmissionSummaryDto Submission,
    WritingResultVisibilityDto Visibility,
    string Status,
    WritingGradeDto? Grade,
    WritingTutorReviewDto? TutorReview,
    IReadOnlyList<WritingFeedbackAnnotationDto> Annotations,
    IReadOnlyList<WritingContentChecklistItemDto> MissingContent,
    IReadOnlyList<WritingContentChecklistItemDto> IrrelevantContent,
    string? ModelAnswerText,
    IReadOnlyList<string> NextSteps);

public sealed record WritingRewriteSideDto(
    string SubmissionId,
    string LetterContent,
    WritingGradeDto? Grade);

public sealed record WritingRewriteComparisonDto(
    WritingRewriteSideDto Original,
    WritingRewriteSideDto Rewrite,
    IReadOnlyDictionary<string, int> PerCriterionDelta);
