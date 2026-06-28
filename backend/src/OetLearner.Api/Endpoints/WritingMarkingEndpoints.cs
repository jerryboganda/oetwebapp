using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// WS-B4 tutor marking surface: marking context, span annotations, content-checklist verdict,
/// double-marking submit, and senior moderation. Routes ARE the contract (see lib/writing/exam-api.ts).
/// The route id is the submission id (reviews are keyed by submission + tutor).
/// </summary>
public static class WritingMarkingEndpoints
{
    public static IEndpointRouteBuilder MapWritingMarkingEndpoints(this IEndpointRouteBuilder app)
    {
        // Mirror WritingTutorPortalEndpoints authorization: authenticated tutor, id from claims.
        var group = app.MapGroup("/v1/writing/tutor/reviews")
            .WithTags("WritingMarking")
            .RequireAuthorization("ExpertOnly");

        group.MapGet("/{id:guid}/context", GetContextAsync);
        group.MapGet("/{id:guid}/case-notes", GetCaseNotesAsync);
        group.MapGet("/{id:guid}/pre-assessment", GetPreAssessmentAsync);
        group.MapGet("/{id:guid}/annotations", ListAnnotationsAsync);
        group.MapPost("/{id:guid}/annotations", CreateAnnotationAsync);
        group.MapDelete("/{id:guid}/annotations/{annotationId:guid}", DeleteAnnotationAsync);
        group.MapGet("/{id:guid}/voice-note", GetVoiceNoteAsync);
        group.MapPost("/{id:guid}/voice-note", UpsertVoiceNoteAsync);
        group.MapPost("/{id:guid}", SubmitReviewAsync);
        group.MapGet("/{id:guid}/moderation", GetModerationAsync);
        group.MapPost("/{id:guid}/moderation/finalize", FinalizeModerationAsync);

        return app;
    }

    // ---- Context ------------------------------------------------------------------------------

    private static async Task<IResult> GetContextAsync(
        ClaimsPrincipal user,
        LearnerDbContext db,
        IWritingHeuristicPreAssessmentService preAssessmentService,
        WritingMarkingVoiceNoteService voiceNotes,
        Guid id,
        CancellationToken ct)
    {
        var tutorId = ResolveTutorId(user);
        if (string.IsNullOrWhiteSpace(tutorId))
        {
            return Results.Unauthorized();
        }

        var submission = await db.WritingSubmissions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (submission is null)
        {
            return Results.NotFound();
        }
        if (!await CanAccessSubmissionAsync(db, id, tutorId, ct))
        {
            return Results.Forbid();
        }

        var scenario = await db.WritingScenarios
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == submission.ScenarioId, ct);
        if (scenario is null)
        {
            return Results.NotFound();
        }

