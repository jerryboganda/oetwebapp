using System.Security.Claims;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin endpoints for the expert side of Speaking grader calibration (owner spec 4 Oct 2026): promote
/// finished AI cards, let Dr Hesham mark them blind to the AI score, and report coverage. Under
/// <c>/v1/admin/speaking/grader-calibration</c>; reads use <c>AdminContentRead</c>, writes use
/// <c>AdminContentWrite</c> with the per-user write rate limit, like the sibling Writing calibration surface.
/// </summary>
public static class SpeakingGraderCalibrationEndpoints
{
    public static IEndpointRouteBuilder MapSpeakingGraderCalibrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/speaking/grader-calibration")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser")
            .WithTags("Speaking grader calibration");

        group.MapGet("/", async (SpeakingGraderCalibrationService service, CancellationToken ct)
                => Results.Ok(await service.GetOverviewAsync(ct)))
            .WithName("SpeakingGraderCalibrationOverview")
            .WithSummary("Coverage of the expert-labelled set and every promoted sample. Never carries an AI score.")
            .WithAdminRead("AdminContentRead");

        group.MapGet("/candidates", async (int? take, SpeakingGraderCalibrationService service, CancellationToken ct)
                => Results.Ok(await service.ListCandidatesAsync(take ?? 50, ct)))
            .WithName("SpeakingGraderCalibrationCandidates")
            .WithSummary("Finished AI cards that could be promoted: no learner identity, no AI result.")
            .WithAdminRead("AdminContentRead");

        group.MapPost("/samples", async (
                SpeakingGraderCalibrationPromoteRequest request,
                HttpContext http,
                SpeakingGraderCalibrationService service,
                CancellationToken ct)
                => Results.Ok(await service.PromoteAsync(AdminId(http), AdminName(http), request.SessionId, ct)))
            .WithName("SpeakingGraderCalibrationPromote")
            .WithSummary("Promote a finished AI card for the expert to mark; keeps its audio for a year and writes an audit event.")
            .WithAdminWrite("AdminContentWrite");

        group.MapGet("/samples/{id}", async (string id, SpeakingGraderCalibrationService service, CancellationToken ct)
                => Results.Ok(await service.GetDetailAsync(id, ct)))
            .WithName("SpeakingGraderCalibrationSample")
            .WithSummary("The blind labelling view: card, transcript and audio clips. Never joins an AI assessment.")
            .WithAdminRead("AdminContentRead");

        group.MapGet("/samples/{id}/audio/{recordingId}", async (
                string id,
                string recordingId,
                HttpContext http,
                SpeakingGraderCalibrationService service,
                IFileStorage storage,
                CancellationToken ct) =>
            {
                var (storagePath, mimeType) = await service.GetClipAsync(id, recordingId, ct);
                // Audited before the bytes leave (a range request re-audits, as the expert route does).
                await service.AuditClipAccessAsync(AdminId(http), AdminName(http), id, recordingId, ct);
                var stream = await storage.OpenReadAsync(storagePath, ct);
                return Results.File(stream, mimeType, enableRangeProcessing: true);
            })
            .WithName("SpeakingGraderCalibrationAudio")
            .WithSummary("Stream one of the sample's audio clips for the expert to hear.")
            .WithAdminRead("AdminContentRead");

        group.MapPut("/samples/{id}/label", async (
                string id,
                SpeakingGraderCalibrationLabelRequest request,
                HttpContext http,
                SpeakingGraderCalibrationService service,
                CancellationToken ct)
                => Results.Ok(await service.LabelAsync(AdminId(http), id, request, ct)))
            .WithName("SpeakingGraderCalibrationLabel")
            .WithSummary("Record the expert's nine criterion scores and overall /500 (steps of 10).")
            .WithAdminWrite("AdminContentWrite");

        group.MapPost("/samples/{id}/exclude", async (
                string id,
                SpeakingGraderCalibrationExcludeRequest request,
                SpeakingGraderCalibrationService service,
                CancellationToken ct)
                => Results.Ok(await service.ExcludeAsync(id, request, ct)))
            .WithName("SpeakingGraderCalibrationExclude")
            .WithSummary("Mark a performance unusable (with a reason); it is kept for audit and never reported.")
            .WithAdminWrite("AdminContentWrite");

        group.MapGet("/runs", async (SpeakingGraderCalibrationService service, CancellationToken ct)
                => Results.Ok(await service.ListRunsAsync(ct)))
            .WithName("SpeakingGraderCalibrationRuns")
            .WithSummary("Recent calibration runs with their progress. Numbers only.")
            .WithAdminRead("AdminContentRead");

        group.MapPost("/runs", async (
                SpeakingGraderCalibrationRunCreateRequest request,
                HttpContext http,
                SpeakingGraderCalibrationService service,
                CancellationToken ct)
                => Results.Ok(await service.CreateRunAsync(AdminId(http), AdminName(http), request, ct)))
            .WithName("SpeakingGraderCalibrationRunCreate")
            .WithSummary("Start grading every expert-marked performance with the current grader (at least twice each).")
            .WithAdminWrite("AdminContentWrite");

        group.MapGet("/runs/{id}", async (string id, SpeakingGraderCalibrationService service, CancellationToken ct)
                => Results.Ok(await service.GetRunAsync(id, ct)))
            .WithName("SpeakingGraderCalibrationRun")
            .WithSummary("A run's progress and the report so far: per-criterion error, score, grade, pass/fail, repeatability.")
            .WithAdminRead("AdminContentRead");

        group.MapPost("/runs/{id}/next", async (
                string id,
                HttpContext http,
                SpeakingGraderCalibrationService service,
                CancellationToken ct)
                => Results.Ok(await service.NextAsync(id, AdminId(http), ct)))
            .WithName("SpeakingGraderCalibrationRunNext")
            .WithSummary("Queue the next calibration grade unless a learner's grade is waiting (queued|busy|yield|done|complete).")
            .WithAdminWrite("AdminContentWrite");

        group.MapPost("/runs/{id}/finalize", async (
                string id,
                HttpContext http,
                SpeakingGraderCalibrationService service,
                CancellationToken ct)
                => Results.Ok(await service.FinalizeAsync(id, AdminId(http), AdminName(http), ct)))
            .WithName("SpeakingGraderCalibrationRunFinalize")
            .WithSummary("Freeze the report and close the run.")
            .WithAdminWrite("AdminContentWrite");

        return app;
    }

    private static string AdminId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated admin id is required.");

    private static string AdminName(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.Name) ?? http.User.Identity?.Name ?? "admin";
}
