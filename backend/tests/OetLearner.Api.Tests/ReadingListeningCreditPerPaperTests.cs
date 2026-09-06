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
        // Attempt start fails closed without an effective marking policy (test
        // hosts never run the governance seed migration).
        OetLearner.Api.Tests.Infrastructure.AssessmentGovernanceSeeder.SeedDefaultEffectivePolicies(db);
        db.SaveChanges();
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

    /// <summary>
    /// Authors a fully valid 20/6/16 structure (parts + texts + published
    /// questions with rationale/evidence) via the structure service, so
    /// full-exam starts pass Gate 5. Mirrors the authoring-test helper;
    /// credit tests only care about billing, not content.
    /// </summary>
    private static async Task AuthorFullStructureAsync(LearnerDbContext db, string paperId)
    {
        var structure = new ReadingStructureService(db);
        await structure.EnsureCanonicalPartsAsync(paperId, default);
        var now = DateTimeOffset.UtcNow;
        foreach (var part in new[] { "A", "B", "C" })
        {
            var mediaId = $"{paperId}-pdf-{part}";
            db.MediaAssets.Add(new MediaAsset
            {
                Id = mediaId,
                OriginalFilename = $"reading-part-{part}.pdf",
                MimeType = "application/pdf",
                Format = "pdf",
                SizeBytes = 10,
                StoragePath = $"reading/{paperId}/part-{part}.pdf",
                Status = MediaAssetStatus.Ready,
                UploadedBy = "admin",
            });
            db.ContentPaperAssets.Add(new ContentPaperAsset
            {
                Id = $"{paperId}-asset-{part}",
                PaperId = paperId,
                Role = PaperAssetRole.QuestionPaper,
                Part = part,
                MediaAssetId = mediaId,
                Title = $"Part {part} PDF",
                DisplayOrder = part == "A" ? 0 : part == "B" ? 1 : 2,
                IsPrimary = true,
                CreatedAt = now,
            });
        }
        await db.SaveChangesAsync();
        var parts = await db.ReadingParts.Where(p => p.PaperId == paperId).ToListAsync();
        var partA = parts.Single(p => p.PartCode == ReadingPartCode.A);
        var partB = parts.Single(p => p.PartCode == ReadingPartCode.B);
        var partC = parts.Single(p => p.PartCode == ReadingPartCode.C);

        var textsA = new List<ReadingText>();
        for (var i = 1; i <= 4; i++)
            textsA.Add(await structure.UpsertTextAsync(new ReadingTextUpsert(
                null, partA.Id, i, $"Text A{i}", "BMJ", "<p>text</p>", 10, null), "admin", default));
        var textsB = new List<ReadingText>();
        for (var i = 1; i <= 6; i++)
            textsB.Add(await structure.UpsertTextAsync(new ReadingTextUpsert(
                null, partB.Id, i, $"Extract B{i}", "NHS", "<p>text</p>", 20, null), "admin", default));
        var textsC = new List<ReadingText>();
        for (var i = 1; i <= 2; i++)
            textsC.Add(await structure.UpsertTextAsync(new ReadingTextUpsert(
                null, partC.Id, i, $"Text C{i}", "Lancet", "<p>text</p>", 300, null), "admin", default));

        for (var i = 1; i <= 7; i++)
            await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
                null, partA.Id, textsA[(i - 1) % textsA.Count].Id, i, 1, ReadingQuestionType.MatchingTextReference,
                $"PA-Q{i}", "[]", $"\"{((char)('A' + ((i - 1) % 4)))}\"", null, false, null, null), "admin", default);
        for (var i = 8; i <= 14; i++)
            await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
                null, partA.Id, textsA[(i - 1) % textsA.Count].Id, i, 1, ReadingQuestionType.ShortAnswer,
                $"PA-Q{i}", "[]", $"\"ans{i}\"", null, false, null, null), "admin", default);
        for (var i = 15; i <= 20; i++)
            await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
                null, partA.Id, textsA[(i - 1) % textsA.Count].Id, i, 1, ReadingQuestionType.SentenceCompletion,
                $"PA-Q{i}", "[]", $"\"ans{i}\"", null, false, null, null), "admin", default);
        for (var i = 1; i <= 6; i++)
            await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
                null, partB.Id, textsB[i - 1].Id, i, 1, ReadingQuestionType.MultipleChoice3,
                $"PB-Q{i}", "[\"a\",\"b\",\"c\"]", "\"B\"", null, false, null, null), "admin", default);
        for (var i = 1; i <= 16; i++)
            await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
                null, partC.Id, textsC[(i - 1) / 8].Id, i, 1, ReadingQuestionType.MultipleChoice4,
                $"PC-Q{i}", "[\"a\",\"b\",\"c\",\"d\"]", "\"C\"", null, false, null, null), "admin", default);

        var partIds = parts.Select(p => p.Id).ToList();
        foreach (var q in await db.ReadingQuestions.Where(q => partIds.Contains(q.ReadingPartId)).ToListAsync())
        {
            q.ReviewState = ReadingReviewState.Published;
            q.ExplanationMarkdown ??= "The correct answer is supported by the text.";
            q.EvidenceSentence ??= "As stated in the passage...";
        }
        await db.SaveChangesAsync();
    }

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
        await AuthorFullStructureAsync(db, "rp-1");
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
        await AuthorFullStructureAsync(db, "rp-1");
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
