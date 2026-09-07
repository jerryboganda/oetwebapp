using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
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
            """{"package_type":"full","flexible_credits":30,"listening_tests":null,"reading_tests":null,"mock_exams":2}""");

        var snapshot = await service.GrantPackageAsync("learner-1", addOn, 1, "cs_1", "quote-1", CancellationToken.None);

        Assert.Equal(30, snapshot.FlexibleCredits);
        Assert.Equal(0, snapshot.SharedCredits);
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
        var starter = AddOn("pkg_writing_starter", 30, 3, """{"package_type":"writing","writing_only_credits":6}""");
        var pro = AddOn("pkg_writing_pro", 180, 15, """{"package_type":"writing","writing_only_credits":30}""");

        await service.GrantPackageAsync("learner-1", starter, 1, "cs_same", "quote-1", CancellationToken.None);
        var duplicate = await service.GrantPackageAsync("learner-1", starter, 1, "cs_same", "quote-1", CancellationToken.None);
        var upgraded = await service.GrantPackageAsync("learner-1", pro, 1, "cs_2", "quote-2", CancellationToken.None);

        Assert.Equal(6, duplicate.WritingOnlyCredits);
        Assert.Equal(36, upgraded.WritingOnlyCredits);
        Assert.True(upgraded.ExpiresAt > DateTimeOffset.UtcNow.AddDays(179));
        Assert.Equal(2, upgraded.Transactions.Count(tx => tx.Reason == nameof(AiPackageCreditReason.Purchase)));
    }

    [Fact]
    public async Task DeductGradingCredit_UsesSubtestSpecificPoolBeforeFlexibleAndRefundRestoresIt()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_quick_check", 30, 5, """{"package_type":"full","flexible_credits":10}"""), 1, "cs_full", null, CancellationToken.None);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_writing_starter", 30, 3, """{"package_type":"writing","writing_only_credits":6}"""), 1, "cs_writing", null, CancellationToken.None);

        var debit = await service.DeductGradingCreditAsync("learner-1", "writing", "we-1", CancellationToken.None);
        var afterDebit = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);
        var refunded = await service.RefundAsync("learner-1", "we-1", "refund:we-1", "refund", CancellationToken.None);
        var afterRefund = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.True(debit.Debited);
        Assert.Equal("dedicated", debit.BalanceSource);
        Assert.Equal(2, debit.CreditsUsed);
        Assert.Equal(4, afterDebit.WritingOnlyCredits);
        Assert.Equal(0, afterDebit.SharedCredits);
        Assert.Equal(10, afterDebit.FlexibleCredits);
        Assert.True(refunded);
        Assert.Equal(6, afterRefund.WritingOnlyCredits);
        Assert.Equal(10, afterRefund.FlexibleCredits);
    }

    [Fact]
    public async Task WritingExam_DeductsTwoCredits_FromWritingPoolFirst()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_writing_starter", 30, 3, """{"package_type":"writing","writing_only_credits":6}"""), 1, "cs_writing", null, CancellationToken.None);

        var debit = await service.DeductGradingCreditAsync("learner-1", "writing", "we-1credit", AiGradingCreditCost.WritingExam, CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.True(debit.Debited);
        Assert.Equal(2, debit.CreditsUsed);
        Assert.Equal(4, snapshot.WritingOnlyCredits);
        Assert.Equal(0, snapshot.FlexibleCredits);
    }

    [Fact]
    public async Task WritingStarter_SixCreditGrant_FundsExactlyThreeAdvertisedLetters()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync(
            "learner-1",
            AddOn("pkg_writing_starter", 30, 3,
                """{"package_type":"writing","writing_only_credits":6,"writing_items":3}"""),
            1,
            "cs_writing_truthful",
            null,
            CancellationToken.None);

        var first = await service.DeductGradingCreditAsync("learner-1", "writing", "letter-1", AiGradingCreditCost.WritingExam, CancellationToken.None);
        var second = await service.DeductGradingCreditAsync("learner-1", "writing", "letter-2", AiGradingCreditCost.WritingExam, CancellationToken.None);
        var third = await service.DeductGradingCreditAsync("learner-1", "writing", "letter-3", AiGradingCreditCost.WritingExam, CancellationToken.None);
        var fourth = await service.DeductGradingCreditAsync("learner-1", "writing", "letter-4", AiGradingCreditCost.WritingExam, CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.True(first.Debited);
        Assert.Equal(2, first.CreditsUsed);
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
            "learner-1", "writing", "mastery-writing", AiGradingCreditCost.WritingExam, CancellationToken.None);
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
            "learner-1", "writing", AiGradingCreditCost.WritingExam, CancellationToken.None);
        Assert.False(parentRevoked.Debited);
        Assert.Equal("no_ai_package_credits", parentRevoked.ErrorCode);

        parent.Status = SubscriptionStatus.Active;
        item.Status = SubscriptionItemStatus.Cancelled;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var revoked = await service.CheckGradingCreditAsync(
            "learner-1", "writing", AiGradingCreditCost.WritingExam, CancellationToken.None);
        Assert.False(revoked.Debited);
        Assert.Equal("no_ai_package_credits", revoked.ErrorCode);

        item.Status = SubscriptionItemStatus.Active;
        item.EndsAt = null;
        await db.SaveChangesAsync();

        // Live Mastery with a missing end date must still unlock Writing/Speaking,
        // matching Listening/Reading Recalc (EndsAt == null || EndsAt > now).
        var openEndedLiveItem = await service.CheckGradingCreditAsync(
            "learner-1", "writing", AiGradingCreditCost.WritingExam, CancellationToken.None);
        var openEndedSnapshot = await service.GetSnapshotAsync("learner-1", 0, CancellationToken.None);
        Assert.True(openEndedLiveItem.Debited);
        Assert.True(openEndedSnapshot.WritingUnlimited);
        Assert.True(openEndedSnapshot.SpeakingUnlimited);

        item.EndsAt = now.AddMinutes(-1);
        var account = await db.AiPackageCreditAccounts.SingleAsync(row => row.UserId == "learner-1");
        account.ExpiresAt = now.AddMinutes(-1);
        await db.SaveChangesAsync();

        var expired = await service.CheckGradingCreditAsync(
            "learner-1", "writing", AiGradingCreditCost.WritingExam, CancellationToken.None);
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
            "learner-1", "writing", AiGradingCreditCost.WritingExam, CancellationToken.None);

        Assert.False(result.Debited);
        Assert.Equal("no_ai_package_credits", result.ErrorCode);
    }

    [Fact]
    public async Task WritingExam_TwoActivities_SpendDedicatedThenShared()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_quick_check", 30, 5, """{"package_type":"full","shared_credits":4}"""), 1, "cs_full", null, CancellationToken.None);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_writing_single", 30, 1, """{"package_type":"writing","writing_only_credits":2}"""), 1, "cs_writing", null, CancellationToken.None);

        var debit = await service.DeductGradingCreditAsync("learner-1", "writing", "we-spill", 2, CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.True(debit.Debited);
        Assert.Equal(4, debit.CreditsUsed);
        Assert.Equal(0, snapshot.WritingOnlyCredits);
        Assert.Equal(0, snapshot.FlexibleCredits);
        Assert.Equal(2, snapshot.SharedCredits);
    }

    [Fact]
    public async Task WritingExam_AllOrNothing_WhenOnlyOneSharedCreditAvailable()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_shared_one", 30, 1, """{"package_type":"full","shared_credits":1}"""), 1, "cs_shared", null, CancellationToken.None);

        var debit = await service.DeductGradingCreditAsync("learner-1", "writing", "we-short", AiGradingCreditCost.WritingExam, CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.False(debit.Debited);
        Assert.Equal("no_ai_package_credits", debit.ErrorCode);
        Assert.Equal(1, snapshot.SharedCredits);
    }

    [Fact]
    public async Task CheckGradingCredit_WithQuantityTwo_RequiresFourDedicatedCredits()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_writing_single", 30, 1, """{"package_type":"writing","writing_only_credits":2}"""), 1, "cs_writing_1", null, CancellationToken.None);

        var withOne = await service.CheckGradingCreditAsync("learner-1", "writing", 2, CancellationToken.None);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_writing_single", 30, 1, """{"package_type":"writing","writing_only_credits":2}"""), 1, "cs_writing_2", null, CancellationToken.None);
        var withTwo = await service.CheckGradingCreditAsync("learner-1", "writing", 2, CancellationToken.None);

        Assert.False(withOne.Debited);
        Assert.True(withTwo.Debited);
    }

    [Fact]
    public async Task RefundGradingCredit_RestoresDedicatedWritingCredit()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_writing_starter", 30, 3, """{"package_type":"writing","writing_only_credits":6}"""), 1, "cs_writing", null, CancellationToken.None);

        await service.DeductGradingCreditAsync("learner-1", "writing", "we-refundable", AiGradingCreditCost.WritingExam, CancellationToken.None);
        var refunded = await service.RefundAsync("learner-1", "we-refundable", "refund:we-refundable", "grading failed", CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.True(refunded);
        Assert.Equal(6, snapshot.WritingOnlyCredits);
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
    public async Task FullCourseGift_FiveSharedCredits_CannotFundWritingSpeakingListeningReading()
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

        var writing = await service.DeductGradingCreditAsync(
            "learner-1", "writing", "gift-writing", AiGradingCreditCost.WritingExam, CancellationToken.None);
        var speaking = await service.DeductGradingCreditAsync(
            "learner-1", "speaking", "gift-speaking", AiGradingCreditCost.SpeakingCard, CancellationToken.None);
        var listening = await service.DeductObjectivePracticeAsync(
            "learner-1", "listening", "gift-listening-paper", CancellationToken.None);
        var reading = await service.DeductObjectivePracticeAsync(
            "learner-1", "reading", "gift-reading-paper", CancellationToken.None);
        var extra = await service.DeductGradingCreditAsync(
            "learner-1", "writing", "gift-writing-2", AiGradingCreditCost.WritingExam, CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.True(granted);
        Assert.False(duplicate);
        Assert.True(writing.Debited);
        Assert.True(speaking.Debited);
        Assert.True(listening.Debited);
        Assert.False(reading.Debited);
        Assert.False(extra.Debited);
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
                """{"package_type":"speaking","speaking_only_credits":6,"speaking_items":3,"listening_tests":0,"reading_tests":0}"""),
            1,
            "cs_speaking_only",
            null,
            CancellationToken.None);

        Assert.Equal(6, snapshot.WritingOnlyCredits);
        Assert.Equal(6, snapshot.SpeakingOnlyCredits);
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
                """{"package_type":"full","flexible_credits":10,"listening_tests":3,"reading_tests":3}"""),
            1,
            "cs_quick_check",
            null,
            CancellationToken.None);

        Assert.Equal(0, snapshot.SharedCredits);
        Assert.Equal(10, snapshot.FlexibleCredits);
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
                """{"package_type":"full","flexible_credits":30,"listening_tests":6,"reading_tests":6}"""),
            1,
            "cs_exam_prep_pro",
            null,
            CancellationToken.None);

        Assert.Equal(0, snapshot.SharedCredits);
        Assert.Equal(30, snapshot.FlexibleCredits);
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
        Assert.Equal(0, snapshot.SharedCredits);
        Assert.Equal(0, snapshot.CreditsGranted);
        Assert.Equal(0, snapshot.CreditsRemaining);
    }

    [Fact]
    public async Task GrantPackage_LeftoverGrantCredits_BecomeSharedNotFlexible()
    {
        await using var db = NewContext();
        var service = NewService(db);

        var snapshot = await service.GrantPackageAsync(
            "learner-1",
            AddOn("pkg_legacy_credits", 30, 5, """{"package_type":"full"}"""),
            1,
            "cs_legacy_shared",
            null,
            CancellationToken.None);

        Assert.Equal(5, snapshot.SharedCredits);
        Assert.Equal(0, snapshot.FlexibleCredits);
    }

    [Fact]
    public async Task QuickCheckShared_CannotFundReadingWhenDedicatedReadingIsZero()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync(
            "learner-1",
            AddOn("pkg_quick_check", 30, 5, """{"package_type":"full","flexible_credits":10,"listening_tests":0,"reading_tests":0}"""),
            1,
            "cs_qc_flex_only",
            null,
            CancellationToken.None);

        var reading = await service.DeductObjectivePracticeAsync(
            "learner-1", "reading", "qc-reading", CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.False(reading.Debited);
        Assert.Equal(10, snapshot.FlexibleCredits);
        Assert.Equal(0, snapshot.SharedCredits);
    }

    [Fact]
    public async Task WritingSubmission_FallsBackToFlexibleWs_WhenDedicatedExhausted()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_quick_check", 30, 5, """{"package_type":"full","flexible_credits":10}"""), 1, "cs_full", null, CancellationToken.None);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_writing_single", 30, 1, """{"package_type":"writing","writing_only_credits":2}"""), 1, "cs_writing", null, CancellationToken.None);

        var first = await service.DeductGradingCreditAsync("learner-1", "writing", "we-dedicated", 1, CancellationToken.None);
        var second = await service.DeductGradingCreditAsync("learner-1", "writing", "we-flexws", 1, CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.True(first.Debited);
        Assert.Equal("dedicated", first.BalanceSource);
        Assert.True(second.Debited);
        Assert.Equal("flexible_ws", second.BalanceSource);
        Assert.Equal(0, snapshot.WritingOnlyCredits);
        Assert.Equal(8, snapshot.FlexibleCredits);
    }

    [Fact]
    public async Task CheckGradingCredit_SharedRate_RequiresFourUniversalCredits()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantCourseGiftCreditsAsync(
            "learner-1", "tiny-gift", "Tiny Gift", 2,
            "plan:one:gift", null, CancellationToken.None);
        var withOneShared = await service.CheckGradingCreditAsync("learner-1", "writing", 2, CancellationToken.None);

        await service.GrantCourseGiftCreditsAsync(
            "learner-1", "second-gift", "Second Gift", 2,
            "plan:two:gift", null, CancellationToken.None);
        var withTwoShared = await service.CheckGradingCreditAsync("learner-1", "writing", 2, CancellationToken.None);

        Assert.False(withOneShared.Debited);
        Assert.True(withTwoShared.Debited);
    }

    // ── FINAL 2026-09-06: 1 card = 2 credits, full exam = 4 ──

    [Fact]
    public async Task SpeakingCard_WithOnlyOneCredit_BlockedBeforeStart()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_speaking_single", 30, 1, """{"package_type":"speaking","speaking_only_credits":1}"""), 1, "cs_speaking_1", null, CancellationToken.None);

        var check = await service.CheckGradingCreditAsync("learner-1", "speaking", AiGradingCreditCost.SpeakingCard, CancellationToken.None);
        var debit = await service.DeductGradingCreditAsync("learner-1", "speaking", "card-lonely", AiGradingCreditCost.SpeakingCard, CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.False(check.Debited);
        Assert.False(debit.Debited);
        Assert.Equal("no_ai_package_credits", debit.ErrorCode);
        Assert.Equal(1, snapshot.SpeakingOnlyCredits);
        Assert.Equal(0, snapshot.AvailableSpeakingActivities);
    }

    [Fact]
    public async Task SpeakingStarter_SixCreditGrant_FundsExactlyThreeAdvertisedCards()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync(
            "learner-1",
            AddOn("pkg_speaking_starter", 30, 3,
                """{"package_type":"speaking","speaking_only_credits":6,"speaking_items":3}"""),
            1,
            "cs_speaking_truthful",
            null,
            CancellationToken.None);

        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);
        Assert.Equal(3, snapshot.AvailableSpeakingActivities);

        for (var card = 1; card <= 3; card++)
        {
            var debit = await service.DeductGradingCreditAsync("learner-1", "speaking", $"card-{card}", AiGradingCreditCost.SpeakingCard, CancellationToken.None);
            Assert.True(debit.Debited);
            Assert.Equal(2, debit.CreditsUsed);
        }

        var fourth = await service.DeductGradingCreditAsync("learner-1", "speaking", "card-4", AiGradingCreditCost.SpeakingCard, CancellationToken.None);
        Assert.False(fourth.Debited);
        Assert.Equal("no_ai_package_credits", fourth.ErrorCode);
    }

    [Fact]
    public async Task FullSpeakingExam_PreGate_RequiresFourCreditsBeforeCardA()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_speaking_single", 30, 1, """{"package_type":"speaking","speaking_only_credits":2}"""), 1, "cs_speaking_2", null, CancellationToken.None);

        // One card (2 credits) is NOT enough to start a full two-card exam.
        var halfFunded = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);
        Assert.True(halfFunded.AvailableSpeakingActivities < AiGradingCreditCost.SpeakingExam);

        await service.GrantPackageAsync("learner-1", AddOn("pkg_speaking_single", 30, 1, """{"package_type":"speaking","speaking_only_credits":2}"""), 1, "cs_speaking_2b", null, CancellationToken.None);
        var fullyFunded = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);
        Assert.True(fullyFunded.AvailableSpeakingActivities >= AiGradingCreditCost.SpeakingExam);

        // Card A + Card B consume exactly 4 total — never 2, never more.
        var cardA = await service.DeductGradingCreditAsync("learner-1", "speaking", "exam:1:cardA", AiGradingCreditCost.SpeakingCard, CancellationToken.None);
        var cardB = await service.DeductGradingCreditAsync("learner-1", "speaking", "exam:1:cardB", AiGradingCreditCost.SpeakingCard, CancellationToken.None);
        Assert.True(cardA.Debited);
        Assert.True(cardB.Debited);
        Assert.Equal(4, cardA.CreditsUsed + cardB.CreditsUsed);
        var after = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);
        Assert.Equal(0, after.SpeakingOnlyCredits);
    }

    [Fact]
    public async Task QuickCheck_TenFlexibleCredits_FundExactlyFiveMixedAttempts()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_quick_check", 30, 5, """{"package_type":"full","flexible_credits":10,"listening_tests":3,"reading_tests":3}"""), 1, "cs_qc", null, CancellationToken.None);

        // 3 Writing + 2 Speaking in any mix, then the 6th new attempt blocks.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var writing = await service.DeductGradingCreditAsync("learner-1", "writing", $"qc-w{attempt}", 1, CancellationToken.None);
            Assert.True(writing.Debited);
        }
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var speaking = await service.DeductGradingCreditAsync("learner-1", "speaking", $"qc-s{attempt}", 1, CancellationToken.None);
            Assert.True(speaking.Debited);
        }
        var sixth = await service.DeductGradingCreditAsync("learner-1", "writing", "qc-w4", 1, CancellationToken.None);
        Assert.False(sixth.Debited);
        Assert.Equal("no_ai_package_credits", sixth.ErrorCode);
    }

    // ── Admin credit adjustment semantics (Part 27 Tests A–F) ──
    // Admin Add/Remove/Set-Exact change TOTAL only; Used is genuine learner
    // consumption and is read-only for allocation operations; Remaining = Total - Used.

    private static async Task SeedSharedAsync(AiPackageCreditService service, string userId, int credits)
        => await service.GrantCourseGiftCreditsAsync(
            userId, "full-course", "Full Course", credits,
            $"admin-package:seed:{userId}", null, CancellationToken.None);

    private static AiPackageCreditAdjustmentRequest SharedAdjustment(
        int? delta = null, int? set = null, string reason = "Admin AI package credit adjustment")
        => new(0, 0, 0, 0, 0, 0, ExpiresAt: null, Reason: reason, SharedCreditsDelta: delta ?? 0, SharedCreditsSet: set);

    [Fact]
    public async Task AdjustAsync_RemoveOneWithNoUsage_ChangesTotalNotUsed()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await SeedSharedAsync(service, "learner-admin-remove", 5);

        var after = await service.AdjustAsync("learner-admin-remove", SharedAdjustment(delta: -1), "admin-1", CancellationToken.None);

        Assert.Equal(4, after.SharedCreditsGranted);
        Assert.Equal(0, after.SharedCreditsUsed);
        Assert.Equal(4, after.SharedCredits);
    }

    [Fact]
    public async Task AdjustAsync_AddThree_ChangesTotalNotUsed()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await SeedSharedAsync(service, "learner-admin-add", 5);

        var after = await service.AdjustAsync("learner-admin-add", SharedAdjustment(delta: 3), "admin-1", CancellationToken.None);

        Assert.Equal(8, after.SharedCreditsGranted);
        Assert.Equal(0, after.SharedCreditsUsed);
        Assert.Equal(8, after.SharedCredits);
    }

    [Fact]
    public async Task AdjustAsync_RemoveAfterRealUsage_PreservesGenuineUsed()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await SeedSharedAsync(service, "learner-admin-remove-used", 5);
        await service.DeductObjectivePracticeAsync("learner-admin-remove-used", "reading", "ref-usage-1", CancellationToken.None);
        await service.DeductObjectivePracticeAsync("learner-admin-remove-used", "reading", "ref-usage-2", CancellationToken.None);

        var before = await service.GetSnapshotAsync("learner-admin-remove-used", 20, CancellationToken.None);
        Assert.Equal(5, before.SharedCreditsGranted);
        Assert.Equal(2, before.SharedCreditsUsed);
        Assert.Equal(3, before.SharedCredits);

        var after = await service.AdjustAsync("learner-admin-remove-used", SharedAdjustment(delta: -1), "admin-1", CancellationToken.None);

        Assert.Equal(4, after.SharedCreditsGranted);
        Assert.Equal(2, after.SharedCreditsUsed);
        Assert.Equal(2, after.SharedCredits);
    }

    [Fact]
    public async Task AdjustAsync_SetExact_IsAbsoluteNotAdditive()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await SeedSharedAsync(service, "learner-admin-set", 5);

        var after = await service.AdjustAsync("learner-admin-set", SharedAdjustment(set: 3), "admin-1", CancellationToken.None);

        Assert.Equal(3, after.SharedCreditsGranted);
        Assert.Equal(0, after.SharedCreditsUsed);
        Assert.Equal(3, after.SharedCredits);
    }

    [Fact]
    public async Task AdjustAsync_SetExactAfterRealUsage_TargetsTotalAndPreservesUsed()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await SeedSharedAsync(service, "learner-admin-set-used", 5);
        for (var i = 1; i <= 4; i++)
        {
            await service.DeductObjectivePracticeAsync("learner-admin-set-used", "reading", $"ref-usage-{i}", CancellationToken.None);
        }

        var after = await service.AdjustAsync("learner-admin-set-used", SharedAdjustment(set: 7), "admin-1", CancellationToken.None);

        Assert.Equal(7, after.SharedCreditsGranted);
        Assert.Equal(4, after.SharedCreditsUsed);
        Assert.Equal(3, after.SharedCredits);
    }

    [Fact]
    public async Task AdjustAsync_SetExactBelowUsed_RejectedWithoutMutation()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await SeedSharedAsync(service, "learner-admin-set-low", 5);
        for (var i = 1; i <= 4; i++)
        {
            await service.DeductObjectivePracticeAsync("learner-admin-set-low", "reading", $"ref-usage-{i}", CancellationToken.None);
        }

        await Assert.ThrowsAsync<ApiException>(() => service.AdjustAsync(
            "learner-admin-set-low", SharedAdjustment(set: 3), "admin-1", CancellationToken.None));

        // No data mutation: balance stays 5 Total / 4 Used / 1 Remaining.
        var snapshot = await service.GetSnapshotAsync("learner-admin-set-low", 20, CancellationToken.None);
        Assert.Equal(5, snapshot.SharedCreditsGranted);
        Assert.Equal(4, snapshot.SharedCreditsUsed);
        Assert.Equal(1, snapshot.SharedCredits);
    }
}
