using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Services.Entitlements;
using OetWithDrHesham.Api.Services.Content;
using OetWithDrHesham.Api.Services.Recalls;
using OetWithDrHesham.Api.Services.VoiceDesign;

namespace OetWithDrHesham.Api.Endpoints;

/// <summary>
/// Unified Recalls endpoints. See <c>docs/RECALLS-MODULE-PLAN.md</c> §3.
/// All routes require <c>LearnerOnly</c> authorization.
/// </summary>
public static class RecallsEndpoints
{
    public static IEndpointRouteBuilder MapRecallsEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").RequireAuthorization("LearnerOnly");
        var recalls = v1.MapGroup("/recalls");

        // Free-preview access: the recalls snapshot / queue / favourites are open
        // to every logged-in learner. Content is scoped to free-preview terms for
        // non-premium learners inside RecallsService (locked content never leaks),
        // so no subscription gate is applied here. See docs/RECALLS-MODULE-PLAN.md.
        recalls.MapGet("/today", async (HttpContext http, RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetTodayAsync(http.UserId(), ct)));

        recalls.MapGet("/queue", async (
            HttpContext http,
            [FromQuery] int limit,
            IEffectiveEntitlementResolver entitlements,
            RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetQueueAsync(
                http.UserId(), limit, await ResolveIsPremiumAsync(http, entitlements, ct), ct)));

        recalls.MapPost("/star", async (
            HttpContext http,
            RecallsStarRequest request,
            IEffectiveEntitlementResolver entitlements,
            RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.StarAsync(
                http.UserId(), request, await ResolveIsPremiumAsync(http, entitlements, ct), ct)));

        recalls.MapGet("/audio/{termId}", async (
            HttpContext http,
            string termId,
            [FromQuery] string? speed,
            RecallsService svc,
            IFileStorage storage,
            IEffectiveEntitlementResolver entitlements,
            CancellationToken ct) =>
        {
            // Click-to-hear pronunciation is a paid-tier feature EXCEPT on curated
            // free-preview terms, which every logged-in learner may use in full.
            // Gate order: admin bypass → eligible subscription → free-preview term;
            // otherwise 402 so the frontend can prompt for upgrade.
            var userId = http.UserId();
            http.Response.Headers.CacheControl = "private, no-store";
            http.Response.Headers.Vary = "Authorization";
            var isAdmin = http.User.IsInRole("admin");
            if (!isAdmin)
            {
                var snapshot = await entitlements.ResolveAsync(userId, ct);
                var eligible = snapshot.HasEligibleSubscription && !snapshot.IsFrozen
                    && snapshot.IsModuleEnabled(ModuleKeys.Recalls);
                if (!eligible && !await svc.IsFreePreviewTermAsync(termId, ct))
                {
                    return Results.Json(
                        new
                        {
                            code = "subscription_required",
                            message = "Pronunciation audio is available for paid candidates only.",
                        },
                        statusCode: StatusCodes.Status402PaymentRequired);
                }
            }

            var audio = await svc.EnsureAudioAsync(userId, termId, speed ?? "normal", ct);
            if (!await storage.ExistsAsync(audio.StorageKey, ct)) return Results.NotFound();

            var stream = await storage.OpenReadAsync(audio.StorageKey, ct);
            return Results.Stream(stream, audio.ContentType, enableRangeProcessing: false);
        });

        recalls.MapGet("/library", async (
            HttpContext http,
            [FromQuery] string? bucket,
            [FromQuery] string? topic,
            IEffectiveEntitlementResolver entitlements,
            RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetLibraryAsync(
                http.UserId(), bucket, topic, await ResolveIsPremiumAsync(http, entitlements, ct), ct)));

        recalls.MapGet("/report/week", async (
            HttpContext http, IEffectiveEntitlementResolver entitlements, RecallsService svc, CancellationToken ct) =>
        {
            if (await RequireRecallEnrolmentAsync(http, entitlements, ct) is { } gate) return gate;
            return Results.Ok(await svc.GetWeeklyReportAsync(http.UserId(), ct));
        });

        recalls.MapGet("/revision-plan", async (
            HttpContext http, IEffectiveEntitlementResolver entitlements, RecallsService svc, CancellationToken ct) =>
        {
            if (await RequireRecallEnrolmentAsync(http, entitlements, ct) is { } gate) return gate;
            return Results.Ok(await svc.GetRevisionPlanAsync(http.UserId(), ct));
        });

