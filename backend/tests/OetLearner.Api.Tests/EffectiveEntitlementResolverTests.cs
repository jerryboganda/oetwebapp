using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Tests;

public class EffectiveEntitlementResolverTests
{
    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    [Fact]
    public async Task ResolveAsync_UsesLatestSubscriptionAndDoesNotRescueCancelledWithStaleActive()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan { Id = "plan-pro", Code = "Pro", Name = "Pro" });
        db.Subscriptions.AddRange(
            new Subscription
            {
                Id = "sub-old-active",
                UserId = "learner-1",
                PlanId = "plan-pro",
                Status = SubscriptionStatus.Active,
                StartedAt = now.AddMonths(-2),
                ChangedAt = now.AddMonths(-2),
            },
            new Subscription
            {
                Id = "sub-new-cancelled",
                UserId = "learner-1",
                PlanId = "plan-pro",
                Status = SubscriptionStatus.Cancelled,
                StartedAt = now.AddMonths(-1),
                ChangedAt = now,
            });
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-1", default);

        Assert.False(snapshot.HasEligibleSubscription);
        Assert.Equal("free", snapshot.Tier);
        Assert.Equal(SubscriptionStatus.Cancelled, snapshot.SubscriptionStatus);
        Assert.Null(snapshot.PlanCode);
    }

    [Fact]
    public async Task ResolveAsync_ResolvesPlanByIdOrCodeAndCapturesActiveAddOns()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan { Id = "plan-pro", Code = "Premium-Monthly", Name = "Pro" });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-active",
            UserId = "learner-2",
            PlanId = "PREMIUM-MONTHLY",
            Status = SubscriptionStatus.Trial,
            StartedAt = now.AddDays(-2),
            ChangedAt = now.AddDays(-2),
        });
        db.SubscriptionItems.AddRange(
            new SubscriptionItem
            {
                Id = "addon-active",
                SubscriptionId = "sub-active",
                ItemCode = "review_booster",
                Status = SubscriptionItemStatus.Active,
                StartsAt = now.AddDays(-1),
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now.AddDays(-1),
            },
            new SubscriptionItem
            {
                Id = "addon-expired",
                SubscriptionId = "sub-active",
                ItemCode = "expired_booster",
                Status = SubscriptionItemStatus.Active,
                StartsAt = now.AddDays(-10),
                EndsAt = now.AddDays(-1),
                CreatedAt = now.AddDays(-10),
                UpdatedAt = now.AddDays(-1),
            });
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-2", default);

        Assert.True(snapshot.HasEligibleSubscription);
        Assert.True(snapshot.IsTrial);
        Assert.Equal("trial", snapshot.Tier);
        Assert.Equal("premium-monthly", snapshot.PlanCode);
        Assert.Equal("pro", snapshot.AiQuotaPlanCode);
        Assert.Equal("fallback", snapshot.AiQuotaPlanCodeSource);
        Assert.Equal(new[] { "review_booster" }, snapshot.ActiveAddOnCodes);
    }

    [Fact]
    public async Task ResolveAsync_SurfacesActiveFreezeOverlayWithoutRemovingPlan()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan { Id = "plan-pro", Code = "pro", Name = "Pro" });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-active",
            UserId = "learner-3",
            PlanId = "plan-pro",
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-10),
            ChangedAt = now.AddDays(-10),
        });
        db.AccountFreezeRecords.Add(new AccountFreezeRecord
        {
            Id = "freeze-active",
            UserId = "learner-3",
            Status = FreezeStatus.Active,
            IsCurrent = true,
            RequestedAt = now,
            StartedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-3", default);

        Assert.True(snapshot.HasEligibleSubscription);
        Assert.True(snapshot.IsFrozen);
        Assert.Equal("pro", snapshot.PlanCode);
        Assert.Contains("freeze.active", snapshot.Trace);
    }

    // ── Phase 2: expiry locks course content ─────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ExpiredCourseSub_LocksModulesAndEligibility()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "plan-course",
            Code = "full-medicine",
            Name = "Full Medicine",
            DashboardModulesJson = "[\"Reading\",\"Listening\",\"Writing\"]",
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-expired",
            UserId = "learner-exp",
            PlanId = "plan-course",
            Status = SubscriptionStatus.Active, // status untouched; expiry is by ExpiresAt
            StartedAt = now.AddMonths(-4),
            ChangedAt = now.AddMonths(-4),
            ExpiresAt = now.AddDays(-1), // elapsed => course locks
        });
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-exp", default);

        Assert.False(snapshot.HasEligibleSubscription);
        Assert.Empty(snapshot.EnabledModules);
        Assert.Contains("subscription.expired", snapshot.Trace);
        // Display fields remain populated for the renewal CTA.
        Assert.Equal(now.AddDays(-1), snapshot.ExpiresAt);
    }

    [Fact]
    public async Task ResolveAsync_ExpiredCourse_WithSeparatePermanentTutorBook_KeepsTutorBookModules()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.AddRange(
            new BillingPlan
            {
                Id = "plan-course",
                Code = "full-medicine",
                Name = "Full Medicine",
                DashboardModulesJson = "[\"Reading\",\"Listening\",\"Writing\"]",
            },
            new BillingPlan
            {
                Id = "plan-tutor-book",
                Code = "tutor-book",
                Name = "The Tutor Book",
                DashboardModulesJson = "[\"TutorBook\",\"AudioScripts\",\"Updates\"]",
            });
        db.Subscriptions.AddRange(
            // Latest sub = expired course (more recent ChangedAt).
            new Subscription
            {
                Id = "sub-course-expired",
                UserId = "learner-tb",
                PlanId = "plan-course",
                Status = SubscriptionStatus.Active,
                StartedAt = now.AddMonths(-4),
                ChangedAt = now.AddDays(-2),
                ExpiresAt = now.AddDays(-1),
            },
            // Separate permanent Tutor Book (accessDays 9999 => ExpiresAt null).
            new Subscription
            {
                Id = "sub-tutor-book",
                UserId = "learner-tb",
                PlanId = "plan-tutor-book",
                Status = SubscriptionStatus.Active,
                StartedAt = now.AddMonths(-6),
                ChangedAt = now.AddMonths(-6),
                ExpiresAt = null,
                TutorBookUnlocked = true,
            });
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-tb", default);

        // Course content is locked (expired latest sub) ...
        Assert.False(snapshot.HasEligibleSubscription);
        Assert.DoesNotContain("Reading", snapshot.EnabledModules);
        Assert.Contains("subscription.expired", snapshot.Trace);
        // ... but the permanent Tutor Book survives.
        Assert.True(snapshot.TutorBookUnlocked);
        Assert.Contains("tutorbook.permanent", snapshot.Trace);
        Assert.Contains("TutorBook", snapshot.EnabledModules);
        Assert.Contains("AudioScripts", snapshot.EnabledModules);
        Assert.Contains("Updates", snapshot.EnabledModules);
    }

    [Fact]
    public async Task ResolveAsync_AddOnTutorBookOnExpiredCourse_TutorBookSurvives()
    {
        // Spec rule #8: the Tutor Book is a Permanent entitlement however it was
        // acquired — the £32 `tutor-book-addon` (a "Permanent entitlement") flips
        // TutorBookUnlocked on the parent COURSE sub, which carries the course's
        // real ExpiresAt. When the course expires the course content locks, but
        // the Tutor Book must remain (matching the TutorBookEndpoints gate that
        // serves the PDF on TutorBookUnlocked && Active, with no expiry check).
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "plan-course",
            Code = "full-medicine-tbook",
            Name = "Full Medicine + Tutor Book",
            DashboardModulesJson = "[\"Reading\",\"TutorBook\"]",
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-course-expired",
            UserId = "learner-addon",
            PlanId = "plan-course",
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddMonths(-4),
            ChangedAt = now.AddDays(-2),
            ExpiresAt = now.AddDays(-1), // course expired
            TutorBookUnlocked = true,    // £32 add-on grant on the course sub
        });
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-addon", default);

        // Course content is locked (expired) ...
        Assert.False(snapshot.HasEligibleSubscription);
        Assert.Contains("subscription.expired", snapshot.Trace);
        Assert.DoesNotContain("Reading", snapshot.EnabledModules);
        // ... but the permanent Tutor Book survives.
        Assert.True(snapshot.TutorBookUnlocked);
        Assert.Contains("tutorbook.permanent", snapshot.Trace);
        Assert.Contains("TutorBook", snapshot.EnabledModules);
        Assert.Contains("AudioScripts", snapshot.EnabledModules);
        Assert.Contains("Updates", snapshot.EnabledModules);
    }

    [Fact]
    public async Task ResolveAsync_ActiveNonExpiredCourse_RemainsUnlocked()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "plan-course",
            Code = "full-medicine",
            Name = "Full Medicine",
            DashboardModulesJson = "[\"Reading\",\"Listening\"]",
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-active",
            UserId = "learner-active",
            PlanId = "plan-course",
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-10),
            ChangedAt = now.AddDays(-10),
            ExpiresAt = now.AddDays(30), // future => not expired
        });
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-active", default);

        Assert.True(snapshot.HasEligibleSubscription);
        Assert.Equal(new[] { "Reading", "Listening" }, snapshot.EnabledModules);
        Assert.DoesNotContain("subscription.expired", snapshot.Trace);
    }

    [Fact]
    public async Task ResolveAsync_PermanentCourse_NullExpiresAt_NotTreatedAsExpired()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "plan-course",
            Code = "full-medicine",
            Name = "Full Medicine",
            DashboardModulesJson = "[\"Reading\",\"Listening\"]",
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-permanent",
            UserId = "learner-perm",
            PlanId = "plan-course",
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddMonths(-2),
            ChangedAt = now.AddMonths(-2),
            ExpiresAt = null, // permanent => never expires
        });
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-perm", default);

        Assert.True(snapshot.HasEligibleSubscription);
        Assert.Equal(new[] { "Reading", "Listening" }, snapshot.EnabledModules);
        Assert.DoesNotContain("subscription.expired", snapshot.Trace);
    }

    [Fact]
    public async Task ResolveAsync_SpeakingSessionSub_FutureExpiry_Unaffected()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "plan-speaking",
            Code = "speaking-sessions",
            Name = "Speaking Sessions",
            DashboardModulesJson = "[\"Speaking\"]",
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-speaking",
            UserId = "learner-speak",
            PlanId = "plan-speaking",
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-5),
            ChangedAt = now.AddDays(-5),
            ExpiresAt = now.AddDays(60), // ~60 days out => not expired
            SpeakingSessionsRemaining = 4,
        });
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-speak", default);

        Assert.True(snapshot.HasEligibleSubscription);
        Assert.Equal(new[] { "Speaking" }, snapshot.EnabledModules);
        Assert.Equal(4, snapshot.SpeakingSessionsRemaining);
        Assert.DoesNotContain("subscription.expired", snapshot.Trace);
    }
}