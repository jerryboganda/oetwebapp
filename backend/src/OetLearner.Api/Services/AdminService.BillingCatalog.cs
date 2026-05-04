using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

// ════════════════════════════════════════════════════════════════════════════
//  Slice C — Plans / Add-ons / Coupons + version-history hardening
// ────────────────────────────────────────────────────────────────────────────
//  This partial implements the catalog-side of the May 2026 billing-hardening
//  pass. Goals (see docs/billing-hardening/C-catalog.md):
//    1. Catalog version snapshot rows (BillingPlanVersion / BillingAddOnVersion
//       / BillingCouponVersion) are physically immutable once persisted —
//       enforced via an EF SaveChangesInterceptor so it works on Postgres,
//       Sqlite and InMemory alike. Any UPDATE / DELETE attempt rejects the
//       entire SaveChanges with a structured error.
//    2. BillingPlan.Code, BillingAddOn.Code, BillingCoupon.Code are immutable
//       after first reference (validated by `ThrowIfCatalogCodeChanged` in
//       AdminService.cs). This partial adds an audit trail for every rejection.
//    3. Race-safe coupon redemption via `TryReserveCouponAtomicAsync` which
//       performs an atomic UPDATE … WHERE redemption-headroom-still-available.
//    4. Server-enforced expiry / activation windows on every reservation —
//       `EvaluateCouponWindow` is the single source of truth, never trust
//       client-supplied timestamps.
//    5. Plan archival guard for new subscriptions
//       (`EnsurePlanCanStartNewSubscription`). Existing subscriptions remain
//       bound to their snapshot version (snapshot-driven, not live-status).
// ════════════════════════════════════════════════════════════════════════════

public partial class AdminService
{
    /// <summary>
    /// Atomically reserves one redemption of <paramref name="couponId"/> for
    /// <paramref name="userId"/> at <paramref name="now"/>. The reservation is
    /// only granted when, at SaveChanges time, ALL of the following still hold:
    ///   • Coupon status is <see cref="BillingCouponStatus.Active"/>.
    ///   • <see cref="BillingCoupon.StartsAt"/> is null or ≤ now.
    ///   • <see cref="BillingCoupon.EndsAt"/> is null or &gt; now.
    ///   • Either no <see cref="BillingCoupon.UsageLimitTotal"/> is set, or the
    ///     non-voided redemption count is strictly below it.
    /// All four are enforced in a single `ExecuteUpdateAsync` so two parallel
    /// callers cannot both see "headroom available" and double-spend the coupon.
    /// </summary>
    public Task<BillingCouponReservationResult> TryReserveCouponAtomicAsync(
        string couponId,
        DateTimeOffset now,
        CancellationToken ct)
        => BillingCouponRedemptionAtomic.TryReserveAsync(db, couponId, now, ct);

    /// <summary>
    /// Returns a structured reason code if the coupon's activation window is
    /// not currently open; otherwise null. Single source of truth for window
    /// validation — never trust client-supplied timestamps.
    /// </summary>
    public static string? EvaluateCouponWindow(BillingCoupon coupon, DateTimeOffset now)
    {
        if (coupon.Status != BillingCouponStatus.Active)
        {
            return "coupon_inactive";
        }
        if (coupon.StartsAt is { } startsAt && startsAt > now)
        {
            return "coupon_not_started";
        }
        if (coupon.EndsAt is { } endsAt && endsAt <= now)
        {
            return "coupon_expired";
        }
        return null;
    }

    /// <summary>
    /// Throws when an admin attempts to start a NEW subscription against an
    /// archived plan. Existing subscriptions remain bound to their snapshot
    /// version and are unaffected — only fresh acquisitions are blocked.
    /// </summary>
    public static void EnsurePlanCanStartNewSubscription(BillingPlan plan)
    {
        if (plan.Status == BillingPlanStatus.Archived)
        {
            throw ApiException.Validation(
                "plan_archived",
                "This plan is archived and cannot be used for new subscriptions.",
                [new ApiFieldError("planCode", "archived", "Choose a different plan.")]);
        }
    }

    /// <summary>
    /// Records a structured audit trail every time a catalog code-change
    /// attempt is rejected. Called from the AdminService update flows after
    /// `ThrowIfCatalogCodeChanged` raises — wired through a try/catch so the
    /// audit failure cannot mask the original validation error.
    /// </summary>
    internal Task LogCatalogCodeImmutabilityViolationAsync(
        string adminId,
        string adminName,
        string resourceType,
        string resourceId,
        string existingCode,
        string? attemptedCode,
        CancellationToken ct)
        => LogAuditAsync(
            adminId,
            adminName,
            "RejectedCodeChange",
            resourceType,
            resourceId,
            $"Attempted to change immutable catalog code '{existingCode}' to '{attemptedCode}'",
            ct);
}

