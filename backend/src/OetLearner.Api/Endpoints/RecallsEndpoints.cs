using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Recalls;

namespace OetLearner.Api.Endpoints;

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

        recalls.MapGet("/today", async (HttpContext http, RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetTodayAsync(http.UserId(), ct)));

        recalls.MapGet("/queue", async (
            HttpContext http,
            [FromQuery] int limit,
            RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetQueueAsync(http.UserId(), limit, ct)));

        recalls.MapPost("/star", async (
            HttpContext http,
            RecallsStarRequest request,
            RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.StarAsync(http.UserId(), request, ct)));

        recalls.MapPost("/listen-type", async (
            HttpContext http,
            RecallsListenTypeRequest request,
            RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListenAndTypeAsync(http.UserId(), request, ct)));

        recalls.MapGet("/audio/{termId}", async (
            HttpContext http,
            string termId,
            [FromQuery] string? speed,
            RecallsService svc,
            IFileStorage storage,
            IEffectiveEntitlementResolver entitlements,
            CancellationToken ct) =>
        {
            // PRD Phase 2 §2 + §3 (Backend Auth Gating): click-to-hear pronunciation
            // is a paid-tier feature. Free / anonymous-tier learners must be turned
            // away with 402 Payment Required so the frontend can prompt for upgrade.
            var userId = http.UserId();
            http.Response.Headers.CacheControl = "private, no-store";
            http.Response.Headers.Vary = "Authorization";
            var snapshot = await entitlements.ResolveAsync(userId, ct);
            if (!snapshot.HasEligibleSubscription || snapshot.IsFrozen)
            {
                return Results.Json(
                    new
                    {
                        code = "subscription_required",
                        message = "Pronunciation audio is available for paid candidates only.",
                    },
                    statusCode: StatusCodes.Status402PaymentRequired);
            }

            var audio = await svc.EnsureAudioAsync(termId, speed ?? "normal", ct);
            if (!storage.Exists(audio.StorageKey)) return Results.NotFound();

            http.Response.Headers["X-Recalls-Tts-Provider"] = audio.Provider;
            var stream = await storage.OpenReadAsync(audio.StorageKey, ct);
            return Results.Stream(stream, audio.ContentType, enableRangeProcessing: false);
        });

        recalls.MapGet("/library", async (
            HttpContext http,
            [FromQuery] string? bucket,
            [FromQuery] string? topic,
            RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetLibraryAsync(http.UserId(), bucket, topic, ct)));

        recalls.MapPost("/explain", async (
            HttpContext http,
            RecallsExplainRequest request,
            RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.ExplainMistakeAsync(http.UserId(), request, ct)));

        recalls.MapGet("/quiz", async (
            HttpContext http,
            [FromQuery] string? mode,
            [FromQuery] int limit,
            RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetQuizAsync(http.UserId(), mode ?? "listen_and_type", limit, ct)));

        recalls.MapGet("/report/week", async (
            HttpContext http, RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetWeeklyReportAsync(http.UserId(), ct)));

        recalls.MapGet("/revision-plan", async (
            HttpContext http, RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetRevisionPlanAsync(http.UserId(), ct)));

        // Admin-only: CSV bulk upload of vocabulary terms (spec §8).
        var adminRecalls = app.MapGroup("/v1/admin/recalls").RequireAuthorization("AdminContentWrite");
        adminRecalls.MapPost("/bulk-upload", () =>
            Results.Json(new
            {
                code = "legacy_recalls_import_disabled",
                message = "Legacy Recalls bulk upload is disabled for production safety. Use /v1/admin/vocabulary/import/preview and /v1/admin/vocabulary/import with dryRun first."
            }, statusCode: StatusCodes.Status409Conflict));

        // Recalls Content Pack v1 (2026-05-05): one-shot ElevenLabs (or any
        // configured ConversationTtsProvider) audio backfill for terms in a
        // given profession that don't yet have AudioUrl. Idempotent — skips
        // terms that already have audio. Bounded by `limit` (default 50) so a
        // single call cannot accidentally rack up unbounded ElevenLabs spend.
        adminRecalls.MapPost("/tts/backfill", async (
            [FromQuery] string profession,
            [FromQuery] int? limit,
            [FromQuery] bool? sentence,
            IRecallsTtsService tts,
            OetLearner.Api.Data.LearnerDbContext db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(profession))
                return Results.BadRequest(new { code = "profession_required" });
            var cap = Math.Clamp(limit ?? 50, 1, 200);
            var includeSentence = sentence ?? false;

            var prof = profession.Trim().ToLowerInvariant();
            var query = db.VocabularyTerms.Where(t =>
                t.ProfessionId == prof &&
                (t.AudioUrl == null || t.AudioUrl == ""));
            var batch = await query.OrderBy(t => t.Term).Take(cap).ToListAsync(ct);

            var generated = 0;
            var failed = 0;
            foreach (var term in batch)
            {
                try
                {
                    var word = await tts.GenerateWordAsync(
                        term.Term,
                        new RecallsTtsOptions(Locale: "en-GB", Speed: "normal", Voice: ""),
                        ct);
                    term.AudioUrl = word.Url;

                    if (includeSentence && !string.IsNullOrWhiteSpace(term.ExampleSentence))
                    {
                        var sentAudio = await tts.GenerateSentenceAsync(
                            term.ExampleSentence,
                            new RecallsTtsOptions(Locale: "en-GB", Speed: "normal", Voice: ""),
                            ct);
                        term.AudioSentenceUrl = sentAudio.Url;
                    }

                    var slow = await tts.GenerateWordAsync(
                        term.Term,
                        new RecallsTtsOptions(Locale: "en-GB", Speed: "slow", Voice: ""),
                        ct);
                    term.AudioSlowUrl = slow.Url;

                    term.UpdatedAt = DateTimeOffset.UtcNow;
                    generated++;
                }
                catch
                {
                    failed++;
                }
            }
            if (generated > 0) await db.SaveChangesAsync(ct);

            var remaining = await db.VocabularyTerms.CountAsync(t =>
                t.ProfessionId == prof &&
                (t.AudioUrl == null || t.AudioUrl == ""), ct);

            return Results.Ok(new
            {
                profession = prof,
                considered = batch.Count,
                generated,
                failed,
                remaining,
            });
        });

        return app;
    }
}

file static class RecallsHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
