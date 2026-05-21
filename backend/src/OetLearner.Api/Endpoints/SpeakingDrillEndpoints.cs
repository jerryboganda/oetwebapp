using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Endpoints;

// Phase 5 (G) of the OET Speaking module roadmap.
//
// Surfaces both halves of the drill bank:
//
//   /v1/speaking/drills/...        — learner-facing list + attempt
//                                    + upload + score.
//   /v1/admin/speaking/drills/...  — admin CRUD + publish/archive.
//
// Permission model mirrors `LearnerSpeakingRolePlayCardEndpoints`
// and `AdminSpeakingContentEndpoints`: learner side requires
// `LearnerOnly`, admin side requires `AdminContentRead` with
// `WithAdminWrite("AdminContentWrite"|"AdminContentPublish")` overlays
// on the mutating routes.
//
// Program.cs registers this via `MapSpeakingDrillEndpoints` — this
// file deliberately exposes only the static extension so the
// integrator decides where in the pipeline it goes.
public static class SpeakingDrillEndpoints
{
    public static IEndpointRouteBuilder MapSpeakingDrillEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Learner-facing routes ───────────────────────────────────────────
        var learner = app.MapGroup("/v1/speaking/drills")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser")
            .WithTags("Speaking Drills (Learner)");

        learner.MapGet("", async (
            SpeakingDrillService service,
            HttpContext http,
            CancellationToken ct,
            [FromQuery] string? kind,
            [FromQuery] string? professionId,
            [FromQuery] string? recommendedForSessionId) =>
        {
            var drills = await service.ListDrillsForLearnerAsync(
                LearnerId(http), kind, professionId, recommendedForSessionId, ct);
            return Results.Ok(new { drills });
        });

        learner.MapPost("/{id}/attempts", async (
            string id,
            DrillStartAttemptRequest? request,
            SpeakingDrillService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.StartAttemptAsync(
                LearnerId(http), id, request?.Source, ct)));

        learner.MapPost("/attempts/{aid}/recordings", async (
            string aid,
            HttpRequest httpRequest,
            SpeakingDrillService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            // Multipart upload — the front-end posts a single audio
            // FormData blob named "audio" with the recorder's
            // mime-type. We pull only what we need to keep the surface
            // tight (no metadata-controlled writes).
            if (!httpRequest.HasFormContentType)
            {
                throw ApiException.Validation("SPEAKING_DRILL_UPLOAD_BAD_REQUEST",
                    "multipart/form-data with an 'audio' file field is required.");
            }
            var form = await httpRequest.ReadFormAsync(ct);
            var file = form.Files["audio"] ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                throw ApiException.Validation("SPEAKING_DRILL_UPLOAD_EMPTY",
                    "No audio file was uploaded.");
            }
            await using var stream = file.OpenReadStream();
            await service.UploadRecordingAsync(
                LearnerId(http),
                aid,
                stream,
                file.ContentType,
                file.Length,
                ct);
            return Results.Ok(new { uploaded = true });
        }).DisableAntiforgery();

        learner.MapPost("/attempts/{aid}/score", async (
            string aid,
            SpeakingDrillService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.ScoreAttemptAsync(LearnerId(http), aid, ct)));

        // ── Admin-facing routes ─────────────────────────────────────────────
        var admin = app.MapGroup("/v1/admin/speaking/drills")
            .RequireAuthorization("AdminContentRead")
            .RequireRateLimiting("PerUser")
            .WithTags("Admin Speaking Drills");

        admin.MapGet("", async (
            AdminService service,
            CancellationToken ct,
            [FromQuery] string? drillKind,
            [FromQuery] string? professionId,
            [FromQuery] string? status) =>
            Results.Ok(await service.ListSpeakingDrillsAsync(drillKind, professionId, status, ct)));

        admin.MapGet("/{id}", async (
            string id,
            AdminService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetSpeakingDrillAsync(id, ct)));

        admin.MapPost("", async (
            AdminDrillCreateRequest request,
            AdminService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.CreateSpeakingDrillAsync(
                AdminId(http), AdminName(http), request, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapPatch("/{id}", async (
            string id,
            AdminDrillUpdateRequest request,
            AdminService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateSpeakingDrillAsync(
                AdminId(http), AdminName(http), id, request, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/{id}/publish", async (
            string id,
            AdminService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.PublishSpeakingDrillAsync(
                AdminId(http), AdminName(http), id, ct)))
            .WithAdminWrite("AdminContentPublish");

        admin.MapPost("/{id}/archive", async (
            string id,
            AdminService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.ArchiveSpeakingDrillAsync(
                AdminId(http), AdminName(http), id, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapDelete("/{id}", async (
            string id,
            AdminService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.DeleteSpeakingDrillAsync(
                AdminId(http), AdminName(http), id, ct)))
            .WithAdminWrite("AdminContentWrite");

        return app;
    }

    private static string LearnerId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.FindFirstValue("sub")
            ?? string.Empty;

    private static string AdminId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.FindFirstValue("sub")
            ?? "system";

    private static string AdminName(HttpContext http)
        => http.User.Identity?.Name ?? AdminId(http);

    /// <summary>POST body for `/v1/speaking/drills/{id}/attempts`. Source
    /// is one of `RecommendedPostAssessment`, `ManualBrowse`,
    /// `LearningPathStage` and drives the recommendation analytics.</summary>
    public sealed record DrillStartAttemptRequest(string? Source);
}