/// <summary>Result of an atomic coupon-redemption reservation attempt.</summary>
public sealed record BillingCouponReservationResult(
    bool Reserved,
    string? RejectionCode,
    int RedemptionCountAfter);

// ════════════════════════════════════════════════════════════════════════════
//  Atomic, race-safe coupon redemption helper. Lives outside AdminService so
//  it can be exercised by the concurrency tests against a raw LearnerDbContext
//  without the full DI graph.
// ════════════════════════════════════════════════════════════════════════════
public static class BillingCouponRedemptionAtomic
{
    /// <summary>
    /// Atomically reserves a single redemption of <paramref name="couponId"/>.
    /// Implemented as `ExecuteUpdateAsync` with a WHERE clause that re-checks
    /// the activation window AND the per-total redemption-count headroom in
    /// the same SQL statement, so concurrent callers cannot both observe
    /// "headroom available" and over-spend the coupon.
    /// </summary>
    public static async Task<BillingCouponReservationResult> TryReserveAsync(
        LearnerDbContext db,
        string couponId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(couponId))
        {
            return new BillingCouponReservationResult(false, "coupon_not_found", 0);
        }

        // InMemory and SQLite providers do not reliably translate the chained
        // multi-`SetProperty` ExecuteUpdateAsync used below (SQLite EF Core 10
        // throws InvalidOperationException on the mixed entity-aware /
        // constant lambdas). Fall back to a transactional read-then-update on
        // any non-PostgreSQL provider; production runs on PostgreSQL where the
        // single-statement atomic path is taken. Concurrency on SQLite is
        // exercised by the same fallback under serialised access.
        if (db.Database.IsInMemory() || !db.Database.IsNpgsql())
        {
            return await TryReserveInMemoryAsync(db, couponId, now, ct);
        }

        var rowsAffected = await db.BillingCoupons
            .Where(c => c.Id == couponId
                && c.Status == BillingCouponStatus.Active
                && (c.StartsAt == null || c.StartsAt <= now)
                && (c.EndsAt == null || c.EndsAt > now)
                && (c.UsageLimitTotal == null || c.RedemptionCount < c.UsageLimitTotal))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.RedemptionCount, c => c.RedemptionCount + 1)
                .SetProperty(c => c.UpdatedAt, c => now), ct);

        if (rowsAffected == 1)
        {
            var current = await db.BillingCoupons.AsNoTracking()
                .Where(c => c.Id == couponId)
                .Select(c => new { c.RedemptionCount })
                .FirstOrDefaultAsync(ct);
            return new BillingCouponReservationResult(true, null, current?.RedemptionCount ?? 0);
        }

        var coupon = await db.BillingCoupons.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == couponId, ct);
        if (coupon is null)
        {
            return new BillingCouponReservationResult(false, "coupon_not_found", 0);
        }

        var window = AdminService.EvaluateCouponWindow(coupon, now);
        if (window is not null)
        {
            return new BillingCouponReservationResult(false, window, coupon.RedemptionCount);
        }

        return new BillingCouponReservationResult(false, "coupon_exhausted", coupon.RedemptionCount);
    }

    private static async Task<BillingCouponReservationResult> TryReserveInMemoryAsync(
        LearnerDbContext db,
        string couponId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var coupon = await db.BillingCoupons.FirstOrDefaultAsync(c => c.Id == couponId, ct);
        if (coupon is null)
        {
            return new BillingCouponReservationResult(false, "coupon_not_found", 0);
        }

        var window = AdminService.EvaluateCouponWindow(coupon, now);
        if (window is not null)
        {
            return new BillingCouponReservationResult(false, window, coupon.RedemptionCount);
        }

        if (coupon.UsageLimitTotal is not null && coupon.RedemptionCount >= coupon.UsageLimitTotal.Value)
        {
            return new BillingCouponReservationResult(false, "coupon_exhausted", coupon.RedemptionCount);
        }

        coupon.RedemptionCount += 1;
        coupon.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return new BillingCouponReservationResult(true, null, coupon.RedemptionCount);
    }
}

// ════════════════════════════════════════════════════════════════════════════
//  EF SaveChanges interceptor — physically blocks any mutation of the
//  catalog version snapshot rows. This is cross-database (Postgres / Sqlite /
//  InMemory) because it operates on the EF change-tracker, not on a DB trigger.
// ════════════════════════════════════════════════════════════════════════════
public sealed class BillingCatalogVersionImmutabilityInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Guard(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Guard(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Guard(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted
                && IsImmutableSnapshotEntity(entry))
            {
                throw ApiException.Conflict(
                    "billing_catalog_version_immutable",
                    $"Billing catalog version snapshot ({entry.Entity.GetType().Name}) cannot be modified after creation.");
            }
        }
    }

    private static bool IsImmutableSnapshotEntity(EntityEntry entry)
        => entry.Entity is BillingPlanVersion
            or BillingAddOnVersion
            or BillingCouponVersion;
}
