using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

/// <summary>
/// Tests for the unified spaced-repetition service behaviour:
/// — Unified queue (native + vocabulary projection)
/// — Summary combining both silos
/// — Suspend / resume / undo lifecycle
/// — Retention + heatmap projections
/// — Vocabulary projection routing on submit
/// </summary>
public class SpacedRepetitionServiceTests
{
    private const string UserId = "user-1";

    private static (LearnerDbContext db, SpacedRepetitionService svc, VocabularyService vocab, ReviewItemSeeder seeder) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var scheduler = new Sm2Scheduler();
        var vocabularyService = new VocabularyService(db, scheduler);
        var service = new SpacedRepetitionService(db, scheduler, vocabularyService);
        var seeder = new ReviewItemSeeder(db, NullLogger<ReviewItemSeeder>.Instance);
        return (db, service, vocabularyService, seeder);
    }

    private static JsonElement ToJson(object obj)
        => JsonSerializer.SerializeToElement(obj);

    [Fact]
    public async Task GetReviewSummary_combines_native_and_vocabulary_counters()
    {
        var (db, svc, _, seeder) = Build();

        // Seed native items (1 due today + 1 mastered)
        await seeder.SeedGrammarErrorAsync(UserId, "oet", "gl-1", "ex-1",
            "Title", "Q", "A", "E", "fill_blank", default);
        var masteredNative = new ReviewItem
        {
            Id = "ri-m",
            UserId = UserId,
            ExamTypeCode = "oet",
            SourceType = ReviewSourceTypes.GrammarError,
            SourceId = "gl-1:ex-2",
            SubtestCode = "grammar",
            QuestionJson = "{}",
            AnswerJson = "{}",
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            Status = "mastered",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ReviewItems.Add(masteredNative);

        // Seed vocabulary: 1 due, 1 mastered
        db.VocabularyTerms.Add(new VocabularyTerm
        {
            Id = "vt-1",
            Term = "dyspnoea",
            Definition = "Difficulty breathing.",
            ExampleSentence = "She had dyspnoea on exertion.",
            ExamTypeCode = "oet",
            Category = "symptoms",
            Difficulty = "medium",
            Status = "active",
        });
        db.LearnerVocabularies.Add(new LearnerVocabulary
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            TermId = "vt-1",
            Mastery = "learning",
            NextReviewDate = DateOnly.FromDateTime(DateTime.UtcNow),
            AddedAt = DateTimeOffset.UtcNow,
        });
        db.LearnerVocabularies.Add(new LearnerVocabulary
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            TermId = "vt-1",
            Mastery = "mastered",
            NextReviewDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(60)),
            AddedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var summary = ToJson(await svc.GetReviewSummaryAsync(UserId, default));

        // 1 active native + 1 mastered native + 1 learning vocab + 1 mastered vocab = 3 (excludes mastered)? No: total = all non-mastered.
        // native.total counts only active native -> 1; mastered not in total. vocab.total counts mastery != mastered -> 1. Sum = 2.
        Assert.Equal(2, summary.GetProperty("total").GetInt32());
        Assert.Equal(2, summary.GetProperty("dueToday").GetInt32());
        Assert.Equal(2, summary.GetProperty("mastered").GetInt32());
    }

    [Fact]
    public async Task GetDueItems_includes_vocabulary_projection_when_enabled()
    {
        var (db, svc, _, seeder) = Build();

        await seeder.SeedReadingMissAsync(UserId, "oet", "paper", "q1",
            "Q1", "What?", "X", "because...", "A", default);
        db.VocabularyTerms.Add(new VocabularyTerm
        {
            Id = "vt-1",
            Term = "stenosis",
            Definition = "Narrowing.",
            ExampleSentence = "Aortic stenosis.",
            ExamTypeCode = "oet",
            Category = "symptoms",
            Difficulty = "medium",
            Status = "active",
        });
        db.LearnerVocabularies.Add(new LearnerVocabulary
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            TermId = "vt-1",
            Mastery = "learning",
            NextReviewDate = DateOnly.FromDateTime(DateTime.UtcNow),
            AddedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var resultIncluded = await svc.GetDueItemsAsync(UserId, 10, null, null, includeVocabulary: true, default);
        var resultExcluded = await svc.GetDueItemsAsync(UserId, 10, null, null, includeVocabulary: false, default);

        // Each response is a list of anonymous objects — count via reflection.
        var included = ((System.Collections.IEnumerable)resultIncluded).Cast<object>().ToList();
        var excluded = ((System.Collections.IEnumerable)resultExcluded).Cast<object>().ToList();

        Assert.Equal(2, included.Count);
        Assert.Single(excluded);
    }

    [Fact]
    public async Task GetDueItems_filters_by_source_type()
    {
        var (db, svc, _, seeder) = Build();

        await seeder.SeedReadingMissAsync(UserId, "oet", "paper", "q1",
            "R1", "?", "X", "because", "A", default);
        await seeder.SeedListeningMissAsync(UserId, "oet", "att", "q1",
            "L1", "?", "X", null, default);

        var onlyReading = await svc.GetDueItemsAsync(UserId, 20, ReviewSourceTypes.ReadingMiss, null, includeVocabulary: false, default);
        var items = ((System.Collections.IEnumerable)onlyReading).Cast<object>().ToList();
        Assert.Single(items);
    }

    [Fact]
    public async Task Submit_rating_advances_schedule_and_records_transition()
    {
        var (db, svc, _, seeder) = Build();
        var seed = await seeder.SeedGrammarErrorAsync(UserId, "oet", "gl", "ex",
            "T", "Q", "A", "E", null, default);
        var originalDue = DateOnly.FromDateTime(DateTime.UtcNow);

        await svc.SubmitReviewAsync(UserId, seed.Id, quality: 5, default);

        var item = await db.ReviewItems.SingleAsync();
        Assert.Equal(1, item.ReviewCount);
        Assert.True(item.DueDate >= originalDue);
        Assert.Equal(5, item.LastQuality);

        var transitions = await db.ReviewItemTransitions.Where(t => t.ReviewItemId == seed.Id).ToListAsync();
        Assert.Single(transitions);
    }

    [Fact]
    public async Task Submit_then_Undo_reverts_the_last_transition()
    {
        var (db, svc, _, seeder) = Build();
        var seed = await seeder.SeedGrammarErrorAsync(UserId, "oet", "gl", "ex",
            "T", "Q", "A", "E", null, default);

        await svc.SubmitReviewAsync(UserId, seed.Id, 5, default);
        var afterSubmit = await db.ReviewItems.SingleAsync();
        var advancedInterval = afterSubmit.IntervalDays;
        var advancedReviewCount = afterSubmit.ReviewCount;
        Assert.Equal(1, advancedReviewCount);

        await svc.UndoLastAsync(UserId, seed.Id, default);

        var afterUndo = await db.ReviewItems.SingleAsync();
        Assert.Equal(0, afterUndo.ReviewCount);
        Assert.Null(afterUndo.LastQuality);
        Assert.Empty(db.ReviewItemTransitions);
        // Prev interval should have been 1 (the default seed interval).
        Assert.Equal(1, afterUndo.IntervalDays);
    }

    [Fact]
    public async Task Suspend_and_Resume_toggle_the_status()
    {
        var (db, svc, _, seeder) = Build();
        var seed = await seeder.SeedGrammarErrorAsync(UserId, "oet", "gl", "ex",
            "T", "Q", "A", "E", null, default);

        await svc.SuspendReviewItemAsync(UserId, seed.Id, "not relevant", default);
        var suspended = await db.ReviewItems.SingleAsync();
        Assert.Equal("suspended", suspended.Status);
        Assert.Equal("not relevant", suspended.SuspendedReason);
        Assert.NotNull(suspended.SuspendedAt);

        await svc.ResumeReviewItemAsync(UserId, seed.Id, default);
        var resumed = await db.ReviewItems.SingleAsync();
        Assert.Equal("active", resumed.Status);
        Assert.Null(resumed.SuspendedReason);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), resumed.DueDate);
    }

    [Fact]
    public async Task Submit_refuses_to_rate_suspended_item()
    {
        var (db, svc, _, seeder) = Build();
        var seed = await seeder.SeedGrammarErrorAsync(UserId, "oet", "gl", "ex",
            "T", "Q", "A", "E", null, default);
        await svc.SuspendReviewItemAsync(UserId, seed.Id, null, default);

        await Assert.ThrowsAsync<ApiException>(() =>
            svc.SubmitReviewAsync(UserId, seed.Id, 5, default));
    }

    [Fact]
    public async Task Submit_routes_vocabulary_projection_through_vocabulary_service()
    {
        var (db, svc, _, _) = Build();

        db.VocabularyTerms.Add(new VocabularyTerm
        {
            Id = "vt-1",
            Term = "stenosis",
            Definition = "Narrowing.",
            ExampleSentence = "Aortic stenosis.",
            ExamTypeCode = "oet",
            Category = "symptoms",
            Difficulty = "medium",
            Status = "active",
        });
        var lv = new LearnerVocabulary
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            TermId = "vt-1",
            Mastery = "learning",
            EaseFactor = 2.5,
            IntervalDays = 1,
            ReviewCount = 0,
            CorrectCount = 0,
            NextReviewDate = DateOnly.FromDateTime(DateTime.UtcNow),
            AddedAt = DateTimeOffset.UtcNow,
        };
        db.LearnerVocabularies.Add(lv);
        await db.SaveChangesAsync();

        var virtualId = $"ri-v-{lv.Id:N}";
        var response = await svc.SubmitReviewAsync(UserId, virtualId, 4, default);

        // Verify LearnerVocabulary row was updated via VocabularyService.
        var updated = await db.LearnerVocabularies.SingleAsync();
        Assert.Equal(1, updated.ReviewCount);
    }

    [Fact]
    public async Task Retention_returns_a_time_series_bucketed_by_day()
    {
        var (db, svc, _, seeder) = Build();
        var seed = await seeder.SeedGrammarErrorAsync(UserId, "oet", "gl", "ex",
            "T", "Q", "A", "E", null, default);
        await svc.SubmitReviewAsync(UserId, seed.Id, 4, default);

        var result = ToJson(await svc.GetRetentionAsync(UserId, 30, default));
        Assert.Equal(30, result.GetProperty("days").GetInt32());
        var series = result.GetProperty("series");
        Assert.Equal(JsonValueKind.Array, series.ValueKind);
        Assert.Equal(30, series.GetArrayLength());
    }

    [Fact]
    public async Task Heatmap_groups_by_source_subtest_and_criterion()
    {
        var (db, svc, _, seeder) = Build();
        await seeder.SeedReadingMissAsync(UserId, "oet", "paper", "q1",
            "R1", "?", "X", "E", "A", default);
        await seeder.SeedReadingMissAsync(UserId, "oet", "paper", "q2",
            "R2", "?", "X", "E", "A", default);
        await seeder.SeedListeningMissAsync(UserId, "oet", "att", "q1",
            "L1", "?", "X", null, default);

        var heatmap = ToJson(await svc.GetHeatmapAsync(UserId, default));
        var cells = heatmap.GetProperty("cells");
        Assert.Equal(JsonValueKind.Array, cells.ValueKind);
        // Expect at least two distinct cells (reading and listening).
        Assert.True(cells.GetArrayLength() >= 2);
    }

    [Fact]
    public async Task Invalid_quality_rejected()
    {
        var (db, svc, _, seeder) = Build();
        var seed = await seeder.SeedGrammarErrorAsync(UserId, "oet", "gl", "ex",
            "T", "Q", "A", "E", null, default);

        await Assert.ThrowsAsync<ApiException>(() =>
            svc.SubmitReviewAsync(UserId, seed.Id, 99, default));
    }
}
