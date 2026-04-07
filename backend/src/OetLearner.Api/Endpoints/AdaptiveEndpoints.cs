using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class AdaptiveEndpoints
{
    public static IEndpointRouteBuilder MapAdaptiveEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").RequireAuthorization("LearnerOnly");

        v1.MapGet("/adaptive/skill-profile", async (
            HttpContext http,
            [FromQuery] string? examTypeCode,
            AdaptiveDifficultyService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetSkillProfileAsync(http.UserId(), examTypeCode, ct)));

        v1.MapGet("/adaptive/content", async (
            HttpContext http,
            [FromQuery] string examTypeCode,
            [FromQuery] string subtestCode,
            [FromQuery] int count,
            AdaptiveDifficultyService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAdaptiveContentAsync(http.UserId(), examTypeCode ?? "oet", subtestCode ?? "writing", count <= 0 ? 5 : count, ct)));

        return app;
    }
}

file static class AdaptiveHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
