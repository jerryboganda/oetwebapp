using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Tests;

public sealed class AiPackageCreditServiceTests
{
    private static LearnerDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LearnerDbContext(options);
    }

    private static AiPackageCreditService NewService(LearnerDbContext db)
        => new(db, NullLogger<AiPackageCreditService>.Instance);

    private static BillingAddOn AddOn(
        string code,
        int durationDays,
        int grantCredits,
        string grantJson,
        decimal price = 1m) => new()
        {
            Id = $"addon_{code}",
            Code = code,
            Name = code,
            Price = price,
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

    [Fact]
    public async Task GrantPackage_ProjectsFullPackagePoolsAndUnlimitedObjectivePractice()
    {
        await using var db = NewContext();
        var service = NewService(db);
        var addOn = AddOn(
            "pkg_exam_prep_pro",
            90,
            15,
            """{"package_type":"full","flexible_credits":15,"listening_tests":null,"reading_tests":null,"mock_exams":2}""");

        var snapshot = await service.GrantPackageAsync("learner-1", addOn, 1, "cs_1", "quote-1", CancellationToken.None);

        Assert.Equal(15, snapshot.FlexibleCredits);
        Assert.Null(snapshot.ListeningTestsRemaining);
        Assert.Null(snapshot.ReadingTestsRemaining);
        Assert.Equal(2, snapshot.MockExamsRemaining);
        Assert.True(snapshot.ExpiresAt > DateTimeOffset.UtcNow.AddDays(89));
    }

    [Fact]
    public async Task GrantPackage_IsIdempotentByStripeSessionAndUsesLaterExpiry()
    {
        await using var db = NewContext();
        var service = NewService(db);
        var starter = AddOn("pkg_writing_starter", 30, 3, """{"package_type":"writing","writing_only_credits":3}""");
        var pro = AddOn("pkg_writing_pro", 180, 15, """{"package_type":"writing","writing_only_credits":15}""");

        await service.GrantPackageAsync("learner-1", starter, 1, "cs_same", "quote-1", CancellationToken.None);
        var duplicate = await service.GrantPackageAsync("learner-1", starter, 1, "cs_same", "quote-1", CancellationToken.None);
        var upgraded = await service.GrantPackageAsync("learner-1", pro, 1, "cs_2", "quote-2", CancellationToken.None);

        Assert.Equal(3, duplicate.WritingOnlyCredits);
        Assert.Equal(18, upgraded.WritingOnlyCredits);
        Assert.True(upgraded.ExpiresAt > DateTimeOffset.UtcNow.AddDays(179));
        Assert.Equal(2, upgraded.Transactions.Count(tx => tx.Reason == nameof(AiPackageCreditReason.Purchase)));
    }

    [Fact]
    public async Task DeductGradingCredit_UsesSubtestSpecificPoolBeforeFlexibleAndRefundRestoresIt()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_quick_check", 30, 5, """{"package_type":"full","flexible_credits":5}"""), 1, "cs_full", null, CancellationToken.None);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_writing_starter", 30, 3, """{"package_type":"writing","writing_only_credits":3}"""), 1, "cs_writing", null, CancellationToken.None);

        var debit = await service.DeductGradingCreditAsync("learner-1", "writing", "we-1", CancellationToken.None);
        var afterDebit = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);
        var refunded = await service.RefundAsync("learner-1", "we-1", "refund:we-1", "refund", CancellationToken.None);
        var afterRefund = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.True(debit.Debited);
        Assert.Equal(2, afterDebit.WritingOnlyCredits);
        Assert.Equal(5, afterDebit.FlexibleCredits);
        Assert.True(refunded);
        Assert.Equal(3, afterRefund.WritingOnlyCredits);
        Assert.Equal(5, afterRefund.FlexibleCredits);
    }

    [Fact]
    public async Task WritingSubmission_DeductsOneCredit_FromWritingPoolFirst()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_writing_starter", 30, 3, """{"package_type":"writing","writing_only_credits":3}"""), 1, "cs_writing", null, CancellationToken.None);

        var debit = await service.DeductGradingCreditAsync("learner-1", "writing", "we-one-submission", 1, CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.True(debit.Debited);
        Assert.Equal("dedicated", debit.BalanceSource);
        Assert.Equal(2, snapshot.WritingOnlyCredits); // 3 - 1
        Assert.Equal(0, snapshot.FlexibleCredits);
    }

    [Fact]
    public async Task WritingStarter_ExactlyThreeAdvertisedLetters_ThenBlocked()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync(
            "learner-1",
            AddOn("pkg_writing_starter", 30, 3,
                """{"package_type":"writing","writing_only_credits":3,"writing_items":3}"""),
            1,
            "cs_writing_truthful",
            null,
            CancellationToken.None);

        var first = await service.DeductGradingCreditAsync("learner-1", "writing", "letter-1", 1, CancellationToken.None);
        var second = await service.DeductGradingCreditAsync("learner-1", "writing", "letter-2", 1, CancellationToken.None);
        var third = await service.DeductGradingCreditAsync("learner-1", "writing", "letter-3", 1, CancellationToken.None);
        var fourth = await service.DeductGradingCreditAsync("learner-1", "writing", "letter-4", 1, CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.True(first.Debited);
        Assert.True(second.Debited);
        Assert.True(third.Debited);
        Assert.False(fourth.Debited);
        Assert.Equal("no_ai_package_credits", fourth.ErrorCode);
        Assert.Equal(0, snapshot.WritingOnlyCredits);
    }

    [Fact]
    public async Task OetMastery_BypassesWritingAndSpeakingOnlyWhilePurchaseItemIsActive()
    {
        await using var db = NewContext();
        var service = NewService(db);
        var now = DateTimeOffset.UtcNow;
        await service.GrantPackageAsync(
            "learner-1",
            AddOn("pkg_oet_mastery", 180, 0,
                """{"package_type":"full","unlimited_grading":true,"listening_tests":null,"reading_tests":null}"""),
            1,
            "cs_mastery_unlimited",
            null,
            CancellationToken.None);
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-mastery",
            UserId = "learner-1",
            PlanId = "plan-free",
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddDays(180),
            ExpiresAt = now.AddDays(180),
            PriceAmount = 0,
            Currency = "GBP",
            Interval = "one_time"
        });
        var item = new SubscriptionItem
        {
            Id = "item-mastery",
            SubscriptionId = "sub-mastery",
            ItemCode = "pkg_oet_mastery",
            ItemType = "addon",
            Status = SubscriptionItemStatus.Active,
            StartsAt = now,
            EndsAt = now.AddDays(180),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.SubscriptionItems.Add(item);
        await db.SaveChangesAsync();

        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);
        Assert.True(snapshot.WritingUnlimited);
        Assert.True(snapshot.SpeakingUnlimited);
        Assert.Equal(0, snapshot.FlexibleCredits);
        Assert.Null(snapshot.ListeningTestsRemaining);
        Assert.Null(snapshot.ReadingTestsRemaining);

        var writing = await service.DeductGradingCreditAsync(
            "learner-1", "writing", "mastery-writing", 2, CancellationToken.None);
        var speaking = await service.CheckGradingCreditAsync(
            "learner-1", "speaking", CancellationToken.None);

        Assert.True(writing.Debited);
        Assert.True(speaking.Debited);
        Assert.Empty(await db.AiPackageCreditTransactions
            .Where(row => row.Reason == AiPackageCreditReason.GradingDeduct)
            .ToListAsync());

        var parent = await db.Subscriptions.SingleAsync(row => row.Id == "sub-mastery");
        parent.Status = SubscriptionStatus.Cancelled;
        await db.SaveChangesAsync();
        var parentRevoked = await service.CheckGradingCreditAsync(
            "learner-1", "writing", 2, CancellationToken.None);
        Assert.False(parentRevoked.Debited);
        Assert.Equal("no_ai_package_credits", parentRevoked.ErrorCode);

        parent.Status = SubscriptionStatus.Active;
        item.Status = SubscriptionItemStatus.Cancelled;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var revoked = await service.CheckGradingCreditAsync(
            "learner-1", "writing", 2, CancellationToken.None);
        Assert.False(revoked.Debited);
        Assert.Equal("no_ai_package_credits", revoked.ErrorCode);

        item.Status = SubscriptionItemStatus.Active;
        item.EndsAt = null;
        await db.SaveChangesAsync();

        // Live Mastery with a missing end date must still unlock Writing/Speaking,
        // matching Listening/Reading Recalc (EndsAt == null || EndsAt > now).
        var openEndedLiveItem = await service.CheckGradingCreditAsync(
            "learner-1", "writing", 2, CancellationToken.None);
        var openEndedSnapshot = await service.GetSnapshotAsync("learner-1", 0, CancellationToken.None);
        Assert.True(openEndedLiveItem.Debited);
        Assert.True(openEndedSnapshot.WritingUnlimited);
        Assert.True(openEndedSnapshot.SpeakingUnlimited);

        item.EndsAt = now.AddMinutes(-1);
        var account = await db.AiPackageCreditAccounts.SingleAsync(row => row.UserId == "learner-1");
        account.ExpiresAt = now.AddMinutes(-1);
        await db.SaveChangesAsync();

        var expired = await service.CheckGradingCreditAsync(
            "learner-1", "writing", 2, CancellationToken.None);
        Assert.False(expired.Debited);
        Assert.Equal("ai_package_expired", expired.ErrorCode);
    }

    [Fact]
    public async Task ObjectiveOnlyPurchase_IsNotTreatedAsLegacyUnlimitedGrading()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync(
            "learner-1",
            AddOn("pkg_listening_starter", 30, 0,
                """{"package_type":"listening","listening_tests":3}"""),
            1,
            "cs_listening_only",
            null,
            CancellationToken.None);

        var result = await service.CheckGradingCreditAsync(
            "learner-1", "writing", 2, CancellationToken.None);

        Assert.False(result.Debited);
        Assert.Equal("no_ai_package_credits", result.ErrorCode);
    }

    [Fact]
    public async Task WritingSubmission_FallsBackToFlexibleWs_WhenDedicatedExhausted()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_quick_check", 30, 5, """{"package_type":"full","flexible_credits":5}"""), 1, "cs_full", null, CancellationToken.None);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_writing_single", 30, 1, """{"package_type":"writing","writing_only_credits":1}"""), 1, "cs_writing", null, CancellationToken.None);

        var first = await service.DeductGradingCreditAsync("learner-1", "writing", "we-dedicated", 1, CancellationToken.None);
        var second = await service.DeductGradingCreditAsync("learner-1", "writing", "we-flexws", 1, CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.True(first.Debited);
        Assert.Equal("dedicated", first.BalanceSource);
        Assert.True(second.Debited);
        Assert.Equal("flexible_ws", second.BalanceSource); // restricted pool, 1 per submission
        Assert.Equal(0, snapshot.WritingOnlyCredits);
        Assert.Equal(4, snapshot.FlexibleCredits);
    }

    [Fact]
    public async Task WritingSubmission_AllOrNothing_WhenOnlyOneSharedCreditAvailable()
    {
        await using var db = NewContext();
        var service = NewService(db);
        // A single universal Shared credit cannot fund a graded submission (§1: Shared W/S rate = 2).
        await service.GrantCourseGiftCreditsAsync(
            "learner-1", "single-gift", "Single Gift", 1,
            "plan:one-shared:gift", null, CancellationToken.None);

        var debit = await service.DeductGradingCreditAsync("learner-1", "writing", "we-short", 1, CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.False(debit.Debited);
        Assert.Equal("no_ai_package_credits", debit.ErrorCode);
        Assert.Equal(1, snapshot.SharedCredits); // untouched — no partial debit
    }

    [Fact]
    public async Task CheckGradingCredit_SharedRate_RequiresTwoUniversalCredits()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantCourseGiftCreditsAsync(
            "learner-1", "tiny-gift", "Tiny Gift", 1,
            "plan:one:gift", null, CancellationToken.None);
        var withOneShared = await service.CheckGradingCreditAsync("learner-1", "writing", 2, CancellationToken.None);

        await service.GrantCourseGiftCreditsAsync(
            "learner-1", "second-gift", "Second Gift", 1,
            "plan:two:gift", null, CancellationToken.None);
        var withTwoShared = await service.CheckGradingCreditAsync("learner-1", "writing", 2, CancellationToken.None);

        Assert.False(withOneShared.Debited);
        Assert.True(withTwoShared.Debited);
    }

    [Fact]
    public async Task RefundGradingCredit_RestoresTheDedicatedCreditOfASubmission()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_writing_starter", 30, 3, """{"package_type":"writing","writing_only_credits":3}"""), 1, "cs_writing", null, CancellationToken.None);

        await service.DeductGradingCreditAsync("learner-1", "writing", "we-refundable", 1, CancellationToken.None);
        var refunded = await service.RefundAsync("learner-1", "we-refundable", "refund:we-refundable", "grading failed", CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.True(refunded);
        Assert.Equal(3, snapshot.WritingOnlyCredits); // 3 - 1 + 1
    }

    [Fact]
    public async Task ObjectiveAndMockAllowances_DeductFinitePoolsWithoutUsingAiCredits()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_listening_starter", 30, 0, """{"package_type":"listening","listening_tests":5}"""), 1, "cs_listening", null, CancellationToken.None);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_mock_1", 180, 0, """{"package_type":"mock","mock_exams":1}"""), 1, "cs_mock", null, CancellationToken.None);

        var listening = await service.DeductObjectivePracticeAsync("learner-1", "listening", "la-1", CancellationToken.None);
        var mock = await service.DeductMockAsync("learner-1", "mock-1", CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.True(listening.Debited);
        Assert.True(mock.Debited);
        Assert.Equal(4, snapshot.ListeningTestsRemaining);
        Assert.Equal(0, snapshot.MockExamsRemaining);
        Assert.Equal(0, snapshot.FlexibleCredits);
    }

    [Fact]
    public async Task ObjectivePractice_SamePaperReference_UnlocksPaperOnceAndReEntryIsFree()
    {
        // Paper is the billing unit: the first attempt on any part or the full
        // paper debits one test; every other part and every re-attempt of that
        // same paper reuses the per-(user, paper) reference and is free.
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_reading_starter", 30, 0, """{"package_type":"reading","reading_tests":5}"""), 1, "cs_reading", null, CancellationToken.None);

        var paperRef = CreditGateExtensions.ObjectivePaperReference("reading", "learner-1", "rp-1");
        var first = await service.DeductObjectivePracticeAsync("learner-1", "reading", paperRef, CancellationToken.None);
        var secondPartSamePaper = await service.DeductObjectivePracticeAsync("learner-1", "reading", paperRef, CancellationToken.None);
        var reAttemptSamePaper = await service.DeductObjectivePracticeAsync("learner-1", "reading", paperRef, CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.True(first.Debited);
        Assert.True(secondPartSamePaper.Debited); // allowed, not blocked
        Assert.True(reAttemptSamePaper.Debited);
        Assert.Equal(4, snapshot.ReadingTestsRemaining); // only ONE credit consumed for the paper
    }

    [Fact]
    public async Task ObjectivePractice_DifferentPapers_EachConsumeOneCredit()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_reading_starter", 30, 0, """{"package_type":"reading","reading_tests":5}"""), 1, "cs_reading", null, CancellationToken.None);

        await service.DeductObjectivePracticeAsync("learner-1", "reading", CreditGateExtensions.ObjectivePaperReference("reading", "learner-1", "rp-1"), CancellationToken.None);
        await service.DeductObjectivePracticeAsync("learner-1", "reading", CreditGateExtensions.ObjectivePaperReference("reading", "learner-1", "rp-2"), CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.Equal(3, snapshot.ReadingTestsRemaining); // two distinct papers => two credits
    }

    [Fact]
    public async Task ObjectivePractice_Reading_DeductsFromReadingPoolNotAiCredits()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_reading_starter", 30, 0, """{"package_type":"reading","reading_tests":5}"""), 1, "cs_reading", null, CancellationToken.None);

        var debit = await service.DeductObjectivePracticeAsync("learner-1", "reading", CreditGateExtensions.ObjectivePaperReference("reading", "learner-1", "rp-1"), CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.True(debit.Debited);
        Assert.Equal(4, snapshot.ReadingTestsRemaining);
        Assert.Equal(0, snapshot.FlexibleCredits);
    }

    [Fact]
    public async Task ObjectivePractice_WhenExhausted_BlocksNewPaperButAllowsAlreadyUnlockedPaper()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_reading_single", 30, 0, """{"package_type":"reading","reading_tests":1}"""), 1, "cs_reading", null, CancellationToken.None);

        var paperARef = CreditGateExtensions.ObjectivePaperReference("reading", "learner-1", "rp-A");
        var paperBRef = CreditGateExtensions.ObjectivePaperReference("reading", "learner-1", "rp-B");

        var unlockA = await service.DeductObjectivePracticeAsync("learner-1", "reading", paperARef, CancellationToken.None); // consumes the only credit
        var reEntryA = await service.DeductObjectivePracticeAsync("learner-1", "reading", paperARef, CancellationToken.None); // already unlocked => free
        var newPaperB = await service.DeductObjectivePracticeAsync("learner-1", "reading", paperBRef, CancellationToken.None); // no credits left => blocked

        Assert.True(unlockA.Debited);
        Assert.True(reEntryA.Debited);
        Assert.False(newPaperB.Debited);
        Assert.Equal("no_reading_tests", newPaperB.ErrorCode);
    }

    [Fact]
    public async Task RecordExamOutcome_WhenPassed_ExpiresAllActiveBalances()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_oet_mastery", 180, 30, """{"package_type":"full","flexible_credits":30,"listening_tests":null,"reading_tests":null,"mock_exams":5}"""), 1, "cs_mastery", null, CancellationToken.None);

        var snapshot = await service.RecordExamOutcomeAsync(
            "learner-1",
            new LearnerExamOutcomeRequest(true, DateTimeOffset.UtcNow, "official pass record"),
            "admin-1",
            "Admin One",
            CancellationToken.None);

        Assert.True(snapshot.ExpiredBecausePassed);
        Assert.Equal(0, snapshot.FlexibleCredits);
        Assert.Equal(0, snapshot.MockExamsRemaining);
        Assert.Equal(0, snapshot.ListeningTestsRemaining);
        Assert.Contains(snapshot.Transactions, tx => tx.Reason == nameof(AiPackageCreditReason.PassExpiry));
    }

    [Fact]
    public async Task FullCourseGift_SharedRates_Writing2Speaking2Listening1Reading1()
    {
        await using var db = NewContext();
        var service = NewService(db);
        var expiry = DateTimeOffset.UtcNow.AddDays(180);

        var granted = await service.GrantCourseGiftCreditsAsync(
            "learner-1",
            "full-condensed-medicine",
            "Full Condensed Recorded OET Course - Medicine",
            5,
            "plan:quote-1:full-condensed-medicine",
            expiry,
            CancellationToken.None);
        var duplicate = await service.GrantCourseGiftCreditsAsync(
            "learner-1",
            "full-condensed-medicine",
            "Full Condensed Recorded OET Course - Medicine",
            5,
            "plan:quote-1:full-condensed-medicine",
            expiry,
            CancellationToken.None);

        // §1 shared rates: Writing 2, Speaking 2, Listening 1, Reading 1.
        // With 5 shared credits the candidate can fund W + S (4) plus one
        // deterministic subtest (1); the next graded subtest is blocked.
        var writing = await service.DeductGradingCreditAsync(
            "learner-1", "writing", "gift-writing", 1, CancellationToken.None);
        var speaking = await service.DeductGradingCreditAsync(
            "learner-1", "speaking", "gift-speaking", 1, CancellationToken.None);
        var reading = await service.DeductObjectivePracticeAsync(
            "learner-1", "reading", "gift-reading-paper", CancellationToken.None);
        var extraWriting = await service.DeductGradingCreditAsync(
            "learner-1", "writing", "gift-writing-2", 1, CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.True(granted);
        Assert.False(duplicate);
        Assert.True(writing.Debited);
        Assert.Equal("shared", writing.BalanceSource);
        Assert.True(speaking.Debited);
        Assert.True(reading.Debited);
        Assert.False(extraWriting.Debited);
        Assert.Equal("no_ai_package_credits", extraWriting.ErrorCode);
        Assert.Equal(0, snapshot.FlexibleCredits);
        Assert.Equal(0, snapshot.SharedCredits);
        Assert.Equal(5, snapshot.CreditsGranted);
        Assert.Equal(5, snapshot.CreditsUsed);
        Assert.Equal(0, snapshot.CreditsRemaining);
    }

    [Fact]
    public async Task GrantPackage_WritingStarter_DoesNotChangeSpeakingCredits()
    {
        await using var db = NewContext();
        var service = NewService(db);

        var snapshot = await service.GrantPackageAsync(
            "learner-1",
            AddOn("pkg_writing_starter", 30, 3,
                """{"package_type":"writing","writing_only_credits":6,"writing_items":3,"listening_tests":0,"reading_tests":0}"""),
            1,
            "cs_writing_only",
            null,
            CancellationToken.None);

        Assert.Equal(6, snapshot.WritingOnlyCredits);
        Assert.Equal(0, snapshot.SpeakingOnlyCredits);
        Assert.Equal(0, snapshot.FlexibleCredits);
        Assert.False(snapshot.WritingUnlimited);
        Assert.False(snapshot.SpeakingUnlimited);
    }

    [Fact]
    public async Task GrantPackage_SpeakingStarter_DoesNotChangeWritingCredits()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync(
            "learner-1",
            AddOn("pkg_writing_starter", 30, 3,
                """{"package_type":"writing","writing_only_credits":6}"""),
            1,
            "cs_writing_first",
            null,
            CancellationToken.None);

        var snapshot = await service.GrantPackageAsync(
            "learner-1",
            AddOn("pkg_speaking_starter", 30, 3,
                """{"package_type":"speaking","speaking_only_credits":3,"speaking_items":3,"listening_tests":0,"reading_tests":0}"""),
            1,
            "cs_speaking_only",
            null,
            CancellationToken.None);

        Assert.Equal(6, snapshot.WritingOnlyCredits);
        Assert.Equal(3, snapshot.SpeakingOnlyCredits);
        Assert.Equal(0, snapshot.FlexibleCredits);
    }

    [Fact]
    public async Task GrantPackage_QuickCheck_AppliesConfiguredListeningReadingAndFlexible()
    {
        await using var db = NewContext();
        var service = NewService(db);

        var snapshot = await service.GrantPackageAsync(
            "learner-1",
            AddOn("pkg_quick_check", 30, 5,
                """{"package_type":"full","flexible_credits":5,"listening_tests":3,"reading_tests":3}"""),
            1,
            "cs_quick_check",
            null,
            CancellationToken.None);

        Assert.Equal(5, snapshot.FlexibleCredits);
        Assert.Equal(0, snapshot.WritingOnlyCredits);
        Assert.Equal(0, snapshot.SpeakingOnlyCredits);
        Assert.Equal(3, snapshot.ListeningTestsRemaining);
        Assert.Equal(3, snapshot.ReadingTestsRemaining);
        Assert.False(snapshot.WritingUnlimited);
        Assert.False(snapshot.SpeakingUnlimited);
    }

    [Fact]
    public async Task GrantPackage_ExamPrepPro_AppliesConfiguredListeningReadingAndFlexible()
    {
        await using var db = NewContext();
        var service = NewService(db);

        var snapshot = await service.GrantPackageAsync(
            "learner-1",
            AddOn("pkg_exam_prep_pro", 90, 15,
                """{"package_type":"full","flexible_credits":15,"listening_tests":6,"reading_tests":6}"""),
            1,
            "cs_exam_prep_pro",
            null,
            CancellationToken.None);

        Assert.Equal(15, snapshot.FlexibleCredits);
        Assert.Equal(0, snapshot.WritingOnlyCredits);
        Assert.Equal(0, snapshot.SpeakingOnlyCredits);
        Assert.Equal(6, snapshot.ListeningTestsRemaining);
        Assert.Equal(6, snapshot.ReadingTestsRemaining);
        Assert.Equal(0, snapshot.MockExamsRemaining);
    }

    [Fact]
    public async Task GrantPackage_OetMastery_IgnoresLegacyFlexibleGrantCredits()
    {
        await using var db = NewContext();
        var service = NewService(db);

        var snapshot = await service.GrantPackageAsync(
            "learner-1",
            AddOn("pkg_oet_mastery", 180, 30,
                """{"package_type":"full","unlimited_grading":true,"flexible_credits":30,"listening_tests":null,"reading_tests":null}"""),
            1,
            "cs_mastery_legacy_flexible",
            null,
            CancellationToken.None);

        Assert.Equal(0, snapshot.FlexibleCredits);
        Assert.Equal(0, snapshot.WritingOnlyCredits);
        Assert.Equal(0, snapshot.SpeakingOnlyCredits);
        Assert.Null(snapshot.ListeningTestsRemaining);
        Assert.Null(snapshot.ReadingTestsRemaining);
        Assert.False(snapshot.WritingUnlimited);
        Assert.False(snapshot.SpeakingUnlimited);
    }

    [Fact]
    public async Task RecalculateObjectiveAllowances_WhenNoActivePacks_ClearsUnlimitedListeningReading()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync(
            "learner-1",
            AddOn("pkg_oet_mastery", 180, 0,
                """{"package_type":"full","unlimited_grading":true,"listening_tests":null,"reading_tests":null}"""),
            1,
            "cs_mastery_unlimited",
            null,
            CancellationToken.None);

        await service.RecalculateObjectiveAllowancesAsync("learner-1", CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 0, CancellationToken.None);

        Assert.Equal(0, snapshot.ListeningTestsRemaining);
        Assert.Equal(0, snapshot.ReadingTestsRemaining);
    }

    [Fact]
    public async Task RecalculateObjectiveAllowances_ReversesOrphanedCourseGiftWhenNoLivePlan()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantCourseGiftCreditsAsync(
            "learner-1",
            "full-condensed-medicine",
            "Medicine",
            5,
            "admin-package:sub-gone:full-condensed-medicine",
            DateTimeOffset.UtcNow.AddDays(180),
            CancellationToken.None);

        await service.RecalculateObjectiveAllowancesAsync("learner-1", CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.Equal(0, snapshot.FlexibleCredits);
        Assert.Equal(0, snapshot.CreditsGranted);
        Assert.Equal(0, snapshot.CreditsRemaining);
    }
}
