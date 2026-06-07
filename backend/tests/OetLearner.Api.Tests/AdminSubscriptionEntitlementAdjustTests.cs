using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

/// <summary>
/// Service-level tests for <see cref="AdminService.AdjustSubscriptionEntitlementsAsync"/> —
/// the admin "inspect and correct entitlements" surface. Each non-null request field is an
/// absolute SET (counters clamp to &gt;= 0); a null field is left unchanged; Reason is
/// required; and every successful mutation writes exactly one matching
/// <see cref="AuditEvent"/>. Mirrors the harness in
/// <see cref="AdminSubscriptionLifecycleTests"/>.
/// </summary>
public class AdminSubscriptionEntitlementAdjustTests
{
    private const string AdminId = "adm-001";
    private const string AdminName = "Admin Tester";
    private const string LearnerId = "usr-001";

    [Fact]
    public async Task AdjustEntitlements_SetsEachCounterAndFlag_Absolute()
    {
        await using var db = NewDb();
        var sub = await SeedSubscriptionAsync(db);
        var service = NewAdminService(db);
        var auditCountBefore = await db.AuditEvents.CountAsync();

        var result = await service.AdjustSubscriptionEntitlementsAsync(
            AdminId, AdminName, sub.Id,
            new AdminSubscriptionEntitlementAdjustRequest(
                WritingAssessmentsRemaining: 7,
                SpeakingSessionsRemaining: 3,
                AiCreditsRemaining: 42,
                TutorBookUnlocked: true,
                BasicEnglishUnlocked: true,
                Reason: "support correction"),
            CancellationToken.None);

        Assert.NotNull(result);

        var refreshed = await db.Subscriptions.SingleAsync();
        Assert.Equal(7, refreshed.WritingAssessmentsRemaining);
        Assert.Equal(3, refreshed.SpeakingSessionsRemaining);
        Assert.Equal(42, refreshed.AiCreditsRemaining);
        Assert.True(refreshed.TutorBookUnlocked);
        Assert.True(refreshed.BasicEnglishUnlocked);

        await AssertOneNewAuditAsync(db, auditCountBefore, "SubscriptionEntitlementsAdjusted", sub.Id);
    }

    [Fact]
    public async Task AdjustEntitlements_NegativeCounters_ClampToZero()
    {
        await using var db = NewDb();
        var sub = await SeedSubscriptionAsync(db, writing: 5, speaking: 5, aiCredits: 5);
        var service = NewAdminService(db);

        await service.AdjustSubscriptionEntitlementsAsync(
            AdminId, AdminName, sub.Id,
            new AdminSubscriptionEntitlementAdjustRequest(
                WritingAssessmentsRemaining: -10,
                SpeakingSessionsRemaining: -1,
                AiCreditsRemaining: -100,
                TutorBookUnlocked: null,
                BasicEnglishUnlocked: null,
                Reason: "clamp negatives"),
            CancellationToken.None);

        var refreshed = await db.Subscriptions.SingleAsync();
        Assert.Equal(0, refreshed.WritingAssessmentsRemaining);
        Assert.Equal(0, refreshed.SpeakingSessionsRemaining);
        Assert.Equal(0, refreshed.AiCreditsRemaining);
    }

    [Fact]
    public async Task AdjustEntitlements_PartialUpdate_LeavesUnspecifiedFieldsUnchanged()
    {
        await using var db = NewDb();
        var sub = await SeedSubscriptionAsync(
            db, writing: 4, speaking: 6, aiCredits: 8, tutorBook: true, basicEnglish: false);
        var service = NewAdminService(db);

        // Only set AiCreditsRemaining; everything else must be untouched.
        await service.AdjustSubscriptionEntitlementsAsync(
            AdminId, AdminName, sub.Id,
            new AdminSubscriptionEntitlementAdjustRequest(
                WritingAssessmentsRemaining: null,
                SpeakingSessionsRemaining: null,
                AiCreditsRemaining: 99,
                TutorBookUnlocked: null,
                BasicEnglishUnlocked: null,
                Reason: "top up credits only"),
            CancellationToken.None);

        var refreshed = await db.Subscriptions.SingleAsync();
        Assert.Equal(4, refreshed.WritingAssessmentsRemaining); // unchanged
        Assert.Equal(6, refreshed.SpeakingSessionsRemaining);   // unchanged
        Assert.Equal(99, refreshed.AiCreditsRemaining);         // set
        Assert.True(refreshed.TutorBookUnlocked);               // unchanged
        Assert.False(refreshed.BasicEnglishUnlocked);           // unchanged
    }

