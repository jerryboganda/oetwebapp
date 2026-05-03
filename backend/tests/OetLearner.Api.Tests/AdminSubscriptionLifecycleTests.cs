using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

/// <summary>
/// Service-level tests for the admin subscription lifecycle methods on
/// <see cref="AdminService"/> (ChangePlan / Extend / Cancel / Reactivate /
/// SetStatus / Create). These exercise the in-process EF Core path and assert
/// that each successful mutation writes exactly one matching <see cref="AuditEvent"/>.
/// </summary>
public class AdminSubscriptionLifecycleTests
{
    private const string AdminId = "adm-001";
    private const string AdminName = "Admin Tester";
    private const string LearnerId = "usr-001";

    // ── ChangeSubscriptionPlanAsync ─────────────────────────────

    [Fact]
    public async Task ChangeSubscriptionPlanAsync_HappyPath_UpdatesSubscriptionAndWritesAudit()
    {
        await using var db = NewDb();
        var (_, plan, _) = await SeedAsync(db);
        var targetPlan = await SeedPlanAsync(db, "premium", "Premium Plan", price: 49.00m, durationMonths: 1, includedCredits: 0);

        var subId = (await db.Subscriptions.SingleAsync()).Id;
        var service = NewAdminService(db);

        var auditCountBefore = await db.AuditEvents.CountAsync();

        var result = await service.ChangeSubscriptionPlanAsync(
            AdminId, AdminName, subId,
            new AdminSubscriptionChangePlanRequest(PlanCode: targetPlan.Code, ResetRenewalDate: true, GrantIncludedCredits: false, Reason: "upgrade"),
            CancellationToken.None);

        Assert.NotNull(result);

        var refreshed = await db.Subscriptions.SingleAsync();
        Assert.Equal(targetPlan.Code, refreshed.PlanId);
        Assert.Equal(targetPlan.Price, refreshed.PriceAmount);
        Assert.Equal(SubscriptionStatus.Active, refreshed.Status);

        await AssertOneNewAuditAsync(db, auditCountBefore, "Subscription Plan Change", subId);
    }

    [Fact]
    public async Task ChangeSubscriptionPlanAsync_GrantIncludedCredits_AddsToWallet()
    {
        await using var db = NewDb();
        await SeedAsync(db);
        var creditPlan = await SeedPlanAsync(db, "credits", "Credits Plan", price: 99.00m, durationMonths: 1, includedCredits: 25);

        var subId = (await db.Subscriptions.SingleAsync()).Id;
        var service = NewAdminService(db);
        var auditCountBefore = await db.AuditEvents.CountAsync();

        await service.ChangeSubscriptionPlanAsync(
            AdminId, AdminName, subId,
            new AdminSubscriptionChangePlanRequest(PlanCode: creditPlan.Code, ResetRenewalDate: false, GrantIncludedCredits: true),
            CancellationToken.None);

        var wallet = await db.Wallets.SingleAsync(w => w.UserId == LearnerId);
        Assert.Equal(25, wallet.CreditBalance);

        await AssertOneNewAuditAsync(db, auditCountBefore, "Subscription Plan Change", subId);
    }

    [Fact]
    public async Task ChangeSubscriptionPlanAsync_PlanNotFound_Throws()
    {
        await using var db = NewDb();
        await SeedAsync(db);
        var subId = (await db.Subscriptions.SingleAsync()).Id;
        var service = NewAdminService(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.ChangeSubscriptionPlanAsync(
            AdminId, AdminName, subId,
            new AdminSubscriptionChangePlanRequest(PlanCode: "no-such-plan"),
            CancellationToken.None));

        Assert.Equal("plan_not_found", ex.ErrorCode);
    }

    // ── ExtendSubscriptionAsync ─────────────────────────────────

