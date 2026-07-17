using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetWithDrHesham.Api.Services;

namespace OetWithDrHesham.Api.Endpoints;

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

        // Plan P2.1 — list endpoint. Filters by the caller's active
        // profession + universal cards. Difficulty is the only supported
        // query filter today; adding scenario tag / criteria filter is
        // tracked under plan §2.3 on the frontend.
        group.MapGet("", async (
            LearnerService service,
            HttpContext http,
            CancellationToken ct,
            [FromQuery] string? difficulty) =>
            Results.Ok(await service.ListSpeakingRolePlayCardsForLearnerAsync(
                LearnerId(http), difficulty, ct)));

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