        var grade = await db.WritingGrades
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.SubmissionId == id, ct);

        // Zero-AI guarantee for mocks: never compute or surface an AI pre-assessment.
        // Mock Writing is human-marked; the AI heuristic panel must not leak into the
        // tutor's marking surface (see memory: no-ai-mock-speaking-writing).
        var preAssessmentDto = IsMock(submission)
            ? NeutralPreAssessment(id, submission.WordCount)
            : MapPreAssessment(await preAssessmentService.AssessAsync(
                new WritingPreAssessmentRequest(id, submission.LetterContent, scenario),
                ct));

        var voiceNote = await voiceNotes.GetForSubmissionAsync(id, ct);

        var existingReview = await db.WritingTutorReviews
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.SubmissionId == id && r.TutorId == tutorId, ct);

        var annotations = await db.WritingFeedbackAnnotations
            .AsNoTracking()
            .Where(a => a.SubmissionId == id)
            .OrderBy(a => a.StartOffset)
            .ThenBy(a => a.CreatedAt)
            .ToListAsync(ct);

        var moderation = await db.WritingModerations
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.SubmissionId == id, ct);

        var markerSequence = ResolveMarkerSequence(scenario, moderation, existingReview, tutorId);

        var dto = new WritingTutorMarkingContextDto(
            MapSubmission(submission),
            // Reuse the admin Task Builder's frontend-aligned WritingTaskDto so the
            // marking workspace receives the exact shape lib/writing/types.ts defines.
            Services.Writing.WritingTaskAuthoringService.MapToDto(scenario),
            grade is null ? null : MapGrade(grade),
            preAssessmentDto,
            existingReview is null ? null : MapReview(existingReview),
            annotations.Select(MapAnnotation).ToList(),
            moderation is null ? null : MapModeration(moderation),
            markerSequence,
            voiceNote);

        return Results.Ok(dto);
    }

    // ---- Case Notes (highlighted stimulus, read-only for the tutor) ---------------------------

    private static async Task<IResult> GetCaseNotesAsync(
        ClaimsPrincipal user,
        LearnerDbContext db,
        IWritingSubmissionService submissions,
        Guid id,
        CancellationToken ct)
    {
        var tutorId = ResolveTutorId(user);
        if (string.IsNullOrWhiteSpace(tutorId))
        {
            return Results.Unauthorized();
        }
        if (!await CanAccessSubmissionAsync(db, id, tutorId, ct))
        {
            return Results.Forbid();
        }

        // Staff-authorized: no owner gate (pass null). Returns the stimulus PDF path + the
        // learner's highlight snapshot so the tutor sees exactly what was marked.
        var caseNotes = await submissions.GetCaseNotesAsync(id, null, ct);
        return caseNotes is null ? Results.NotFound() : Results.Ok(caseNotes);
    }

    // ---- Pre-assessment ----------------------------------------------------------------------

    private static async Task<IResult> GetPreAssessmentAsync(
        ClaimsPrincipal user,
        LearnerDbContext db,
        IWritingHeuristicPreAssessmentService preAssessmentService,
        Guid id,
        CancellationToken ct)
    {
        var tutorId = ResolveTutorId(user);
        if (string.IsNullOrWhiteSpace(tutorId))
        {
            return Results.Unauthorized();
        }

        var submission = await db.WritingSubmissions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (submission is null)
        {
            return Results.NotFound();
        }
        if (!await CanAccessSubmissionAsync(db, id, tutorId, ct))
        {
            return Results.Forbid();
        }

        // Zero-AI guarantee for mocks — return a neutral pre-assessment without ever
        // invoking the heuristic AI scorer.
        if (IsMock(submission))
        {
            return Results.Ok(NeutralPreAssessment(id, submission.WordCount));
        }

        var scenario = await db.WritingScenarios
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == submission.ScenarioId, ct);
        if (scenario is null)
        {
            return Results.NotFound();
        }

        var preAssessment = await preAssessmentService.AssessAsync(
            new WritingPreAssessmentRequest(id, submission.LetterContent, scenario),
            ct);

        return Results.Ok(MapPreAssessment(preAssessment));
    }

    // ---- Annotations -------------------------------------------------------------------------

    private static async Task<IResult> ListAnnotationsAsync(
        ClaimsPrincipal user,
        LearnerDbContext db,
        IWritingAnnotationService annotations,
        Guid id,
        CancellationToken ct)
    {
        var tutorId = ResolveTutorId(user);
        if (string.IsNullOrWhiteSpace(tutorId))
        {
            return Results.Unauthorized();
        }

        if (!await CanAccessSubmissionAsync(db, id, tutorId, ct))
        {
            return Results.Forbid();
        }

        var items = await annotations.ListAsync(id, ct);
        return Results.Ok(new { items = items.Select(MapAnnotation).ToList() });
    }

    private static async Task<IResult> CreateAnnotationAsync(
        ClaimsPrincipal user,
        LearnerDbContext db,
        IWritingAnnotationService annotations,
        Guid id,
        [FromBody] CreateAnnotationRequest body,
        CancellationToken ct)
    {
        var tutorId = ResolveTutorId(user);
        if (string.IsNullOrWhiteSpace(tutorId))
        {
            return Results.Unauthorized();
        }

        if (body is null || string.IsNullOrWhiteSpace(body.HighlightedText) || string.IsNullOrWhiteSpace(body.FeedbackText))
        {
            return Results.BadRequest(new { error = "highlightedText and feedbackText are required." });
        }

        var submissionExists = await db.WritingSubmissions
            .AsNoTracking()
            .AnyAsync(s => s.Id == id, ct);
        if (!submissionExists)
        {
            return Results.NotFound();
        }
        if (!await CanAccessSubmissionAsync(db, id, tutorId, ct))
        {
            return Results.Forbid();
        }

        var created = await annotations.CreateAsync(
            id,
            tutorId,
            new WritingAnnotationInput(
                body.Criterion,
                body.HighlightedText,
                body.StartOffset,
                body.EndOffset,
                body.Severity ?? "medium",
                body.Suggestion,
                body.FeedbackText),
            ct);

        return Results.Ok(MapAnnotation(created));
    }

    private static async Task<IResult> DeleteAnnotationAsync(
        ClaimsPrincipal user,
        LearnerDbContext db,
        IWritingAnnotationService annotations,
        Guid id,
        Guid annotationId,
        CancellationToken ct)
    {
        var tutorId = ResolveTutorId(user);
        if (string.IsNullOrWhiteSpace(tutorId))
        {
            return Results.Unauthorized();
        }

        if (!await CanAccessSubmissionAsync(db, id, tutorId, ct))
        {
            return Results.Forbid();
        }

        var deleted = await annotations.DeleteAsync(id, annotationId, tutorId, ct);
        return deleted ? Results.NoContent() : Results.NotFound();
    }

    // ---- Voice note (one overall note per submission, System A) -------------------------------

    private static async Task<IResult> GetVoiceNoteAsync(
        ClaimsPrincipal user,
        LearnerDbContext db,
        WritingMarkingVoiceNoteService voiceNotes,
        Guid id,
        CancellationToken ct)
    {
        var tutorId = ResolveTutorId(user);
        if (string.IsNullOrWhiteSpace(tutorId))
        {
            return Results.Unauthorized();
        }

        if (!await CanAccessSubmissionAsync(db, id, tutorId, ct))
        {
            return Results.Forbid();
        }

        var dto = await voiceNotes.GetForSubmissionAsync(id, ct);
        return Results.Ok(dto);
    }

    private static async Task<IResult> UpsertVoiceNoteAsync(
        ClaimsPrincipal user,
        LearnerDbContext db,
        WritingMarkingVoiceNoteService voiceNotes,
        Guid id,
        [FromBody] UpsertVoiceNoteRequest body,
        CancellationToken ct)
    {
        var tutorId = ResolveTutorId(user);
        if (string.IsNullOrWhiteSpace(tutorId))
        {
            return Results.Unauthorized();
        }

        if (body is null || string.IsNullOrWhiteSpace(body.MediaAssetId))
        {
            return Results.BadRequest(new { error = "mediaAssetId is required." });
        }

        var submissionExists = await db.WritingSubmissions
            .AsNoTracking()
            .AnyAsync(s => s.Id == id, ct);
        if (!submissionExists)
        {
            return Results.NotFound();
        }
        if (!await CanAccessSubmissionAsync(db, id, tutorId, ct))
        {
            return Results.Forbid();
        }

        var dto = await voiceNotes.UpsertAsync(id, tutorId, body.MediaAssetId, body.DurationSeconds, ct);
        return Results.Ok(dto);
    }

    // ---- Submit review ------------------------------------------------------------------------

    private static async Task<IResult> SubmitReviewAsync(
        ClaimsPrincipal user,
        LearnerDbContext db,
        IWritingTutorReviewService reviews,
        Guid id,
        [FromBody] SubmitReviewRequest body,
        CancellationToken ct)
    {
        var tutorId = ResolveTutorId(user);
        if (string.IsNullOrWhiteSpace(tutorId))
        {
            return Results.Unauthorized();
        }

        body ??= new SubmitReviewRequest();

        if (!await CanAccessSubmissionAsync(db, id, tutorId, ct))
        {
            return Results.Forbid();
        }

        var result = await reviews.SubmitMarkingReviewAsync(
            id,
            tutorId,
            new WritingTutorReviewSubmitInput(
                body.FreeTextFeedback,
                body.PerCriterionComments,
                body.ScoreOverride is null ? null : ToCriteriaScores(body.ScoreOverride),
                body.ContentChecklistVerdict,
                body.MarkerSequence,
                body.AcceptedAiPreAssessment ?? false),
            ct);

        return Results.Ok(new
        {
            review = MapReview(result.Review),
            moderation = result.Moderation is null ? null : MapModeration(result.Moderation),
        });
    }

    // ---- Moderation --------------------------------------------------------------------------

    private static async Task<IResult> GetModerationAsync(
        ClaimsPrincipal user,
        LearnerDbContext db,
        IWritingModerationService moderation,
        Guid id,
        CancellationToken ct)
    {
        var tutorId = ResolveTutorId(user);
        if (string.IsNullOrWhiteSpace(tutorId))
        {
            return Results.Unauthorized();
        }

        if (!await CanAccessSubmissionAsync(db, id, tutorId, ct))
        {
            return Results.Forbid();
        }

        var row = await moderation.GetAsync(id, ct);
        return Results.Ok(row is null ? null : MapModeration(row));
    }

    private static async Task<IResult> FinalizeModerationAsync(
        ClaimsPrincipal user,
        LearnerDbContext db,
        IWritingModerationService moderation,
        Guid id,
        [FromBody] FinalizeModerationRequest body,
        CancellationToken ct)
    {
        var tutorId = ResolveTutorId(user);
        if (string.IsNullOrWhiteSpace(tutorId))
        {
            return Results.Unauthorized();
        }

        if (!await CanAccessSubmissionAsync(db, id, tutorId, ct))
        {
            return Results.Forbid();
        }

        if (body?.FinalScore is null)
        {
            return Results.BadRequest(new { error = "finalScore is required." });
        }

        var row = await moderation.FinalizeAsync(
            id,
            tutorId,
            ToCriteriaScores(body.FinalScore),
            body.FinalDecisionNote ?? string.Empty,
            ct);

        return row is null ? Results.NotFound() : Results.Ok(MapModeration(row));
    }

    // ---- Marker sequence resolution (read path) ----------------------------------------------

    private static string ResolveMarkerSequence(
        WritingScenario scenario,
        WritingModeration? moderation,
        WritingTutorReview? existingReview,
        string tutorId)
    {
        // If this tutor already has a review, surface the sequence they used.
        if (existingReview is not null && !string.IsNullOrWhiteSpace(existingReview.MarkerSequence))
        {
            return existingReview.MarkerSequence;
        }

        if (!string.Equals(scenario.MarkingMode, "double", StringComparison.OrdinalIgnoreCase))
        {
            return "first";
        }

        if (moderation is null)
        {
            return "first";
        }

        if (string.Equals(moderation.Status, "pending_moderation", StringComparison.Ordinal))
        {
            return "senior";
        }

        if (!string.IsNullOrWhiteSpace(moderation.FirstMarkerId)
            && !string.Equals(moderation.FirstMarkerId, tutorId, StringComparison.Ordinal))
        {
            return "second";
        }

        return "first";
    }

    // ---- Claim extraction (mirrors WritingTutorPortalEndpoints.ResolveTutorId) -----------------

    private static string? ResolveTutorId(ClaimsPrincipal user)
    {
        return user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub")
            ?? user.FindFirstValue("uid");
    }

    private static Task<bool> CanAccessSubmissionAsync(
        LearnerDbContext db,
        Guid submissionId,
        string tutorId,
        CancellationToken ct)
        => db.WritingTutorReviewAssignments
            .AsNoTracking()
            .AnyAsync(a =>
                a.SubmissionId == submissionId
                && a.TutorId == tutorId
                && (a.Status == "claimed" || a.Status == "submitted"),
                ct);

    // ---- Mapping to camelCase DTOs ------------------------------------------------------------

    private static WritingMarkingSubmissionDto MapSubmission(WritingSubmission s)
        => new(
            s.Id.ToString(),
            s.UserId,
            s.ScenarioId.ToString(),
            s.Mode,
            s.LetterContent,
            s.LetterContentHash,
            s.WordCount,
            s.TimeSpentSeconds,
            s.StartedAt.ToString("o"),
            s.SubmittedAt.ToString("o"),
            s.IsRevision,
            s.OriginalSubmissionId?.ToString(),
            s.Status,
            s.GradingTier,
            s.InputSource);

    private static WritingGradeDto MapGrade(WritingGrade g)
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
            DeserializeStringMap(g.PerCriterionFeedbackJson));

    private static bool IsMock(WritingSubmission s)
        => string.Equals(s.Mode, "mock", StringComparison.OrdinalIgnoreCase);

    // Neutral, AI-free pre-assessment for mock submissions. Keeps SuggestedCriterionFeedback
    // a non-null empty map (the frontend AiPreAnalysisPanel does Object.keys on it) but carries
    // zero AI signal — confidence "n/a", source "suppressed_mock".
    private static WritingPreAssessmentDto NeutralPreAssessment(Guid submissionId, int wordCount)
        => new(
            submissionId.ToString(),
            wordCount,
            false,
            0,
            new List<string>(),
            new List<string>(),
            new List<string>(),
            new WritingCriteriaScoresDto(0, 0, 0, 0, 0, 0),
            0,
            string.Empty,
            "n/a",
            "suppressed_mock",
            new Dictionary<string, string>());

    private static WritingPreAssessmentDto MapPreAssessment(WritingPreAssessmentResult r)
        => new(
            r.SubmissionId.ToString(),
            r.WordCount,
            r.WithinWordGuide,
            r.KeyContentCoveragePercent,
            r.MissingKeyContent.ToList(),
            r.DetectedIrrelevantContent.ToList(),
            r.LanguageNotes.ToList(),
            MapScores(r.EstimatedBands),
            r.EstimatedRawTotal,
            r.EstimatedBandLabel,
            r.Confidence,
            r.Source,
            r.SuggestedCriterionFeedback);

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
            DeserializeNullablePreAssessment(r.AcceptedAiPreAssessmentJson),
            r.CreatedAt.ToString("o"),
            r.SubmittedAt?.ToString("o"));

    private static WritingModerationDto MapModeration(WritingModeration m)
        => new(
            m.Id.ToString(),
            m.SubmissionId.ToString(),
            m.FirstMarkerId,
            m.SecondMarkerId,
            m.SeniorMarkerId,
            DeserializeNullableScores(m.FirstScoreJson),
            DeserializeNullableScores(m.SecondScoreJson),
            DeserializeNullableScores(m.FinalScoreJson),
            m.VariancePoints,
            m.VarianceReason,
            m.FinalDecisionNote,
            m.Status,
            m.CreatedAt.ToString("o"),
            m.UpdatedAt.ToString("o"));

    private static WritingCriteriaScoresDto MapScores(WritingCriteriaScores s)
        => new(s.C1Purpose, s.C2Content, s.C3Conciseness, s.C4Genre, s.C5Organisation, s.C6Language);

    private static WritingCriteriaScores ToCriteriaScores(WritingCriteriaScoresDto dto)
        => new(dto.C1, dto.C2, dto.C3, dto.C4, dto.C5, dto.C6);

    // ---- JSON helpers -------------------------------------------------------------------------

    private static Dictionary<string, string> DeserializeStringMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>();
        }

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
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Score JSON may be a criteria object ({c1Purpose..}) or the legacy short-key map ({c1..c6}).
    /// Both are normalised to a <see cref="WritingCriteriaScoresDto"/>.
    /// </summary>
    private static WritingCriteriaScoresDto? DeserializeNullableScores(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            int Read(params string[] names)
            {
                foreach (var name in names)
                {
                    if (root.TryGetProperty(name, out var el) && el.TryGetInt32(out var v))
                    {
                        return v;
                    }
                }
                return 0;
            }

            return new WritingCriteriaScoresDto(
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

    private static WritingPreAssessmentDto? DeserializeNullablePreAssessment(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var result = JsonSerializer.Deserialize<WritingPreAssessmentResult>(json);
            return result is null ? null : MapPreAssessment(result);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ---- Request bodies -----------------------------------------------------------------------

    public sealed class CreateAnnotationRequest
    {
        public string? Criterion { get; set; }
        public string HighlightedText { get; set; } = string.Empty;
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
        public string? Severity { get; set; }
        public string? Suggestion { get; set; }
        public string FeedbackText { get; set; } = string.Empty;
    }

    public sealed class SubmitReviewRequest
    {
        public string? FreeTextFeedback { get; set; }
        public Dictionary<string, string>? PerCriterionComments { get; set; }
        public WritingCriteriaScoresDto? ScoreOverride { get; set; }
        public Dictionary<string, string>? ContentChecklistVerdict { get; set; }
        public string? MarkerSequence { get; set; }
        public bool? AcceptedAiPreAssessment { get; set; }
    }

    public sealed class FinalizeModerationRequest
    {
        public WritingCriteriaScoresDto? FinalScore { get; set; }
        public string? FinalDecisionNote { get; set; }
    }

    public sealed class UpsertVoiceNoteRequest
    {
        public string MediaAssetId { get; set; } = string.Empty;
        public int DurationSeconds { get; set; }
    }
}

// ---- Response DTOs (mirror lib/writing/types.ts, camelCase JSON) -------------------------------

// Criteria scores carried on the wire as the frontend's short keys {c1..c6}
// (lib/writing/types.ts WritingCriteriaScoresDto). The internal domain holder
// WritingCriteriaScores keeps the verbose C1Purpose..C6Language names.
public sealed record WritingCriteriaScoresDto(
    int C1,
    int C2,
    int C3,
    int C4,
    int C5,
    int C6);

public sealed record WritingTutorMarkingContextDto(
    WritingMarkingSubmissionDto Submission,
    Services.Writing.WritingTaskDto Task,
    WritingGradeDto? AiGrade,
    WritingPreAssessmentDto PreAssessment,
    WritingTutorReviewDto? ExistingReview,
    IReadOnlyList<WritingFeedbackAnnotationDto> Annotations,
    WritingModerationDto? Moderation,
    string MarkerSequence,
    Services.Writing.WritingMarkingVoiceNoteDto? VoiceNote);

// Full submission shape the marking workspace consumes (lib/writing/types.ts
// WritingSubmissionDto). Distinct from the leaner WritingSubmissionSummaryDto
// still used by the learner gated-feedback bundle.
public sealed record WritingMarkingSubmissionDto(
    string Id,
    string UserId,
    string ScenarioId,
    string Mode,
    string LetterContent,
    string ContentHash,
    int WordCount,
    int TimeSpentSeconds,
    string StartedAt,
    string SubmittedAt,
    bool IsRevision,
    string? OriginalSubmissionId,
    string Status,
    string GradingTier,
    string InputSource);

public sealed record WritingSubmissionSummaryDto(
    string Id,
    string ScenarioId,
    string LearnerId,
    string LetterText,
    int WordCount,
    string Status,
    string? SubmittedAt);

public sealed record WritingGradeDto(
    string Id,
    string SubmissionId,
    int C1Purpose,
    int C2Content,
    int C3Conciseness,
    int C4Genre,
    int C5Organisation,
    int C6Language,
    int RawTotal,
    string EstimatedBand,
    string BandLabel,
    IReadOnlyDictionary<string, string> PerCriterionFeedback);

public sealed record WritingPreAssessmentDto(
    string SubmissionId,
    int WordCount,
    bool WithinWordGuide,
    int KeyContentCoveragePercent,
    IReadOnlyList<string> MissingKeyContent,
    IReadOnlyList<string> DetectedIrrelevantContent,
    IReadOnlyList<string> LanguageNotes,
    WritingCriteriaScoresDto EstimatedBands,
    int EstimatedRawTotal,
    string EstimatedBandLabel,
    string Confidence,
    string Source,
    // The frontend AiPreAnalysisPanel does Object.keys(suggestedCriterionFeedback);
    // it is a required field of the lib/writing/types.ts WritingPreAssessmentDto.
    // (Dropping it here made the marking workspace crash on render with
    // "Cannot convert undefined or null to object".)
    IReadOnlyDictionary<string, string> SuggestedCriterionFeedback);

public sealed record WritingFeedbackAnnotationDto(
    string Id,
    string SubmissionId,
    string? ReviewId,
    string TutorId,
    string? Criterion,
    string HighlightedText,
    int StartOffset,
    int EndOffset,
    string Severity,
    string? Suggestion,
    string FeedbackText,
    string CreatedAt);

public sealed record WritingTutorReviewDto(
    string Id,
    string SubmissionId,
    string TutorId,
    string Status,
    string? FreeTextFeedback,
    IReadOnlyDictionary<string, string>? PerCriterionComments,
    WritingCriteriaScoresDto? ScoreOverride,
    string MarkerSequence,
    bool IsContentChecklistMarked,
    IReadOnlyDictionary<string, string> ContentChecklistVerdict,
    WritingPreAssessmentDto? AcceptedAiPreAssessment,
    string CreatedAt,
    string? SubmittedAt);

public sealed record WritingModerationDto(
    string Id,
    string SubmissionId,
    string? FirstMarkerId,
    string? SecondMarkerId,
    string? SeniorMarkerId,
    WritingCriteriaScoresDto? FirstScore,
    WritingCriteriaScoresDto? SecondScore,
    WritingCriteriaScoresDto? FinalScore,
    int? VariancePoints,
    string? VarianceReason,
    string? FinalDecisionNote,
    string Status,
    string CreatedAt,
    string UpdatedAt);
