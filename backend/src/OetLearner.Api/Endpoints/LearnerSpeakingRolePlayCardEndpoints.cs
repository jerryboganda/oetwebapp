using System.Security.Claims;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

// Phase 1 (B.1) of the OET Speaking module roadmap.
//
// Learner-facing read of a single `RolePlayCard`. The handler delegates
// to `LearnerService.GetSpeakingRolePlayCardForLearnerAsync` which is
// the **only** path through which a learner can fetch a role-play card
// and is contractually obliged never to leak interlocutor data. The
// xUnit test `RolePlayCardSerializationTests` enforces that guarantee.
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
