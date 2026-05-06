using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class ExpertCompensationEndpoints
{
    public static IEndpointRouteBuilder MapExpertCompensationEndpoints(this IEndpointRouteBuilder app)
    {
        var comp = app.MapGroup("/v1/expert/compensation")
            .RequireAuthorization("ExpertOnly")
            .RequireRateLimiting("PerUser");

        comp.MapGet("/", async (HttpContext http, ExpertCompensationService service, CancellationToken ct)
            => Results.Ok(await service.GetCompensationSummaryAsync(http.ExpertId(), ct)));

        comp.MapGet("/earnings", async (HttpContext http, ExpertCompensationService service, CancellationToken ct,
            [FromQuery] int? page, [FromQuery] int? pageSize)
            => Results.Ok(await service.GetEarningsHistoryAsync(http.ExpertId(), page ?? 1, pageSize ?? 25, ct)));

        comp.MapGet("/payouts", async (HttpContext http, ExpertCompensationService service, CancellationToken ct)
            => Results.Ok(await service.GetPayoutsAsync(http.ExpertId(), ct)));

        return app;
    }

    private static string ExpertId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated expert id is required.");
}
