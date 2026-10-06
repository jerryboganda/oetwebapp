using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Services;

namespace OetLearner.Api.Services.Writing;

// ─────────────────────────────────────────────────────────────────────────────
// Learner-facing gated feedback (spec §15.2, WS-B4 Section D). The
// result-visibility CONFIG resolver/upsert lives in WritingResultVisibilityService;
// THIS service composes the gated learner bundle (owner-checked). Every
// learner-visible field is populated only when the corresponding visibility flag
// allows it AND the result has been released (WritingResultRelease: not while the
// 15-minute window is open).
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
}

public sealed class WritingResultFeedbackService(
    LearnerDbContext db,
    IWritingResultVisibilityService visibility,
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

        // Release gate: until the result is released (graded AND past the 15-minute window, or an
        // allowlisted account) nothing the AI produced is read, mapped or returned - the bundle is the
        // existing "submitted_awaiting_review" shell with no grade, assessment or next steps.
        var release = WritingResultRelease.Describe(
            submission.Status,
            submission.SubmittedAt,
            await WritingUnrestrictedAccounts.IsUnrestrictedAsync(db, userId, ct),
            DateTimeOffset.UtcNow);
        var released = release.State == WritingResultRelease.Released;

        var assessment = !released
            ? null
            : await db.WritingAssessmentReportsV11.AsNoTracking()
                .Include(x => x.Facts)
                .Include(x => x.Errors)
                .Include(x => x.Criteria)
                .SingleOrDefaultAsync(x => x.SubmissionId == submissionId, ct);
        // Live verified task answer, never the grading-time snapshot (Rev8 §19.6).
        var modelAnswer = assessment is null
            ? null
            : await WritingAssessmentV11ResultService.LoadVerifiedTaskModelAnswerAsync(db, submission.ScenarioId, ct);
        var assessmentCandidateVisible = assessment is not null
            && assessment.Status == WritingAssessmentV11Status.CandidateReady
            && assessment.CandidateNumericScoreEnabled
            && assessment.CandidateReportVisible;

        // Authoritative grade for the submission (latest tutor-attached, else latest AI).
        var grade = !released
            ? null
            : await db.WritingGrades
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
        // A held / still-processing result is always submitted_awaiting_review.
        string status;
        if (!released)
        {
            status = "submitted_awaiting_review";
        }
        else if (tutorFinalised)
        {
            status = "tutor_reviewed";
        }
        else if (grade is not null && vis.ShowAiEstimate && assessmentCandidateVisible)
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
            var gradeVisible = isTutorGrade
                ? vis.ShowTutorScore
                : vis.ShowAiEstimate && assessmentCandidateVisible;
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

        // ── Next steps (weakest criteria → short prompts) ─────────────────────────
        var nextSteps = released ? BuildNextSteps(gradeDto is null ? null : grade, vis) : new List<string>();

        // assessment is only loaded once released, so Map never sees a held result.
        WritingAssessmentV11ReportResponse? assessmentDto = assessment is null
            ? null
            : WritingAssessmentV11ResultService.Map(assessment, modelAnswer);
        if (assessmentDto is not null)
        {
            if (!vis.ShowFullCriteria) assessmentDto = assessmentDto with { Criteria = [] };
            if (!vis.ShowMissingContent) assessmentDto = assessmentDto with { Facts = [] };
            if (!vis.ShowModelAnswer) assessmentDto = assessmentDto with { ModelAnswer = null };
        }

        return new WritingSubmissionFeedbackDto(
            MapSubmission(submission, release.EffectiveStatus),
            visDto,
            status,
            gradeDto,
            reviewDto,
            annotations,
            nextSteps,
            assessmentDto);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static List<string> BuildNextSteps(
        WritingGrade? grade,
        WritingResultVisibilityConfig vis)
    {
        var steps = new List<string>();

        if (grade is not null && vis.ShowFullCriteria)
        {
            // Weakest criteria first, normalised against each criterion's max (c1=3, others=7).
            var ranked = new (string Label, double Ratio)[]
            {
                ("Purpose", grade.C1Purpose / 3.0),
                ("Content", grade.C2Content / 7.0),
                ("Conciseness", grade.C3Conciseness / 7.0),
                ("Genre/format", grade.C4Genre / 7.0),
                ("Organisation", grade.C5Organisation / 7.0),
                ("Language", grade.C6Language / 7.0),
            }
            .OrderBy(x => x.Ratio)
            .ToArray();

            foreach (var weak in ranked.Where(x => x.Ratio < 0.75).Take(2))
            {
                steps.Add($"Focus on {weak.Label}, your weakest criterion this attempt.");
            }
        }

        if (steps.Count == 0)
        {
            steps.Add("Strong attempt — review the model answer and refine your phrasing.");
        }

        return steps;
    }

    // ── Mapping (mirrors WritingMarkingEndpoints camelCase DTOs) ────────────────

    /// <param name="effectiveStatus">The learner-facing status: a graded letter inside its release window reads as grading.</param>
    private static WritingSubmissionSummaryDto MapSubmission(WritingSubmission s, string effectiveStatus)
        => new(
            s.Id.ToString(),
            s.ScenarioId.ToString(),
            s.UserId,
            s.LetterContent,
            s.WordCount,
            effectiveStatus,
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
            includeCriteria ? ReadCriterionFeedback(g.PerCriterionFeedbackJson) : new Dictionary<string, string>());

    /// <summary>
    /// Criterion key to plain feedback text. The grader stores one object per criterion
    /// (<c>score</c>, <c>feedback</c>, citation fields), a tutor-authored grade stores plain strings:
    /// both read as text, and only the feedback sentence (cleaned of internal labels) reaches the candidate.
    /// </summary>
    private static Dictionary<string, string> ReadCriterionFeedback(string? json)
    {
        var map = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(json)) return map;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return map;
            foreach (var criterion in doc.RootElement.EnumerateObject())
            {
                string? text = null;
                if (criterion.Value.ValueKind == JsonValueKind.String)
                {
                    text = criterion.Value.GetString();
                }
                else if (criterion.Value.ValueKind == JsonValueKind.Object
                    && criterion.Value.TryGetProperty("feedback", out var feedback)
                    && feedback.ValueKind == JsonValueKind.String)
                {
                    text = feedback.GetString();
                }
                var clean = WritingReportDigest.Clean(text);
                if (clean.Length > 0) map[criterion.Name] = clean;
            }
        }
        catch (JsonException)
        {
            // malformed stored JSON reads as no per-criterion feedback
        }
        return map;
    }

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
// Learner gated-feedback DTO (mirrors lib/writing/types.ts).
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
    IReadOnlyList<string> NextSteps,
    WritingAssessmentV11ReportResponse? AssessmentV11);
