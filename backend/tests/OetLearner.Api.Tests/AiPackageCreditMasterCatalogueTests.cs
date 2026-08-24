using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Tests;

/// <summary>
/// Acceptance coverage for the OET 2026 Master Catalogue credit rules:
/// universal Shared Credits vs the restricted Flexible W/S pool,
/// specific-before-shared priority, exact subtest caps, and snapshot
/// bucket enrichment (A01-A05, A07/A08, A17-style logic).
/// </summary>
public sealed class AiPackageCreditMasterCatalogueTests
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

    private static BillingAddOn AddOn(string code, int durationDays, string grantJson)
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
            GrantCredits = 0,
            GrantEntitlementsJson = grantJson,
            AddonKind = "ai_package",
            AppliesToAllPlans = true,
            IsStackable = true,
            QuantityStep = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private const string QuickCheckJson =
        """{"package_type":"full","flexible_credits":5,"listening_tests":3,"reading_tests":3}""";

    private const string ReadingStarterJson =
        """{"package_type":"reading","reading_tests":3}""";

    private const string WritingStarterJson =
        """{"package_type":"writing","writing_only_credits":3,"writing_items":3}""";

    private const string SpeakingStarterJson =
        """{"package_type":"speaking","speaking_only_credits":3,"speaking_items":3}""";

    // ── A10/A11: Full Course gift lands in the universal Shared pool ──

    [Fact]
    public async Task CourseGift_GrantsUniversalSharedCredits_ExactlyOncePerReference()
    {
        await using var db = NewContext();
        var service = NewService(db);

        var granted = await service.GrantCourseGiftCreditsAsync(
            "learner-1", "full-nursing", "Full Nursing OET Course", 5,
            "plan:q1:full-nursing", DateTimeOffset.UtcNow.AddDays(180), CancellationToken.None);
        var replayed = await service.GrantCourseGiftCreditsAsync(
            "learner-1", "full-nursing", "Full Nursing OET Course", 5,
            "plan:q1:full-nursing", DateTimeOffset.UtcNow.AddDays(180), CancellationToken.None);

        Assert.True(granted);
        Assert.False(replayed);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);
        Assert.Equal(5, snapshot.SharedCredits);
        Assert.Equal(5, snapshot.SharedCreditsGranted);
        Assert.Equal(0, snapshot.FlexibleCredits);
    }

    // ── A01: a credit-only account cannot exceed its balance on Reading ──

    [Fact]
    public async Task CreditOnlyAccount_AllowsExactlyFiveReadingAttempts_ThenBlocks()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantCourseGiftCreditsAsync(
            "learner-1", "crash-course", "Full Crash Course", 5,
            "plan:q2:crash-course", DateTimeOffset.UtcNow.AddDays(180), CancellationToken.None);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var debit = await service.DeductObjectivePracticeAsync(
                "learner-1", "reading", $"objective:reading:learner-1:paper-{attempt}", CancellationToken.None);
            Assert.True(debit.Debited, $"attempt {attempt} should pass");
            Assert.Equal("shared", debit.BalanceSource);
        }

        var sixth = await service.DeductObjectivePracticeAsync(
            "learner-1", "reading", "objective:reading:learner-1:paper-6", CancellationToken.None);
        Assert.False(sixth.Debited);
        Assert.Equal("no_reading_tests", sixth.ErrorCode);
    }

    // ── A02: Writing costs 2 Shared credits and the message says so ──

    [Fact]
    public async Task SharedCredits_WritingConsumesTwo_WithExplicitFeedbackMessage()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantCourseGiftCreditsAsync(
            "learner-1", "crash-course", "Full Crash Course", 5,
            "plan:q3:crash-course", null, CancellationToken.None);

        var debit = await service.DeductGradingCreditAsync("learner-1", "writing", "we-a02", 2, CancellationToken.None);
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);

        Assert.True(debit.Debited);
        Assert.Equal("shared", debit.BalanceSource);
        Assert.Contains("Shared Credit", debit.FeedbackMessage);
        Assert.Contains("Writing", debit.FeedbackMessage);
        Assert.Equal(3, snapshot.SharedCredits);
    }

    // ── A03 + §5: specific pool first; restricted Flexible W/S never covers Reading ──

    [Fact]
    public async Task QuickCheckFlexibleWs_IsNeverSpentOnReading()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_quick_check", 30, QuickCheckJson), 1, "cs-qc", null, CancellationToken.None);

        // Exhaust the 3 Reading tests.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var debit = await service.DeductObjectivePracticeAsync(
                "learner-1", "reading", $"objective:reading:learner-1:rpaper-{attempt}", CancellationToken.None);
            Assert.True(debit.Debited);
            Assert.Equal("reading", debit.BalanceSource);
        }

        // The 4th must block even though 5 Flexible W/S credits remain.
        var fourth = await service.DeductObjectivePracticeAsync(
            "learner-1", "reading", "objective:reading:learner-1:rpaper-4", CancellationToken.None);
        Assert.False(fourth.Debited);
        Assert.Equal("no_reading_tests", fourth.ErrorCode);

        // And the Flexible W/S pool is still intact afterwards.
        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);
        Assert.Equal(5, snapshot.FlexibleCredits);
        Assert.Equal(0, snapshot.SharedCredits);
    }

    // ── A03: Reading draws its own allowance before Shared ──

    [Fact]
    public async Task ReadingStarter_PlusSharedCredits_DrainsSpecificPoolFirst()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_reading_starter", 30, ReadingStarterJson), 1, "cs-rs", null, CancellationToken.None);
        await service.GrantCourseGiftCreditsAsync(
            "learner-1", "full-nursing", "Full Nursing OET Course", 5,
            "plan:q4:full-nursing", null, CancellationToken.None);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var debit = await service.DeductObjectivePracticeAsync(
                "learner-1", "reading", $"objective:reading:learner-1:mix-{attempt}", CancellationToken.None);
            Assert.Equal("reading", debit.BalanceSource);
        }

        var fourth = await service.DeductObjectivePracticeAsync(
            "learner-1", "reading", "objective:reading:learner-1:mix-4", CancellationToken.None);
        Assert.Equal("shared", fourth.BalanceSource);

        var snapshot = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);
        Assert.Equal(4, snapshot.SharedCredits);
        Assert.Equal(0, snapshot.ReadingTestsRemaining);
    }

    // ── A07/A08: exact caps — dedicated pools only, no hidden bonuses ──

    [Fact]
    public async Task WritingAndSpeakingStarters_GrantExactCaps_AndBlockTheFourthSubmission()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_writing_starter", 30, WritingStarterJson), 1, "cs-w", null, CancellationToken.None);
        await service.GrantPackageAsync("learner-2", AddOn("pkg_speaking_starter", 30, SpeakingStarterJson), 1, "cs-s", null, CancellationToken.None);

        for (var submission = 1; submission <= 3; submission++)
        {
            var writingDebit = await service.DeductGradingCreditAsync("learner-1", "writing", $"we-w{submission}", 1, CancellationToken.None);
            Assert.True(writingDebit.Debited, $"writing {submission}");
            var speakingDebit = await service.DeductGradingCreditAsync("learner-2", "speaking", $"se-s{submission}", CancellationToken.None);
            Assert.True(speakingDebit.Debited, $"speaking {submission}");
        }

        var fourthWriting = await service.DeductGradingCreditAsync("learner-1", "writing", "we-w4", 1, CancellationToken.None);
        var fourthSpeaking = await service.DeductGradingCreditAsync("learner-2", "speaking", "se-s4", CancellationToken.None);
        Assert.False(fourthWriting.Debited);
        Assert.False(fourthSpeaking.Debited);
        Assert.Equal("no_ai_package_credits", fourthWriting.ErrorCode);

        var writer = await service.GetSnapshotAsync("learner-1", 20, CancellationToken.None);
        Assert.Equal(0, writer.WritingOnlyCredits);
        Assert.Equal(0, writer.FlexibleCredits);
        Assert.Equal(0, writer.SharedCredits);
    }

    // ── A17: expired balances cannot be consumed; nothing resurrects them ──

    [Fact]
    public async Task ExpiredPackage_BlocksAllConsumption()
    {
        await using var db = NewContext();
        var service = NewService(db);
        var addOn = AddOn("pkg_writing_starter", 30, WritingStarterJson);
        await service.GrantPackageAsync("learner-1", addOn, 1, "cs-exp", null, CancellationToken.None);

        // Force-expire by rewinding the account expiry.
        var account = await db.AiPackageCreditAccounts.SingleAsync(a => a.UserId == "learner-1");
        account.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync(CancellationToken.None);

        var grading = await service.DeductGradingCreditAsync("learner-1", "writing", "we-exp", 2, CancellationToken.None);
        Assert.False(grading.Debited);
        Assert.Equal("ai_package_expired", grading.ErrorCode);

        // A fresh grant reactivates the wallet.
        await service.GrantCourseGiftCreditsAsync(
            "learner-1", "full-nursing", "Full Nursing OET Course", 5,
            "plan:q5:full-nursing", DateTimeOffset.UtcNow.AddDays(180), CancellationToken.None);
        var revived = await service.DeductGradingCreditAsync("learner-1", "writing", "we-revived", 2, CancellationToken.None);
        Assert.True(revived.Debited);
        Assert.Equal("shared", revived.BalanceSource);
    }

    // ── Snapshot enrichment: per-bucket totals/source/validity/days-left ──

    [Fact]
    public async Task Snapshot_ProjectsBucketCardsWithSourceValidityAndDaysLeft()
    {
        await using var db = NewContext();
        var service = NewService(db);
        await service.GrantPackageAsync("learner-1", AddOn("pkg_quick_check", 30, QuickCheckJson), 1, "cs-bucket-qc", null, CancellationToken.None);
        await service.GrantCourseGiftCreditsAsync(
            "learner-1", "full-nursing", "Full Nursing OET Course", 5,
            "plan:q6:full-nursing", DateTimeOffset.UtcNow.AddDays(180), CancellationToken.None);

        var snapshot = await service.GetSnapshotAsync("learner-1", 50, CancellationToken.None);

        Assert.NotNull(snapshot.Buckets);
        var buckets = snapshot.Buckets!;
        var reading = Assert.Single(buckets, b => b.Key == "reading");
        Assert.Equal(3, reading.Remaining);
        Assert.Equal(3, reading.TotalGranted);
        Assert.Equal("quick check", reading.SourcePackages);

        var shared = Assert.Single(buckets, b => b.Key == "shared");
        Assert.Equal(5, shared.TotalGranted);
        Assert.Contains("full nursing", shared.SourcePackages, StringComparison.OrdinalIgnoreCase);
        Assert.True(shared.DaysLeft > 170);
        Assert.NotNull(shared.ValidFrom);
        Assert.NotEmpty(shared.Grants);
    }
}
