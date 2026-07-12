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

    [Fact]
    public async Task ResolveAsync_PermanentSubWithoutTutorBookUnlocked_GetsNoTutorBookGrant()
    {
        // Negative guard: the permanent-Tutor-Book grant hinges on the
        // `&& s.TutorBookUnlocked` conjunct. A permanent (ExpiresAt==null) sub
        // that has NOT unlocked the book must not receive the TutorBook modules
        // or the "tutorbook.permanent" trace.
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
            Id = "sub-permanent-no-tb",
            UserId = "learner-no-tb",
            PlanId = "plan-course",
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddMonths(-2),
            ChangedAt = now.AddMonths(-2),
            ExpiresAt = null,            // permanent
            TutorBookUnlocked = false,   // never unlocked the book
        });
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-no-tb", default);

        Assert.False(snapshot.TutorBookUnlocked);
        Assert.DoesNotContain("tutorbook.permanent", snapshot.Trace);
        Assert.DoesNotContain("TutorBook", snapshot.EnabledModules);
        Assert.DoesNotContain("AudioScripts", snapshot.EnabledModules);
    }

    // ── Multi-package aggregation (admin allocates ≥1 packages) ──────────────

    [Fact]
    public async Task ResolveAsync_MultipleActiveCoursePackages_UnionsModulesSumsCountersMaxExpiry()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var expNear = now.AddDays(30);
        var expFar = now.AddDays(60);
        db.BillingPlans.AddRange(
            new BillingPlan { Id = "plan-med", Code = "med", Name = "Medicine", DashboardModulesJson = "[\"Recalls\",\"MaterialsLibrary\"]" },
            new BillingPlan { Id = "plan-physio", Code = "physio", Name = "Physio", DashboardModulesJson = "[\"VideoLibrary\",\"Mocks\"]" });
        db.Subscriptions.AddRange(
            new Subscription
            {
                Id = "sub-med",
                UserId = "learner-multi",
                PlanId = "plan-med",
                Status = SubscriptionStatus.Active,
                StartedAt = now.AddDays(-2),
                ChangedAt = now.AddDays(-1),
                ExpiresAt = expNear,
                WritingAssessmentsRemaining = 2,
                AiCreditsRemaining = 10,
            },
            new Subscription
            {
                Id = "sub-physio",
                UserId = "learner-multi",
                PlanId = "plan-physio",
                Status = SubscriptionStatus.Active,
                StartedAt = now.AddDays(-1),
                ChangedAt = now, // latest
                ExpiresAt = expFar,
                WritingAssessmentsRemaining = 3,
                AiCreditsRemaining = 5,
            });
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-multi", default);

        Assert.True(snapshot.HasEligibleSubscription);
        Assert.Contains("Recalls", snapshot.EnabledModules);
        Assert.Contains("MaterialsLibrary", snapshot.EnabledModules);
        Assert.Contains("VideoLibrary", snapshot.EnabledModules);
        Assert.Contains("Mocks", snapshot.EnabledModules);
        Assert.Equal(5, snapshot.WritingAssessmentsRemaining); // 2 + 3
        Assert.Equal(15, snapshot.AiCreditsRemaining);         // 10 + 5
        Assert.Equal(expFar, snapshot.ExpiresAt);              // max of the two
        Assert.Contains("packages.2", snapshot.Trace);
    }

    [Fact]
    public async Task ResolveAsync_ExpiredLatestCourse_ButOlderCourseStillActive_RescuesEligibility()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.AddRange(
            new BillingPlan { Id = "plan-med", Code = "med", Name = "Medicine", DashboardModulesJson = "[\"Recalls\"]" },
            new BillingPlan { Id = "plan-physio", Code = "physio", Name = "Physio", DashboardModulesJson = "[\"Mocks\"]" });
        db.Subscriptions.AddRange(
            new Subscription
            {
                Id = "sub-med-active",
                UserId = "learner-stagger",
                PlanId = "plan-med",
                Status = SubscriptionStatus.Active,
                StartedAt = now.AddDays(-5),
                ChangedAt = now.AddDays(-5),
                ExpiresAt = now.AddDays(30), // still active
            },
            new Subscription
            {
                Id = "sub-physio-expired",
                UserId = "learner-stagger",
                PlanId = "plan-physio",
                Status = SubscriptionStatus.Active,
                StartedAt = now.AddDays(-1),
                ChangedAt = now, // latest by ChangedAt, but expired
                ExpiresAt = now.AddDays(-1),
            });
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-stagger", default);

        // The latest sub is expired, but the older active course rescues access.
        Assert.True(snapshot.HasEligibleSubscription);
        Assert.Contains("Recalls", snapshot.EnabledModules);
        Assert.DoesNotContain("Mocks", snapshot.EnabledModules); // expired package excluded
    }

    // ── Per-user module overrides ────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_PerUserDisableOverride_BlocksModuleEvenThoughPlanEnablesIt()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "plan-course",
            Code = "full",
            Name = "Full",
            DashboardModulesJson = "[\"Recalls\",\"MaterialsLibrary\",\"VideoLibrary\",\"Mocks\"]",
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-active",
            UserId = "learner-ovr",
            PlanId = "plan-course",
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-5),
            ChangedAt = now.AddDays(-5),
            ExpiresAt = now.AddDays(30),
        });
        db.UserModuleOverrides.Add(new UserModuleOverride
        {
            Id = "umo-1",
            UserId = "learner-ovr",
            ModuleKey = "Mocks",
            Enabled = false,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-ovr", default);

        Assert.False(snapshot.IsModuleEnabled("Mocks"));
        Assert.True(snapshot.IsModuleEnabled("Recalls"));
        Assert.Contains("Mocks", snapshot.DisabledModules);
        Assert.DoesNotContain("Mocks", snapshot.EnabledModules);
    }

    [Fact]
    public async Task ResolveAsync_PerUserDisable_SurvivesFailOpenWhenEnabledListEmpties()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "plan-single",
            Code = "single",
            Name = "Single",
            DashboardModulesJson = "[\"Mocks\"]", // only one module
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-single",
            UserId = "learner-single",
            PlanId = "plan-single",
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-5),
            ChangedAt = now.AddDays(-5),
            ExpiresAt = now.AddDays(30),
        });
        db.UserModuleOverrides.Add(new UserModuleOverride
        {
            Id = "umo-2",
            UserId = "learner-single",
            ModuleKey = "Mocks",
            Enabled = false,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-single", default);

        // Disabling the only module must NOT re-open everything via fail-open.
        Assert.False(snapshot.IsModuleEnabled("Mocks"));
        // An unrelated module the plan never listed stays fail-open (unchanged behaviour).
        Assert.True(snapshot.IsModuleEnabled("Recalls"));
    }

    [Fact]
    public async Task ResolveAsync_PerUserEnableOverride_AddsModuleNotInPlan()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "plan-course",
            Code = "full",
            Name = "Full",
            DashboardModulesJson = "[\"Recalls\"]",
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-active",
            UserId = "learner-en",
            PlanId = "plan-course",
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-5),
            ChangedAt = now.AddDays(-5),
            ExpiresAt = now.AddDays(30),
        });
        db.UserModuleOverrides.Add(new UserModuleOverride
        {
            Id = "umo-3",
            UserId = "learner-en",
            ModuleKey = "VideoLibrary",
            Enabled = true,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-en", default);

        Assert.Contains("Recalls", snapshot.EnabledModules);
        Assert.Contains("VideoLibrary", snapshot.EnabledModules);
        Assert.True(snapshot.IsModuleEnabled("VideoLibrary"));
    }
}