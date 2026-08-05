using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin surface for <see cref="OetLearner.Api.Domain.SecurityEvent"/> and
/// per-account session/device management (Course Platform Security
/// Requirements §4.4): events/video-protection-events read, plus targeted
/// session revoke, trusted-device reset, and immediate playback block —
/// all via <see cref="AdminSecurityService"/>.
/// </summary>
public static class AdminSecurityEndpoints
{
    public static IEndpointRouteBuilder MapAdminSecurityEndpoints(this IEndpointRouteBuilder app)
    {
        var security = app.MapGroup("/v1/admin/security")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        security.MapGet("/events", async (
            LearnerDbContext db,
            CancellationToken ct,
            string? accountId,
            string? kind,
            string? severity,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int? page,
            int? pageSize) =>
        {
            var resolvedPage = page is > 0 ? page.Value : 1;
            var resolvedPageSize = pageSize is > 0 and <= 200 ? pageSize.Value : 50;

            var query = db.SecurityEvents.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(accountId))
            {
                query = query.Where(e => e.AuthAccountId == accountId);
            }
            if (!string.IsNullOrWhiteSpace(kind))
            {
                query = query.Where(e => e.Kind == kind);
            }
            if (!string.IsNullOrWhiteSpace(severity))
            {
                query = query.Where(e => e.Severity == severity);
            }
            if (from is not null)
            {
                query = query.Where(e => e.OccurredAt >= from.Value);
            }
            if (to is not null)
            {
                query = query.Where(e => e.OccurredAt <= to.Value);
            }

            var totalCount = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(e => e.OccurredAt)
                .Skip((resolvedPage - 1) * resolvedPageSize)
                .Take(resolvedPageSize)
                .Select(e => new
                {
                    e.Id,
                    e.OccurredAt,
                    e.AuthAccountId,
                    e.Kind,
                    e.Severity,
                    e.IpAddress,
                    e.CountryCode,
                    e.UserAgent,
                    e.Platform,
                    e.SessionFamilyId,
                    e.DeviceId,
                    e.DetailsJson,
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                items,
                page = resolvedPage,
                pageSize = resolvedPageSize,
                totalCount,
            });
        })
        .WithAdminRead("AdminSecurityRead");

        security.MapGet("/video-protection-events", async (
            LearnerDbContext db,
            CancellationToken ct,
            string? userId,
            string? videoId,
            string? kind,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int? page,
            int? pageSize) =>
        {
            var resolvedPage = page is > 0 ? page.Value : 1;
            var resolvedPageSize = pageSize is > 0 and <= 200 ? pageSize.Value : 50;

            var query = db.VideoProtectionEvents.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(userId))
            {
                query = query.Where(e => e.UserId == userId);
            }
            if (!string.IsNullOrWhiteSpace(videoId))
            {
                query = query.Where(e => e.VideoId == videoId);
            }
            if (!string.IsNullOrWhiteSpace(kind))
            {
                query = query.Where(e => e.Kind == kind);
            }
            if (from is not null)
            {
                query = query.Where(e => e.OccurredAt >= from.Value);
            }
            if (to is not null)
            {
                query = query.Where(e => e.OccurredAt <= to.Value);
            }

            var totalCount = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(e => e.OccurredAt)
                .Skip((resolvedPage - 1) * resolvedPageSize)
                .Take(resolvedPageSize)
                .Select(e => new
                {
                    e.Id,
                    e.OccurredAt,
                    e.UserId,
                    e.VideoId,
                    e.SessionId,
                    e.Kind,
                    e.Severity,
                    e.Platform,
                    e.IpAddress,
                    e.MetadataJson,
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                items,
                page = resolvedPage,
                pageSize = resolvedPageSize,
                totalCount,
            });
        })
        .WithAdminRead("AdminSecurityRead");

        var userSecurity = app.MapGroup("/v1/admin/users/{userId}/security")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        userSecurity.MapGet("/sessions", async (string userId, AdminSecurityService service, CancellationToken ct)
                => Results.Ok(await service.GetSessionsAsync(userId, ct)))
            .WithAdminRead("AdminSecurityRead");

        userSecurity.MapGet("/devices", async (string userId, AdminSecurityService service, CancellationToken ct)
                => Results.Ok(await service.GetDevicesAsync(userId, ct)))
            .WithAdminRead("AdminSecurityRead");

        userSecurity.MapPost("/sessions/{familyId:guid}/revoke", async (
                string userId, Guid familyId, HttpContext http, AdminSecurityService service, CancellationToken ct)
                => Results.Ok(new { revoked = await service.RevokeSessionAsync(http.AdminId(), http.AdminName(), userId, familyId, ct) }))
            .WithAdminWrite("AdminSecurityWrite");

        userSecurity.MapPost("/devices/reset", async (
                string userId, HttpContext http, AdminSecurityService service, CancellationToken ct) =>
        {
            await service.ResetDeviceAsync(http.AdminId(), http.AdminName(), userId, ct);
            return Results.Ok(new { reset = true });
        })
            .WithAdminWrite("AdminSecurityWrite");

        userSecurity.MapPost("/block-playback", async (
                string userId, HttpContext http, AdminSecurityService service, CancellationToken ct)
                => Results.Ok(new { revoked = await service.BlockPlaybackAsync(http.AdminId(), http.AdminName(), userId, ct) }))
            .WithAdminWrite("AdminSecurityWrite");

        userSecurity.MapPost("/device-exemption", async (
                string userId, SetDeviceExemptionRequest request, HttpContext http, AdminSecurityService service, CancellationToken ct) =>
        {
            var result = await service.SetDeviceExemptionAsync(http.AdminId(), http.AdminName(), userId, request.Exempt, ct);
            return Results.Ok(new { exempt = result });
        })
            .WithAdminWrite("AdminSecurityWrite");

        userSecurity.MapPost("/clear-cooldown", async (
                string userId, HttpContext http, AdminSecurityService service, CancellationToken ct) =>
        {
            await service.ClearDeviceCooldownAsync(http.AdminId(), http.AdminName(), userId, ct);
            return Results.Ok(new { cleared = true });
        })
            .WithAdminWrite("AdminSecurityWrite");

        userSecurity.MapPost("/device-limit", async (
                string userId, SetCandidateDeviceLimitRequest request, HttpContext http, AdminSecurityService service, CancellationToken ct) =>
        {
            var result = await service.SetCandidateDeviceLimitAsync(http.AdminId(), http.AdminName(), userId, request.MaxDevices, ct);
            return Results.Ok(new
            {
                maxDevices = result.MaxDevices,
                effectiveMaxDevices = result.EffectiveMaxDevices,
                revokedDevices = result.RevokedDevices,
            });
        })
            .WithAdminWrite("AdminSecurityWrite");

        userSecurity.MapPost("/devices/{deviceId:guid}/revoke", async (
                string userId, Guid deviceId, HttpContext http, AdminSecurityService service, CancellationToken ct) =>
        {
            var revoked = await service.RevokeDeviceAsync(http.AdminId(), http.AdminName(), userId, deviceId, ct);
            return Results.Ok(new { revoked });
        })
            .WithAdminWrite("AdminSecurityWrite");

        return app;
    }

    private static string AdminId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated admin id is required.");

    private static string AdminName(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.Name) ?? "Admin";
}

public sealed record SetDeviceExemptionRequest(bool Exempt);
public sealed record SetCandidateDeviceLimitRequest(int? MaxDevices);
