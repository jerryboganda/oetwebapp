using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class GamificationEndpoints
{
    public static IEndpointRouteBuilder MapGamificationEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").RequireAuthorization("LearnerOnly");

        // ── XP ──────────────────────────────────────────────────────────
        v1.MapGet("/gamification/xp", async (HttpContext http, GamificationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetXpAsync(http.UserId(), ct)));

        // ── Streaks ──────────────────────────────────────────────────────
        v1.MapGet("/gamification/streak", async (HttpContext http, GamificationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetStreakAsync(http.UserId(), ct)));

        v1.MapPost("/gamification/streak/activity", async (HttpContext http, GamificationService svc, CancellationToken ct) =>
            Results.Ok(await svc.RecordActivityAsync(http.UserId(), ct)));

        // ── Achievements ──────────────────────────────────────────────────
        v1.MapGet("/gamification/achievements", async (HttpContext http, GamificationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAchievementsAsync(http.UserId(), ct)));

        // ── Leaderboard ──────────────────────────────────────────────────
        v1.MapGet("/gamification/leaderboard", async (
            [FromQuery] string? examTypeCode,
            [FromQuery] string period,
            GamificationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetLeaderboardAsync(examTypeCode, period ?? "weekly", ct)));

        v1.MapGet("/gamification/leaderboard/my-position", async (
            HttpContext http,
            [FromQuery] string? examTypeCode,
            [FromQuery] string? period,
            GamificationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetLeaderboardPositionAsync(http.UserId(), examTypeCode, period ?? "weekly", ct)));

        v1.MapPost("/gamification/leaderboard/opt-in", async (
            HttpContext http,
            OptInRequest request,
            GamificationService svc, CancellationToken ct) =>
            Results.Ok(await svc.SetLeaderboardOptInAsync(http.UserId(), request.OptedIn, ct)));

        return app;
    }
}

public record OptInRequest(bool OptedIn);

file static class GamificationHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
