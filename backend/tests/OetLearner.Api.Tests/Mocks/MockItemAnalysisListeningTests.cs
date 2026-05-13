using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests.Mocks;

/// <summary>
/// Mocks Wave 4 closure (May 2026). Locks the Listening leg of
/// <see cref="MockItemAnalysisService"/> introduced for the new admin
/// endpoint <c>GET /v1/admin/mocks/{bundleId}/listening-item-analysis</c>.
///
/// Coverage:
///   • <see cref="MockItemAnalysisService.RecomputeAsync"/> walks listening
///     sections and produces one snapshot per <see cref="ListeningQuestion"/>.
///   • Difficulty (proportion correct) and distractor counts come from
///     <see cref="ListeningAnswer"/> rows attached to Submitted attempts only.
///   • Flagging heuristics (too_easy / too_hard / tempting_distractor) reuse
///     the Reading thresholds (N≥30, ≥0.95, ≤0.20, ≥40% of incorrect).
///   • In-progress attempts are excluded.
///   • <see cref="MockItemAnalysisService.GetForBundleListeningAsync"/>
///     returns only listening rows, sorted by label.
/// </summary>
public class MockItemAnalysisListeningTests
{
    private const string BundleId = "mock-bundle-listening-w4";
    private const string ListeningPaperId = "paper-listening-w4";
    private const string ReadingPaperId = "paper-reading-w4";

    private static LearnerDbContext NewDb(string? name = null) =>
        new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task RecomputeAsync_WritesListeningSnapshots_PerQuestion()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;

        SeedBundleWithListeningSection(db, now);
        var (qShort, qMcq) = SeedListeningPartAndTwoQuestions(db, now);

        // 5 submitted attempts; q1 = 4/5 correct, q2 = 2/5 correct (3 wrong: 2 ReusedKeyword, 1 TooStrong).
        SeedAttemptsAndAnswers(db, now, qShort.Id, qMcq.Id, totalAttempts: 5,
            shortCorrect: 4,
            mcqCorrect: 2,
            distractors: new[]
            {
                ListeningDistractorCategory.ReusedKeyword,
                ListeningDistractorCategory.ReusedKeyword,
                ListeningDistractorCategory.TooStrong,
            });
        await db.SaveChangesAsync();

        var service = new MockItemAnalysisService(db);
        await service.RecomputeAsync(BundleId, "admin-1", CancellationToken.None);

        var snapshots = await db.MockItemAnalysisSnapshots
            .Where(s => s.MockBundleId == BundleId && s.SubtestCode == "listening")
            .OrderBy(s => s.Label)
            .ToListAsync();

        Assert.Equal(2, snapshots.Count);

        var first = snapshots[0];
        Assert.Equal("Listening A1 · Q1", first.Label);
        Assert.Equal(5, first.TotalAttempts);
        Assert.Equal(4, first.CorrectCount);
        Assert.InRange(first.Difficulty, 0.799, 0.801);
        // Short-answer item — distractor map is empty by design.
        Assert.Equal("{}", first.DistractorJson);
        Assert.Null(first.Flag);

        var second = snapshots[1];
        Assert.Equal("Listening B · Q2", second.Label);
        Assert.Equal(5, second.TotalAttempts);
        Assert.Equal(2, second.CorrectCount);
        Assert.InRange(second.Difficulty, 0.399, 0.401);
        var distractor = JsonSerializer.Deserialize<Dictionary<string, int>>(second.DistractorJson)!;
        Assert.Equal(2, distractor["ReusedKeyword"]);
        Assert.Equal(1, distractor["TooStrong"]);
        // N=5 < 30 and incorrect=3 < 10 → no tempting_distractor flag yet.
        Assert.Null(second.Flag);

