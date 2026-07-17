using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.Rulebook;
using OetWithDrHesham.Api.Services.Speaking;

namespace OetWithDrHesham.Api.Endpoints;

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

        // NOTE: GET /v1/speaking/drills is owned by Wave 6
        // LearnerEndpoints.cs (`speaking.MapGet("/drills", ...)`) which
        // returns the canonical { kinds, items } shape consumed by the
        // learner UI and SpeakingDrillsListingTests. Mapping it here
        // again would cause an AmbiguousMatchException at runtime, so
        // this group only registers attempt/recording/score routes.

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
        }).DisableAntiforgery().RequireRateLimiting("PerUserWrite");

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

        admin.MapPost("/{id}/force-delete", async (
            string id,
            AdminService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.ForceDeleteSpeakingDrillAsync(
                AdminId(http), AdminName(http), id, ct)))
            .WithAdminWrite("AdminSystemAdmin");

        // ── Bulk action (publish | archive | delete) ────────────────────────
        //
        // T3: one atomic endpoint over a set of drills. The route enforces the
        // shared minimum (AdminContentWrite); the service additionally requires
        // content:publish when action=publish (the required grant depends on the
        // request body, so it can't be a static route policy). Unknown actions
        // → 400 from the service.
        admin.MapPost("/bulk", async (
            SpeakingDrillBulkRequest request,
            AdminService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.BulkSpeakingDrillsAsync(
                AdminId(http), AdminName(http), request.Action, request.Ids, ct)))
            .WithAdminWrite("AdminContentWrite");

        // ── Phase 11 (G.11) — AI-assisted draft ─────────────────────────────
        //
        // Routes through the grounded gateway via
        // `AdminService.AiDraftSpeakingDrillAsync`. Persists a Draft
        // SpeakingDrillItem + ContentItem atomically and surfaces an
        // optional `warning` when the AI reply could not be parsed and
        // the deterministic fallback was used. Gateway refuses
        // ungrounded prompts at construction time.
        admin.MapPost("/ai-draft", async (
            AdminSpeakingDrillAiDraftRequest request,
            AdminService service,
            IAiGatewayService gateway,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.AiDraftSpeakingDrillAsync(
                gateway, AdminId(http), AdminName(http), request, ct)))
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
