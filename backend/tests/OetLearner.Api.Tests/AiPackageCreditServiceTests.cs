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
    public async Task WritingExam_DeductsTwoCredits_FromWritingPoolFirst()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_writing_starter", 30, 3, """{"package_type":"writing","writing_only_credits":3}"""), 1, "cs_writing", null, CancellationToken.None);

        var debit = await service.DeductGradingCreditAsync("learner-1", "writing", "we-2credit", 2, CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.True(debit.Debited);
        Assert.Equal(1, snapshot.WritingOnlyCredits); // 3 - 2
        Assert.Equal(0, snapshot.FlexibleCredits);
    }

    [Fact]
    public async Task WritingExam_SpillsFromWritingThenFlexible()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_quick_check", 30, 5, """{"package_type":"full","flexible_credits":5}"""), 1, "cs_full", null, CancellationToken.None);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_writing_single", 30, 1, """{"package_type":"writing","writing_only_credits":1}"""), 1, "cs_writing", null, CancellationToken.None);

        var debit = await service.DeductGradingCreditAsync("learner-1", "writing", "we-spill", 2, CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.True(debit.Debited);
        Assert.Equal(0, snapshot.WritingOnlyCredits); // dedicated pool drained first
        Assert.Equal(4, snapshot.FlexibleCredits);    // then 1 from flexible
    }

    [Fact]
    public async Task WritingExam_AllOrNothing_WhenOnlyOneCreditAvailable()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_writing_single", 30, 1, """{"package_type":"writing","writing_only_credits":1}"""), 1, "cs_writing", null, CancellationToken.None);

        var debit = await service.DeductGradingCreditAsync("learner-1", "writing", "we-short", 2, CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.False(debit.Debited);
        Assert.Equal("no_ai_package_credits", debit.ErrorCode);
        Assert.Equal(1, snapshot.WritingOnlyCredits); // untouched — no partial debit
    }

    [Fact]
    public async Task CheckGradingCredit_WithQuantityTwo_RequiresTwoCredits()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_writing_single", 30, 1, """{"package_type":"writing","writing_only_credits":1}"""), 1, "cs_writing_1", null, CancellationToken.None);

        var withOne = await service.CheckGradingCreditAsync("learner-1", "writing", 2, CancellationToken.None);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_writing_single", 30, 1, """{"package_type":"writing","writing_only_credits":1}"""), 1, "cs_writing_2", null, CancellationToken.None);
        var withTwo = await service.CheckGradingCreditAsync("learner-1", "writing", 2, CancellationToken.None);

        Assert.False(withOne.Debited);
        Assert.True(withTwo.Debited);
    }

    [Fact]
    public async Task RefundGradingCredit_RestoresBothCreditsOfATwoCreditExam()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_writing_starter", 30, 3, """{"package_type":"writing","writing_only_credits":3}"""), 1, "cs_writing", null, CancellationToken.None);

        await service.DeductGradingCreditAsync("learner-1", "writing", "we-refundable", 2, CancellationToken.None);
        var refunded = await service.RefundAsync("learner-1", "we-refundable", "refund:we-refundable", "grading failed", CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.True(refunded);
        Assert.Equal(3, snapshot.WritingOnlyCredits); // 3 - 2 + 2
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
}
