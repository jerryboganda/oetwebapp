using System.Security.Claims;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class CustomerSupportAdminEndpoints
{
    public static IEndpointRouteBuilder MapCustomerSupportAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var support = app.MapGroup("/v1/admin/support")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        support.MapGet("/cases", async (
                string? ticketId,
                CustomerSupportCaseService service,
                CancellationToken ct) =>
                Results.Ok(await service.ListAsync(ticketId, ct)))
            .WithAdminRead("AdminCustomerSupportRead");

        support.MapPost("/cases", async (
                HttpContext http,
                CustomerSupportCaseCreateRequest request,
                CustomerSupportCaseService service,
                CancellationToken ct) =>
                Results.Ok(await service.CreateAsync(
                    http.AdminId(),
                    http.AdminName(),
                    request,
                    ct)))
            .WithAdminWrite("AdminCustomerSupportWrite");

        support.MapGet("/cases/{caseId}/candidate", async (
                string caseId,
                HttpContext http,
                CustomerSupportCaseService service,
                CancellationToken ct) =>
                Results.Ok(await service.GetCandidateAsync(
                    http.AdminId(),
                    http.AdminName(),
                    caseId,
                    ct)))
            .WithAdminRead("AdminCustomerSupportRead");

        support.MapPost("/cases/{caseId}/close", async (
                string caseId,
                HttpContext http,
                CustomerSupportCaseCloseRequest request,
                CustomerSupportCaseService service,
                CancellationToken ct) =>
                Results.Ok(await service.CloseAsync(
                    http.AdminId(),
                    http.AdminName(),
                    caseId,
                    request,
                    ct)))
            .WithAdminWrite("AdminCustomerSupportWrite");

        return app;
    }

    private static string AdminId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated admin id is required.");

    private static string AdminName(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.Name) ?? "Admin";
}