    [Fact]
    public async Task ExtendSubscriptionAsync_AddMonths_AdvancesRenewal()
    {
        await using var db = NewDb();
        await SeedAsync(db);
        var sub = await db.Subscriptions.SingleAsync();
        var originalRenewal = sub.NextRenewalAt;
        var service = NewAdminService(db);
        var auditCountBefore = await db.AuditEvents.CountAsync();

        await service.ExtendSubscriptionAsync(
            AdminId, AdminName, sub.Id,
            new AdminSubscriptionExtendRequest(AddMonths: 2, Reason: "complimentary"),
            CancellationToken.None);

        var refreshed = await db.Subscriptions.SingleAsync();
        Assert.Equal(originalRenewal.AddMonths(2), refreshed.NextRenewalAt);

        await AssertOneNewAuditAsync(db, auditCountBefore, "Subscription Extension", sub.Id);
    }

    [Fact]
    public async Task ExtendSubscriptionAsync_AbsoluteDate_SetsRenewal()
    {
        await using var db = NewDb();
        await SeedAsync(db);
        var sub = await db.Subscriptions.SingleAsync();
        var service = NewAdminService(db);

        var target = DateTimeOffset.UtcNow.AddMonths(6);

        await service.ExtendSubscriptionAsync(
            AdminId, AdminName, sub.Id,
            new AdminSubscriptionExtendRequest(NewRenewalAt: target),
            CancellationToken.None);

        var refreshed = await db.Subscriptions.SingleAsync();
        Assert.Equal(target, refreshed.NextRenewalAt);
    }

    [Fact]
    public async Task ExtendSubscriptionAsync_NoAxis_Throws()
    {
        await using var db = NewDb();
        await SeedAsync(db);
        var sub = await db.Subscriptions.SingleAsync();
        var service = NewAdminService(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.ExtendSubscriptionAsync(
            AdminId, AdminName, sub.Id,
            new AdminSubscriptionExtendRequest(),
            CancellationToken.None));

        Assert.Equal("extend_input_invalid", ex.ErrorCode);
    }

    // ── CancelSubscriptionAsync ─────────────────────────────────

