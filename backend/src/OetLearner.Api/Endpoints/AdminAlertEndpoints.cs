using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class AdminAlertEndpoints
{
    public static IEndpointRouteBuilder MapAdminAlertEndpoints(this IEndpointRouteBuilder app)
    {
        var alerts = app.MapGroup("/v1/admin/alerts")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        alerts.MapGet("/", async (AdminAlertService service, CancellationToken ct)
            => Results.Ok(await service.GetAlertsAsync(ct)))
            .WithAdminRead("AdminSystemAdmin");

        return app;
    }
}
