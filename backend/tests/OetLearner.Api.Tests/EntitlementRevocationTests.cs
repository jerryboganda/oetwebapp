using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Tests;

/// <summary>
/// Regression suite for the critical entitlement-revocation defect: deleting a
/// course/package/add-on via Save Access must immediately stop every
/// entitlement that source granted (ghost Unlimited reading credits being the
/// reported repro). Covers the central invariant: a deleted/revoked/inactive
/// entitlement source can never contribute to current learner access, while
/// unrelated surviving grants (manual credits, second unlimited source) stay
/// intact and repeated revocation stays idempotent.
/// </summary>
public class EntitlementRevocationTests
{
    private static LearnerDbContext CreateDb()
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static async Task SeedLearnerAsync(LearnerDbContext db, string userId)
    {
        db.Users.Add(new LearnerUser
        {
            Id = userId,
            Role = ApplicationUserRoles.Learner,
            DisplayName = "Test",
            Email = $"{userId}@t.dev",
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            AccountStatus = "active",
        });
        await db.SaveChangesAsync();
    }

    private static void SeedCoursePlan(LearnerDbContext db, string code = "full-condensed-medicine")
    {
        db.BillingPlans.Add(new BillingPlan
        {
            Id = $"plan-{code}",
            Code = code,
            Name = "Medicine Full Course",
            DurationMonths = 6,
            AccessDurationDays = 180,
            BundledAiCredits = 0,
        });
    }

