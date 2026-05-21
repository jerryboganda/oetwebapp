using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class MockAdminEndpoints
{
    public static IEndpointRouteBuilder MapMockAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/mock-bundles")
            .RequireAuthorization("AdminContentRead")
            .RequireRateLimiting("PerUser")
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
            .WithAdminWrite("AdminContentWrite");

        group.MapPut("/{id}", async (
            string id,
            AdminMockBundleUpdateRequest request,
            MockService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateBundleAsync(id, request, AdminId(http), ct)))
            .WithAdminWrite("AdminContentWrite");

        group.MapDelete("/{id}", async (
            string id,
            MockService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.ArchiveBundleAsync(id, AdminId(http), ct)))
            .WithAdminWrite("AdminContentWrite");

        group.MapPost("/{id}/sections", async (
            string id,
            AdminMockBundleSectionRequest request,
            MockService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.AddSectionAsync(id, request, AdminId(http), ct)))
            .WithAdminWrite("AdminContentWrite");

        group.MapPut("/{id}/sections/reorder", async (
            string id,
            AdminMockBundleReorderRequest request,
            MockService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.ReorderSectionsAsync(id, request, AdminId(http), ct)))
            .WithAdminWrite("AdminContentWrite");

        group.MapPost("/{id}/publish", async (
            string id,
            MockService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.PublishBundleAsync(id, AdminId(http), ct)))
            .WithAdminWrite("AdminContentPublish");

        // Mocks V2 Wave 3 — item analysis dashboard.
        group.MapGet("/{id}/item-analysis", async (
            string id,
            MockItemAnalysisService analysis,
            CancellationToken ct) =>
            Results.Ok(await analysis.GetForBundleAsync(id, ct)))
            .RequireAuthorization("AdminQualityAnalytics");

        group.MapPost("/{id}/item-analysis/recompute", async (
            string id,
            MockItemAnalysisService analysis,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await analysis.RecomputeAsync(id, AdminId(http), ct)))
            .WithAdminWrite("AdminContentWrite");

        // Mocks Wave 4 — listening-only filtered view of the item-analysis
        // dashboard so admins can drill into Listening item difficulty,
        // distractor pull, and tempting-distractor flags without scrolling
        // through Reading rows. Recompute is shared (POST .../recompute).
        group.MapGet("/{id}/listening-item-analysis", async (
            string id,
            MockItemAnalysisService analysis,
            CancellationToken ct) =>
            Results.Ok(await analysis.GetForBundleListeningAsync(id, ct)))
            .RequireAuthorization("AdminQualityAnalytics");

        var adminMocks = app.MapGroup("/v1/admin/mocks")
            .RequireAuthorization("AdminContentRead")
            .RequireRateLimiting("PerUser")
            .WithTags("Admin Mocks");

        adminMocks.MapGet("/item-analysis", async (
            MockItemAnalysisService analysis,
            CancellationToken ct,
            [FromQuery] string? bundleId,
            [FromQuery] string? paperId) =>
            Results.Ok(await analysis.GetDashboardAsync(bundleId, paperId, ct)))
            .RequireAuthorization("AdminQualityAnalytics");

        adminMocks.MapGet("/analytics", async (MockService service, CancellationToken ct) =>
            Results.Ok(await service.GetAdminMockAnalyticsAsync(ct)))
            .RequireAuthorization("AdminQualityAnalytics");

        adminMocks.MapGet("/risk-list", async (MockService service, CancellationToken ct) =>
            Results.Ok(await service.GetAdminMockRiskListAsync(ct)))
            .RequireAuthorization("AdminQualityAnalytics");

        // Mocks Wave 8 — admin leak-report queue.
        adminMocks.MapGet("/leak-reports", async (
            MockService service,
            CancellationToken ct,
            [FromQuery] string? status,
            [FromQuery] int? limit) =>
            Results.Ok(new
            {
                items = await service.ListLeakReportsAsync(status, limit ?? 50, ct)
            }))
            .WithAdminRead("AdminContentRead");

        adminMocks.MapPatch("/leak-reports/{id}", async (
            string id,
            MockLeakReportUpdateRequest request,
            MockService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateLeakReportAsync(AdminId(http), id, request, ct)))
            .WithAdminWrite("AdminContentWrite");

        // Mocks Module Phase 6 — admin item retire endpoint. PATCH because the
        // change is partial (we toggle a soft-retire flag without rewriting
        // the rest of the snapshot row). Idempotent: a duplicate call with the
        // same body returns the cached envelope and does not re-emit audit.
        adminMocks.MapPatch("/items/{itemId}", async (
            string itemId,
            MockItemRetireRequest request,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await RetireMockItemAsync(db, AdminId(http), itemId, request, ct)))
            .WithAdminWrite("AdminContentWrite");

        // Mocks V2 Wave 4 — admin booking management.
        var bookings = app.MapGroup("/v1/admin/mock-bookings")
            .RequireAuthorization("AdminContentRead")
            .RequireRateLimiting("PerUser")
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
            .WithAdminWrite("AdminContentWrite");

        bookings.MapPost("/{bookingId}/live-room/transition", async (
            string bookingId,
            LiveRoomTransitionRequest request,
            MockBookingService service,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await service.TransitionLiveRoomAsync(AdminId(http), ApplicationUserRoles.Admin, isAdmin: true, bookingId, request, ct)))
            .WithAdminWrite("AdminContentWrite");

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

    /// <summary>
    /// Mocks Module Phase 6 — soft-retire an item-analysis row. The item id
    /// corresponds to <see cref="MockItemAnalysisSnapshot.ItemId"/>. The same
    /// item id can appear once per bundle, so callers may pass a
    /// <c>bundleId</c> hint when ambiguity is possible; without it we retire
    /// every snapshot matching the item id (rare in practice — the unique
    /// index <c>UX_MockItemAnalysis_Bundle_Item</c> keys on the pair).
    /// </summary>
    private static async Task<MockItemRetireResponse> RetireMockItemAsync(
        LearnerDbContext db,
        string adminId,
        string itemId,
        MockItemRetireRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            throw ApiException.Validation(
                "mock_item_retire_id_required",
                "itemId is required.");
        }

        if (!request.Retire)
        {
            throw ApiException.Validation(
                "mock_item_retire_flag_required",
                "Set retire: true to retire the item.");
        }

        // Idempotency — same itemId + scope returns the cached envelope so a
        // repeated retire call is a no-op. Audit fires once per logical retire.
        const string idempotencyScope = "mock_item_retire";
        var idempotencyKey = $"mock_item_retire:{itemId}";
        var existingIdempotency = await db.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Scope == idempotencyScope && r.Key == idempotencyKey, ct);
        if (existingIdempotency is not null)
        {
            var cached = JsonSupport.Deserialize<MockItemRetireResponse>(
                existingIdempotency.ResponseJson,
                new MockItemRetireResponse(itemId, 0, null, null, null));
            return cached;
        }

        var rows = await db.MockItemAnalysisSnapshots
            .Where(s => s.ItemId == itemId
                && (request.BundleId == null || s.MockBundleId == request.BundleId))
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            throw ApiException.NotFound(
                "mock_item_not_found",
                $"Item-analysis snapshot for item '{itemId}' was not found.");
        }

        var now = DateTimeOffset.UtcNow;
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason!.Trim();

        var before = rows
            .Select(r => new
            {
                snapshotId = r.Id,
                bundleId = r.MockBundleId,
                retiredAt = r.RetiredAt,
                retiredReason = r.RetiredReason,
                retiredByAdminId = r.RetiredByAdminId,
            })
            .ToArray();

        foreach (var row in rows)
        {
            row.RetiredAt = now;
            row.RetiredReason = reason;
            row.RetiredByAdminId = adminId;
        }

        var after = rows
            .Select(r => new
            {
                snapshotId = r.Id,
                bundleId = r.MockBundleId,
                retiredAt = r.RetiredAt,
                retiredReason = r.RetiredReason,
                retiredByAdminId = r.RetiredByAdminId,
            })
            .ToArray();

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit-{Guid.NewGuid():N}",
            OccurredAt = now,
            ActorId = adminId,
            ActorName = adminId,
            Action = "mock_item_retired",
            ResourceType = "mock_item",
            ResourceId = itemId,
            Details = JsonSupport.Serialize(new
            {
                reason,
                affectedSnapshots = rows.Count,
                bundleId = request.BundleId,
                beforeJson = JsonSupport.Serialize(before),
                afterJson = JsonSupport.Serialize(after),
            }),
        });

        var response = new MockItemRetireResponse(
            ItemId: itemId,
            AffectedSnapshots: rows.Count,
            RetiredAt: now,
            Reason: reason,
            RetiredByAdminId: adminId);

        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = $"idem-{Guid.NewGuid():N}",
            Scope = idempotencyScope,
            Key = idempotencyKey,
            ResponseJson = JsonSupport.Serialize(response),
            CreatedAt = now,
        });

        await db.SaveChangesAsync(ct);
        return response;
    }
}

/// <summary>
/// Request body for <c>PATCH /v1/admin/mocks/items/{itemId}</c>.
/// Mocks Module Phase 6.
/// </summary>
public sealed record MockItemRetireRequest(bool Retire, string? Reason, string? BundleId);

/// <summary>
/// Response envelope for <c>PATCH /v1/admin/mocks/items/{itemId}</c>.
/// Mocks Module Phase 6.
/// </summary>
public sealed record MockItemRetireResponse(
    string ItemId,
    int AffectedSnapshots,
    DateTimeOffset? RetiredAt,
    string? Reason,
    string? RetiredByAdminId);
