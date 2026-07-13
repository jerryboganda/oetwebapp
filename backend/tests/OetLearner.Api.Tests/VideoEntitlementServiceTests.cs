using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.VideoLibrary;

namespace OetLearner.Api.Tests;

public class VideoEntitlementServiceTests
{
    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static VideoEntitlementService CreateService(LearnerDbContext db)
        => new(db, new EffectiveEntitlementResolver(db));

    private static LibraryVideo Video(string accessTier = "premium", string? subtestCode = null) => new()
    {
        Id = $"vid-{Guid.NewGuid():N}",
        Title = "Test video",
        AccessTier = accessTier,
        SubtestCode = subtestCode,
        Status = ContentStatus.Published,
        DurationSeconds = 600,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static void SeedSubscription(
        LearnerDbContext db,
        string userId,
        string entitlementsJson,
        SubscriptionStatus status = SubscriptionStatus.Active,
        DateTimeOffset? expiresAt = null,
        string? dashboardModulesJson = null)
    {
        var now = DateTimeOffset.UtcNow;
        var planCode = $"plan-{Guid.NewGuid():N}"[..24];
        db.BillingPlans.Add(new BillingPlan
        {
            Id = planCode,
            Code = planCode,
            Name = "Test plan",
            EntitlementsJson = entitlementsJson,
            // Default "[]" (no modules) keeps the legacy tests exercising the entitlement-node path;
            // callers that pass a list exercise the admin "Videos" module-toggle grant.
            DashboardModulesJson = dashboardModulesJson ?? "[]",
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = userId,
            PlanId = planCode,
            Status = status,
            StartedAt = now.AddDays(-1),
            ChangedAt = now,
            ExpiresAt = expiresAt,
        });
    }

    [Fact]
    public async Task FreeVideo_SignedInLearner_IsAllowed()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var result = await service.AllowAccessAsync("learner-1", Video("free"), default);

        Assert.True(result.Allowed);
        Assert.Equal("free_tier", result.Reason);
    }

    [Fact]
    public async Task PremiumVideo_NoSubscription_IsDenied()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var result = await service.AllowAccessAsync("learner-1", Video(), default);

