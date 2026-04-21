using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class MockAdminEndpoints
{
    public static IEndpointRouteBuilder MapMockAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/mock-bundles")
            .RequireAuthorization("AdminContentRead")
            .WithTags("Admin Mock Bundles");

        group.MapGet("", async (
            MockService service,
            CancellationToken ct,
            [FromQuery] string? status,
            [FromQuery] string? mockType,
            [FromQuery] string? subtest) =>
            Results.Ok(await service.ListBundlesAsync(status, mockType, subtest, ct)));

        group.MapGet("/{id}", async (string id, MockService service, CancellationToken ct) =>
            Results.Ok(await service.GetBundleAsync(id, ct)));

        group.MapPost("", async (
            AdminMockBundleCreateRequest request,
            MockService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.CreateBundleAsync(request, AdminId(http), ct)))
            .RequireAuthorization("AdminContentWrite");

        group.MapPut("/{id}", async (
            string id,
            AdminMockBundleUpdateRequest request,
            MockService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateBundleAsync(id, request, AdminId(http), ct)))
            .RequireAuthorization("AdminContentWrite");

        group.MapDelete("/{id}", async (
            string id,
            MockService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.ArchiveBundleAsync(id, AdminId(http), ct)))
            .RequireAuthorization("AdminContentWrite");

        group.MapPost("/{id}/sections", async (
            string id,
            AdminMockBundleSectionRequest request,
            MockService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.AddSectionAsync(id, request, AdminId(http), ct)))
            .RequireAuthorization("AdminContentWrite");

        group.MapPut("/{id}/sections/reorder", async (
            string id,
            AdminMockBundleReorderRequest request,
            MockService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.ReorderSectionsAsync(id, request, AdminId(http), ct)))
            .RequireAuthorization("AdminContentWrite");

        group.MapPost("/{id}/publish", async (
            string id,
            MockService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.PublishBundleAsync(id, AdminId(http), ct)))
            .RequireAuthorization("AdminContentPublish");

        return app;
    }

    private static string AdminId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.FindFirstValue("sub")
            ?? "system";
}
