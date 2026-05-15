using System.Security.Claims;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class AdminLaunchReadinessEndpoints
{
    public static IEndpointRouteBuilder MapAdminLaunchReadinessEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin/launch-readiness")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        admin.MapGet("/settings", async (ILaunchReadinessService service, CancellationToken ct)
                => Results.Ok(await service.GetSettingsAsync(ct)))
            .WithAdminRead("AdminSystemAdmin");

        admin.MapPut("/settings", async (
                HttpContext http,
                AdminLaunchReadinessSettingsRequest request,
                ILaunchReadinessService service,
                CancellationToken ct) =>
            {
                var updated = await service.UpdateSettingsAsync(http.AdminId(), http.AdminName(), request, ct);
                return Results.Ok(updated);
            })
            .WithAdminWrite("AdminSystemAdmin");

        app.MapGet("/v1/app-release", async (string? platform, ILaunchReadinessService service, CancellationToken ct)
                => Results.Ok(await service.GetPublicReleasePolicyAsync(platform, ct)))
            .AllowAnonymous()
            .RequireRateLimiting("PerUser");

        return app;
    }

    private static string AdminId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated admin id is required.");

    private static string AdminName(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.Name) ?? "Admin";
}
