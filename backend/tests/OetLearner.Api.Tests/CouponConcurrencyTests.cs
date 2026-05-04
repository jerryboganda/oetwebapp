using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

/// <summary>
/// Slice C — May 2026 billing-hardening tests for race-safe coupon redemption.
///
/// The atomic reservation path is exercised against a real SQLite database
/// (in-memory) so that EF's `ExecuteUpdateAsync` is dispatched as a single
/// SQL statement subject to SQLite's locking semantics. The InMemory provider
/// is intentionally avoided here — it cannot model true write concurrency.
///
/// Coverage:
///   • 32 parallel reservation attempts against a coupon with
///     UsageLimitTotal = 5 → exactly 5 succeed, 27 are rejected with
///     `coupon_exhausted`, RedemptionCount lands on 5.
///   • Expired coupon → `coupon_expired`.
///   • Not-yet-active coupon → `coupon_not_started`.
///   • Inactive (paused) coupon → `coupon_inactive`.
///   • Coupon with no usage cap → all reservations succeed.
/// </summary>
public class CouponConcurrencyTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public CouponConcurrencyTests()
    {
        // Shared in-memory database — kept alive by the open connection.
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            // Re-attach the production interceptor so version-snapshot
            // immutability is enforced in this test harness too.
            .AddInterceptors(new BillingCatalogVersionImmutabilityInterceptor())
            .Options;

        using var seedContext = new LearnerDbContext(_options);
        seedContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task ParallelReservations_AreCappedAtUsageLimitTotal()
    {
        const int parallelism = 32;
        const int usageLimitTotal = 5;
        var couponId = await SeedCouponAsync(usageLimitTotal: usageLimitTotal);

        var results = await Task.WhenAll(Enumerable.Range(0, parallelism).Select(async _ =>
        {
            // Each attempt uses its own DbContext to mimic per-request scopes.
            using var db = new LearnerDbContext(_options);
            return await BillingCouponRedemptionAtomic.TryReserveAsync(
                db,
                couponId,
                DateTimeOffset.UtcNow,
                CancellationToken.None);
        }));

        var succeeded = results.Count(r => r.Reserved);
        var rejected = results.Where(r => !r.Reserved).ToList();

        Assert.Equal(usageLimitTotal, succeeded);
        Assert.Equal(parallelism - usageLimitTotal, rejected.Count);
        Assert.All(rejected, r => Assert.Equal("coupon_exhausted", r.RejectionCode));

        using var verifier = new LearnerDbContext(_options);
        var coupon = await verifier.BillingCoupons.AsNoTracking().FirstAsync(c => c.Id == couponId);
        Assert.Equal(usageLimitTotal, coupon.RedemptionCount);
    }

    [Fact]
    public async Task ExpiredCoupon_IsRejectedWithStructuredReason()
    {
        var couponId = await SeedCouponAsync(
            usageLimitTotal: 5,
            startsAt: DateTimeOffset.UtcNow.AddDays(-10),
            endsAt: DateTimeOffset.UtcNow.AddDays(-1));

        using var db = new LearnerDbContext(_options);
        var result = await BillingCouponRedemptionAtomic.TryReserveAsync(db, couponId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(result.Reserved);
        Assert.Equal("coupon_expired", result.RejectionCode);
    }

    [Fact]
    public async Task NotYetActiveCoupon_IsRejectedWithStructuredReason()
    {
        var couponId = await SeedCouponAsync(
            usageLimitTotal: 5,
            startsAt: DateTimeOffset.UtcNow.AddDays(2));

        using var db = new LearnerDbContext(_options);
        var result = await BillingCouponRedemptionAtomic.TryReserveAsync(db, couponId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(result.Reserved);
        Assert.Equal("coupon_not_started", result.RejectionCode);
    }

    [Fact]
    public async Task InactiveCoupon_IsRejectedWithStructuredReason()
    {
        var couponId = await SeedCouponAsync(usageLimitTotal: 5, status: BillingCouponStatus.Inactive);

        using var db = new LearnerDbContext(_options);
        var result = await BillingCouponRedemptionAtomic.TryReserveAsync(db, couponId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(result.Reserved);
        Assert.Equal("coupon_inactive", result.RejectionCode);
    }

    [Fact]
    public async Task UnlimitedCoupon_AllowsAllParallelReservations()
    {
        const int parallelism = 16;
        var couponId = await SeedCouponAsync(usageLimitTotal: null);

        var results = await Task.WhenAll(Enumerable.Range(0, parallelism).Select(async _ =>
        {
            using var db = new LearnerDbContext(_options);
            return await BillingCouponRedemptionAtomic.TryReserveAsync(db, couponId, DateTimeOffset.UtcNow, CancellationToken.None);
        }));

        Assert.All(results, r => Assert.True(r.Reserved, $"Unexpected rejection: {r.RejectionCode}"));

        using var verifier = new LearnerDbContext(_options);
        var coupon = await verifier.BillingCoupons.AsNoTracking().FirstAsync(c => c.Id == couponId);
        Assert.Equal(parallelism, coupon.RedemptionCount);
    }

    [Fact]
    public async Task UnknownCoupon_ReturnsNotFound()
    {
        using var db = new LearnerDbContext(_options);
        var result = await BillingCouponRedemptionAtomic.TryReserveAsync(db, "coupon-does-not-exist", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(result.Reserved);
        Assert.Equal("coupon_not_found", result.RejectionCode);
    }

    private async Task<string> SeedCouponAsync(
        int? usageLimitTotal,
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null,
        BillingCouponStatus status = BillingCouponStatus.Active)
    {
        var id = $"coupon-{Guid.NewGuid():N}";
        using var db = new LearnerDbContext(_options);
        db.BillingCoupons.Add(new BillingCoupon
        {
            Id = id,
            Code = id.ToUpperInvariant(),
            Name = "Slice C concurrency coupon",
            Description = "Coupon used by Slice C concurrency tests.",
            DiscountType = BillingDiscountType.Percentage,
            DiscountValue = 10m,
            Currency = "AUD",
            Status = status,
            StartsAt = startsAt,
            EndsAt = endsAt,
            UsageLimitTotal = usageLimitTotal,
            UsageLimitPerUser = null,
            MinimumSubtotal = null,
            ApplicablePlanCodesJson = "[]",
            ApplicableAddOnCodesJson = "[]",
            IsStackable = false,
            Notes = null,
            RedemptionCount = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return id;
    }
}
