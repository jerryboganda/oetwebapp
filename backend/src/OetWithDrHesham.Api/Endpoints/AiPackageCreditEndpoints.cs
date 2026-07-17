using System.Security.Claims;
using OetWithDrHesham.Api.Services.Billing;

namespace OetWithDrHesham.Api.Endpoints;

public static class AiPackageCreditEndpoints
{
    public static IEndpointRouteBuilder MapAiPackageCreditEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");

        v1.MapGet("/me/ai-package-credits", async (HttpContext http, IAiPackageCreditService service, CancellationToken ct, int? pageSize) =>
            Results.Ok(await service.GetSnapshotAsync(http.UserId(), pageSize ?? 50, ct)))
            .RequireAuthorization();

        var admin = v1.MapGroup("/admin/ai-package-credits");
        admin.MapGet("/{userId}", async (string userId, IAiPackageCreditService service, CancellationToken ct, int? pageSize) =>
            Results.Ok(await service.GetSnapshotAsync(userId, pageSize ?? 100, ct)))
            .RequireAuthorization("AdminBillingRead");

        admin.MapPost("/{userId}/adjust", async (
            string userId,
            AiPackageCreditAdjustmentRequest request,
            HttpContext http,
            IAiPackageCreditService service,
            CancellationToken ct) =>
                Results.Ok(await service.AdjustAsync(userId, request, http.AdminId(), ct)))
            .WithAdminWrite("AdminBillingSubscriptionWrite");

        admin.MapPost("/{userId}/exam-outcomes", async (
            string userId,
            LearnerExamOutcomeRequest request,
            HttpContext http,
            IAiPackageCreditService service,
            CancellationToken ct) =>
                Results.Ok(await service.RecordExamOutcomeAsync(userId, request, http.AdminId(), http.AdminName(), ct)))
            .WithAdminWrite("AdminBillingSubscriptionWrite");

        return app;
    }

    private static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? httpContext.User.FindFirstValue("sub")
           ?? httpContext.User.Identity?.Name
           ?? string.Empty;

    private static string AdminId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? httpContext.User.FindFirstValue("sub")
           ?? httpContext.User.Identity?.Name
           ?? "admin";

    private static string AdminName(this HttpContext httpContext)
        => httpContext.User.Identity?.Name ?? AdminId(httpContext);
}
