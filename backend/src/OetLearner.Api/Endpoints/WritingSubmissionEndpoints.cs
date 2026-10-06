using OetLearner.Api.Contracts;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

public static class WritingSubmissionEndpoints
{
    public static IEndpointRouteBuilder MapWritingSubmissionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/writing/submissions")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapPost("/", async (
            WritingSubmissionCreateRequest request,
            HttpContext http,
            IWritingSubmissionService service,
            CancellationToken ct) =>
        {
            var submission = await service.CreateSubmissionAsync(http.WritingV2UserId(), request, ct);
            return Results.Created($"/v1/writing/submissions/{submission.Id}", submission);
        })
        .RequireRateLimiting("AiScoring")
        .WithName("CreateWritingSubmission");

        group.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingSubmissionService service,
            CancellationToken ct) =>
        {
            var submission = await service.GetSubmissionAsync(http.WritingV2UserId(), id, ct);
            return submission is null ? Results.NotFound() : Results.Ok(submission);
        })
        .WithName("GetWritingSubmission");

        group.MapGet("/{id:guid}/grade", async (
            Guid id,
            HttpContext http,
            IWritingSubmissionService service,
            CancellationToken ct) =>
        {
            var grade = await service.GetSubmissionGradeAsync(http.WritingV2UserId(), id, ct);
            return grade is null ? Results.NotFound() : Results.Ok(grade);
        })
        .WithName("GetWritingSubmissionGrade");

        // Controlled resume after a transient provider/rate-limit failure:
        // re-grades the SAME persisted submission without retyping and without
        // a duplicate paid workflow (reservation + grade-reuse are idempotent
        // on the submission). Rate-limited like the initial submit.
        group.MapPost("/{id:guid}/retry-grade", async (
            Guid id,
            HttpContext http,
            IWritingSubmissionService service,
            CancellationToken ct) =>
        {
            // Grading resumes off the request path; the returned submission
            // reads queued until the grade lands (the grading page polls).
            var submission = await service.RetryGradeAsync(http.WritingV2UserId(), id, ct);
            return Results.Ok(submission);
        })
        .RequireRateLimiting("AiScoring")
        .WithName("RetryWritingSubmissionGrade");

        group.MapGet("/{id:guid}/assessment-v11", async (
            Guid id,
            HttpContext http,
            IWritingAssessmentV11ResultService service,
            CancellationToken ct) =>
        {
            var report = await service.GetForLearnerAsync(http.WritingV2UserId(), id, ct);
            return report is null ? Results.NotFound() : Results.Ok(report);
        })
        .WithName("GetWritingAssessmentV11");

        // Answer-sheet / model-answer PDF for a submitted letter — revealed on the results page
        // only (post-submission, owner-gated). Returns Ok({ answerSheetPdfDownloadPath: null })
        // when none is attached or the submission isn't owned, so the results page degrades quietly.
        group.MapGet("/{id:guid}/answer-sheet", async (
            Guid id,
            HttpContext http,
            IWritingSubmissionService service,
            CancellationToken ct) =>
        {
            var path = await service.GetAnswerSheetDownloadPathAsync(http.WritingV2UserId(), id, ct);
            return Results.Ok(new { answerSheetPdfDownloadPath = path });
        })
        .WithName("GetWritingSubmissionAnswerSheet");

        // Case Notes PDF + the learner's highlight snapshot for a submitted letter — rendered
        // read-only on the results page so the learner can review what they highlighted.
        // Owner-gated; Ok(null) when not owned / not found.
        group.MapGet("/{id:guid}/case-notes", async (
            Guid id,
            HttpContext http,
            IWritingSubmissionService service,
            CancellationToken ct) =>
        {
            var caseNotes = await service.GetCaseNotesAsync(id, http.WritingV2UserId(), ct);
            return Results.Ok(caseNotes);
        })
        .WithName("GetWritingSubmissionCaseNotes");

        // Tutor's overall voice note for this submission (mock + normal). Returns Ok(null)
        // when owned but no submitted note exists yet; 404 when the submission isn't owned.
        group.MapGet("/{id:guid}/voice-note", async (
            Guid id,
            HttpContext http,
            WritingMarkingVoiceNoteService service,
            CancellationToken ct) =>
        {
            var note = await service.GetForLearnerAsync(http.WritingV2UserId(), id, ct);
            return Results.Ok(note);
        })
        .WithName("GetWritingSubmissionVoiceNote");

        // Retired (owner handoff, 6 Oct 2026): a second try is a fresh attempt via "Practice this again".
        // Kept mapped as a hard-disabled stub so a stale cached client gets a clear 409 instead of a bare
        // 404: no body binding, no AI rate limiter, no service call, nothing is graded or charged.
        group.MapPost("/{id:guid}/revise", () =>
        {
            throw ApiException.Conflict(
                "writing_revise_retired",
                "This option is no longer available. To try this task again, start a new attempt from the task page.");
        })
        .WithName("ReviseWritingSubmission");

        group.MapPost("/{id:guid}/dispute-violation", async (
            Guid id,
            WritingDisputeViolationRequest request,
            HttpContext http,
            IWritingCanonService service,
            CancellationToken ct) =>
        {
            var updated = await service.DisputeViolationAsync(http.WritingV2UserId(), id, request, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("DisputeWritingCanonViolation");

        return app;
    }
}