    private static void SeedReadingPro(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        db.BillingAddOns.Add(new BillingAddOn
        {
            Id = "addon_pkg_reading_pro",
            Code = "pkg_reading_pro",
            Name = "Reading Pro",
            Status = BillingAddOnStatus.Active,
            AddonKind = "ai_package",
            RequiresEligibleParent = false,
            GrantCredits = 0,
            GrantEntitlementsJson = """{"package_type":"reading","reading_tests":null}""",
            DurationDays = 180,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    private static void SeedListeningPro(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        db.BillingAddOns.Add(new BillingAddOn
        {
            Id = "addon_pkg_listening_pro",
            Code = "pkg_listening_pro",
            Name = "Listening Pro",
            Status = BillingAddOnStatus.Active,
            AddonKind = "ai_package",
            RequiresEligibleParent = false,
            GrantCredits = 0,
            GrantEntitlementsJson = """{"package_type":"listening","listening_tests":null}""",
            DurationDays = 180,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    private static (UserAccessAllocationService Access, AiPackageCreditService Credits) CreateServices(LearnerDbContext db)
    {
        var credits = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        var processor = new AddonGrantProcessor(db, NullLogger<AddonGrantProcessor>.Instance, credits);
        return (new UserAccessAllocationService(db, processor, TimeProvider.System, credits), credits);
    }

    [Fact]
    public async Task RemovePackage_WithAttachedReadingPro_ClearsUnlimitedReading()
    {
        await using var db = CreateDb();
        const string userId = "learner-revoke-reading-pro";
        await SeedLearnerAsync(db, userId);
        SeedCoursePlan(db);
        SeedReadingPro(db);
        await db.SaveChangesAsync();
        var (access, credits) = CreateServices(db);

        var granted = await access.GrantPackageAsync("admin", "Admin", userId,
            new AdminUserAccessPackageRequest("full-condensed-medicine", null, null, true, false, false), default);
        var subscriptionId = granted.Subscriptions.Single().Id;
        await access.GrantAddonAsync("admin", "Admin", userId,
            new AdminUserAccessAddonRequest("pkg_reading_pro", subscriptionId, 1), default);

        var before = await credits.GetSnapshotAsync(userId, 0, default);
        Assert.Null(before.ReadingTestsRemaining);
        Assert.True(await credits.HasObjectivePracticeAllowanceAsync(userId, "reading", default));

        var removed = await access.RemovePackageAsync("admin", "Admin", userId, subscriptionId, default);
        var after = await credits.GetSnapshotAsync(userId, 0, default);

        Assert.Empty(removed.Subscriptions);
        Assert.Empty(removed.AddOns);
        Assert.Equal(0, after.ReadingTestsRemaining);
        Assert.False(after.ReadingUnlimited);
        Assert.False(await credits.HasObjectivePracticeAllowanceAsync(userId, "reading", default));
        var debit = await credits.DeductObjectivePracticeAsync(userId, "reading", "revoke-check-1", default);
        Assert.False(debit.Debited);
    }

    [Fact]
    public async Task RemoveAddon_ReadingProStandalone_ClearsUnlimitedReading()
    {
        await using var db = CreateDb();
        const string userId = "learner-revoke-standalone";
        await SeedLearnerAsync(db, userId);
        SeedReadingPro(db);
        await db.SaveChangesAsync();
        var (access, credits) = CreateServices(db);

        await access.GrantAddonAsync("admin", "Admin", userId,
            new AdminUserAccessAddonRequest("pkg_reading_pro", null, 1), default);
        Assert.Null((await credits.GetSnapshotAsync(userId, 0, default)).ReadingTestsRemaining);

        await access.RemoveAddonAsync("admin", "Admin", userId, "pkg_reading_pro", null, default);
        var after = await credits.GetSnapshotAsync(userId, 0, default);

        Assert.Empty((await access.GetAccessAsync(userId, default)).AddOns);
        Assert.Equal(0, after.ReadingTestsRemaining);
        Assert.False(await credits.HasObjectivePracticeAllowanceAsync(userId, "reading", default));
    }

    [Fact]
    public async Task TwoUnlimitedReadingSources_OneRemoved_OtherSurvives_BothRemoved_Clears()
    {
        await using var db = CreateDb();
        const string userId = "learner-two-unlimited";
        await SeedLearnerAsync(db, userId);
        SeedCoursePlan(db);
        SeedReadingPro(db);
        var now = DateTimeOffset.UtcNow;
        db.BillingAddOns.Add(new BillingAddOn
        {
            Id = "addon_pkg_oet_mastery",
            Code = "pkg_oet_mastery",
            Name = "OET Mastery",
            Status = BillingAddOnStatus.Active,
            AddonKind = "ai_package",
            RequiresEligibleParent = false,
            GrantEntitlementsJson = """{"package_type":"full","unlimited_grading":true,"listening_tests":null,"reading_tests":null}""",
            DurationDays = 180,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        var (access, credits) = CreateServices(db);

        var granted = await access.GrantPackageAsync("admin", "Admin", userId,
            new AdminUserAccessPackageRequest("full-condensed-medicine", null, null, true, false, false), default);
        var subscriptionId = granted.Subscriptions.Single().Id;
        await access.GrantAddonAsync("admin", "Admin", userId,
            new AdminUserAccessAddonRequest("pkg_reading_pro", subscriptionId, 1), default);
        await access.GrantAddonAsync("admin", "Admin", userId,
            new AdminUserAccessAddonRequest("pkg_oet_mastery", null, 1), default);

        await access.RemoveAddonAsync("admin", "Admin", userId, "pkg_reading_pro", subscriptionId, default);
        var middle = await credits.GetSnapshotAsync(userId, 0, default);
        Assert.Null(middle.ReadingTestsRemaining);

        await access.RemoveAddonAsync("admin", "Admin", userId, "pkg_oet_mastery", null, default);
        var after = await credits.GetSnapshotAsync(userId, 0, default);
        Assert.Equal(0, after.ReadingTestsRemaining);
        Assert.False(await credits.HasObjectivePracticeAllowanceAsync(userId, "reading", default));
    }

    [Fact]
    public async Task SuspendPackage_ParksUnlimited_RestoreRevives()
    {
        await using var db = CreateDb();
        const string userId = "learner-suspend-park";
        await SeedLearnerAsync(db, userId);
        SeedCoursePlan(db);
        SeedReadingPro(db);
        await db.SaveChangesAsync();
        var (access, credits) = CreateServices(db);

        var granted = await access.GrantPackageAsync("admin", "Admin", userId,
            new AdminUserAccessPackageRequest("full-condensed-medicine", null, null, true, false, false), default);
        var subscriptionId = granted.Subscriptions.Single().Id;
        await access.GrantAddonAsync("admin", "Admin", userId,
            new AdminUserAccessAddonRequest("pkg_reading_pro", subscriptionId, 1), default);

        await access.SuspendPackageAsync("admin", "Admin", userId, subscriptionId, default);
        var suspended = await credits.GetSnapshotAsync(userId, 0, default);
        Assert.Equal(0, suspended.ReadingTestsRemaining);
        Assert.False(await credits.HasObjectivePracticeAllowanceAsync(userId, "reading", default));

        await access.RestorePackageAsync("admin", "Admin", userId, subscriptionId, default);
        var restored = await credits.GetSnapshotAsync(userId, 0, default);
        Assert.Null(restored.ReadingTestsRemaining);
        Assert.True(await credits.HasObjectivePracticeAllowanceAsync(userId, "reading", default));
    }

    [Fact]
    public async Task UpdatePackageDates_PastExpiry_RevokesUnlimited_ExtendRevives()
    {
        await using var db = CreateDb();
        const string userId = "learner-date-override";
        await SeedLearnerAsync(db, userId);
        SeedCoursePlan(db);
        SeedReadingPro(db);
        await db.SaveChangesAsync();
        var (access, credits) = CreateServices(db);

        var granted = await access.GrantPackageAsync("admin", "Admin", userId,
            new AdminUserAccessPackageRequest("full-condensed-medicine", null, null, true, false, false), default);
        var subscriptionId = granted.Subscriptions.Single().Id;
        await access.GrantAddonAsync("admin", "Admin", userId,
            new AdminUserAccessAddonRequest("pkg_reading_pro", subscriptionId, 1), default);

        await access.UpdatePackageDatesAsync("admin", "Admin", userId, subscriptionId,
            new AdminUserAccessPackageDatesRequest(null, DateTimeOffset.UtcNow.AddDays(-1)), default);
        var expired = await credits.GetSnapshotAsync(userId, 0, default);
        Assert.Equal(SubscriptionStatus.Expired,
            (await db.Subscriptions.SingleAsync(s => s.Id == subscriptionId)).Status);
        Assert.Equal(0, expired.ReadingTestsRemaining);
        Assert.False(await credits.HasObjectivePracticeAllowanceAsync(userId, "reading", default));

        await access.UpdatePackageDatesAsync("admin", "Admin", userId, subscriptionId,
            new AdminUserAccessPackageDatesRequest(null, DateTimeOffset.UtcNow.AddDays(30)), default);
        var extended = await credits.GetSnapshotAsync(userId, 0, default);
        Assert.Null(extended.ReadingTestsRemaining);
        Assert.True(await credits.HasObjectivePracticeAllowanceAsync(userId, "reading", default));
    }

    [Fact]
    public async Task Recalculate_LegacyNullPoolWithNoLots_ClearsGhostUnlimited()
    {
        await using var db = CreateDb();
        const string userId = "learner-legacy-null";
        await SeedLearnerAsync(db, userId);
        var now = DateTimeOffset.UtcNow;
        db.AiPackageCreditAccounts.Add(new AiPackageCreditAccount
        {
            Id = "aipkg-acct-legacy",
            UserId = userId,
            ListeningTestsRemaining = 0,
            ReadingTestsRemaining = null,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        var credits = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);

        await credits.RecalculateObjectiveAllowancesAsync(userId, default);
        var snapshot = await credits.GetSnapshotAsync(userId, 0, default);

        Assert.Equal(0, snapshot.ReadingTestsRemaining);
        Assert.False(await credits.HasObjectivePracticeAllowanceAsync(userId, "reading", default));
    }

    [Fact]
    public async Task Recalculate_LegacyNullSourceUnlimitedLot_ExpiresGhost()
    {
        await using var db = CreateDb();
        const string userId = "learner-legacy-lot";
        await SeedLearnerAsync(db, userId);
        var now = DateTimeOffset.UtcNow;
        var account = new AiPackageCreditAccount
        {
            Id = "aipkg-acct-legacylot",
            UserId = userId,
            ListeningTestsRemaining = 0,
            ReadingTestsRemaining = null,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AiPackageCreditAccounts.Add(account);
        await db.SaveChangesAsync();
        db.AiPackageCreditLots.Add(new AiPackageCreditLot
        {
            Id = "aipkg-lot-legacy",
            UserId = userId,
            AccountId = account.Id,
            PackageId = "legacy",
            PackageType = "legacy",
            ReadingTestsRemaining = null,
            UnlimitedReading = true,
            ValidFrom = now.AddDays(-10),
            ExpiresAt = now.AddDays(170),
            SourceReferenceId = null,
            CreatedAt = now.AddDays(-10),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var credits = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);

        await credits.RecalculateObjectiveAllowancesAsync(userId, default);
        var snapshot = await credits.GetSnapshotAsync(userId, 0, default);

        Assert.Equal(0, snapshot.ReadingTestsRemaining);
        Assert.True((await db.AiPackageCreditLots.SingleAsync(l => l.Id == "aipkg-lot-legacy")).Expired);
        Assert.False(await credits.HasObjectivePracticeAllowanceAsync(userId, "reading", default));
    }

    [Fact]
    public async Task RemoveAddon_PreservesIndependentManualReadingGrant()
    {
        await using var db = CreateDb();
        const string userId = "learner-manual-survives";
        await SeedLearnerAsync(db, userId);
        SeedReadingPro(db);
        await db.SaveChangesAsync();
        var (access, credits) = CreateServices(db);

        await credits.AdjustAsync(userId,
            new AiPackageCreditAdjustmentRequest(0, 0, 0, 0, 5, 0, null, "Manual grant +5 reading"),
            "admin", default);
        await access.GrantAddonAsync("admin", "Admin", userId,
            new AdminUserAccessAddonRequest("pkg_reading_pro", null, 1), default);
        Assert.Null((await credits.GetSnapshotAsync(userId, 0, default)).ReadingTestsRemaining);

        await access.RemoveAddonAsync("admin", "Admin", userId, "pkg_reading_pro", null, default);
        var after = await credits.GetSnapshotAsync(userId, 20, default);

        Assert.Equal(5, after.ReadingTestsRemaining);
        Assert.True(await credits.HasObjectivePracticeAllowanceAsync(userId, "reading", default));
        Assert.True(after.SharedCredits >= 0);
        Assert.True(after.FlexibleCredits >= 0);
        Assert.True(after.WritingOnlyCredits >= 0);
        Assert.True(after.SpeakingOnlyCredits >= 0);
        Assert.True(after.MockExamsRemaining >= 0);
        Assert.True((after.ListeningTestsRemaining ?? 0) >= 0);
    }

    [Fact]
    public async Task DoubleRemovePackage_IsIdempotent_NoNegativeBalances()
    {
        await using var db = CreateDb();
        const string userId = "learner-double-remove";
        await SeedLearnerAsync(db, userId);
        SeedCoursePlan(db);
        SeedReadingPro(db);
        SeedListeningPro(db);
        await db.SaveChangesAsync();
        var (access, credits) = CreateServices(db);

        // Standalone listening pack first (no course package yet → hidden
        // standalone container): an independent source that must survive the
        // course-package removal below.
        await access.GrantAddonAsync("admin", "Admin", userId,
            new AdminUserAccessAddonRequest("pkg_listening_pro", null, 1), default);
        var granted = await access.GrantPackageAsync("admin", "Admin", userId,
            new AdminUserAccessPackageRequest("full-condensed-medicine", null, null, true, false, false), default);
        var subscriptionId = granted.Subscriptions.Single().Id;
        await access.GrantAddonAsync("admin", "Admin", userId,
            new AdminUserAccessAddonRequest("pkg_reading_pro", subscriptionId, 1), default);

        await access.RemovePackageAsync("admin", "Admin", userId, subscriptionId, default);
        var first = await credits.GetSnapshotAsync(userId, 0, default);
        // Standalone listening pack is an independent source and must survive.
        Assert.Equal(0, first.ReadingTestsRemaining);
        Assert.Null(first.ListeningTestsRemaining);

        await access.RemovePackageAsync("admin", "Admin", userId, subscriptionId, default);
        var second = await credits.GetSnapshotAsync(userId, 0, default);
        Assert.Equal(0, second.ReadingTestsRemaining);
        Assert.Null(second.ListeningTestsRemaining);
        Assert.True(second.SharedCredits >= 0);
        Assert.True(second.MockExamsRemaining >= 0);
    }

    [Fact]
    public async Task HasAllowance_DeniesGhostNullPoolWithoutLiveLot()
    {
        await using var db = CreateDb();
        const string userId = "learner-ghost-deny";
        await SeedLearnerAsync(db, userId);
        var now = DateTimeOffset.UtcNow;
        db.AiPackageCreditAccounts.Add(new AiPackageCreditAccount
        {
            Id = "aipkg-acct-ghost",
            UserId = userId,
            ListeningTestsRemaining = null,
            ReadingTestsRemaining = null,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        var credits = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);

        Assert.False(await credits.HasObjectivePracticeAllowanceAsync(userId, "reading", default));
        Assert.False(await credits.HasObjectivePracticeAllowanceAsync(userId, "listening", default));
        Assert.False((await credits.DeductObjectivePracticeAsync(userId, "reading", "ghost-deny-1", default)).Debited);
    }
}
