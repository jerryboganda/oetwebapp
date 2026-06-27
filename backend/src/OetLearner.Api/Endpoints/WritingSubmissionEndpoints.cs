using OetLearner.Api.Contracts;
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
        .RequireRateLimiting("writing-submissions-free")
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

        group.MapPost("/{id:guid}/revise", async (
            Guid id,
            WritingReviseRequest request,
            HttpContext http,
            IWritingSubmissionService service,
            CancellationToken ct) =>
        {
            var revision = await service.ReviseSubmissionAsync(http.WritingV2UserId(), id, request, ct);
            return revision is null ? Results.NotFound() : Results.Created($"/v1/writing/submissions/{revision.Id}", revision);
        })
        .RequireRateLimiting("writing-submissions-free")
        .WithName("ReviseWritingSubmission");

        group.MapPost("/{id:guid}/appeal", async (
            Guid id,
            WritingAppealRequest? request,
            HttpContext http,
            IWritingAppealService service,
            CancellationToken ct) =>
        {
            var appeal = await service.RequestAppealAsync(http.WritingV2UserId(), id, request?.Reason, ct);
            return appeal is null ? Results.NotFound() : Results.Accepted($"/v1/writing/submissions/{id}/appeal", appeal);
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("AppealWritingSubmission");

        // Read-only poll for the appeal UI (Writing V2 Score Appeal page).
        // Returns the latest appeal row for this submission, or 404 if
        // none exists. Polled by the appeal page after the POST returns.
        group.MapGet("/{id:guid}/appeal", async (
            Guid id,
            HttpContext http,
            IWritingAppealService service,
            CancellationToken ct) =>
        {
            var appeal = await service.GetLatestAppealAsync(http.WritingV2UserId(), id, ct);
            return appeal is null ? Results.NotFound() : Results.Ok(appeal);
        })
        .WithName("GetWritingSubmissionAppeal");

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
