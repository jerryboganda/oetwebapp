using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Recalls;

namespace OetLearner.Api.Tests;

public sealed class VocabularyRecallsPerformanceTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly CommandCapture commands = new();
    private DbContextOptions<LearnerDbContext> options = default!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(commands)
            .Options;

        await using var db = new LearnerDbContext(options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await connection.DisposeAsync();

    [Fact]
    public async Task GetToday_UsesThreeReadCommands_AndPreservesOutput()
    {
        await using var db = new LearnerDbContext(options);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.VocabularyTerms.AddRange(
            Term("term-1", "alpha", "respiratory"),
            Term("term-2", "bravo", "respiratory"),
            Term("term-3", "charlie", "medication"));
        db.LearnerVocabularies.AddRange(
            LearnerWord("user-1", "term-1", "mastered", today, reviewCount: 10, correctCount: 9, starred: true, reviewedAt: DateTimeOffset.UtcNow),
            LearnerWord("user-1", "term-2", "learning", today.AddDays(-1), reviewCount: 10, correctCount: 5, reviewedAt: DateTimeOffset.UtcNow.AddDays(-1)),
            LearnerWord("user-1", "term-3", "new", today.AddDays(1)));
        db.ReviewItems.AddRange(
            Review("review-1", "user-1", "active", today, starred: true),
            Review("review-2", "user-1", "active", today.AddDays(1)),
            Review("review-3", "user-1", "mastered", today));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        commands.Clear();

        var result = await new RecallsService(db, null!, null!)
            .GetTodayAsync("user-1", CancellationToken.None);

        Assert.Equal(3, commands.Items.Count);
        Assert.Equal(3, result.DueToday);
        Assert.Equal(2, result.Mastered);
        Assert.Equal(5, result.Total);
        Assert.Equal(2, result.Starred);
        Assert.Equal(2, result.VocabDueToday);
        Assert.Equal(1, result.ReviewDueToday);
        Assert.Equal(39, result.ReadinessScore);
        var weak = Assert.Single(result.WeakTopics);
        Assert.Equal(("respiratory", 2, 1), (weak.Topic, weak.Total, weak.WeakCount));
        Assert.Equal(2, commands.Items.Count(sql => sql.Contains("COUNT", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(commands.Items, sql =>
            sql.Contains("JOIN", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetStats_UsesThreeCommands_WithDistinctBoundedActivityDates()
    {
        await using var db = new LearnerDbContext(options);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.VocabularyTerms.AddRange(Enumerable.Range(1, 5).Select(i =>
            Term($"term-{i}", $"term{i}", "medical")));
        db.VocabularyTerms.Add(Term("archived", "archived", "medical", status: "archived"));
        db.LearnerVocabularies.AddRange(
            LearnerWord("user-1", "term-1", "mastered", today, reviewedAt: DateTimeOffset.UtcNow),
            LearnerWord("user-1", "term-2", "reviewing", today, reviewedAt: DateTimeOffset.UtcNow.AddDays(-1)),
            LearnerWord("user-1", "term-3", "learning", today.AddDays(1)),
            LearnerWord("user-1", "term-4", "new", nextReviewDate: null));
        db.VocabularyQuizResults.Add(new VocabularyQuizResult
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            TermsQuizzed = 1,
            CorrectCount = 1,
            DurationSeconds = 10,
            Format = "definition_match",
            ResultsJson = "[]",
            CompletedAt = DateTimeOffset.UtcNow.AddDays(-2),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        commands.Clear();

        var result = await Service(db).GetStatsAsync("user-1", CancellationToken.None);

        Assert.Equal(3, commands.Items.Count);
        Assert.Equal((4, 1, 1, 1, 1), (
            result.TotalInList, result.Mastered, result.Reviewing, result.Learning, result.New));
        Assert.Equal(2, result.DueToday);
        Assert.Equal(2, result.DueThisWeek);
        Assert.Equal(3, result.StreakDays);
        Assert.Equal(5, result.TotalTermsInCatalog);
        var activitySql = Assert.Single(commands.Items.Where(sql =>
            sql.Contains("VocabularyQuizResults", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("LearnerVocabularies", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("DISTINCT", activitySql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", activitySql, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetTerms_RestrictedUser_FiltersAndCountsInSql()
    {
        await using var db = new LearnerDbContext(options);
        db.VocabularyTerms.AddRange(Enumerable.Range(1, 240).Select(i =>
        {
            var term = Term($"term-{i:000}", $"term{i:000}", "medical");
            term.RecallSetCodesJson = i % 2 == 0 ? "[\"set-a\"]" : "[\"set-b\"]";
            return term;
        }));
        db.UserRecallSetAccesses.Add(new UserRecallSetAccess
        {
            Id = "access-1",
            UserId = "user-1",
            RecallSetCode = "SET-A",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        commands.Clear();

        var result = await Service(db).GetTermsAsync(
            "oet", null, null, null, page: 3, pageSize: 10,
            CancellationToken.None, userId: "user-1");

        Assert.Equal(2, commands.Items.Count);
        Assert.Equal(120, result.Total);
        Assert.Equal(10, result.Items.Count);
        Assert.All(result.Items, item => Assert.Contains("set-a", item.RecallSetCodes));
        Assert.All(commands.Items, sql =>
            Assert.Contains("UserRecallSetAccesses", sql, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(commands.Items, sql =>
            sql.Contains("EXISTS", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetRecallSets_UsesGroupedTagPayloadAggregate()
    {
        await using var db = new LearnerDbContext(options);
        var now = DateTimeOffset.UtcNow;
        db.RecallSetTags.AddRange(
            new RecallSetTag { Code = "set-a", DisplayName = "Set A", SortOrder = 1, IsActive = true, CreatedAt = now, UpdatedAt = now },
            new RecallSetTag { Code = "set-b", DisplayName = "Set B", SortOrder = 2, IsActive = true, CreatedAt = now, UpdatedAt = now });
        var first = Term("term-1", "alpha", "medical");
        first.RecallSetCodesJson = "[\"set-a\"]";
        first.IsFreePreview = true;
        var second = Term("term-2", "bravo", "medical");
        second.RecallSetCodesJson = "[\"set-a\",\"set-b\"]";
        var third = Term("term-3", "charlie", "medical");
        third.IsFreePreview = true;
        db.VocabularyTerms.AddRange(first, second, third);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        commands.Clear();

        var result = await Service(db).GetRecallSetsAsync("oet", null, CancellationToken.None);

        Assert.Equal(2, commands.Items.Count);
        Assert.Equal(2, result.FreePreviewCount);
        Assert.Equal(2, result.Sets.Single(x => x.Code == "set-a").TermCount);
        Assert.Equal(1, result.Sets.Single(x => x.Code == "set-b").TermCount);
        var tallySql = Assert.Single(commands.Items.Where(sql =>
            sql.Contains("VocabularyTerms", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("GROUP BY", tallySql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COUNT", tallySql, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetQuiz_BoundsCandidateAndDistractorReadsToTwoLimitedWindows()
    {
        await using var db = new LearnerDbContext(options);
        db.VocabularyTerms.AddRange(Enumerable.Range(1, 260).Select(i =>
            Term($"active-{i:000}", $"active{i:000}", "medical")));
        db.VocabularyTerms.AddRange(Enumerable.Range(1, 20).Select(i =>
            Term($"archived-{i:000}", $"archived{i:000}", "medical", status: "archived")));
        db.LearnerVocabularies.AddRange(Enumerable.Range(1, 40).Select(i =>
            LearnerWord("user-1", $"active-{i:000}", "learning", DateOnly.FromDateTime(DateTime.UtcNow))));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        commands.Clear();

        var result = await Service(db).GetQuizAsync(
            "user-1", 25, "definition_match", CancellationToken.None);

        Assert.Equal(4, commands.Items.Count);
        Assert.Equal(25, result.Questions.Count);
        Assert.Equal(25, result.Questions.Select(q => q.TermId).Distinct().Count());
        Assert.All(result.Questions, q => Assert.StartsWith("active-", q.TermId));
        var materializationSql = commands.Items
            .Where(sql => sql.Contains("VocabularyTerms", StringComparison.OrdinalIgnoreCase)
                          && sql.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Equal(2, materializationSql.Count);
        Assert.All(materializationSql, sql =>
        {
            Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("RANDOM", sql, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task SubmitQuiz_PreloadsLearnerRowsOnce_AndPreservesSm2Updates()
    {
        await using var db = new LearnerDbContext(options);
        db.VocabularyTerms.AddRange(Enumerable.Range(1, 8).Select(i =>
            Term($"term-{i}", $"term{i}", "medical")));
        db.LearnerVocabularies.AddRange(Enumerable.Range(1, 8).Select(i =>
            LearnerWord("user-1", $"term-{i}", "new", DateOnly.FromDateTime(DateTime.UtcNow))));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        commands.Clear();

        var answers = Enumerable.Range(1, 8)
            .Select(i => new VocabQuizAnswerV2($"term-{i}", i % 2 == 0, $"answer-{i}"))
            .ToList();
        var result = await Service(db).SubmitQuizAsync(
            "user-1",
            new VocabQuizSubmissionV2("definition_match", answers, 25),
            CancellationToken.None);

        var learnerSelects = commands.Items.Where(sql =>
            sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("LearnerVocabularies", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(learnerSelects);
        Assert.Equal((8, 4, 65), (result.TermsQuizzed, result.CorrectCount, result.XpAwarded));
        var rows = await db.LearnerVocabularies.AsNoTracking()
            .Where(x => x.UserId == "user-1")
            .OrderBy(x => x.TermId)
            .ToListAsync();
        Assert.All(rows, row => Assert.Equal(1, row.ReviewCount));
        Assert.Equal(4, rows.Count(row => row.CorrectCount == 1));
        Assert.Equal(1, await db.VocabularyQuizResults.CountAsync());
    }

    [Fact]
    public async Task MyVocabulary_PagedEnvelopeIsBounded_WhileLegacyListRemainsAnArray()
    {
        await using var db = new LearnerDbContext(options);
        db.VocabularyTerms.AddRange(Enumerable.Range(1, 45).Select(i =>
            Term($"term-{i:00}", $"term{i:00}", "medical")));
        db.LearnerVocabularies.AddRange(Enumerable.Range(1, 45).Select(i =>
            LearnerWord("user-1", $"term-{i:00}", "new", DateOnly.FromDateTime(DateTime.UtcNow))));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        commands.Clear();

        var page = await Service(db).GetMyVocabularyPageAsync(
            "user-1", null, page: 2, pageSize: 20, CancellationToken.None);

        Assert.Equal(2, commands.Items.Count);
        Assert.Equal(45, page.Total);
        Assert.Equal(20, page.Items.Count);
        Assert.Empty(db.ChangeTracker.Entries());

        commands.Clear();
        var legacy = await Service(db).GetMyVocabularyAsync(
            "user-1", null, CancellationToken.None);
        Assert.Single(commands.Items);
        Assert.Equal(45, legacy.Count);
        Assert.Equal(JsonValueKind.Array, JsonSerializer.SerializeToElement(legacy).ValueKind);
    }

    private static VocabularyService Service(LearnerDbContext db)
        => new(db, new Sm2Scheduler());

    private static VocabularyTerm Term(
        string id,
        string term,
        string category,
        string status = "active")
        => new()
        {
            Id = id,
            Term = term,
            Definition = $"definition-{term}",
            ExampleSentence = $"Example using {term}.",
            ExamTypeCode = "oet",
            Category = category,
            Status = status,
            IsFreePreview = true,
        };

    private static LearnerVocabulary LearnerWord(
        string userId,
        string termId,
        string mastery,
        DateOnly? nextReviewDate,
        int reviewCount = 0,
        int correctCount = 0,
        bool starred = false,
        DateTimeOffset? reviewedAt = null)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TermId = termId,
            Mastery = mastery,
            EaseFactor = 2.5,
            IntervalDays = 1,
            ReviewCount = reviewCount,
            CorrectCount = correctCount,
            NextReviewDate = nextReviewDate,
            LastReviewedAt = reviewedAt,
            AddedAt = DateTimeOffset.UtcNow,
            Starred = starred,
        };

    private static ReviewItem Review(
        string id,
        string userId,
        string status,
        DateOnly dueDate,
        bool starred = false)
        => new()
        {
            Id = id,
            UserId = userId,
            ExamTypeCode = "oet",
            SourceType = "vocabulary",
            SubtestCode = "reading",
            QuestionJson = "{}",
            AnswerJson = "{}",
            DueDate = dueDate,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = status,
            Starred = starred,
        };

    private sealed class CommandCapture : DbCommandInterceptor
    {
        public List<string> Items { get; } = [];

        public void Clear() => Items.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Items.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Items.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