        // Admin-only: CSV bulk upload of vocabulary terms (spec §8).
        var adminRecalls = app.MapGroup("/v1/admin/recalls")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");
        adminRecalls.MapPost("/bulk-upload", () =>
            Results.Json(new
            {
                code = "legacy_recalls_import_disabled",
                message = "Legacy Recalls bulk upload is disabled for production safety. Use /v1/admin/vocabulary/import/preview and /v1/admin/vocabulary/import with dryRun first."
            }, statusCode: StatusCodes.Status409Conflict))
            .WithAdminWrite("AdminContentWrite");

        adminRecalls.MapPost("/audio/backfill", async (
            [FromBody] AdminAudioRegenerateRequest? request,
            HttpContext http,
            IVoiceDesignRegenerationService regenService,
            CancellationToken ct) =>
        {
            var body = request ?? new AdminAudioRegenerateRequest(
                AudioType: "recalls",
                Scope: "missing",
                ModelVariant: null,
                VoiceId: null,
                Instructions: null,
                Speed: null,
                Pitch: null,
                Emotion: null,
                ProviderName: "elevenlabs",
                ForceRegenerate: false,
                DryRun: false);

            body = body with
            {
                AudioType = "recalls",
                ProviderName = "elevenlabs",
            };
            var userId = http.User.FindFirst("sub")?.Value ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var result = await regenService.EnqueueBulkRegenerationAsync(body, userId, ct);
            return Results.Ok(result);
        }).WithAdminWrite("AdminAiConfig");

        adminRecalls.MapGet("/audio/batches/{batchId}", async (
            string batchId,
            IVoiceDesignRegenerationService regenService,
            CancellationToken ct) =>
        {
            var batch = await regenService.GetBatchProgressAsync(batchId, ct);
            return batch is null ? Results.NotFound() : Results.Ok(batch);
        }).WithAdminRead("AdminAiConfig");

        adminRecalls.MapPost("/tts/backfill", async (
            [FromQuery] bool? execute,
            HttpContext http,
            IVoiceDesignRegenerationService regenService,
            CancellationToken ct) =>
        {
            var body = new AdminAudioRegenerateRequest(
                AudioType: "recalls",
                Scope: "missing",
                ModelVariant: null,
                VoiceId: null,
                Instructions: null,
                Speed: null,
                Pitch: null,
                Emotion: null,
                ProviderName: "elevenlabs",
                ForceRegenerate: false,
                DryRun: execute != true);
            var userId = http.User.FindFirst("sub")?.Value ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var result = await regenService.EnqueueBulkRegenerationAsync(body, userId, ct);
            return Results.Ok(result);
        }).WithAdminWrite("AdminAiConfig");

        return app;
    }

    /// <summary>
    /// Authoritative premium check for content scoping: admins and learners with an
    /// eligible, non-frozen subscription are "premium" (see full content); everyone
    /// else is scoped to free-preview terms. Mirrors the audio endpoint's own gate.
    /// (VocabularyEndpoints' IsPremiumAsync is file-scoped, hence this local twin.)
    /// </summary>
    static async Task<bool> ResolveIsPremiumAsync(
        HttpContext http, IEffectiveEntitlementResolver entitlements, CancellationToken ct)
    {
        if (http.User.IsInRole("admin")) return true;
        var snapshot = await entitlements.ResolveAsync(http.UserId(), ct);
        return snapshot.HasEligibleSubscription && !snapshot.IsFrozen
            && snapshot.IsModuleEnabled(ModuleKeys.Recalls);
    }

    /// <summary>
    /// Locks learner-facing Recalls CONTENT behind active enrolment (spec: recalls
    /// "locked_until_enrolled"). Returns <c>null</c> when access is allowed, otherwise a
    /// 402 result. Admins bypass the gate. Mirrors the audio endpoint's gate: admin role
    /// via <see cref="ClaimsPrincipal.IsInRole"/> and user id via the NameIdentifier claim.
    /// </summary>
    static async Task<IResult?> RequireRecallEnrolmentAsync(
        HttpContext http, IEffectiveEntitlementResolver entitlements, CancellationToken ct)
    {
        if (http.User.IsInRole("admin")) return null;
        var snapshot = await entitlements.ResolveAsync(http.UserId(), ct);
        if (snapshot.HasEligibleSubscription && !snapshot.IsFrozen
            && snapshot.IsModuleEnabled(ModuleKeys.Recalls)) return null;
        return Results.Json(
            new
            {
                code = "enrolment_required",
                message = "Recalls content is available after enrolment.",
            },
            statusCode: StatusCodes.Status402PaymentRequired);
    }
}

file static class RecallsHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
