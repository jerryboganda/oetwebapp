using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Tests;

/// <summary>
/// Locks the "one credit per paper" billing rule for objective practice:
/// opening any part (or the full paper) of a sample consumes one test credit,
/// and every other part plus every re-attempt of that same sample is free.
/// Mock sections are billed via the mock credit, so they must NOT touch the
/// per-paper Reading/Listening allowance.
/// </summary>
public sealed class ReadingListeningCreditPerPaperTests
{
    private static (LearnerDbContext db, ReadingAttemptService attempt, AiPackageCreditService credit) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new LearnerDbContext(options);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var policy = new ReadingPolicyService(db, cache);
        var grader = new ReadingGradingService(db, policy, NullLogger<ReadingGradingService>.Instance);
        var entitlements = new ContentEntitlementService(db, new EffectiveEntitlementResolver(db));
        var credit = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        var attempt = new ReadingAttemptService(db, policy, grader, entitlements, NullLogger<ReadingAttemptService>.Instance, credit);
        return (db, attempt, credit);
    }

    private static async Task SeedFreePaperAsync(LearnerDbContext db, string paperId)
    {
        db.ContentPapers.Add(new ContentPaper
        {
            Id = paperId,
            SubtestCode = "reading",
            Title = $"Reading {paperId}",
            Slug = $"reading-{paperId}",
            AppliesToAllProfessions = true,
            Difficulty = "standard",
            EstimatedDurationMinutes = 60,
            Status = ContentStatus.Published,
            SourceProvenance = "Test",
            TagsCsv = "access:free",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task GrantReadingTestsAsync(AiPackageCreditService credit, string userId, int readingTests)
        => await credit.GrantPackageAsync(
            userId,
            new BillingAddOn
            {
                Id = "addon_reading",
                Code = "pkg_reading_starter",
                Name = "Reading pack",
                Price = 1m,
                Currency = "GBP",
                Interval = "one_time",
                Status = BillingAddOnStatus.Active,
                DurationDays = 30,
                GrantCredits = 0,
                GrantEntitlementsJson = $$"""{"package_type":"reading","reading_tests":{{readingTests}}}""",
                AddonKind = "ai_package",
                AppliesToAllPlans = true,
                IsStackable = true,
                QuantityStep = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            1,
            "cs_reading",
            null,
            CancellationToken.None);

    private static string PartScope(string partCode)
        => $$"""{"kind":"part-practice","partCode":"{{partCode}}","questionIds":["q-{{partCode}}"]}""";

    [Fact]
    public async Task PartAThenPartB_OnSamePaper_ConsumeOneCredit()
    {
        var (db, attempt, credit) = Build();
        await SeedFreePaperAsync(db, "rp-1");
        await GrantReadingTestsAsync(credit, "u1", 5);

        // Part A practice, then Part B practice — both scoped subsets of the same paper.
        await attempt.StartInModeAsync("u1", "rp-1", ReadingAttemptMode.Drill, PartScope("A"), CancellationToken.None);
        await attempt.StartInModeAsync("u1", "rp-1", ReadingAttemptMode.Drill, PartScope("B"), CancellationToken.None);

        var snapshot = await credit.GetSnapshotAsync("u1", 20, CancellationToken.None);
        Assert.Equal(4, snapshot.ReadingTestsRemaining); // one paper => one credit
    }

    [Fact]
    public async Task DifferentPapers_EachConsumeOneCredit()
    {
        var (db, attempt, credit) = Build();
        await SeedFreePaperAsync(db, "rp-1");
        await SeedFreePaperAsync(db, "rp-2");
        await GrantReadingTestsAsync(credit, "u1", 5);

        await attempt.StartInModeAsync("u1", "rp-1", ReadingAttemptMode.Drill, PartScope("A"), CancellationToken.None);
        await attempt.StartInModeAsync("u1", "rp-2", ReadingAttemptMode.Drill, PartScope("A"), CancellationToken.None);

        var snapshot = await credit.GetSnapshotAsync("u1", 20, CancellationToken.None);
        Assert.Equal(3, snapshot.ReadingTestsRemaining); // two papers => two credits
    }

    [Fact]
    public async Task MockSection_DoesNotConsumeReadingTestAllowance()
    {
        var (db, attempt, credit) = Build();
        await SeedFreePaperAsync(db, "rp-1");
        await GrantReadingTestsAsync(credit, "u1", 5);

        // isMockSection: the mock is billed via the mock credit, so the per-paper
        // Reading objective-practice debit must be skipped.
        await attempt.StartAsync("u1", "rp-1", CancellationToken.None, isMockSection: true);

        var snapshot = await credit.GetSnapshotAsync("u1", 20, CancellationToken.None);
        Assert.Equal(5, snapshot.ReadingTestsRemaining); // untouched
    }

    [Fact]
    public async Task StandaloneAfterMock_StillUnlocksPaperOnce()
    {
        var (db, attempt, credit) = Build();
        await SeedFreePaperAsync(db, "rp-1");
        await GrantReadingTestsAsync(credit, "u1", 5);

        // Mock section first (no debit), then standalone part practice on the same
        // paper — the standalone attempt is the first metered touch and unlocks it once.
        await attempt.StartAsync("u1", "rp-1", CancellationToken.None, isMockSection: true);
        await attempt.StartInModeAsync("u1", "rp-1", ReadingAttemptMode.Drill, PartScope("A"), CancellationToken.None);
        await attempt.StartInModeAsync("u1", "rp-1", ReadingAttemptMode.Drill, PartScope("B"), CancellationToken.None);

        var snapshot = await credit.GetSnapshotAsync("u1", 20, CancellationToken.None);
        Assert.Equal(4, snapshot.ReadingTestsRemaining); // one credit total
    }
}
