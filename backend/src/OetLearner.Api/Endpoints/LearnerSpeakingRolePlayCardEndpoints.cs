using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Services;
using OetLearner.Api.Services.FreeSamples;

namespace OetLearner.Api.Endpoints;

// Phase 1 (B.1) of the OET Speaking module roadmap.
//
// Learner-facing list + detail for `RolePlayCard`. The handlers delegate
// to `LearnerService.GetSpeakingRolePlayCardForLearnerAsync` /
// `ListSpeakingRolePlayCardsForLearnerAsync` which are the **only** code
// paths through which a learner can fetch a role-play card. They are
// contractually obliged never to leak interlocutor data (pinned by the
// xUnit tests `RolePlayCardSerializationTests` +
// `InterlocutorScriptLeakageTests`) and to filter results by the
// caller's active profession (pinned by
// `RolePlayCardProfessionFilterTests`).
//
// `Program.cs` registers this via `MapLearnerSpeakingRolePlayCardEndpoints`.
public static class LearnerSpeakingRolePlayCardEndpoints
{
    public static IEndpointRouteBuilder MapLearnerSpeakingRolePlayCardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/speaking/role-play-cards")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser")
            .WithTags("Speaking Role-Play Cards (Learner)");

        // Plan P2.1 → FINAL 2026-09-06 — list endpoint. Server-side filters
        // by explicit profession (shared Writing/Speaking master list) and
        // primary card category, defaulting to the caller's active
        // profession. Returns the server-derived totalCount of the same set.
        // Difficulty was removed: no query parameter remains and it never
        // affects results.
        group.MapGet("", async (
            LearnerService service,
            HttpContext http,
            CancellationToken ct,
            [FromQuery] string? professionId,
            [FromQuery] string? primaryCategory) =>
            Results.Ok(await service.ListSpeakingRolePlayCardsForLearnerAsync(
                LearnerId(http), professionId, primaryCategory, ct)));

        group.MapGet("/free", async (
            LearnerService service,
            LearnerDbContext db,
            IFreeSampleService freeSamples,
            HttpContext http,
            CancellationToken ct,
            [FromQuery] string? cardId) =>
        {
            var userId = LearnerId(http);
            var requestedCardId = cardId?.Trim();
            if (string.IsNullOrWhiteSpace(requestedCardId))
            {
                var offers = await freeSamples.ListAsync(userId, FreeSampleService.Speaking, ct);
                var profession = await db.Users.AsNoTracking()
                    .Where(user => user.Id == userId)
                    .Select(user => user.ActiveProfessionId)
                    .FirstOrDefaultAsync(ct);
                var normalizedProfession = FreeSampleService.NormalizeProfession(profession);
                requestedCardId = offers.FirstOrDefault(offer => offer.ProfessionId == normalizedProfession)?.ContentId
                    ?? offers.FirstOrDefault()?.ContentId;
            }

            if (string.IsNullOrWhiteSpace(requestedCardId)
                || !await freeSamples.IsOfferedAsync(userId, FreeSampleService.Speaking, requestedCardId, ct))
            {
                throw ApiException.Conflict(
                    "free_sample_unavailable",
                    "No eligible free Speaking card is available for this learner.");
            }

            var resolvedCardId = await db.RolePlayCards.AsNoTracking()
                .Where(card => card.Id == requestedCardId || card.ContentItemId == requestedCardId)
                .Select(card => card.Id)
                .FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(resolvedCardId))
            {
                throw ApiException.NotFound("role_play_card_not_found", "That role-play card does not exist.");
            }

            return Results.Ok(await service.GetSpeakingRolePlayCardForLearnerAsync(userId, resolvedCardId, ct, allowFreeSample: true));
        })
        .WithName("GetFreeSpeakingCard");

        group.MapGet("/{id}", async (
            string id,
            LearnerService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.GetSpeakingRolePlayCardForLearnerAsync(
                LearnerId(http), id, ct)));

        return app;
    }

    private static string LearnerId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.FindFirstValue("sub")
            ?? string.Empty;
}