        var audit = await db.AuditEvents.FirstOrDefaultAsync(a =>
            a.Action == "mock_item_analysis_recomputed" && a.ResourceId == BundleId);
        Assert.NotNull(audit);
    }

    [Fact]
    public async Task RecomputeAsync_IgnoresInProgressListeningAttempts()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;

        SeedBundleWithListeningSection(db, now);
        var (qShort, _) = SeedListeningPartAndTwoQuestions(db, now);

        // 2 submitted (both correct) + 3 in-progress (all wrong) — only the 2 should count.
        for (var i = 0; i < 2; i++)
        {
            var aid = $"attempt-submitted-{i}";
            db.Set<ListeningAttempt>().Add(new ListeningAttempt
            {
                Id = aid,
                UserId = $"user-{i}",
                PaperId = ListeningPaperId,
                Status = ListeningAttemptStatus.Submitted,
                StartedAt = now.AddMinutes(-30),
                LastActivityAt = now,
                MaxRawScore = 42,
            });
            db.Set<ListeningAnswer>().Add(new ListeningAnswer
            {
                Id = $"answer-submitted-{i}",
                ListeningAttemptId = aid,
                ListeningQuestionId = qShort.Id,
                IsCorrect = true,
                PointsEarned = 1,
                AnsweredAt = now,
            });
        }
        for (var i = 0; i < 3; i++)
        {
            var aid = $"attempt-inprogress-{i}";
            db.Set<ListeningAttempt>().Add(new ListeningAttempt
            {
                Id = aid,
                UserId = $"user-ip-{i}",
                PaperId = ListeningPaperId,
                Status = ListeningAttemptStatus.InProgress,
                StartedAt = now,
                LastActivityAt = now,
                MaxRawScore = 42,
            });
            db.Set<ListeningAnswer>().Add(new ListeningAnswer
            {
                Id = $"answer-inprogress-{i}",
                ListeningAttemptId = aid,
                ListeningQuestionId = qShort.Id,
                IsCorrect = false,
                PointsEarned = 0,
                AnsweredAt = now,
            });
        }
        await db.SaveChangesAsync();

        var service = new MockItemAnalysisService(db);
        await service.RecomputeAsync(BundleId, "admin-1", CancellationToken.None);

        var snapshot = await db.MockItemAnalysisSnapshots
            .FirstAsync(s => s.MockBundleId == BundleId && s.Label == "Listening A1 · Q1");
        Assert.Equal(2, snapshot.TotalAttempts);
        Assert.Equal(2, snapshot.CorrectCount);
        Assert.Equal(1.0d, snapshot.Difficulty);
        // N=2 < 30 → no flag despite 100% correct.
        Assert.Null(snapshot.Flag);
    }

    [Fact]
    public async Task RecomputeAsync_ComputesDiscriminationIndex_FromTopAndBottomScoreCohorts()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;

        SeedBundleWithListeningSection(db, now);
        var (qShort, _) = SeedListeningPartAndTwoQuestions(db, now);

        for (var i = 0; i < 10; i++)
        {
            var aid = $"attempt-discrimination-{i}";
            db.Set<ListeningAttempt>().Add(new ListeningAttempt
            {
                Id = aid,
                UserId = $"user-d-{i}",
                PaperId = ListeningPaperId,
                Status = ListeningAttemptStatus.Submitted,
                StartedAt = now.AddMinutes(-30),
                LastActivityAt = now,
                RawScore = 42 - i,
                MaxRawScore = 42,
            });
            db.Set<ListeningAnswer>().Add(new ListeningAnswer
            {
                Id = $"answer-discrimination-{i}",
                ListeningAttemptId = aid,
                ListeningQuestionId = qShort.Id,
                IsCorrect = i < 3,
                PointsEarned = i < 3 ? 1 : 0,
                AnsweredAt = now,
            });
        }
        await db.SaveChangesAsync();

        var service = new MockItemAnalysisService(db);
        await service.RecomputeAsync(BundleId, "admin-1", CancellationToken.None);

        var snapshot = await db.MockItemAnalysisSnapshots
            .FirstAsync(s => s.MockBundleId == BundleId && s.Label == "Listening A1 · Q1");

        Assert.NotNull(snapshot.DiscriminationIndex);
        Assert.InRange(snapshot.DiscriminationIndex.Value, 0.999, 1.001);
    }

    [Fact]
    public async Task RecomputeAsync_FlagsTooEasy_When30PlusAttemptsAndDifficultyHigh()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;

        SeedBundleWithListeningSection(db, now);
        var (qShort, _) = SeedListeningPartAndTwoQuestions(db, now);

        // 30 submitted attempts, all correct on q1 → too_easy.
        for (var i = 0; i < 30; i++)
        {
            var aid = $"attempt-easy-{i}";
            db.Set<ListeningAttempt>().Add(new ListeningAttempt
            {
                Id = aid,
                UserId = $"user-e-{i}",
                PaperId = ListeningPaperId,
                Status = ListeningAttemptStatus.Submitted,
                StartedAt = now.AddMinutes(-30),
                LastActivityAt = now,
                MaxRawScore = 42,
            });
            db.Set<ListeningAnswer>().Add(new ListeningAnswer
            {
                Id = $"answer-easy-{i}",
                ListeningAttemptId = aid,
                ListeningQuestionId = qShort.Id,
                IsCorrect = true,
                PointsEarned = 1,
                AnsweredAt = now,
            });
        }
        await db.SaveChangesAsync();

        var service = new MockItemAnalysisService(db);
        await service.RecomputeAsync(BundleId, "admin-1", CancellationToken.None);

        var snapshot = await db.MockItemAnalysisSnapshots
            .FirstAsync(s => s.Label == "Listening A1 · Q1");
        Assert.Equal("too_easy", snapshot.Flag);
    }

    [Fact]
    public async Task RecomputeAsync_FlagsTemptingDistractor_WhenSingleCategoryDominates()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;

        SeedBundleWithListeningSection(db, now);
        var (_, qMcq) = SeedListeningPartAndTwoQuestions(db, now);

        // 12 submitted attempts on q2: 1 correct, 11 wrong; 5 ReusedKeyword + 5 TooStrong + 1 OutOfScope.
        // incorrect=11, top distractor count=5, ratio=5/11≈0.45 ≥ 0.40 → tempting_distractor.
        var wrongs = new List<ListeningDistractorCategory>();
        for (var i = 0; i < 5; i++) wrongs.Add(ListeningDistractorCategory.ReusedKeyword);
        for (var i = 0; i < 5; i++) wrongs.Add(ListeningDistractorCategory.TooStrong);
        wrongs.Add(ListeningDistractorCategory.OutOfScope);

        for (var i = 0; i < 12; i++)
        {
            var aid = $"attempt-tempt-{i}";
            db.Set<ListeningAttempt>().Add(new ListeningAttempt
            {
                Id = aid,
                UserId = $"user-t-{i}",
                PaperId = ListeningPaperId,
                Status = ListeningAttemptStatus.Submitted,
                StartedAt = now.AddMinutes(-30),
                LastActivityAt = now,
                MaxRawScore = 42,
            });

            var isCorrect = i == 0;
            db.Set<ListeningAnswer>().Add(new ListeningAnswer
            {
                Id = $"answer-tempt-{i}",
                ListeningAttemptId = aid,
                ListeningQuestionId = qMcq.Id,
                IsCorrect = isCorrect,
                PointsEarned = isCorrect ? 1 : 0,
                SelectedDistractorCategory = isCorrect ? null : wrongs[i - 1],
                AnsweredAt = now,
            });
        }
        await db.SaveChangesAsync();

        var service = new MockItemAnalysisService(db);
        await service.RecomputeAsync(BundleId, "admin-1", CancellationToken.None);

        var snapshot = await db.MockItemAnalysisSnapshots
            .FirstAsync(s => s.Label == "Listening B · Q2");
        Assert.Equal("tempting_distractor", snapshot.Flag);
        var distractor = JsonSerializer.Deserialize<Dictionary<string, int>>(snapshot.DistractorJson)!;
        Assert.Equal(5, distractor["ReusedKeyword"]);
        Assert.Equal(5, distractor["TooStrong"]);
        Assert.Equal(1, distractor["OutOfScope"]);
    }

    [Fact]
    public async Task GetForBundleListeningAsync_ReturnsOnlyListeningRows_OrderedByLabel()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;

        SeedBundleWithListeningSection(db, now);
        SeedListeningPartAndTwoQuestions(db, now);

        // Manually add a stale reading row to confirm the filter excludes it.
        db.MockItemAnalysisSnapshots.Add(new MockItemAnalysisSnapshot
        {
            Id = "snap-reading-noise",
            MockBundleId = BundleId,
            ItemId = "rq-1",
            SubtestCode = "reading",
            Label = "Reading A · Q1",
            TotalAttempts = 0,
            CorrectCount = 0,
            Difficulty = 0d,
            DistractorJson = "{}",
            GeneratedAt = now,
        });
        await db.SaveChangesAsync();

        var service = new MockItemAnalysisService(db);
        await service.RecomputeAsync(BundleId, "admin-1", CancellationToken.None);

        var payload = await service.GetForBundleListeningAsync(BundleId, CancellationToken.None);

        // anonymous-typed payload — read via reflection.
        var subtest = (string)payload.GetType().GetProperty("subtest")!.GetValue(payload)!;
        Assert.Equal("listening", subtest);

        var items = (Array)payload.GetType().GetProperty("items")!.GetValue(payload)!;
        Assert.Equal(2, items.Length);

        var labels = items.Cast<object>()
            .Select(item => (string)item.GetType().GetProperty("label")!.GetValue(item)!)
            .ToArray();
        Assert.Equal(new[] { "Listening A1 · Q1", "Listening B · Q2" }, labels);
        Assert.DoesNotContain(labels, l => l.StartsWith("Reading", StringComparison.Ordinal));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Seed helpers
    // ─────────────────────────────────────────────────────────────────────

    private static void SeedBundleWithListeningSection(LearnerDbContext db, DateTimeOffset now)
    {
        const string provenance = "Wave 4 listening item-analysis test seed.";

        db.ContentPapers.Add(new ContentPaper
        {
            Id = ListeningPaperId,
            SubtestCode = "listening",
            Title = "Listening Wave 4 paper",
            Slug = ListeningPaperId,
            AppliesToAllProfessions = true,
            Difficulty = "standard",
            EstimatedDurationMinutes = 42,
            Status = ContentStatus.Published,
            SourceProvenance = provenance,
            CreatedByAdminId = "admin-1",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });
        db.ContentPapers.Add(new ContentPaper
        {
            Id = ReadingPaperId,
            SubtestCode = "reading",
            Title = "Reading Wave 4 paper",
            Slug = ReadingPaperId,
            AppliesToAllProfessions = true,
            Difficulty = "standard",
            EstimatedDurationMinutes = 60,
            Status = ContentStatus.Published,
            SourceProvenance = provenance,
            CreatedByAdminId = "admin-1",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });

        db.MockBundles.Add(new MockBundle
        {
            Id = BundleId,
            Title = "Wave 4 Listening Bundle",
            Slug = "wave-4-listening-bundle",
            MockType = "full",
            AppliesToAllProfessions = true,
            Status = ContentStatus.Published,
            EstimatedDurationMinutes = 102,
            SourceProvenance = provenance,
            CreatedByAdminId = "admin-1",
            UpdatedByAdminId = "admin-1",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });

        db.MockBundleSections.Add(new MockBundleSection
        {
            Id = "section-listening-w4",
            MockBundleId = BundleId,
            SectionOrder = 1,
            SubtestCode = "listening",
            ContentPaperId = ListeningPaperId,
            TimeLimitMinutes = 42,
            ReviewEligible = false,
            IsRequired = true,
            CreatedAt = now,
        });
        db.MockBundleSections.Add(new MockBundleSection
        {
            Id = "section-reading-w4",
            MockBundleId = BundleId,
            SectionOrder = 2,
            SubtestCode = "reading",
            ContentPaperId = ReadingPaperId,
            TimeLimitMinutes = 60,
            ReviewEligible = false,
            IsRequired = true,
            CreatedAt = now,
        });
    }

    private static (ListeningQuestion shortAnswer, ListeningQuestion mcq) SeedListeningPartAndTwoQuestions(
        LearnerDbContext db, DateTimeOffset now)
    {
        var partA1 = new ListeningPart
        {
            Id = "part-a1-w4",
            PaperId = ListeningPaperId,
            PartCode = ListeningPartCode.A1,
            MaxRawScore = 12,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var partB = new ListeningPart
        {
            Id = "part-b-w4",
            PaperId = ListeningPaperId,
            PartCode = ListeningPartCode.B,
            MaxRawScore = 6,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Set<ListeningPart>().AddRange(partA1, partB);

        var q1 = new ListeningQuestion
        {
            Id = "lq-1-w4",
            PaperId = ListeningPaperId,
            ListeningPartId = partA1.Id,
            QuestionNumber = 1,
            DisplayOrder = 1,
            QuestionType = ListeningQuestionType.ShortAnswer,
            Stem = "Type the missing word",
            CorrectAnswerJson = "\"diuretic\"",
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var q2 = new ListeningQuestion
        {
            Id = "lq-2-w4",
            PaperId = ListeningPaperId,
            ListeningPartId = partB.Id,
            QuestionNumber = 2,
            DisplayOrder = 1,
            QuestionType = ListeningQuestionType.MultipleChoice3,
            Stem = "What does the consultant suggest?",
            CorrectAnswerJson = "\"A\"",
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Set<ListeningQuestion>().AddRange(q1, q2);
        return (q1, q2);
    }

    private static void SeedAttemptsAndAnswers(
        LearnerDbContext db,
        DateTimeOffset now,
        string shortQuestionId,
        string mcqQuestionId,
        int totalAttempts,
        int shortCorrect,
        int mcqCorrect,
        IReadOnlyList<ListeningDistractorCategory> distractors)
    {
        if (distractors.Count != totalAttempts - mcqCorrect)
        {
            throw new InvalidOperationException(
                "Distractor categories must match the number of incorrect MCQ answers.");
        }

        var distractorIndex = 0;
        for (var i = 0; i < totalAttempts; i++)
        {
            var aid = $"attempt-{i}";
            db.Set<ListeningAttempt>().Add(new ListeningAttempt
            {
                Id = aid,
                UserId = $"user-{i}",
                PaperId = ListeningPaperId,
                Status = ListeningAttemptStatus.Submitted,
                StartedAt = now.AddMinutes(-30),
                LastActivityAt = now,
                MaxRawScore = 42,
            });

            var shortIsCorrect = i < shortCorrect;
            db.Set<ListeningAnswer>().Add(new ListeningAnswer
            {
                Id = $"answer-short-{i}",
                ListeningAttemptId = aid,
                ListeningQuestionId = shortQuestionId,
                IsCorrect = shortIsCorrect,
                PointsEarned = shortIsCorrect ? 1 : 0,
                AnsweredAt = now,
            });

            var mcqIsCorrect = i < mcqCorrect;
            db.Set<ListeningAnswer>().Add(new ListeningAnswer
            {
                Id = $"answer-mcq-{i}",
                ListeningAttemptId = aid,
                ListeningQuestionId = mcqQuestionId,
                IsCorrect = mcqIsCorrect,
                PointsEarned = mcqIsCorrect ? 1 : 0,
                SelectedDistractorCategory = mcqIsCorrect ? null : distractors[distractorIndex++],
                AnsweredAt = now,
            });
        }
    }
}