        Assert.False(result.Allowed);
        Assert.Equal("no_active_subscription", result.Reason);
    }

    [Fact]
    public async Task PremiumVideo_NoSubscription_RequireThrows402ContentLocked()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var ex = await Assert.ThrowsAsync<OetLearner.Api.Services.ApiException>(
            () => service.RequireAccessAsync("learner-1", Video(), default));

        Assert.Equal(402, ex.StatusCode);
        Assert.Equal("content_locked", ex.ErrorCode);
    }

    [Fact]
    public async Task PremiumVideo_PlanGrantsVideoLibraryPremium_IsAllowed()
    {
        await using var db = CreateDb();
        SeedSubscription(db, "learner-1", """{"video_library":{"tier":"premium"}}""");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.AllowAccessAsync("learner-1", Video(), default);

        Assert.True(result.Allowed);
        Assert.Equal("plan_grants_video_library", result.Reason);
    }

    [Fact]
    public async Task PremiumVideo_PlanEnablesVideoLibraryModule_IsAllowed()
    {
        // The admin "Videos" toggle (DashboardModulesJson contains "VideoLibrary") is now a
        // first-class grant — no separate video_library entitlement node required. This is the
        // path every real production plan takes (migration 20260725 back-filled the key).
        await using var db = CreateDb();
        SeedSubscription(db, "learner-1", "{}", dashboardModulesJson: """["Recalls","VideoLibrary","Mocks"]""");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.AllowAccessAsync("learner-1", Video(), default);

        Assert.True(result.Allowed);
        Assert.Equal("plan_grants_video_library", result.Reason);
    }

    [Fact]
    public async Task PremiumVideo_PlanEnablesVideoLibraryModule_UnlocksEverySubtest()
    {
        // Module-toggle grant is unrestricted (no subtest node) → all OET modules unlock.
        await using var db = CreateDb();
        SeedSubscription(db, "learner-1", "{}", dashboardModulesJson: """["VideoLibrary"]""");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        foreach (var subtest in new[] { "listening", "reading", "writing", "speaking" })
        {
            var result = await service.AllowAccessAsync("learner-1", Video(subtestCode: subtest), default);
            Assert.True(result.Allowed, $"expected {subtest} to unlock");
        }
    }

    [Fact]
    public async Task PremiumVideo_PlanModuleListExcludesVideoLibrary_IsDenied()
    {
        // A non-empty module list that omits VideoLibrary = the admin DISABLED the Videos toggle
        // (the FULL gate). No entitlement node, no add-on → locked.
        await using var db = CreateDb();
        SeedSubscription(db, "learner-1", "{}", dashboardModulesJson: """["Recalls","MaterialsLibrary","Mocks"]""");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.AllowAccessAsync("learner-1", Video(), default);

        Assert.False(result.Allowed);
        Assert.Equal("plan_does_not_grant", result.Reason);
    }

    [Fact]
    public async Task PremiumVideo_ModuleEnabledButExpiredSubscription_IsDeniedAsExpired()
    {
        // The module toggle never overrides the eligibility/expiry gate.
        await using var db = CreateDb();
        SeedSubscription(db, "learner-1", "{}",
            expiresAt: DateTimeOffset.UtcNow.AddDays(-1),
            dashboardModulesJson: """["VideoLibrary"]""");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.AllowAccessAsync("learner-1", Video(), default);

        Assert.False(result.Allowed);
        Assert.Equal("subscription_expired", result.Reason);
    }

    [Fact]
    public async Task PremiumVideo_PlanHasContentPremiumButNoVideoLibraryNode_IsDenied()
    {
        await using var db = CreateDb();
        // Content-premium grants papers, NOT the Video Library — absent node
        // must never fall back to a legacy premium default here.
        SeedSubscription(db, "learner-1", """{"content":{"tier":"premium"}}""");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.AllowAccessAsync("learner-1", Video(), default);

        Assert.False(result.Allowed);
        Assert.Equal("plan_does_not_grant", result.Reason);
    }

    [Fact]
    public async Task PremiumVideo_ActiveAddOnGrantsVideoLibrary_IsAllowed()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        SeedSubscription(db, "learner-1", "{}");
        await db.SaveChangesAsync();

        var subscriptionId = db.Subscriptions.Single(s => s.UserId == "learner-1").Id;
        db.BillingAddOns.Add(new BillingAddOn
        {
            Id = "addon-video-library",
            Code = "video-library-addon",
            Name = "Video Library",
            GrantEntitlementsJson = """{"videoLibrary":true}""",
        });
        db.SubscriptionItems.Add(new SubscriptionItem
        {
            Id = $"item-{Guid.NewGuid():N}",
            SubscriptionId = subscriptionId,
            ItemCode = "video-library-addon",
            Quantity = 1,
            Status = SubscriptionItemStatus.Active,
            StartsAt = now.AddDays(-1),
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.AllowAccessAsync("learner-1", Video(), default);

        Assert.True(result.Allowed);
        Assert.Equal("addon_grants_video_library", result.Reason);
    }

    [Fact]
    public async Task PremiumVideo_FrozenSubscription_IsDeniedAsFrozen()
    {
        await using var db = CreateDb();
        SeedSubscription(db, "learner-1",
            """{"video_library":{"tier":"premium"}}""",
            status: SubscriptionStatus.Frozen);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.AllowAccessAsync("learner-1", Video(), default);
        Assert.False(result.Allowed);
        Assert.Equal("subscription_frozen", result.Reason);

        var ex = await Assert.ThrowsAsync<OetLearner.Api.Services.ApiException>(
            () => service.RequireAccessAsync("learner-1", Video(), default));
        Assert.Equal(403, ex.StatusCode);
        Assert.Equal("subscription_frozen", ex.ErrorCode);
    }

    [Fact]
    public async Task PremiumVideo_ExpiredSubscription_IsDeniedAsExpired()
    {
        await using var db = CreateDb();
        SeedSubscription(db, "learner-1",
            """{"video_library":{"tier":"premium"}}""",
            expiresAt: DateTimeOffset.UtcNow.AddDays(-1));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.AllowAccessAsync("learner-1", Video(), default);

        Assert.False(result.Allowed);
        Assert.Equal("subscription_expired", result.Reason);
    }

    [Fact]
    public async Task PremiumVideo_MalformedEntitlementsJson_FailsLow()
    {
        await using var db = CreateDb();
        SeedSubscription(db, "learner-1", "{not-json!!");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.AllowAccessAsync("learner-1", Video(), default);

        Assert.False(result.Allowed);
        var ex = await Assert.ThrowsAsync<OetLearner.Api.Services.ApiException>(
            () => service.RequireAccessAsync("learner-1", Video(), default));
        Assert.Equal(402, ex.StatusCode);
    }

    [Fact]
    public async Task Admin_IsAlwaysAllowed()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var context = await service.ResolveContextAsync("admin-1", isAdmin: true, default);
        var result = service.Evaluate(context, Video());

        Assert.True(result.Allowed);
        Assert.Equal("admin", result.Reason);
    }

    // ── Per-subtest (per-module) package gating ─────────────────────────────

    [Theory]
    [InlineData("writing", true, "plan_grants_video_library")]
    [InlineData("listening", false, "plan_does_not_grant_subtest")]
    public async Task PremiumVideo_PlanGrantsOnlyWritingSubtest_GatesByModule(
        string subtest, bool allowed, string reason)
    {
        await using var db = CreateDb();
        SeedSubscription(db, "learner-1", """{"video_library":{"tier":"premium","subtests":["writing"]}}""");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.AllowAccessAsync("learner-1", Video(subtestCode: subtest), default);

        Assert.Equal(allowed, result.Allowed);
        Assert.Equal(reason, result.Reason);
    }

    [Fact]
    public async Task PremiumVideo_PlanGrantsPremiumWithNoSubtestList_UnlocksAllModules()
    {
        // Backward compatibility: absent subtests list = all modules (today's behaviour).
        await using var db = CreateDb();
        SeedSubscription(db, "learner-1", """{"video_library":{"tier":"premium"}}""");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        foreach (var subtest in new[] { "listening", "reading", "writing", "speaking" })
        {
            var result = await service.AllowAccessAsync("learner-1", Video(subtestCode: subtest), default);
            Assert.True(result.Allowed, $"expected {subtest} to unlock");
        }
    }

    [Fact]
    public async Task PremiumVideo_SubtestRestrictedPlan_VideoWithNoSubtestCode_FailsOpen()
    {
        await using var db = CreateDb();
        SeedSubscription(db, "learner-1", """{"video_library":{"tier":"premium","subtests":["writing"]}}""");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.AllowAccessAsync("learner-1", Video(subtestCode: null), default);

        Assert.True(result.Allowed);
    }

    [Theory]
    [InlineData("listening", true)]
    [InlineData("writing", false)]
    public async Task PremiumVideo_AddOnScopedToListening_GatesByModule(string subtest, bool allowed)
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        SeedSubscription(db, "learner-1", "{}");
        await db.SaveChangesAsync();
        var subscriptionId = db.Subscriptions.Single(s => s.UserId == "learner-1").Id;
        db.BillingAddOns.Add(new BillingAddOn
        {
            Id = "addon-listening",
            Code = "video-listening-addon",
            Name = "Listening videos",
            GrantEntitlementsJson = """{"videoLibrary":true,"videoLibrarySubtests":["listening"]}""",
        });
        db.SubscriptionItems.Add(new SubscriptionItem
        {
            Id = $"item-{Guid.NewGuid():N}",
            SubscriptionId = subscriptionId,
            ItemCode = "video-listening-addon",
            Quantity = 1,
            Status = SubscriptionItemStatus.Active,
            StartsAt = now.AddDays(-1),
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.AllowAccessAsync("learner-1", Video(subtestCode: subtest), default);

        Assert.Equal(allowed, result.Allowed);
    }

    [Theory]
    [InlineData("""{"video_library":{"tier":"premium"}}""", 0)]
    [InlineData("""{"video_library":{"tier":"premium","subtests":["writing"]}}""", 1)]
    [InlineData("""{"video_library":{"tier":"premium","subtests":["Writing","LISTENING"]}}""", 2)]
    [InlineData("""{"video_library":{"tier":"premium","modules":["reading"]}}""", 1)]
    public void ParseVideoLibraryBundle_ReadsSubtestScope(string json, int expectedCount)
    {
        var bundle = VideoEntitlementService.ParseVideoLibraryBundle(json);
        Assert.Equal(expectedCount, bundle.Subtests.Count);
        Assert.Equal(expectedCount > 0, bundle.RestrictsSubtests);
        // Normalised to lowercase.
        Assert.All(bundle.Subtests, s => Assert.Equal(s.ToLowerInvariant(), s));
    }

    [Theory]
    [InlineData("""{"videoLibrary":true}""", true, true)]
    [InlineData("""{"videoLibrary":true,"videoLibrarySubtests":["writing"]}""", true, false)]
    [InlineData("""{"video_library":true,"video_library_subtests":["reading"]}""", true, false)]
    [InlineData("""{"videoLibrary":false}""", false, false)]
    public void ParseAddOnVideoGrant_ReadsSubtestScope(string json, bool grants, bool allSubtests)
    {
        var grant = VideoEntitlementService.ParseAddOnVideoGrant(json);
        Assert.Equal(grants, grant.Grants);
        Assert.Equal(allSubtests, grant.AllSubtests);
    }

    // ── ParseVideoLibraryBundle ─────────────────────────────────────────────

    [Theory]
    [InlineData(null, false, "none")]
    [InlineData("", false, "none")]
    [InlineData("not json", false, "none")]
    [InlineData("[]", false, "none")]
    [InlineData("{}", false, "none")]
    [InlineData("""{"content":{"tier":"premium"}}""", false, "none")]
    [InlineData("""{"video_library":{"tier":"premium"}}""", true, "premium")]
    [InlineData("""{"videoLibrary":{"tier":"premium"}}""", true, "premium")]
    [InlineData("""{"video_library":{"tier":"free"}}""", true, "free")]
    [InlineData("""{"video_library":{}}""", true, "free")]
    [InlineData("""{"video_library":true}""", false, "none")]
    public void ParseVideoLibraryBundle_CoversShapes(string? json, bool hasNode, string tier)
    {
        var bundle = VideoEntitlementService.ParseVideoLibraryBundle(json);
        Assert.Equal(hasNode, bundle.HasNode);
        Assert.Equal(tier, bundle.Tier);
    }

    [Fact]
    public void ParseVideoLibraryBundle_GrantsPremiumOnlyForPremiumTier()
    {
        Assert.True(VideoEntitlementService.ParseVideoLibraryBundle(
            """{"video_library":{"tier":"premium"}}""").GrantsPremium);
        Assert.False(VideoEntitlementService.ParseVideoLibraryBundle(
            """{"video_library":{"tier":"free"}}""").GrantsPremium);
        Assert.False(VideoEntitlementService.ParseVideoLibraryBundle("{}").GrantsPremium);
    }

    [Theory]
    [InlineData("""{"videoLibrary":true}""", true)]
    [InlineData("""{"video_library":true}""", true)]
    [InlineData("""{"videoLibrary":"true"}""", true)]
    [InlineData("""{"videoLibrary":false}""", false)]
    [InlineData("""{"mockFull":3}""", false)]
    [InlineData("broken", false)]
    [InlineData(null, false)]
    public void GrantsVideoLibrary_CoversAddOnShapes(string? json, bool expected)
        => Assert.Equal(expected, VideoEntitlementService.GrantsVideoLibrary(json));
}
