using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Writing Rule Enforcement Addendum Rev5 (10 Sep 2026), §12 + §14 mandatory
/// regression/acceptance tests for the "Practice this" gate.
///
/// <see cref="WritingEntitlementService.AuthorizeStartAsync"/> is the
/// canonical entitlement DECISION-and-CHARGE call now used by the
/// GET /v1/writing/scenarios/{id}/eligibility endpoint — the live
/// writing-v2 "Practice this" gate — replacing the direct
/// <c>AiPackageCreditService.DeductGradingCreditAsync</c> bypass that had no
/// free-tier fallback and could block a learner the Dashboard showed as
/// Allowed/Unlimited. (The legacy Writing-Tasks content-item flow,
/// <c>LearnerService.CreateWritingAttemptAsync</c>, was deliberately left on
/// its original gate — confirmed unreachable from any current frontend page
/// — see LearnerService.cs for why.)
///
/// Uses the REAL <see cref="AiPackageCreditService"/> (via
/// <see cref="AiPackageCreditService.GrantPackageAsync"/>, mirroring
/// AiPackageCreditServiceTests) so the finite-balance and idempotent-dedupe
/// assertions exercise the actual ledger, not a stub.
/// </summary>
public sealed class WritingEntitlementAuthorizeStartTests
{
    private static LearnerDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LearnerDbContext(options);
    }

    private static (WritingEntitlementService entitlement, AiPackageCreditService credits, WritingOptionsProvider options) BuildServices(LearnerDbContext db)
    {
        var credits = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        var options = new WritingOptionsProvider(db, new MemoryCache(new MemoryCacheOptions()));
        var resolver = new EffectiveEntitlementResolver(db);
        var entitlement = new WritingEntitlementService(db, resolver, options, credits);
        return (entitlement, credits, options);
    }

    private static BillingAddOn AddOn(string code, int durationDays, int grantCredits, string grantJson)
        => new()
        {
            Id = $"addon_{code}",
            Code = code,
            Name = code,
            Price = 1m,
            Currency = "GBP",
            Interval = "one_time",
            Status = BillingAddOnStatus.Active,
            DurationDays = durationDays,
            GrantCredits = grantCredits,
            GrantEntitlementsJson = grantJson,
            AddonKind = "ai_package",
            AppliesToAllPlans = true,
            IsStackable = true,
            QuantityStep = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>
    /// Mirrors AiPackageCreditServiceTests.OetMastery_BypassesWritingAndSpeakingOnlyWhilePurchaseItemIsActive:
    /// GrantPackageAsync alone only credits the ledger pools; the "unlimited"
    /// signal (AiPackageCreditService.HasActiveUnlimitedGradingAsync) comes
    /// from an active pkg_oet_mastery SubscriptionItem, seeded the same way
    /// the real checkout/webhook flow creates one.
    /// </summary>
    private static async Task SeedActiveMasterySubscriptionAsync(LearnerDbContext db, string userId)
    {
        var now = DateTimeOffset.UtcNow;
        var subId = $"sub-mastery-{userId}";
        db.Subscriptions.Add(new Subscription
        {
            Id = subId,
            UserId = userId,
            PlanId = "plan-free",
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddDays(180),
            ExpiresAt = now.AddDays(180),
            PriceAmount = 0,
            Currency = "GBP",
            Interval = "one_time",
        });
        db.SubscriptionItems.Add(new SubscriptionItem
        {
            Id = $"item-mastery-{userId}",
            SubscriptionId = subId,
            ItemCode = "pkg_oet_mastery",
            ItemType = "addon",
            Status = SubscriptionItemStatus.Active,
            StartsAt = now,
            EndsAt = now.AddDays(180),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task UnlimitedGrant_AllowsWithoutCharging()
    {
        await using var db = NewContext();
        var (entitlement, credits, _) = BuildServices(db);
        await credits.GrantPackageAsync(
            "learner-unlimited",
            AddOn("pkg_oet_mastery", 180, 0, """{"package_type":"full","unlimited_grading":true,"listening_tests":null,"reading_tests":null}"""),
            1, "cs_mastery_unlimited", null, CancellationToken.None);
        await SeedActiveMasterySubscriptionAsync(db, "learner-unlimited");

        var result = await entitlement.AuthorizeStartAsync("learner-unlimited", "ref-unlimited-1", "scenario-1", CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.False(result.Charged);
        Assert.Equal("unlimited", result.EntitlementSource);
        Assert.Empty(db.AiPackageCreditTransactions.Where(t => t.Reason == AiPackageCreditReason.GradingDeduct));
    }

    [Fact]
    public async Task FiniteBalance_DeductsExactlyOneActivityAndAllows()
    {
        await using var db = NewContext();
        var (entitlement, credits, _) = BuildServices(db);
        // 6 writing-only credits = 3 activities at 2 credits each.
        await credits.GrantPackageAsync(
            "learner-finite",
            AddOn("pkg_writing_starter", 30, 3, """{"package_type":"writing","writing_only_credits":6}"""),
            1, "cs_writing_starter", null, CancellationToken.None);

        var result = await entitlement.AuthorizeStartAsync("learner-finite", "ref-finite-1", "scenario-2", CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.True(result.Charged);
        Assert.Equal("ai_package", result.EntitlementSource);
        var deducts = await db.AiPackageCreditTransactions
            .Where(t => t.Reason == AiPackageCreditReason.GradingDeduct)
            .ToListAsync();
        Assert.Single(deducts);
        Assert.Equal(2, deducts[0].WritingOnlyCreditsDelta * -1); // 2 raw credits spent for 1 activity

        var snapshot = await credits.GetSnapshotAsync("learner-finite", 0, CancellationToken.None);
        Assert.Equal(4, snapshot.WritingOnlyCredits); // 6 - 2
    }

    [Fact]
    public async Task NoCreditsAndFreeTierDisabled_BlocksBeforeAnyDeduction()
    {
        await using var db = NewContext();
        var (entitlement, _, _) = BuildServices(db);

        var result = await entitlement.AuthorizeStartAsync("learner-blocked", "ref-blocked-1", "scenario-3", CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.False(result.Charged);
        Assert.NotNull(result.ErrorMessage);
        Assert.Empty(db.AiPackageCreditTransactions);
    }

    [Fact]
    public async Task DoubleTapSameReferenceId_DeductsAtMostOnce()
    {
        await using var db = NewContext();
        var (entitlement, credits, _) = BuildServices(db);
        await credits.GrantPackageAsync(
            "learner-retry",
            AddOn("pkg_writing_starter", 30, 3, """{"package_type":"writing","writing_only_credits":6}"""),
            1, "cs_writing_starter_retry", null, CancellationToken.None);

        var first = await entitlement.AuthorizeStartAsync("learner-retry", "ref-same-attempt", "scenario-4", CancellationToken.None);
        var retry = await entitlement.AuthorizeStartAsync("learner-retry", "ref-same-attempt", "scenario-4", CancellationToken.None);

        Assert.True(first.Allowed);
        Assert.True(first.Charged);
        Assert.True(retry.Allowed); // resumes authorised, never blocks a legitimate retry

        var deducts = await db.AiPackageCreditTransactions
            .Where(t => t.Reason == AiPackageCreditReason.GradingDeduct && t.ReferenceId == "ref-same-attempt")
            .ToListAsync();
        Assert.Single(deducts); // exactly one charge across both calls

        var snapshot = await credits.GetSnapshotAsync("learner-retry", 0, CancellationToken.None);
        Assert.Equal(4, snapshot.WritingOnlyCredits); // only 2 spent, not 4
    }

    [Fact]
    public async Task FreeTierValid_AllowsEvenWithZeroAiPackageBalance()
    {
        // This is the confirmed P0 regression: a learner whose valid entitlement
        // comes from the free-tier window (not an AI package/subscription) must
        // be authorised here exactly as Dashboard (GET /v1/writing/entitlement)
        // already shows them — never blocked with "no_ai_package_credits" just
        // because the unrelated AI-package ledger balance is zero.
        await using var db = NewContext();
        var (entitlement, _, options) = BuildServices(db);
        await options.UpdateAsync(new WritingOptions
        {
            Id = "global",
            AiGradingEnabled = true,
            AiCoachEnabled = true,
            FreeTierEnabled = true,
            FreeTierLimit = 2,
            FreeTierWindowDays = 7,
        }, "admin-1", CancellationToken.None);

        var result = await entitlement.AuthorizeStartAsync("learner-free-tier", "ref-free-1", "scenario-5", CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.False(result.Charged);
        Assert.Equal("free_tier", result.EntitlementSource);
        Assert.Empty(db.AiPackageCreditTransactions);
    }

    /// <summary>
    /// §12.4 regression: "Practice this again" after a completed submission
    /// is a genuinely new attempt and must charge a NEW finite credit, while
    /// every retry/refresh/duplicate start before that submission is graded
    /// must keep resuming the SAME attempt for free. Exercises the real
    /// <see cref="WritingEntitlementService.BuildScenarioStartReferenceIdAsync"/>
    /// scheme end to end: (a) first start charges, (b) a pre-grading retry
    /// recomputes the same reference and does not charge again, (c) the
    /// attempt's submission is graded, (d) starting again recomputes a new
    /// reference and charges a genuinely new credit.
    /// </summary>
    [Fact]
    public async Task PracticeThisAgain_AfterGradedSubmission_ChargesASecondNewCredit()
    {
        await using var db = NewContext();
        var (entitlement, credits, _) = BuildServices(db);
        await credits.GrantPackageAsync(
            "learner-practice-again",
            AddOn("pkg_writing_starter", 30, 3, """{"package_type":"writing","writing_only_credits":6}"""),
            1, "cs_writing_starter_practice_again", null, CancellationToken.None);
        var scenarioId = Guid.NewGuid();

        // (a) start attempt 1 — charges one credit.
        var refA = await entitlement.BuildScenarioStartReferenceIdAsync("learner-practice-again", scenarioId, CancellationToken.None);
        var start1 = await entitlement.AuthorizeStartAsync("learner-practice-again", refA, scenarioId.ToString("D"), CancellationToken.None);
        Assert.True(start1.Allowed);
        Assert.True(start1.Charged);

        // (b) retry/refresh BEFORE the submission is graded — same reference, resumes, no second charge.
        var refB = await entitlement.BuildScenarioStartReferenceIdAsync("learner-practice-again", scenarioId, CancellationToken.None);
        Assert.Equal(refA, refB);
        var retry = await entitlement.AuthorizeStartAsync("learner-practice-again", refB, scenarioId.ToString("D"), CancellationToken.None);
        Assert.True(retry.Allowed);
        Assert.Single(await db.AiPackageCreditTransactions
            .Where(t => t.Reason == AiPackageCreditReason.GradingDeduct)
            .ToListAsync());

        // (c) attempt 1's submission reaches terminal (graded) state.
        db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = Guid.NewGuid(),
            UserId = "learner-practice-again",
            ScenarioId = scenarioId,
            Mode = "practice",
            LetterContent = "Dear Doctor, ...",
            LetterContentHash = "hash-attempt-1",
            Status = WritingSubmissionStatuses.Graded,
            WordCount = 180,
            TimeSpentSeconds = 900,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-15),
            SubmittedAt = DateTimeOffset.UtcNow,
            InputSource = "editor",
            CaseNoteHighlightsJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        // (d) "Practice this again" on the same scenario — new reference, new charge, new attempt.
        var refD = await entitlement.BuildScenarioStartReferenceIdAsync("learner-practice-again", scenarioId, CancellationToken.None);
        Assert.NotEqual(refA, refD);
        var again = await entitlement.AuthorizeStartAsync("learner-practice-again", refD, scenarioId.ToString("D"), CancellationToken.None);
        Assert.True(again.Allowed);
        Assert.True(again.Charged);

        var deducts = await db.AiPackageCreditTransactions
            .Where(t => t.Reason == AiPackageCreditReason.GradingDeduct)
            .ToListAsync();
        Assert.Equal(2, deducts.Count); // one for attempt 1, one for the new "Practice this again" attempt

        var snapshot = await credits.GetSnapshotAsync("learner-practice-again", 0, CancellationToken.None);
        Assert.Equal(2, snapshot.WritingOnlyCredits); // 6 - 2 - 2
    }

    [Fact]
    public async Task AllowedStart_PersistsAttemptLevelBillingRecord()
    {
        await using var db = NewContext();
        var (entitlement, credits, _) = BuildServices(db);
        await credits.GrantPackageAsync(
            "learner-audit",
            AddOn("pkg_writing_starter", 30, 3, """{"package_type":"writing","writing_only_credits":6}"""),
            1, "cs_writing_starter_audit", null, CancellationToken.None);

        await entitlement.AuthorizeStartAsync("learner-audit", "ref-audit-1", "scenario-6", CancellationToken.None);

        var evt = await db.AnalyticsEvents.SingleAsync(e => e.EventName == "writing_practice_start_authorized" && e.UserId == "learner-audit");
        Assert.Contains("ref-audit-1", evt.PayloadJson);
        Assert.Contains("scenario-6", evt.PayloadJson);
        Assert.Contains("ai_package", evt.PayloadJson);
    }
}
