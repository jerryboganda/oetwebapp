using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class AnswerKeyReportAdminEndpoints
{
    public static IEndpointRouteBuilder MapAnswerKeyReportAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/answer-key-reports")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        group.MapGet("", async (
                AnswerKeyReportService service,
                CancellationToken ct,
                [FromQuery] string? status,
                [FromQuery] string? assessment,
                [FromQuery] int? limit) =>
                Results.Ok(new
                {
                    items = await service.ListAdminAsync(status, assessment, limit ?? 50, ct)
                }))
            .WithAdminRead("AdminContentRead");

        group.MapGet("/{id}", async (
                string id,
                AnswerKeyReportService service,
                CancellationToken ct) =>
                Results.Ok(await service.GetAdminAsync(id, ct)))
            .WithAdminRead("AdminContentRead");

        group.MapPatch("/{id}", async (
                string id,
                AnswerKeyReportUpdateRequest request,
                AnswerKeyReportService service,
                HttpContext http,
                CancellationToken ct) =>
                Results.Ok(await service.UpdateAdminAsync(AdminId(http), id, request, ct)))
            .WithAdminWrite("AdminContentWrite");

        return app;
    }

    private static string AdminId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? http.User.FindFirstValue("sub")
           ?? "system";
}
