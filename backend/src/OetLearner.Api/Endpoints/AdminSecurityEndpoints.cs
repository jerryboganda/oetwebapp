using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin read surface for <see cref="OetLearner.Api.Domain.SecurityEvent"/>
/// (Course Platform Security Requirements §4.4). Session/device management
/// mutations (targeted revoke, device reset, block playback) land here in a
/// later phase once <c>ISessionRevocationService</c> exists; this phase is
/// read-only.
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

        return app;
    }
}
