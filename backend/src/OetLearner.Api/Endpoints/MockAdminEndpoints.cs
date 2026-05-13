using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;
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

        // Mocks V2 Wave 3 — item analysis dashboard.
        group.MapGet("/{id}/item-analysis", async (
            string id,
            MockItemAnalysisService analysis,
            CancellationToken ct) =>
            Results.Ok(await analysis.GetForBundleAsync(id, ct)));

        group.MapPost("/{id}/item-analysis/recompute", async (
            string id,
            MockItemAnalysisService analysis,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await analysis.RecomputeAsync(id, AdminId(http), ct)))
            .RequireAuthorization("AdminContentWrite");

        // Mocks Wave 4 — listening-only filtered view of the item-analysis
        // dashboard so admins can drill into Listening item difficulty,
        // distractor pull, and tempting-distractor flags without scrolling
        // through Reading rows. Recompute is shared (POST .../recompute).
        group.MapGet("/{id}/listening-item-analysis", async (
            string id,
            MockItemAnalysisService analysis,
            CancellationToken ct) =>
            Results.Ok(await analysis.GetForBundleListeningAsync(id, ct)));

        var adminMocks = app.MapGroup("/v1/admin/mocks")
            .RequireAuthorization("AdminContentRead")
            .WithTags("Admin Mocks");

        adminMocks.MapGet("/item-analysis", async (
            MockItemAnalysisService analysis,
            CancellationToken ct,
            [FromQuery] string? bundleId,
            [FromQuery] string? paperId) =>
            Results.Ok(await analysis.GetDashboardAsync(bundleId, paperId, ct)));

        adminMocks.MapGet("/analytics", async (MockService service, CancellationToken ct) =>
            Results.Ok(await service.GetAdminMockAnalyticsAsync(ct)));

        adminMocks.MapGet("/risk-list", async (MockService service, CancellationToken ct) =>
            Results.Ok(await service.GetAdminMockRiskListAsync(ct)));

        // Mocks Wave 8 — admin leak-report queue.
        adminMocks.MapGet("/leak-reports", async (
            MockService service,
            CancellationToken ct,
            [FromQuery] string? status,
            [FromQuery] int? limit) =>
            Results.Ok(new
            {
                items = await service.ListLeakReportsAsync(status, limit ?? 50, ct)
            }));

        adminMocks.MapPatch("/leak-reports/{id}", async (
            string id,
            MockLeakReportUpdateRequest request,
            MockService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateLeakReportAsync(AdminId(http), id, request, ct)))
            .RequireAuthorization("AdminContentWrite");

        // Mocks V2 Wave 4 — admin booking management.
        var bookings = app.MapGroup("/v1/admin/mock-bookings")
            .RequireAuthorization("AdminContentRead")
            .WithTags("Admin Mock Bookings");

        bookings.MapGet("", async (
            MockBookingService service,
            CancellationToken ct,
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to) =>
            Results.Ok(await service.ListForAdminAsync(from, to, ct)));

        bookings.MapGet("/{bookingId}", async (
            string bookingId,
            MockBookingService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.GetForExpertAsync(AdminId(http), isAdmin: true, bookingId, ct)));

        bookings.MapPatch("/{bookingId}/assign", async (
            string bookingId,
            MockBookingAssignmentRequest request,
            MockBookingService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.AssignStaffAsync(AdminId(http), bookingId, request, ct)))
            .RequireAuthorization("AdminContentWrite");

        bookings.MapPost("/{bookingId}/live-room/transition", async (
            string bookingId,
            LiveRoomTransitionRequest request,
            MockBookingService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.TransitionLiveRoomAsync(AdminId(http), ApplicationUserRoles.Admin, isAdmin: true, bookingId, request, ct)))
            .RequireAuthorization("AdminContentWrite")
            .RequireRateLimiting("PerUserWrite");

        adminMocks.MapGet("/bookings", async (
            MockBookingService service,
            CancellationToken ct,
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to) =>
            Results.Ok(await service.ListForAdminAsync(from, to, ct)));

        return app;
    }

    private static string AdminId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.FindFirstValue("sub")
            ?? "system";
}