    [Fact]
    public async Task CancelSubscriptionAsync_Immediate_SetsRenewalToNow()
    {
        await using var db = NewDb();
        await SeedAsync(db);
        var sub = await db.Subscriptions.SingleAsync();
        var service = NewAdminService(db);
        var auditCountBefore = await db.AuditEvents.CountAsync();

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        await service.CancelSubscriptionAsync(
            AdminId, AdminName, sub.Id,
            new AdminSubscriptionCancelRequest(Immediate: true, Reason: "refund"),
            CancellationToken.None);

        var refreshed = await db.Subscriptions.SingleAsync();
        Assert.Equal(SubscriptionStatus.Cancelled, refreshed.Status);
        Assert.True(refreshed.NextRenewalAt >= before);
        Assert.True(refreshed.NextRenewalAt <= DateTimeOffset.UtcNow.AddSeconds(5));

        await AssertOneNewAuditAsync(db, auditCountBefore, "Subscription Cancellation", sub.Id);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_EndOfPeriod_KeepsRenewal()
    {
        await using var db = NewDb();
        await SeedAsync(db);
        var sub = await db.Subscriptions.SingleAsync();
        var originalRenewal = sub.NextRenewalAt;
        var originalStatus = sub.Status;
        var service = NewAdminService(db);

        await service.CancelSubscriptionAsync(
            AdminId, AdminName, sub.Id,
            new AdminSubscriptionCancelRequest(Immediate: false),
            CancellationToken.None);

        var refreshed = await db.Subscriptions.SingleAsync();
        // End-of-period cancellation: status and renewal stay put. Audit row records intent.
        Assert.Equal(originalStatus, refreshed.Status);
        Assert.Equal(originalRenewal, refreshed.NextRenewalAt);
        var audit = await db.AuditEvents.SingleAsync(a => a.ResourceType == "Subscription");
        Assert.Contains("Scheduled cancellation", audit.Details);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_AlreadyCancelled_Throws()
    {
        await using var db = NewDb();
        await SeedAsync(db);
        var sub = await db.Subscriptions.SingleAsync();
        sub.Status = SubscriptionStatus.Cancelled;
        await db.SaveChangesAsync();

        var service = NewAdminService(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.CancelSubscriptionAsync(
            AdminId, AdminName, sub.Id,
            new AdminSubscriptionCancelRequest(),
            CancellationToken.None));

        Assert.Equal("subscription_already_cancelled", ex.ErrorCode);
    }

    // ── ReactivateSubscriptionAsync ─────────────────────────────

    [Fact]
    public async Task ReactivateSubscriptionAsync_FromCancelled_ResumesActive()
    {
        await using var db = NewDb();
        await SeedAsync(db);
        var sub = await db.Subscriptions.SingleAsync();
        sub.Status = SubscriptionStatus.Cancelled;
        sub.NextRenewalAt = DateTimeOffset.UtcNow.AddDays(-5);
        await db.SaveChangesAsync();

        var service = NewAdminService(db);
        var auditCountBefore = await db.AuditEvents.CountAsync();

        await service.ReactivateSubscriptionAsync(
            AdminId, AdminName, sub.Id,
            new AdminSubscriptionReactivateRequest(ResetRenewalDate: true, Reason: "win-back"),
            CancellationToken.None);

        var refreshed = await db.Subscriptions.SingleAsync();
        Assert.Equal(SubscriptionStatus.Active, refreshed.Status);
        Assert.True(refreshed.NextRenewalAt > DateTimeOffset.UtcNow);

        await AssertOneNewAuditAsync(db, auditCountBefore, "Subscription Reactivation", sub.Id);
    }

    [Fact]
    public async Task ReactivateSubscriptionAsync_AlreadyActive_Throws()
    {
        await using var db = NewDb();
        await SeedAsync(db);
        var sub = await db.Subscriptions.SingleAsync();
        // Seed creates an Active subscription by default.
        var service = NewAdminService(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.ReactivateSubscriptionAsync(
            AdminId, AdminName, sub.Id,
            new AdminSubscriptionReactivateRequest(),
            CancellationToken.None));

        Assert.Equal("subscription_already_active", ex.ErrorCode);
    }

    // ── SetSubscriptionStatusAsync ──────────────────────────────

    [Fact]
    public async Task SetSubscriptionStatusAsync_ValidStatus_Updates()
    {
        await using var db = NewDb();
        await SeedAsync(db);
        var sub = await db.Subscriptions.SingleAsync();
        var service = NewAdminService(db);
        var auditCountBefore = await db.AuditEvents.CountAsync();

        await service.SetSubscriptionStatusAsync(
            AdminId, AdminName, sub.Id,
            new AdminSubscriptionStatusRequest(Status: "Suspended", Reason: "fraud review"),
            CancellationToken.None);

        var refreshed = await db.Subscriptions.SingleAsync();
        Assert.Equal(SubscriptionStatus.Suspended, refreshed.Status);

        await AssertOneNewAuditAsync(db, auditCountBefore, "Subscription Status Change", sub.Id);
    }

    [Fact]
    public async Task SetSubscriptionStatusAsync_InvalidStatus_Throws()
    {
        await using var db = NewDb();
        await SeedAsync(db);
        var sub = await db.Subscriptions.SingleAsync();
        var service = NewAdminService(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.SetSubscriptionStatusAsync(
            AdminId, AdminName, sub.Id,
            new AdminSubscriptionStatusRequest(Status: "not-a-real-status"),
            CancellationToken.None));

        Assert.Equal("subscription_status_invalid", ex.ErrorCode);
    }

    // ── CreateSubscriptionAsync ─────────────────────────────────

    [Fact]
    public async Task CreateSubscriptionAsync_HappyPath_InsertsSubscriptionAndAudit()
    {
        await using var db = NewDb();
        // Seed plan + learner WITHOUT an existing subscription.
        var plan = await SeedPlanAsync(db, "starter", "Starter Plan", price: 19.00m, durationMonths: 1, includedCredits: 5);
        await SeedLearnerAsync(db);
        var service = NewAdminService(db);
        var auditCountBefore = await db.AuditEvents.CountAsync();

        await service.CreateSubscriptionAsync(
            AdminId, AdminName,
            new AdminSubscriptionCreateRequest(UserId: LearnerId, PlanCode: plan.Code, GrantIncludedCredits: true, Reason: "comp"),
            CancellationToken.None);

        var sub = await db.Subscriptions.SingleAsync();
        Assert.Equal(LearnerId, sub.UserId);
        Assert.Equal(plan.Code, sub.PlanId);
        Assert.Equal(SubscriptionStatus.Active, sub.Status);

        var wallet = await db.Wallets.SingleAsync(w => w.UserId == LearnerId);
        Assert.Equal(5, wallet.CreditBalance);

        await AssertOneNewAuditAsync(db, auditCountBefore, "Subscription Created", sub.Id);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_ExistingSubscription_Throws()
    {
        await using var db = NewDb();
        await SeedAsync(db);
        var existingPlanCode = (await db.BillingPlans.FirstAsync()).Code;
        var service = NewAdminService(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.CreateSubscriptionAsync(
            AdminId, AdminName,
            new AdminSubscriptionCreateRequest(UserId: LearnerId, PlanCode: existingPlanCode),
            CancellationToken.None));

        Assert.Equal("subscription_exists", ex.ErrorCode);
    }

    // ── Helpers ─────────────────────────────────────────────────

    private static LearnerDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LearnerDbContext(options);
    }

    private static AdminService NewAdminService(LearnerDbContext db)
    {
        // The subscription-lifecycle methods only touch `db` and `DateTimeOffset.UtcNow`;
        // the other primary-constructor parameters are not invoked by these code paths,
        // so passing null! is safe and keeps these tests narrowly scoped.
        return new AdminService(
            db,
            emailOtpService: null!,
            passwordHasher: null!,
            timeProvider: TimeProvider.System,
            notifications: null!,
            learnerService: null!);
    }

    private static async Task<(LearnerUser learner, BillingPlan plan, Subscription sub)> SeedAsync(LearnerDbContext db)
    {
        var learner = await SeedLearnerAsync(db);
        var plan = await SeedPlanAsync(db, "basic", "Basic Plan", price: 29.00m, durationMonths: 1, includedCredits: 0);

        var now = DateTimeOffset.UtcNow;
        var sub = new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = learner.Id,
            PlanId = plan.Code,
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-30),
            ChangedAt = now.AddDays(-30),
            NextRenewalAt = now.AddDays(15),
            PriceAmount = plan.Price,
            Currency = plan.Currency,
            Interval = plan.Interval,
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();
        return (learner, plan, sub);
    }

    private static async Task<LearnerUser> SeedLearnerAsync(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var learner = new LearnerUser
        {
            Id = LearnerId,
            DisplayName = "Test Learner",
            Email = "learner@example.test",
            CreatedAt = now,
            LastActiveAt = now,
        };
        db.Users.Add(learner);
        await db.SaveChangesAsync();
        return learner;
    }

    private static async Task<BillingPlan> SeedPlanAsync(
        LearnerDbContext db, string code, string name, decimal price, int durationMonths, int includedCredits)
    {
        var now = DateTimeOffset.UtcNow;
        var plan = new BillingPlan
        {
            Id = $"plan-{code}",
            Code = code,
            Name = name,
            Price = price,
            Currency = "USD",
            Interval = "month",
            DurationMonths = durationMonths,
            IncludedCredits = includedCredits,
            Status = BillingPlanStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.BillingPlans.Add(plan);
        await db.SaveChangesAsync();
        return plan;
    }

    private static async Task AssertOneNewAuditAsync(
        LearnerDbContext db, int countBefore, string expectedAction, string expectedResourceId)
    {
        var newAudits = await db.AuditEvents
            .Where(a => a.ResourceType == "Subscription" && a.ResourceId == expectedResourceId && a.Action == expectedAction)
            .ToListAsync();

        Assert.Single(newAudits);
        Assert.Equal(countBefore + 1, await db.AuditEvents.CountAsync());
    }
}