    [Fact]
    public async Task AdjustEntitlements_CanLockFlags_WhenFalseProvided()
    {
        await using var db = NewDb();
        var sub = await SeedSubscriptionAsync(db, tutorBook: true, basicEnglish: true);
        var service = NewAdminService(db);

        await service.AdjustSubscriptionEntitlementsAsync(
            AdminId, AdminName, sub.Id,
            new AdminSubscriptionEntitlementAdjustRequest(
                WritingAssessmentsRemaining: null,
                SpeakingSessionsRemaining: null,
                AiCreditsRemaining: null,
                TutorBookUnlocked: false,
                BasicEnglishUnlocked: false,
                Reason: "revoke unlocks"),
            CancellationToken.None);

        var refreshed = await db.Subscriptions.SingleAsync();
        Assert.False(refreshed.TutorBookUnlocked);
        Assert.False(refreshed.BasicEnglishUnlocked);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AdjustEntitlements_MissingReason_Throws(string reason)
    {
        await using var db = NewDb();
        var sub = await SeedSubscriptionAsync(db);
        var service = NewAdminService(db);
        var auditCountBefore = await db.AuditEvents.CountAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.AdjustSubscriptionEntitlementsAsync(
            AdminId, AdminName, sub.Id,
            new AdminSubscriptionEntitlementAdjustRequest(
                WritingAssessmentsRemaining: 1,
                SpeakingSessionsRemaining: null,
                AiCreditsRemaining: null,
                TutorBookUnlocked: null,
                BasicEnglishUnlocked: null,
                Reason: reason),
            CancellationToken.None));

        Assert.Equal("reason_required", ex.ErrorCode);
        // No mutation, no audit row.
        Assert.Equal(auditCountBefore, await db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task AdjustEntitlements_WritesAuditWithBeforeAndAfterSnapshot()
    {
        await using var db = NewDb();
        var sub = await SeedSubscriptionAsync(db, aiCredits: 2);
        var service = NewAdminService(db);

        await service.AdjustSubscriptionEntitlementsAsync(
            AdminId, AdminName, sub.Id,
            new AdminSubscriptionEntitlementAdjustRequest(
                WritingAssessmentsRemaining: null,
                SpeakingSessionsRemaining: null,
                AiCreditsRemaining: 10,
                TutorBookUnlocked: null,
                BasicEnglishUnlocked: null,
                Reason: "ledger fix"),
            CancellationToken.None);

        var audit = await db.AuditEvents.SingleAsync(a =>
            a.ResourceType == "Subscription" && a.Action == "SubscriptionEntitlementsAdjusted");
        Assert.Equal(sub.Id, audit.ResourceId);
        Assert.Equal(AdminId, audit.ActorId);
        Assert.Contains("ledger fix", audit.Details);
        Assert.Contains("before", audit.Details);
        Assert.Contains("after", audit.Details);
        // before snapshot carries the original value (2), after carries the new value (10).
        Assert.Contains("\"AiCreditsRemaining\":2", audit.Details);
        Assert.Contains("\"AiCreditsRemaining\":10", audit.Details);
    }

    [Fact]
    public async Task AdjustEntitlements_UnknownSubscription_Throws404()
    {
        await using var db = NewDb();
        await SeedSubscriptionAsync(db);
        var service = NewAdminService(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.AdjustSubscriptionEntitlementsAsync(
            AdminId, AdminName, "sub-does-not-exist",
            new AdminSubscriptionEntitlementAdjustRequest(
                WritingAssessmentsRemaining: 1,
                SpeakingSessionsRemaining: null,
                AiCreditsRemaining: null,
                TutorBookUnlocked: null,
                BasicEnglishUnlocked: null,
                Reason: "no such sub"),
            CancellationToken.None));

        Assert.Equal("subscription_not_found", ex.ErrorCode);
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
        // The entitlement-adjust method only touches `db` and `DateTimeOffset.UtcNow`;
        // the other primary-constructor parameters are not invoked by this code path,
        // so passing null! is safe and keeps these tests narrowly scoped (mirrors
        // AdminSubscriptionLifecycleTests).
        return new AdminService(
            db,
            emailOtpService: null!,
            passwordHasher: null!,
            passwordPolicyService: null!,
            timeProvider: TimeProvider.System,
            notifications: null!,
            learnerService: null!);
    }

    private static async Task<Subscription> SeedSubscriptionAsync(
        LearnerDbContext db,
        int writing = 0,
        int speaking = 0,
        int aiCredits = 0,
        bool tutorBook = false,
        bool basicEnglish = false)
    {
        var now = DateTimeOffset.UtcNow;
        var sub = new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = LearnerId,
            PlanId = "basic",
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-30),
            ChangedAt = now.AddDays(-30),
            NextRenewalAt = now.AddDays(15),
            PriceAmount = 29.00m,
            Currency = "USD",
            Interval = "month",
            WritingAssessmentsRemaining = writing,
            SpeakingSessionsRemaining = speaking,
            AiCreditsRemaining = aiCredits,
            TutorBookUnlocked = tutorBook,
            BasicEnglishUnlocked = basicEnglish,
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();
        return sub;
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
