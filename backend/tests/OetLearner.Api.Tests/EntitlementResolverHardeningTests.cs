using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Tests;

// ═════════════════════════════════════════════════════════════════════════════
// SLICE E — fail-low entitlement hardening.
//
// These tests pin the contract that ANY ambiguity (suspended/disputed status,
// deleted plan, missing version row, malformed entitlements JSON) collapses
// the resolved entitlement to FREE rather than silently elevating the user.
// They are deliberately additive — existing happy-path tests live in
// EffectiveEntitlementResolverTests.cs and continue to pass unmodified.
// ═════════════════════════════════════════════════════════════════════════════
public class EntitlementResolverHardeningTests
{
    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static void SeedPlan(LearnerDbContext db, string id = "plan-pro", string code = "premium-monthly", string entitlementsJson = "{}")
    {
        db.BillingPlans.Add(new BillingPlan
        {
            Id = id,
            Code = code,
            Name = code,
            EntitlementsJson = entitlementsJson,
        });
    }

    private static void SeedSubscription(
        LearnerDbContext db,
        string userId,
        SubscriptionStatus status,
        string planId = "plan-pro",
        string? planVersionId = null,
        string id = "sub-1")
    {
        var now = DateTimeOffset.UtcNow;
        db.Subscriptions.Add(new Subscription
        {
            Id = id,
            UserId = userId,
            PlanId = planId,
            PlanVersionId = planVersionId,
            Status = status,
            StartedAt = now.AddDays(-7),
            ChangedAt = now.AddDays(-1),
        });
    }

