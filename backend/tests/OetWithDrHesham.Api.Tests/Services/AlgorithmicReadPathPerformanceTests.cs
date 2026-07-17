using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.Billing;

namespace OetWithDrHesham.Api.Tests.Services;

public sealed class AlgorithmicReadPathPerformanceTests
{
    [Fact]
    public async Task AdaptiveContent_UsesBoundedStableOrderSampling_AndPreservesFallbackCardinality()
    {
        await using var database = await TestDatabase.CreateAsync();
        var db = database.Db;
        var now = DateTimeOffset.UtcNow;

        db.ContentItems.AddRange(
            CreateContent("adaptive-01", 1450, ContentStatus.Published, "OET", "reading", now),
            CreateContent("adaptive-02", 1550, ContentStatus.Published, "OET", "reading", now),
            CreateContent("adaptive-03", 800, ContentStatus.Published, "OET", "reading", now),
            CreateContent("adaptive-04", 900, ContentStatus.Published, "OET", "reading", now),
            CreateContent("adaptive-05", 2100, ContentStatus.Published, "OET", "reading", now),
            CreateContent("adaptive-06", 2200, ContentStatus.Published, "OET", "reading", now),
            CreateContent("wrong-exam", 1500, ContentStatus.Published, "IELTS", "reading", now),
            CreateContent("wrong-subtest", 1500, ContentStatus.Published, "OET", "writing", now),
            CreateContent("draft", 1500, ContentStatus.Draft, "OET", "reading", now));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        database.Sql.Clear();

        var response = await new AdaptiveDifficultyService(db)
            .GetAdaptiveContentAsync("adaptive-user", "OET", "reading", 5, CancellationToken.None);
        var items = JsonSerializer.SerializeToElement(response).EnumerateArray().ToArray();

        Assert.Equal(5, items.Length);
        Assert.Equal(5, items.Select(item => item.GetProperty("id").GetString()).Distinct().Count());
        Assert.All(items, item => Assert.Equal("reading", item.GetProperty("subtestCode").GetString()));
        Assert.Equal(
            2,
            items.Count(item => item.GetProperty("difficulty").GetInt32() is >= 1300 and <= 1700));

        var contentCommands = database.Sql.SelectCommandsFor("ContentItems");
        Assert.InRange(contentCommands.Count, 4, 6);
        Assert.DoesNotContain(
            contentCommands,
            command =>
                command.Contains("random(", StringComparison.OrdinalIgnoreCase) ||
                command.Contains("newid(", StringComparison.OrdinalIgnoreCase) ||
                command.Contains("gen_random_uuid(", StringComparison.OrdinalIgnoreCase));

        var materializationCommands = contentCommands
            .Where(command => !command.Contains("COUNT(", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.All(materializationCommands, command =>
        {
            Assert.Contains("ORDER BY", command, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"Id\"", command, StringComparison.Ordinal);
            Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task ReviewSummary_UsesOneConditionalAggregate_AndPreservesBuckets()
    {
        await using var database = await TestDatabase.CreateAsync();
        var db = database.Db;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        db.ReviewItems.AddRange(
            CreateReviewItem("past", "review-user", "active", today.AddDays(-1)),
            CreateReviewItem("today", "review-user", "active", today),
            CreateReviewItem("upcoming", "review-user", "active", today.AddDays(3)),
            CreateReviewItem("later", "review-user", "active", today.AddDays(10)),
            CreateReviewItem("mastered", "review-user", "mastered", today),
            CreateReviewItem("other-user", "other-user", "active", today));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        database.Sql.Clear();

        var response = await new SpacedRepetitionService(db, new Sm2Scheduler())
            .GetReviewSummaryAsync("review-user", CancellationToken.None);
        var summary = JsonSerializer.SerializeToElement(response);

        Assert.Equal(4, summary.GetProperty("total").GetInt32());
        Assert.Equal(2, summary.GetProperty("due").GetInt32());
        Assert.Equal(1, summary.GetProperty("dueToday").GetInt32());
        Assert.Equal(1, summary.GetProperty("mastered").GetInt32());
        Assert.Equal(1, summary.GetProperty("upcoming").GetInt32());

        var command = Assert.Single(database.Sql.SelectCommandsFor("ReviewItems"));
        Assert.Contains("COUNT", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CASE", command, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AchievementCheck_UsesTwoSetBasedReads_AndRemainsIdempotent()
    {
        await using var database = await TestDatabase.CreateAsync();
        var db = database.Db;
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        db.Users.Add(CreateUser("achievement-user", now));
        db.LearnerXPs.Add(new LearnerXP
        {
            UserId = "achievement-user",
            TotalXP = 100,
            WeeklyXP = 100,
            MonthlyXP = 100,
            Level = 2,
            WeekStartDate = today,
            MonthStartDate = new DateOnly(today.Year, today.Month, 1)
        });
        db.LearnerStreaks.Add(new LearnerStreak
        {
            UserId = "achievement-user",
            CurrentStreak = 5,
            LongestStreak = 5,
            LastActiveDate = today
        });
        db.Attempts.AddRange(
            CreateAttempt("achievement-attempt-1", "achievement-user", "reading", now.AddDays(-2)),
            CreateAttempt("achievement-attempt-2", "achievement-user", "reading", now.AddDays(-1)));
        db.LearnerVocabularies.AddRange(
            CreateVocabulary("achievement-user", "term-1", "mastered", now),
            CreateVocabulary("achievement-user", "term-2", "learning", now));
        db.Achievements.AddRange(
            CreateAchievement("attempts", "attempt_count", 2, 10),
            CreateAchievement("streak", "streak_days", 5, 20),
            CreateAchievement("xp", "total_xp", 100, 30),
            CreateAchievement("vocabulary", "vocab_added", 2, 40),
            CreateAchievement("already", "attempt_count", 1, 1_000));
        db.LearnerAchievements.Add(new LearnerAchievement
        {
            Id = Guid.NewGuid(),
            UserId = "achievement-user",
            AchievementId = "already",
            UnlockedAt = now,
            Notified = false
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        database.Sql.Clear();

        var service = new GamificationService(db);
        await service.CheckAndAwardAchievementsAsync(
            "achievement-user",
            "test",
            CancellationToken.None);

        var firstReadCommands = database.Sql.SelectCommands;
        Assert.Equal(2, firstReadCommands.Count);
        Assert.Contains(
            firstReadCommands,
            command => command.Contains("NOT EXISTS", StringComparison.OrdinalIgnoreCase));
        var aggregateCommand = Assert.Single(
            firstReadCommands,
            command => command.Contains("FROM \"Users\"", StringComparison.OrdinalIgnoreCase));
        Assert.True(CountOccurrences(aggregateCommand, "COUNT(") >= 3);

        db.ChangeTracker.Clear();
        Assert.Equal(
            5,
            await db.LearnerAchievements.CountAsync(row => row.UserId == "achievement-user"));
        Assert.Equal(200, (await db.LearnerXPs.SingleAsync(row => row.UserId == "achievement-user")).TotalXP);

        db.ChangeTracker.Clear();
        database.Sql.Clear();
        await service.CheckAndAwardAchievementsAsync(
            "achievement-user",
            "test",
            CancellationToken.None);

        Assert.Single(database.Sql.SelectCommands);
        Assert.Equal(
            5,
            await db.LearnerAchievements.CountAsync(row => row.UserId == "achievement-user"));
    }

    [Fact]
    public async Task Readiness_UsesOneGroupedProjection_InsteadOfLoadingAttemptHistory()
    {
        await using var database = await TestDatabase.CreateAsync();
        var db = database.Db;
        var now = DateTimeOffset.UtcNow;

        for (var index = 0; index < 30; index++)
            db.Attempts.Add(CreateAttempt($"reading-{index}", "readiness-user", "reading", now.AddDays(-index)));
        db.Attempts.AddRange(
            CreateAttempt("listening-1", "readiness-user", "listening", now.AddDays(-1)),
            CreateAttempt("listening-2", "readiness-user", "listening", now.AddDays(-2)),
            CreateAttempt("writing-1", "readiness-user", "writing", now.AddDays(-1)),
            CreateAttempt("ignored-user", "other-user", "speaking", now),
            CreateAttempt("ignored-state", "readiness-user", "speaking", now, AttemptState.InProgress));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        database.Sql.Clear();

        var response = await new LearnerActionsService(db)
            .GetReadinessBlockersAsync("readiness-user", CancellationToken.None);

        Assert.Equal(3, response.Blockers.Count);
        Assert.Equal(
            new[] { "listening", "speaking", "writing" },
            response.Blockers.Select(blocker => blocker.SubTest).OrderBy(value => value));
        Assert.Equal(30, response.OverallReadiness);

        var command = Assert.Single(database.Sql.SelectCommandsFor("Attempts"));
        Assert.Contains("GROUP BY", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COUNT", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DraftContent", command, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProgressTrend_GroupsDailyInSql_AndOnlyReadsThreeMonthWindow()
    {
        await using var database = await TestDatabase.CreateAsync();
        var db = database.Db;
        var now = DateTimeOffset.UtcNow;
        var thisMonday = now.UtcDateTime.Date.AddDays(-(((int)now.UtcDateTime.DayOfWeek + 6) % 7));
        var priorMonday = thisMonday.AddDays(-7);

        for (var index = 0; index < 3; index++)
        {
            db.Attempts.Add(CreateAttempt(
                $"trend-current-{index}",
                "trend-user",
                "reading",
                new DateTimeOffset(thisMonday.AddDays(1).AddHours(index), TimeSpan.Zero)));
            db.Attempts.Add(CreateAttempt(
                $"trend-prior-{index}",
                "trend-user",
                "reading",
                new DateTimeOffset(priorMonday.AddDays(1).AddHours(index), TimeSpan.Zero)));
        }
        db.Attempts.Add(CreateAttempt("trend-old", "trend-user", "reading", now.AddMonths(-4)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        database.Sql.Clear();

        var response = await new LearnerActionsService(db)
            .GetProgressTrendAsync("trend-user", CancellationToken.None);

        Assert.Equal(2, response.Points.Count);
        Assert.All(response.Points, point =>
        {
            Assert.Equal(3, point.AttemptCount);
            Assert.StartsWith("20", point.Period, StringComparison.Ordinal);
            Assert.Contains("-W", point.Period, StringComparison.Ordinal);
        });

        var command = Assert.Single(database.Sql.SelectCommandsFor("Attempts"));
        Assert.Contains("GROUP BY", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COUNT", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DraftContent", command, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BillingMetricsRead_AwaitsOneNoTrackingOrderedQuery()
    {
        await using var database = await TestDatabase.CreateAsync();
        var db = database.Db;
        var date = new DateOnly(2026, 7, 13);

        db.BillingMetricDailies.AddRange(
            CreateBillingMetric("metric-1", date, "mrr", "GLOBAL", 20),
            CreateBillingMetric("metric-2", date.AddDays(1), "mrr", "GLOBAL", 30),
            CreateBillingMetric("metric-3", date, "arr", "GLOBAL", 240));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        database.Sql.Clear();

        var metrics = await new BillingMetricsService(
                db,
                NullLogger<BillingMetricsService>.Instance)
            .ReadAsync(date, date.AddDays(1), "mrr", "GLOBAL", CancellationToken.None);

        Assert.Equal(new[] { "metric-1", "metric-2" }, metrics.Select(metric => metric.Id));
        var command = Assert.Single(database.Sql.SelectCommandsFor("BillingMetricDailies"));
        Assert.Contains("ORDER BY", command, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private static ContentItem CreateContent(
        string id,
        int rating,
        ContentStatus status,
        string examTypeCode,
        string subtestCode,
        DateTimeOffset now) => new()
    {
        Id = id,
        ContentType = "practice",
        SubtestCode = subtestCode,
        Title = id,
        Difficulty = "medium",
        EstimatedDurationMinutes = 10,
        PublishedRevisionId = "revision",
        Status = status,
        CreatedAt = now,
        UpdatedAt = now,
        ExamTypeCode = examTypeCode,
        DifficultyRating = rating
    };

    private static ReviewItem CreateReviewItem(
        string id,
        string userId,
        string status,
        DateOnly dueDate) => new()
    {
        Id = id,
        UserId = userId,
        ExamTypeCode = "OET",
        SourceType = "test",
        SubtestCode = "reading",
        DueDate = dueDate,
        CreatedAt = DateTimeOffset.UtcNow,
        Status = status
    };

    private static LearnerUser CreateUser(string id, DateTimeOffset now) => new()
    {
        Id = id,
        DisplayName = id,
        Email = $"{id}@example.test",
        CreatedAt = now,
        LastActiveAt = now
    };

    private static Attempt CreateAttempt(
        string id,
        string userId,
        string subtestCode,
        DateTimeOffset completedAt,
        AttemptState state = AttemptState.Completed) => new()
    {
        Id = id,
        UserId = userId,
        ContentId = "content",
        SubtestCode = subtestCode,
        Context = "practice",
        Mode = "timed",
        State = state,
        StartedAt = completedAt.AddMinutes(-30),
        SubmittedAt = state == AttemptState.Completed ? completedAt : null,
        CompletedAt = state == AttemptState.Completed ? completedAt : null,
        CreatedAt = completedAt
    };

    private static LearnerVocabulary CreateVocabulary(
        string userId,
        string termId,
        string mastery,
        DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TermId = termId,
        Mastery = mastery,
        AddedAt = now
    };

    private static Achievement CreateAchievement(
        string id,
        string type,
        int threshold,
        int reward) => new()
    {
        Id = id,
        Code = id,
        Label = id,
        Description = id,
        Category = "test",
        XPReward = reward,
        CriteriaJson = JsonSerializer.Serialize(new { type, threshold }),
        Status = "active"
    };

    private static BillingMetricDaily CreateBillingMetric(
        string id,
        DateOnly date,
        string code,
        string region,
        decimal value) => new()
    {
        Id = id,
        MetricDate = date,
        MetricCode = code,
        Region = region,
        Currency = "USD",
        Value = value,
        ComputedAt = DateTimeOffset.UtcNow
    };

    private static int CountOccurrences(string value, string fragment)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(fragment, offset, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            offset += fragment.Length;
        }
        return count;
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestDatabase(SqliteConnection connection, LearnerDbContext db, SqlCaptureInterceptor sql)
        {
            _connection = connection;
            Db = db;
            Sql = sql;
        }

        public LearnerDbContext Db { get; }
        public SqlCaptureInterceptor Sql { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var sql = new SqlCaptureInterceptor();
            var options = new DbContextOptionsBuilder<LearnerDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(sql)
                .Options;
            var db = new LearnerDbContext(options);
            await db.Database.EnsureCreatedAsync();
            sql.Clear();
            return new TestDatabase(connection, db, sql);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class SqlCaptureInterceptor : DbCommandInterceptor
    {
        private readonly List<string> _commands = [];

        public IReadOnlyList<string> SelectCommands
            => _commands
                .Where(command => command.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                .ToList();

        public void Clear() => _commands.Clear();

        public IReadOnlyList<string> SelectCommandsFor(string tableName)
            => SelectCommands
                .Where(command => command.Contains($"FROM \"{tableName}\"", StringComparison.OrdinalIgnoreCase))
                .ToList();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            _commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            _commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
