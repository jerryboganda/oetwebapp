using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Endpoints;

// Phase 1 (B.1) of the OET Speaking module roadmap.
//
// Wires the two-card role-play admin surface:
//   * Candidate-card CRUD + lifecycle (publish / archive / duplicate)
//   * Hidden interlocutor-script GET + PUT (upsert)
//
// Authorization mirrors `SpeakingSharedResourcesEndpoints` and the
// existing admin route helpers:
//   - Group:  RequireAuthorization("AdminContentRead") + PerUser rate
//   - Writes: WithAdminWrite("AdminContentWrite") (or AdminContentPublish
//     for the publish transition) — adds PerUserWrite rate + the
//     granular permission on top of the group's AdminContentRead.
//
// Program.cs registers this via `MapAdminSpeakingContentEndpoints` —
// this file deliberately exposes only the static extension so the
// integrator can decide where in the pipeline it goes.
public static class AdminSpeakingContentEndpoints
{
    public static IEndpointRouteBuilder MapAdminSpeakingContentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/speaking/role-play-cards")
            .RequireAuthorization("AdminContentRead")
            .RequireRateLimiting("PerUser")
            .WithTags("Admin Speaking Role-Play Cards");

        // ── Role-play card CRUD ───────────────────────────────────────────

        group.MapGet("", async (
            AdminService service,
            CancellationToken ct,
            [FromQuery] string? professionId,
            [FromQuery] string? difficulty,
            [FromQuery] string? status) =>
            Results.Ok(await service.ListSpeakingRolePlayCardsAsync(professionId, difficulty, status, ct)));

        group.MapGet("/{id}", async (
            string id,
            AdminService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetSpeakingRolePlayCardAsync(id, ct)));

        group.MapPost("", async (
            AdminRolePlayCardCreateRequest request,
            AdminService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.CreateSpeakingRolePlayCardAsync(
                AdminId(http), AdminName(http), request, ct)))
            .WithAdminWrite("AdminContentWrite");

        group.MapPatch("/{id}", async (
            string id,
            AdminRolePlayCardUpdateRequest request,
            AdminService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateSpeakingRolePlayCardAsync(
                AdminId(http), AdminName(http), id, request, ct)))
            .WithAdminWrite("AdminContentWrite");

        group.MapPost("/{id}/publish", async (
            string id,
            AdminService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.PublishSpeakingRolePlayCardAsync(
                AdminId(http), AdminName(http), id, ct)))
            .WithAdminWrite("AdminContentPublish");

        group.MapPost("/{id}/archive", async (
            string id,
            AdminService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.ArchiveSpeakingRolePlayCardAsync(
                AdminId(http), AdminName(http), id, ct)))
            .WithAdminWrite("AdminContentWrite");

        group.MapPost("/{id}/duplicate", async (
            string id,
            AdminService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.DuplicateSpeakingRolePlayCardAsync(
                AdminId(http), AdminName(http), id, ct)))
            .WithAdminWrite("AdminContentWrite");

        // ── Phase 11 (G.11) — AI-assisted draft ───────────────────────────
        //
        // Routes through the grounded gateway via
        // `AdminService.AiDraftRolePlayCardAsync`. Persists a Draft card +
        // hidden script atomically. Admin reviews + edits before
        // publishing. Refuses ungrounded prompts at the gateway layer
        // (PromptNotGroundedException), so callers cannot bypass the
        // rulebook + scoring guardrails.
        group.MapPost("/ai-draft", async (
            AdminRolePlayCardAiDraftRequest request,
            AdminService service,
            IAiGatewayService gateway,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.AiDraftRolePlayCardAsync(
                gateway, AdminId(http), AdminName(http), request, ct)))
            .WithAdminWrite("AdminContentWrite");

        // ── WS9 (SPK-007) — scanned/text PDF import → structured draft ─────
        //
        // Admin uploads a source paper (scanned or text PDF). The source is
        // persisted via IFileStorage (provenance) and text is extracted; a
        // builder-validation report mirrors the publish gate. When
        // `autoDraft=true` and usable text was extracted, the grounded
        // AI-draft path produces a reviewable Draft card. Mock-safe: a scanned
        // PDF with no OCR provider simply returns the validation report + the
        // saved source asset for manual structuring.
        group.MapPost("/import", async (
            IFormFile file,
            [FromForm] string professionId,
            [FromForm] string? topic,
            [FromForm] bool? autoDraft,
            ISpeakingContentImportService importer,
            IAiGatewayService gateway,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (file is null || file.Length <= 0)
            {
                return Results.BadRequest(new { code = "speaking_import_file_required" });
            }
            // Bound the upload (PDFs only; 25 MB ceiling matches admin imports).
            if (file.Length > 25 * 1024 * 1024)
            {
                return Results.BadRequest(new { code = "speaking_import_file_too_large" });
            }
            await using var stream = file.OpenReadStream();
            var result = await importer.ImportAsync(
                gateway,
                AdminId(http),
                AdminName(http),
                professionId,
                topic,
                autoDraft ?? false,
                file.FileName,
                file.ContentType ?? "application/pdf",
                stream,
                ct);
            return Results.Ok(result);
        })
            .DisableAntiforgery()
            .RequireRateLimiting("PerUserWrite")
            .WithAdminWrite("AdminContentWrite");

        // ── Hidden interlocutor script ────────────────────────────────────
        //
        // GET returns the script, or 404 when none exists yet — the
        // typed frontend client `lib/api/speaking-role-play-cards.ts`
        // already handles 404 as "no script yet".
        group.MapGet("/{id}/interlocutor-script", async (
            string id,
            AdminService service,
            CancellationToken ct) =>
        {
            var detail = await service.GetInterlocutorScriptAsync(id, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        group.MapPut("/{id}/interlocutor-script", async (
            string id,
            AdminInterlocutorScriptUpsertRequest request,
            AdminService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.UpsertInterlocutorScriptAsync(
                AdminId(http), AdminName(http), id, request, ct)))
            .WithAdminWrite("AdminContentWrite");

        return app;
    }

    private static string AdminId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.FindFirstValue("sub")
            ?? "system";

    private static string AdminName(HttpContext http)
        => http.User.Identity?.Name ?? AdminId(http);
}