    [Theory]
    [InlineData(SubscriptionStatus.Suspended)]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Pending)]
    [InlineData(SubscriptionStatus.Cancelled)]
    [InlineData(SubscriptionStatus.Expired)]
    public async Task NonEligibleStatuses_AlwaysResolveToFreeTier(SubscriptionStatus status)
    {
        await using var db = CreateDb();
        SeedPlan(db);
        SeedSubscription(db, userId: "u1", status: status);
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("u1", default);

        Assert.False(snapshot.HasEligibleSubscription);
        Assert.Equal("free", snapshot.Tier);
        Assert.Null(snapshot.PlanId);
        Assert.Null(snapshot.PlanCode);
        Assert.Null(snapshot.PlanVersionId);
        Assert.Null(snapshot.AiQuotaPlanCode);
        Assert.Empty(snapshot.ActiveAddOnCodes);
        // Status is preserved on the snapshot for diagnostics, but never
        // confers entitlements.
        Assert.Equal(status, snapshot.SubscriptionStatus);
    }

    [Fact]
    public async Task DeletedPlan_OnActiveSubscription_FailsLowToFree()
    {
        await using var db = CreateDb();
        // Note: no BillingPlan seeded — simulates a plan deleted out from
        // under an active subscription.
        SeedSubscription(db, userId: "u1", status: SubscriptionStatus.Active, planId: "plan-ghost");
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("u1", default);

        Assert.False(snapshot.HasEligibleSubscription);
        Assert.Equal("free", snapshot.Tier);
        Assert.Null(snapshot.PlanCode);
        Assert.Contains("plan.missing", snapshot.Trace);
        Assert.Contains("fail_low.plan.missing", snapshot.Trace);
    }

    [Fact]
    public async Task MissingPlanVersionRow_FailsLowToFree()
    {
        await using var db = CreateDb();
        SeedPlan(db);
        SeedSubscription(db, userId: "u1", status: SubscriptionStatus.Active, planVersionId: "ver-ghost");
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("u1", default);

        Assert.False(snapshot.HasEligibleSubscription);
        Assert.Equal("free", snapshot.Tier);
        Assert.Contains("plan.version.missing", snapshot.Trace);
        Assert.Contains("fail_low.plan.version.missing", snapshot.Trace);
    }

    [Fact]
    public async Task ValidPlanVersionRow_PreservesEligibility()
    {
        await using var db = CreateDb();
        SeedPlan(db);
        db.BillingPlanVersions.Add(new BillingPlanVersion
        {
            Id = "ver-1",
            PlanId = "plan-pro",
            Code = "premium-monthly",
            Name = "Pro",
            VersionNumber = 1,
        });
        SeedSubscription(db, userId: "u1", status: SubscriptionStatus.Active, planVersionId: "ver-1");
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("u1", default);

        Assert.True(snapshot.HasEligibleSubscription);
        Assert.Equal("ver-1", snapshot.PlanVersionId);
        Assert.DoesNotContain(snapshot.Trace, t => t.StartsWith("fail_low.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MalformedEntitlementsJson_FailsLowToFree()
    {
        await using var db = CreateDb();
        // Truncated/garbage JSON — must not throw, must not elevate.
        SeedPlan(db, entitlementsJson: "{not-json:::");
        SeedSubscription(db, userId: "u1", status: SubscriptionStatus.Active);
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("u1", default);

        Assert.False(snapshot.HasEligibleSubscription);
        Assert.Equal("free", snapshot.Tier);
        Assert.Contains("entitlements.malformed", snapshot.Trace);
        Assert.Contains("fail_low.entitlements.malformed", snapshot.Trace);
        Assert.Null(snapshot.AiQuotaPlanCode);
    }

    [Fact]
    public async Task EntitlementsJsonIsArray_TreatedAsMalformed()
    {
        await using var db = CreateDb();
        // EntitlementsJson must be a JSON object; arrays are a contract
        // violation and must fail-low rather than be silently ignored.
        SeedPlan(db, entitlementsJson: "[\"oops\"]");
        SeedSubscription(db, userId: "u1", status: SubscriptionStatus.Active);
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("u1", default);

        Assert.False(snapshot.HasEligibleSubscription);
        Assert.Contains("entitlements.malformed", snapshot.Trace);
    }

    [Fact]
    public async Task DowngradeMidCycle_NewActionsResolveLowerTier_NoCachingElevation()
    {
        // Models the "downgrade behavior" requirement: in-flight calls that
        // already captured a plan snapshot continue with their snapshot, but
        // any *new* call to ResolveAsync after the state change must return
        // the lower tier immediately. The resolver does NOT cache, so this
        // test pins the behavioral contract.
        await using var db = CreateDb();
        SeedPlan(db, id: "plan-pro", code: "premium-monthly");
        SeedSubscription(db, userId: "u1", status: SubscriptionStatus.Active);
        await db.SaveChangesAsync();

        var resolver = new EffectiveEntitlementResolver(db);

        // First call: still on Pro.
        var before = await resolver.ResolveAsync("u1", default);
        Assert.True(before.HasEligibleSubscription);
        Assert.Equal("premium-monthly", before.PlanCode);

        // Subscription is downgraded mid-cycle (e.g. webhook flips status).
        var sub = await db.Subscriptions.FirstAsync(s => s.UserId == "u1");
        sub.Status = SubscriptionStatus.Cancelled;
        sub.ChangedAt = DateTimeOffset.UtcNow.AddSeconds(1);
        await db.SaveChangesAsync();

        // Second call (representing a *new* action) must reflect downgrade.
        var after = await resolver.ResolveAsync("u1", default);
        Assert.False(after.HasEligibleSubscription);
        Assert.Equal("free", after.Tier);
        Assert.Equal(SubscriptionStatus.Cancelled, after.SubscriptionStatus);
        // The earlier snapshot is unchanged (idempotent for in-flight ops).
        Assert.Equal("premium-monthly", before.PlanCode);
    }

    [Fact]
    public async Task NoUserSubscription_ReturnsAnonymousFree()
    {
        await using var db = CreateDb();
        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("u-none", default);
        Assert.False(snapshot.HasEligibleSubscription);
        Assert.Equal("free", snapshot.Tier);
        Assert.Contains("subscription.none", snapshot.Trace);
    }
}
