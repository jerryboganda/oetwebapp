using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

// ═════════════════════════════════════════════════════════════════════════════
// SLICE E — AI quota / credit hardening tests.
//
// 1. AiQuotaService must NEVER elevate a user above the FREE plan when the
//    upstream entitlement is suspended, the explicit quotaPlanCode is unknown,
//    or the plan EntitlementsJson is malformed.
// 2. AiCreditRenewalWorker must be exactly-once under parallel execution
//    (10× concurrent RunOnceAsync = exactly 1 ledger grant per user/period).
//    This is enforced by the unique index UX_AiCreditLedger_PlanRenewal_ReferenceId.
// ═════════════════════════════════════════════════════════════════════════════
public class AiQuotaMappingTests
{
    private static (LearnerDbContext db, AiQuotaService quota) BuildInMemory(
        SubscriptionStatus subscriptionStatus,
        string? planEntitlementsJson)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new LearnerDbContext(options);

        db.AiGlobalPolicies.Add(new AiGlobalPolicy
        {
            Id = "global",
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.AiQuotaPlans.Add(new AiQuotaPlan
        {
            Id = "free-plan",
            Code = "free",
            Name = "Free",
            MonthlyTokenCap = 1_000,
            DailyTokenCap = 100,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.AiQuotaPlans.Add(new AiQuotaPlan
        {
            Id = "pro-plan",
            Code = "pro",
            Name = "Pro",
            MonthlyTokenCap = 100_000,
            DailyTokenCap = 10_000,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "plan-pro",
            Code = "premium-monthly",
            Name = "Premium",
            EntitlementsJson = planEntitlementsJson ?? "{}",
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-1",
            UserId = "u1",
            PlanId = "plan-pro",
            Status = subscriptionStatus,
            StartedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ChangedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        db.SaveChanges();

        var quota = new AiQuotaService(
            db,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AiQuotaService>.Instance,
            new EffectiveEntitlementResolver(db));
        return (db, quota);
    }

    [Fact]
    public async Task UnknownExplicitQuotaPlanCode_FallsBackToFree_DoesNotElevate()
    {
        // Plan declares an explicit quotaPlanCode that does not exist in the
        // AiQuotaPlans table. AiQuotaService must fall back to the FREE plan
        // (not to the Pro fallback that would otherwise be triggered by the
        // BillingPlan code mapping).
        var (db, quota) = BuildInMemory(
            SubscriptionStatus.Active,
            planEntitlementsJson: "{\"ai\":{\"quotaPlanCode\":\"unicorn-tier\"}}");

        var snapshot = await quota.GetUserPolicyAsync("u1", default);

        Assert.Equal("free", snapshot.PlanCode);
        Assert.Equal(1_000, snapshot.MonthlyTokenCap);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task MalformedEntitlementsJson_OnActivePlan_ResolvesToFreePolicy()
    {
        var (db, quota) = BuildInMemory(
            SubscriptionStatus.Active,
            planEntitlementsJson: "{not-json:::");

        var snapshot = await quota.GetUserPolicyAsync("u1", default);

        // Resolver fails low → entitlement.HasEligibleSubscription=false →
        // ResolvePlanAsync returns default ("free") plan.
        Assert.Equal("free", snapshot.PlanCode);
        Assert.Equal(1_000, snapshot.MonthlyTokenCap);
        await db.DisposeAsync();
    }

    [Theory]
    [InlineData(SubscriptionStatus.Suspended)]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Cancelled)]
    public async Task NonEligibleSubscription_ResolvesToFreePolicy(SubscriptionStatus status)
    {
        var (db, quota) = BuildInMemory(
            status,
            planEntitlementsJson: "{\"ai\":{\"quotaPlanCode\":\"pro\"}}");

        var snapshot = await quota.GetUserPolicyAsync("u1", default);

        Assert.Equal("free", snapshot.PlanCode);
        Assert.Equal(1_000, snapshot.MonthlyTokenCap);
        await db.DisposeAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Renewal-race regression: requires SQLite to honour the unique filtered
    // index UX_AiCreditLedger_PlanRenewal_ReferenceId — EF InMemory does not
    // enforce unique indexes, so we open a shared in-memory SQLite connection.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ParallelRenewal_GrantsExactlyOnce_PerUserPeriod()
    {
        // Shared in-memory SQLite — every DbContext opens its OWN connection
        // but they all see the same database. This avoids the "single shared
        // SqliteConnection disposed twice" NullReferenceException that hits
        // when ServiceProvider tears down scoped DbContexts that share one
        // connection instance, while still honouring the unique filtered
        // index UX_AiCreditLedger_PlanRenewal_ReferenceId.
        var dbName = $"renewal-race-{Guid.NewGuid():N}";
        var connectionString = $"Data Source=file:{dbName}?mode=memory&cache=shared";

        // The shared in-memory database survives only while at least one
        // connection to it is open. Hold a "keep-alive" open for the whole
        // test so concurrent scoped contexts can come and go.
        using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();

        var contextOptions = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connectionString)
            .Options;

        // Seed a user with an Active subscription on the Pro plan.
        await using (var seed = new LearnerDbContext(contextOptions))
        {
            await seed.Database.EnsureCreatedAsync();

            seed.AiGlobalPolicies.Add(new AiGlobalPolicy
            {
                Id = "global",
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            seed.AiQuotaPlans.Add(new AiQuotaPlan
            {
                Id = "ai-pro",
                Code = "pro",
                Name = "Pro",
                MonthlyTokenCap = 100_000,
                DailyTokenCap = 10_000,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            seed.AiQuotaPlans.Add(new AiQuotaPlan
            {
                Id = "ai-free",
                Code = "free",
                Name = "Free",
                MonthlyTokenCap = 1_000,
                DailyTokenCap = 100,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            seed.BillingPlans.Add(new BillingPlan
            {
                Id = "plan-pro",
                Code = "premium-monthly", // maps via fallback → "pro"
                Name = "Premium",
            });
            seed.Subscriptions.Add(new Subscription
            {
                Id = "sub-1",
                UserId = "u1",
                PlanId = "plan-pro",
                Status = SubscriptionStatus.Active,
                StartedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ChangedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
            await seed.SaveChangesAsync();
        }

        // Build a ServiceProvider so AiCreditRenewalWorker can resolve a fresh
        // scope per call — exactly mirrors production wiring.
        // Note: we substitute a STUB IEffectiveEntitlementResolver to avoid
        // an unrelated SQLite-translation limitation in
        // EffectiveEntitlementResolver.ResolveActiveAddOnCodesAsync (its
        // DateTimeOffset disjunction does not translate on the SQLite
        // provider). The renewal-race contract under test depends on the
        // unique filtered index, not on resolver internals.
        var services = new ServiceCollection();
        services.AddDbContext<LearnerDbContext>(o => o.UseSqlite(connectionString),
            contextLifetime: ServiceLifetime.Scoped);
        services.AddScoped<IEffectiveEntitlementResolver, StubEligiblePremiumResolver>();
        services.AddScoped<IAiCreditService, AiCreditService>();
        services.AddLogging();
        await using var sp = services.BuildServiceProvider();

        var worker = new AiCreditRenewalWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AiCreditRenewalWorker>.Instance);

        // Fan out 10 parallel renewals. The unique filtered index on
        // (ReferenceId WHERE Source=PlanRenewal) MUST collapse them to one.
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => worker.RunOnceAsync(default)))
            .ToArray();
        await Task.WhenAll(tasks);

        await using var verify = new LearnerDbContext(contextOptions);
        var grants = await verify.AiCreditLedger
            .Where(x => x.Source == AiCreditSource.PlanRenewal && x.UserId == "u1")
            .ToListAsync();

        Assert.Single(grants);
        Assert.StartsWith("renewal:u1:month:", grants[0].ReferenceId);
        Assert.Equal(100_000, grants[0].TokensDelta);

        // Sum of `renewed` reported across workers should also be exactly 1
        // (the other nine see the row already exists or catch the unique
        //  violation and treat it as a no-op).
        var totalReported = tasks.Sum(t => t.Result.renewed);
        Assert.Equal(1, totalReported);
    }
}

/// <summary>
/// Test-only resolver that always returns an eligible Pro entitlement for
/// "u1". Used by <see cref="AiQuotaMappingTests.ParallelRenewal_GrantsExactlyOnce_PerUserPeriod"/>
/// to bypass an unrelated SQLite query-translation limitation in the
/// production resolver.
/// </summary>
internal sealed class StubEligiblePremiumResolver : IEffectiveEntitlementResolver
{
    public Task<EffectiveEntitlementSnapshot> ResolveAsync(string? userId, CancellationToken ct)
        => Task.FromResult(new EffectiveEntitlementSnapshot(
            UserId: userId,
            HasEligibleSubscription: true,
            IsTrial: false,
            Tier: "paid",
            SubscriptionId: "sub-1",
            SubscriptionStatus: SubscriptionStatus.Active,
            PlanId: "plan-pro",
            PlanVersionId: null,
            PlanCode: "premium-monthly",
            AiQuotaPlanCode: "pro",
            AiQuotaPlanCodeSource: "fallback",
            ActiveAddOnCodes: Array.Empty<string>(),
            IsFrozen: false,
            Trace: new[] { "stub.eligible" }));
}
