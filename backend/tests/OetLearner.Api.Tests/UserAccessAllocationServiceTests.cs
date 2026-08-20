using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Tests;

public class UserAccessAllocationServiceTests
{
    private static LearnerDbContext CreateDb()
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NoopAddonProcessor : IAddonGrantProcessor
    {
        public Task<AddonGrantResult> ApplyAsync(string eventId, string subscriptionId, string addOnCode, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<AddonGrantResult> ReverseAsync(string eventId, string subscriptionId, string addOnCode, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private static UserAccessAllocationService CreateService(LearnerDbContext db, IAiPackageCreditService? credits = null)
        => new(db, new NoopAddonProcessor(), TimeProvider.System, credits);

    private static async Task SeedLearnerAsync(LearnerDbContext db, string userId, string? authId = null)
    {
        db.Users.Add(new LearnerUser
        {
            Id = userId,
            AuthAccountId = authId,
            Role = ApplicationUserRoles.Learner,
            DisplayName = "Test",
            Email = $"{userId}@t.dev",
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            AccountStatus = "active",
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GrantPackage_CreatesSubscription_WithCustomExpiry_AndSetsPrimary()
    {
        await using var db = CreateDb();
        await SeedLearnerAsync(db, "learner-1");
        db.BillingPlans.Add(new BillingPlan { Id = "plan-med", Code = "med", Name = "Medicine", DurationMonths = 6, AccessDurationDays = 180 });
        await db.SaveChangesAsync();
        var customExpiry = DateTimeOffset.UtcNow.AddDays(90);

        var access = await CreateService(db).GrantPackageAsync("admin", "Admin", "learner-1",
            new AdminUserAccessPackageRequest("med", StartsAt: null, ExpiresAt: customExpiry,
                MakePrimary: true, GrantIncludedCredits: false, OverrideProfessionMismatch: false), default);

        Assert.Single(access.Subscriptions);
        var sub = access.Subscriptions[0];
        Assert.Equal("med", sub.PlanCode);
        Assert.True(sub.IsPrimary);
        Assert.Equal(customExpiry, sub.ExpiresAt);
        Assert.Equal("med", (await db.Users.FirstAsync(u => u.Id == "learner-1")).CurrentPlanId);
    }

    [Fact]
    public async Task GrantPackage_SamePlanTwice_IsIdempotent_NoDuplicateRow()
    {
        await using var db = CreateDb();
        await SeedLearnerAsync(db, "learner-2");
        db.BillingPlans.Add(new BillingPlan { Id = "plan-med", Code = "med", Name = "Medicine", DurationMonths = 6, AccessDurationDays = 180 });
        await db.SaveChangesAsync();
        var svc = CreateService(db);
        var req = new AdminUserAccessPackageRequest("med", StartsAt: null, ExpiresAt: null,
            MakePrimary: true, GrantIncludedCredits: false, OverrideProfessionMismatch: false);

        await svc.GrantPackageAsync("admin", "Admin", "learner-2", req, default);
        var access = await svc.GrantPackageAsync("admin", "Admin", "learner-2", req, default);

        Assert.Single(access.Subscriptions);
        Assert.Equal(1, await db.Subscriptions.CountAsync(s => s.UserId == "learner-2"));
    }

    [Fact]
    public async Task GrantPackage_FullCourse_PutsGiftedAiCreditsInSpendableWallet()
    {
        await using var db = CreateDb();
        await SeedLearnerAsync(db, "learner-gift");
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "plan-med",
            Code = "full-condensed-medicine",
            Name = "Medicine Full Course",
            DurationMonths = 6,
            AccessDurationDays = 180,
            BundledAiCredits = 5,
        });
        await db.SaveChangesAsync();
        var credits = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);

        await CreateService(db, credits).GrantPackageAsync(
            "admin",
            "Admin",
            "learner-gift",
            new AdminUserAccessPackageRequest(
                "full-condensed-medicine",
                StartsAt: null,
                ExpiresAt: null,
                MakePrimary: true,
                GrantIncludedCredits: false,
                OverrideProfessionMismatch: false),
            default);

        var snapshot = await credits.GetSnapshotAsync("learner-gift", 20, default);
        Assert.Equal(5, snapshot.FlexibleCredits);
        var writing = await credits.CheckGradingCreditAsync(
            "learner-gift", "writing", AiGradingCreditCost.WritingExam, default);
        Assert.True(writing.Debited);
    }

    [Fact]
    public async Task GrantPackage_ReGrant_AfterGiftedCreditsAreSet_FundsSpendableWallet()
    {
        await using var db = CreateDb();
        await SeedLearnerAsync(db, "learner-regrant-gift");
        var plan = new BillingPlan
        {
            Id = "plan-nursing",
            Code = "full-nursing",
            Name = "Full Nursing OET Course",
            DurationMonths = 6,
            AccessDurationDays = 180,
            BundledAiCredits = 0,
        };
        db.BillingPlans.Add(plan);
        await db.SaveChangesAsync();
        var credits = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        var svc = CreateService(db, credits);
        var req = new AdminUserAccessPackageRequest(
            "full-nursing",
            StartsAt: null,
            ExpiresAt: null,
            MakePrimary: true,
            GrantIncludedCredits: false,
            OverrideProfessionMismatch: false);

        await svc.GrantPackageAsync("admin", "Admin", "learner-regrant-gift", req, default);
        Assert.Equal(0, (await credits.GetSnapshotAsync("learner-regrant-gift", 20, default)).FlexibleCredits);

        plan.BundledAiCredits = 5;
        await db.SaveChangesAsync();
        await svc.GrantPackageAsync("admin", "Admin", "learner-regrant-gift", req, default);

        var snapshot = await credits.GetSnapshotAsync("learner-regrant-gift", 20, default);
        Assert.Equal(5, snapshot.FlexibleCredits);
        Assert.Equal(5, snapshot.CreditsGranted);
        Assert.Equal(1, await db.Subscriptions.CountAsync(s => s.UserId == "learner-regrant-gift"));
    }

    [Fact]
    public async Task GrantPackage_TwoDifferentPlans_ProducesTwoSubscriptions()
    {
        await using var db = CreateDb();
        await SeedLearnerAsync(db, "learner-3");
        db.BillingPlans.AddRange(
            new BillingPlan { Id = "plan-med", Code = "med", Name = "Medicine", DurationMonths = 6, AccessDurationDays = 180 },
            new BillingPlan { Id = "plan-physio", Code = "physio", Name = "Physio", DurationMonths = 6, AccessDurationDays = 180 });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        await svc.GrantPackageAsync("admin", "Admin", "learner-3", new AdminUserAccessPackageRequest("med", null, null, true, false, false), default);
        var access = await svc.GrantPackageAsync("admin", "Admin", "learner-3", new AdminUserAccessPackageRequest("physio", null, null, false, false, false), default);

        Assert.Equal(2, access.Subscriptions.Count);
    }

    [Fact]
    public async Task RemovePackage_HidesCancelledRow_RevokesLinkedAddon_AndIsIdempotent()
    {
        await using var db = CreateDb();
        await SeedLearnerAsync(db, "learner-remove");
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "plan-med",
            Code = "med",
            Name = "Medicine",
            DurationMonths = 6,
            AccessDurationDays = 180,
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var granted = await service.GrantPackageAsync(
            "admin", "Admin", "learner-remove",
            new AdminUserAccessPackageRequest("med", null, null, true, false, false), default);
        var subscriptionId = granted.Subscriptions.Single().Id;
        var now = DateTimeOffset.UtcNow;
        db.SubscriptionItems.Add(new SubscriptionItem
        {
            Id = "subitem-remove",
            SubscriptionId = subscriptionId,
            ItemCode = "tutor-book-addon",
            ItemType = "addon",
            Status = SubscriptionItemStatus.Active,
            StartsAt = now.AddMinutes(-1),
            CreatedAt = now.AddMinutes(-1),
            UpdatedAt = now.AddMinutes(-1),
        });
        await db.SaveChangesAsync();

        var removed = await service.RemovePackageAsync("admin", "Admin", "learner-remove", subscriptionId, default);
        Assert.Empty(removed.Subscriptions);
        Assert.Empty(removed.AddOns);
        Assert.Equal(SubscriptionStatus.Cancelled, (await db.Subscriptions.SingleAsync()).Status);
        Assert.Contains(await db.AuditEvents.ToListAsync(),
            audit => audit.Action == "Package Removed" && audit.ResourceId == subscriptionId);

        var auditCount = await db.AuditEvents.CountAsync(a => a.Action == "Package Removed");
        var repeated = await service.RemovePackageAsync("admin", "Admin", "learner-remove", subscriptionId, default);
        Assert.Empty(repeated.Subscriptions);
        Assert.Equal(auditCount, await db.AuditEvents.CountAsync(a => a.Action == "Package Removed"));
    }

    [Fact]
    public async Task SetPrimaryPackage_ChangesOnlyRepresentative_AndRemovalRepointsPrimary()
    {
        await using var db = CreateDb();
        await SeedLearnerAsync(db, "learner-primary");
        db.BillingPlans.AddRange(
            new BillingPlan { Id = "plan-med", Code = "med", Name = "Medicine", DurationMonths = 6, AccessDurationDays = 180 },
            new BillingPlan { Id = "plan-physio", Code = "physio", Name = "Physio", DurationMonths = 6, AccessDurationDays = 180 });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var medicine = await service.GrantPackageAsync(
            "admin", "Admin", "learner-primary",
            new AdminUserAccessPackageRequest("med", null, null, true, false, false), default);
        var physio = await service.GrantPackageAsync(
            "admin", "Admin", "learner-primary",
            new AdminUserAccessPackageRequest("physio", null, null, false, false, false), default);

        var promoted = await service.SetPrimaryPackageAsync(
            "admin", "Admin", "learner-primary", physio.Subscriptions.Single(s => s.PlanCode == "physio").Id, default);
        Assert.Single(promoted.Subscriptions, s => s.IsPrimary);
        Assert.Equal("physio", promoted.Subscriptions.Single(s => s.IsPrimary).PlanCode);
        Assert.Equal(2, promoted.Subscriptions.Count);

        var removed = await service.RemovePackageAsync(
            "admin", "Admin", "learner-primary", physio.Subscriptions.Single(s => s.PlanCode == "physio").Id, default);
        Assert.Single(removed.Subscriptions);
        Assert.True(removed.Subscriptions.Single().IsPrimary);
        Assert.Equal("med", removed.Subscriptions.Single().PlanCode);
        Assert.Equal(medicine.Subscriptions.Single().Id, removed.Subscriptions.Single().Id);
    }

    [Fact]
    public async Task RemovePackage_ReversesIncludedCreditsOnce_WithoutNegativeBalance()
    {
        await using var db = CreateDb();
        await SeedLearnerAsync(db, "learner-credits");
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "plan-credits",
            Code = "credits",
            Name = "Credits",
            DurationMonths = 1,
            AccessDurationDays = 30,
            IncludedCredits = 10,
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var granted = await service.GrantPackageAsync(
            "admin", "Admin", "learner-credits",
            new AdminUserAccessPackageRequest("credits", null, null, true, true, false), default);
        var subscriptionId = granted.Subscriptions.Single().Id;

        await service.RemovePackageAsync("admin", "Admin", "learner-credits", subscriptionId, default);
        await service.RemovePackageAsync("admin", "Admin", "learner-credits", subscriptionId, default);

        var wallet = await db.Wallets.SingleAsync(w => w.UserId == "learner-credits");
        Assert.Equal(0, wallet.CreditBalance);
        Assert.Single(await db.WalletTransactions.Where(tx => tx.IdempotencyKey == $"admin_package_revoke:{subscriptionId}").ToListAsync());
        Assert.DoesNotContain(await db.WalletTransactions.ToListAsync(), tx => tx.BalanceAfter < 0);
    }

    [Fact]
    public async Task RegrantAfterRemoval_CreatesOneCurrentRow_AndKeepsHistory()
    {
        await using var db = CreateDb();
        await SeedLearnerAsync(db, "learner-regrant");
        db.BillingPlans.Add(new BillingPlan { Id = "plan-med", Code = "med", Name = "Medicine", DurationMonths = 6, AccessDurationDays = 180 });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var first = await service.GrantPackageAsync(
            "admin", "Admin", "learner-regrant",
            new AdminUserAccessPackageRequest("med", null, null, true, false, false), default);
        await service.RemovePackageAsync("admin", "Admin", "learner-regrant", first.Subscriptions.Single().Id, default);
        var current = await service.GrantPackageAsync(
            "admin", "Admin", "learner-regrant",
            new AdminUserAccessPackageRequest("med", null, null, false, false, false), default);

        Assert.Single(current.Subscriptions);
        Assert.Equal("Active", current.Subscriptions.Single().Status);
        Assert.Equal(2, await db.Subscriptions.CountAsync(s => s.UserId == "learner-regrant"));
        Assert.Equal(SubscriptionStatus.Cancelled,
            (await db.Subscriptions.SingleAsync(s => s.Id == first.Subscriptions.Single().Id)).Status);
    }

    [Fact]
    public async Task PutScope_ReplacesModuleOverrides_AndSetsMasterExpiry()
    {
        await using var db = CreateDb();
        await SeedLearnerAsync(db, "learner-4");
        var expiry = DateTimeOffset.UtcNow.AddDays(30);

        var access = await CreateService(db).PutScopeAsync("admin", "Admin", "learner-4",
            new AdminUserAccessScopeRequest(
                Modules: new List<AdminModuleOverrideDto> { new("Mocks", false), new("VideoLibrary", true) },
                MaterialFolderIds: new List<string> { "mfd_a", "mfd_b" },
                RecallSetCodes: new List<string> { "2026" },
                AccessExpiresAt: expiry,
                ClearAccessExpiry: false), default);

        Assert.Equal(2, access.ModuleOverrides.Count);
        Assert.Contains(access.ModuleOverrides, m => m.ModuleKey == "Mocks" && !m.Enabled);
        Assert.Equal(2, access.MaterialFolderIds.Count);
        Assert.Equal(new[] { "2026" }, access.RecallSetCodes);
        Assert.Equal(expiry, access.AccessExpiresAt);
    }

    [Fact]
    public async Task PutScope_PreservesInitialVideoScopeDate_WhenUpdatingVideoSelection()
    {
        await using var db = CreateDb();
        await SeedLearnerAsync(db, "learner-video-scope");
        var initialScopeAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        db.UserVideoAccesses.Add(new UserVideoAccess
        {
            Id = "uva-initial",
            UserId = "learner-video-scope",
            VideoId = "vid-initial",
            CreatedAt = initialScopeAt,
        });
        await db.SaveChangesAsync();

        await CreateService(db).PutScopeAsync(
            "admin", "Admin", "learner-video-scope",
            new AdminUserAccessScopeRequest(
                Modules: null,
                MaterialFolderIds: null,
                RecallSetCodes: null,
                AccessExpiresAt: null,
                ClearAccessExpiry: false,
                VideoIds: ["vid-updated"]),
            default);

        var updated = await db.UserVideoAccesses.SingleAsync();
        Assert.Equal("vid-updated", updated.VideoId);
        Assert.Equal(initialScopeAt, updated.CreatedAt);
    }

    [Fact]
    public async Task PutScope_PastExpiry_RevokesActiveRefreshTokens()
    {
        await using var db = CreateDb();
        await SeedLearnerAsync(db, "learner-5", authId: "auth-5");
        var tokenId = Guid.NewGuid();
        db.RefreshTokenRecords.Add(new RefreshTokenRecord
        {
            Id = tokenId,
            ApplicationUserAccountId = "auth-5",
            TokenHash = "hash",
            FamilyId = Guid.NewGuid(),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            RevokedAt = null,
        });
        await db.SaveChangesAsync();

        await CreateService(db).PutScopeAsync("admin", "Admin", "learner-5",
            new AdminUserAccessScopeRequest(null, null, null,
                AccessExpiresAt: DateTimeOffset.UtcNow.AddDays(-1), ClearAccessExpiry: false), default);

        var token = await db.RefreshTokenRecords.FirstAsync(t => t.Id == tokenId);
        Assert.NotNull(token.RevokedAt);
    }

    [Fact]
    public async Task GrantAddon_WithProductionLengthIds_SavesItem_FitsKeys_AndFundsAiPackage()
    {
        await using var db = CreateDb();
        const string userId = "learner_b8d731647c9b4462bd7151e55e1e4558";
        var subscriptionId = $"sub-{Guid.NewGuid():N}";
        await SeedLearnerAsync(db, userId);
        var now = DateTimeOffset.UtcNow;
        db.Subscriptions.Add(new Subscription
        {
            Id = subscriptionId,
            UserId = userId,
            PlanId = "full-condensed-medicine-tbook",
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddMonths(6),
        });
        db.BillingAddOns.Add(new BillingAddOn
        {
            Id = "addon_pkg_writing_starter",
            Code = "pkg_writing_starter",
            Name = "Writing Starter",
            Status = BillingAddOnStatus.Active,
            AddonKind = "ai_package",
            GrantCredits = 3,
            GrantEntitlementsJson = """{"package_type":"writing","writing_only_credits":3}""",
            DurationDays = 30,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var credits = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        var processor = new AddonGrantProcessor(db, NullLogger<AddonGrantProcessor>.Instance, credits);
        var service = new UserAccessAllocationService(db, processor, TimeProvider.System, credits);

        var access = await service.GrantAddonAsync(
            "admin", "Admin", userId,
            new AdminUserAccessAddonRequest("pkg_writing_starter", subscriptionId, 1), default);
        var snapshot = await credits.GetSnapshotAsync(userId, 20, default);
        var idem = Assert.Single(await db.IdempotencyRecords.ToListAsync());
        var tx = Assert.Single(await db.AiPackageCreditTransactions.ToListAsync());

        Assert.Contains(access.AddOns, addOn => addOn.Code == "pkg_writing_starter" && addOn.SubscriptionId == subscriptionId);
        Assert.True(idem.Key.Length <= 128);
        Assert.True((tx.StripeSessionId ?? string.Empty).Length <= 128);
        Assert.True((tx.ReferenceId ?? string.Empty).Length <= 128);
        Assert.Equal(3, snapshot.WritingOnlyCredits);
        Assert.Equal(3, snapshot.CreditsRemaining);
    }

    [Fact]
    public async Task GrantAddon_AiPackage_WithoutMainPlan_CreatesStandaloneEntitlement()
    {
        await using var db = CreateDb();
        const string userId = "learner-standalone-ai";
        await SeedLearnerAsync(db, userId);
        var now = DateTimeOffset.UtcNow;
        db.BillingAddOns.Add(new BillingAddOn
        {
            Id = "addon_pkg_reading_starter",
            Code = "pkg_reading_starter",
            Name = "Reading Starter",
            Status = BillingAddOnStatus.Active,
            AddonKind = "ai_package",
            RequiresEligibleParent = false,
            GrantCredits = 0,
            GrantEntitlementsJson = """{"package_type":"reading","reading_tests":5}""",
            DurationDays = 30,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var credits = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        var processor = new AddonGrantProcessor(db, NullLogger<AddonGrantProcessor>.Instance, credits);
        var service = new UserAccessAllocationService(db, processor, TimeProvider.System, credits);

        var access = await service.GrantAddonAsync(
            "admin", "Admin", userId,
            new AdminUserAccessAddonRequest("pkg_reading_starter", null, 1), default);

        Assert.Empty(access.Subscriptions);
        Assert.Contains(access.AddOns, addOn => addOn.Code == "pkg_reading_starter");
        Assert.Null((await db.Users.SingleAsync(u => u.Id == userId)).CurrentPlanId);
        Assert.Equal(1, await db.Subscriptions.CountAsync(s => s.UserId == userId && s.PlanId == Subscription.StandaloneAddonPlanId));
        var snapshot = await credits.GetSnapshotAsync(userId, 20, default);
        Assert.Equal(5, snapshot.ReadingTestsRemaining);
    }

    [Fact]
    public async Task GrantAddon_WritingThenSpeaking_KeepsPoolsSeparate()
    {
        await using var db = CreateDb();
        const string userId = "learner-skill-separate";
        await SeedLearnerAsync(db, userId);
        var now = DateTimeOffset.UtcNow;
        db.BillingAddOns.AddRange(
            new BillingAddOn
            {
                Id = "addon_pkg_writing_starter",
                Code = "pkg_writing_starter",
                Name = "Writing Starter",
                Status = BillingAddOnStatus.Active,
                AddonKind = "ai_package",
                RequiresEligibleParent = false,
                GrantCredits = 3,
                GrantEntitlementsJson = """{"package_type":"writing","writing_only_credits":6,"listening_tests":0,"reading_tests":0}""",
                DurationDays = 30,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new BillingAddOn
            {
                Id = "addon_pkg_speaking_starter",
                Code = "pkg_speaking_starter",
                Name = "Speaking Starter",
                Status = BillingAddOnStatus.Active,
                AddonKind = "ai_package",
                RequiresEligibleParent = false,
                GrantCredits = 3,
                GrantEntitlementsJson = """{"package_type":"speaking","speaking_only_credits":3,"listening_tests":0,"reading_tests":0}""",
                DurationDays = 30,
                CreatedAt = now,
                UpdatedAt = now,
            });
        await db.SaveChangesAsync();

        var credits = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        var processor = new AddonGrantProcessor(db, NullLogger<AddonGrantProcessor>.Instance, credits);
        var service = new UserAccessAllocationService(db, processor, TimeProvider.System, credits);

        await service.GrantAddonAsync(
            "admin", "Admin", userId,
            new AdminUserAccessAddonRequest("pkg_writing_starter", null, 1), default);
        var afterWriting = await credits.GetSnapshotAsync(userId, 20, default);
        await service.GrantAddonAsync(
            "admin", "Admin", userId,
            new AdminUserAccessAddonRequest("pkg_speaking_starter", null, 1), default);
        var afterSpeaking = await credits.GetSnapshotAsync(userId, 20, default);

        Assert.Equal(6, afterWriting.WritingOnlyCredits);
        Assert.Equal(0, afterWriting.SpeakingOnlyCredits);
        Assert.Equal(6, afterSpeaking.WritingOnlyCredits);
        Assert.Equal(3, afterSpeaking.SpeakingOnlyCredits);
        Assert.Equal(0, afterSpeaking.FlexibleCredits);
    }

    [Fact]
    public async Task GrantAddon_OetMastery_MarksWritingAndSpeakingUnlimited()
    {
        await using var db = CreateDb();
        const string userId = "learner-mastery-unlimited";
        await SeedLearnerAsync(db, userId);
        var now = DateTimeOffset.UtcNow;
        db.BillingAddOns.Add(new BillingAddOn
        {
            Id = "addon_pkg_oet_mastery",
            Code = "pkg_oet_mastery",
            Name = "OET Mastery",
            Status = BillingAddOnStatus.Active,
            AddonKind = "ai_package",
            RequiresEligibleParent = false,
            GrantCredits = 30,
            GrantEntitlementsJson = """{"package_type":"full","unlimited_grading":true,"flexible_credits":30,"listening_tests":null,"reading_tests":null}""",
            DurationDays = 180,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var credits = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        var processor = new AddonGrantProcessor(db, NullLogger<AddonGrantProcessor>.Instance, credits);
        var service = new UserAccessAllocationService(db, processor, TimeProvider.System, credits);

        var access = await service.GrantAddonAsync(
            "admin", "Admin", userId,
            new AdminUserAccessAddonRequest("pkg_oet_mastery", null, 1), default);
        var snapshot = await credits.GetSnapshotAsync(userId, 20, default);

        Assert.Contains(access.AddOns, addOn => addOn.Code == "pkg_oet_mastery");
        Assert.True(snapshot.WritingUnlimited);
        Assert.True(snapshot.SpeakingUnlimited);
        Assert.Equal(0, snapshot.FlexibleCredits);
        Assert.Null(snapshot.ListeningTestsRemaining);
        Assert.Null(snapshot.ReadingTestsRemaining);
    }

    [Fact]
    public async Task GrantAddon_ParentRequired_WithoutMainPlan_DoesNotUseMainPlanError()
    {
        await using var db = CreateDb();
        const string userId = "learner-parent-addon";
        await SeedLearnerAsync(db, userId);
        var now = DateTimeOffset.UtcNow;
        db.BillingAddOns.Add(new BillingAddOn
        {
            Id = "addon_access_ext",
            Code = "access_extension",
            Name = "Access Extension",
            Status = BillingAddOnStatus.Active,
            AddonKind = "access_extension",
            RequiresEligibleParent = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            CreateService(db).GrantAddonAsync(
                "admin", "Admin", userId,
                new AdminUserAccessAddonRequest("access_extension", null, 1), default));

        Assert.Equal("addon_needs_package", ex.ErrorCode);
        Assert.DoesNotContain("main plan", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no active subscription to attach", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoveAddon_DeletedAddOn_DoesNotReturnOnGetAccess()
    {
        await using var db = CreateDb();
        const string userId = "learner-remove-addon";
        await SeedLearnerAsync(db, userId);
        var now = DateTimeOffset.UtcNow;
        db.BillingAddOns.Add(new BillingAddOn
        {
            Id = "addon_pkg_reading_pro",
            Code = "pkg_reading_pro",
            Name = "Reading Pro",
            Status = BillingAddOnStatus.Active,
            AddonKind = "ai_package",
            RequiresEligibleParent = false,
            GrantEntitlementsJson = """{"package_type":"reading","reading_tests":null}""",
            DurationDays = 180,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var credits = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        var processor = new AddonGrantProcessor(db, NullLogger<AddonGrantProcessor>.Instance, credits);
        var service = new UserAccessAllocationService(db, processor, TimeProvider.System, credits);

        var granted = await service.GrantAddonAsync(
            "admin", "Admin", userId,
            new AdminUserAccessAddonRequest("pkg_reading_pro", null, 1), default);
        Assert.Contains(granted.AddOns, addOn => addOn.Code == "pkg_reading_pro");

        var removed = await service.RemoveAddonAsync(
            "admin", "Admin", userId, "pkg_reading_pro", granted.AddOns[0].SubscriptionId, default);

        Assert.Empty(removed.AddOns);
        Assert.Empty((await service.GetAccessAsync(userId, default)).AddOns);
        Assert.Equal(SubscriptionItemStatus.Cancelled, (await db.SubscriptionItems.SingleAsync()).Status);
    }
}
