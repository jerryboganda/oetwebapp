using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.Billing;
using OetWithDrHesham.Api.Services.Mocks.Results;
using OetWithDrHesham.Api.Services.Reading;

namespace OetWithDrHesham.Api.Tests.Services;

public sealed class ServicePerformanceFixTests
{
    [Fact]
    public async Task EngagementRisk_UsesServerSideCount_AndPreservesRiskOutput()
    {
        await using var database = await TestDatabase.CreateAsync();
        var db = database.Db;
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        db.Users.Add(new LearnerUser
        {
            Id = "engagement-user",
            DisplayName = "Engagement User",
            Email = "engagement@example.test",
            CurrentStreak = 4,
            TotalPracticeMinutes = 1_000,
            TotalPracticeSessions = 10,
            LastPracticeDate = now.AddDays(-7),
            CreatedAt = now.AddMonths(-2),
            LastActiveAt = now
        });
        db.Goals.Add(new LearnerGoal
        {
            Id = Guid.NewGuid(),
            UserId = "engagement-user",
            ProfessionId = "medicine",
            TargetExamDate = today.AddDays(56),
            UpdatedAt = now
        });
        for (var index = 0; index < 4; index++)
        {
            db.Attempts.Add(CreateAttempt(
                $"recent-{index}",
                "engagement-user",
                now.AddDays(-(index + 1))));
        }
        db.Attempts.Add(CreateAttempt("old", "engagement-user", now.AddDays(-40)));
        db.Attempts.Add(CreateAttempt("unsubmitted", "engagement-user", null));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        database.Sql.Clear();

        var risk = await new EngagementService(db)
            .CalculateTargetDateRiskAsync("engagement-user", CancellationToken.None);

        var attemptCommand = Assert.Single(database.Sql.SelectCommandsFor("Attempts"));
        Assert.Contains("COUNT", attemptCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("low", risk.RiskLevel);
        Assert.Equal(85, risk.ReadinessProbability);
        Assert.DoesNotContain(risk.Factors, factor => factor.FactorId == "low_recent_activity");
    }

    [Fact]
    public async Task BillingRollup_PreloadsMetricsOnce_AndPreservesUpsertCardinalityAndValues()
    {
        await using var database = await TestDatabase.CreateAsync();
        var db = database.Db;
        var date = new DateOnly(2026, 7, 12);
        var dayStart = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        db.BillingMetricDailies.Add(new BillingMetricDaily
        {
            Id = "existing-mrr",
            MetricDate = date,
            MetricCode = "mrr",
            Region = "GLOBAL",
            Currency = "GBP",
            Value = 999m,
            DetailsJson = """{"preserve":true}""",
            ComputedAt = dayStart.AddDays(-1)
        });
        db.Subscriptions.AddRange(
            new Subscription
            {
                Id = "active",
                UserId = "billing-user",
                PlanId = "monthly",
                Status = SubscriptionStatus.Active,
                StartedAt = dayStart.AddHours(1),
                ChangedAt = dayStart.AddHours(1),
                NextRenewalAt = dayStart.AddMonths(1),
                PriceAmount = 25m,
                Interval = "monthly"
            },
            new Subscription
            {
                Id = "cancelled",
                UserId = "billing-user",
                PlanId = "cancelled",
                Status = SubscriptionStatus.Cancelled,
                StartedAt = dayStart.AddMonths(-2),
                ChangedAt = dayStart.AddHours(2),
                NextRenewalAt = dayStart,
                PriceAmount = 10m,
                Interval = "monthly"
            },
            new Subscription
            {
                Id = "paused",
                UserId = "billing-user",
                PlanId = "paused",
                Status = SubscriptionStatus.Paused,
                StartedAt = dayStart.AddMonths(-2),
                ChangedAt = dayStart.AddDays(-1),
                NextRenewalAt = dayStart,
                PriceAmount = 15m,
                Interval = "monthly"
            });
        db.PaymentTransactions.AddRange(
            CreatePayment("completed", "completed", 40m, dayStart.AddHours(3)),
            CreatePayment("refunded", "refunded", 5m, dayStart.AddHours(4)));
        db.WalletTransactions.Add(new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = "wallet",
            TransactionType = "credit_purchase",
            Amount = 7,
            BalanceAfter = 7,
            CreatedAt = dayStart.AddHours(5)
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = new BillingMetricsService(db, NullLogger<BillingMetricsService>.Instance);
        database.Sql.Clear();
        await service.RollupAsync(date, CancellationToken.None);
        Assert.Single(database.Sql.SelectCommandsFor("BillingMetricDailies"));

        database.Sql.Clear();
        await service.RollupAsync(date, CancellationToken.None);
        Assert.Single(database.Sql.SelectCommandsFor("BillingMetricDailies"));

        var metrics = await db.BillingMetricDailies.AsNoTracking()
            .Where(metric => metric.MetricDate == date && metric.Region == "GLOBAL")
            .ToListAsync();
        Assert.Equal(12, metrics.Count);
        Assert.Equal(12, metrics.Select(metric => metric.MetricCode).Distinct(StringComparer.Ordinal).Count());
        AssertMetric(metrics, "mrr", 25m);
        AssertMetric(metrics, "arr", 300m);
        AssertMetric(metrics, "active_subscriptions", 1m);
        AssertMetric(metrics, "new_subscriptions", 1m);
        AssertMetric(metrics, "cancelled_subscriptions", 1m);
        AssertMetric(metrics, "paused_subscriptions", 1m);
        AssertMetric(metrics, "churn_rate", 100m);
        AssertMetric(metrics, "refund_rate", 12.5m);
        AssertMetric(metrics, "arpu", 25m);
        AssertMetric(metrics, "gross_revenue", 40m);
        AssertMetric(metrics, "net_revenue", 35m);
        AssertMetric(metrics, "credits_sold", 7m);
        var existing = Assert.Single(metrics, metric => metric.MetricCode == "mrr");
        Assert.Equal("existing-mrr", existing.Id);
        Assert.Equal("GBP", existing.Currency);
        Assert.Equal("""{"preserve":true}""", existing.DetailsJson);
    }

    [Fact]
    public async Task MockSectionResolution_BatchesSixAuthoritativeAttemptsIntoAtMostTwoQueries()
    {
        await using var database = await TestDatabase.CreateAsync();
        var db = database.Db;
        var now = DateTimeOffset.UtcNow;
        var mockAttempt = new MockAttempt
        {
            Id = "mock-attempt",
            UserId = "mock-user",
            MockBundleId = "bundle",
            MockType = "full",
            State = AttemptState.InProgress,
            StartedAt = now,
            ConfigJson = "{}"
        };
        var contexts = new List<MockSectionResultContext>();
        for (var index = 0; index < 3; index++)
        {
            var readingAttemptId = $"reading-attempt-{index}";
            db.ReadingAttempts.Add(new ReadingAttempt
            {
                Id = readingAttemptId,
                UserId = mockAttempt.UserId,
                PaperId = "reading-paper",
                Status = ReadingAttemptStatus.Submitted,
                StartedAt = now.AddHours(-1),
                SubmittedAt = now,
                LastActivityAt = now,
                RawScore = 20 + index,
                MaxRawScore = 42
            });
            contexts.Add(CreateMockSectionContext(
                db, mockAttempt, $"reading-section-{index}", "reading", "reading-paper", readingAttemptId, index));

            var listeningAttemptId = $"listening-attempt-{index}";
            db.ListeningAttempts.Add(new ListeningAttempt
            {
                Id = listeningAttemptId,
                UserId = mockAttempt.UserId,
                PaperId = "listening-paper",
                Status = ListeningAttemptStatus.Submitted,
                StartedAt = now.AddHours(-1),
                SubmittedAt = now,
                LastActivityAt = now,
                RawScore = 30 + index,
                MaxRawScore = 42
            });
            contexts.Add(CreateMockSectionContext(
                db, mockAttempt, $"listening-section-{index}", "listening", "listening-paper", listeningAttemptId, index + 3));
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        database.Sql.Clear();
        var resolver = new MockSectionResultResolver([
            new ReadingMockSectionResultAdapter(),
            new ListeningMockSectionResultAdapter(),
            new LegacyMockSectionResultAdapter()
        ]);

        var resolved = await resolver.ResolveAllAsync(contexts, CancellationToken.None);

        var sectionQueries = database.Sql.SelectCommandsFor("ReadingAttempts")
            .Concat(database.Sql.SelectCommandsFor("ListeningAttempts"))
            .ToList();
        Assert.Equal(2, sectionQueries.Count);
        Assert.Equal(6, resolved.Count);
        Assert.All(contexts.Where(context => context.SectionAttempt.SubtestCode == "reading"), context =>
        {
            Assert.Equal("reading_attempt", resolved[context.SectionAttempt.Id].EvidenceSource);
            Assert.Equal(resolved[context.SectionAttempt.Id].RawScore, context.SectionAttempt.RawScore);
        });
        Assert.All(contexts.Where(context => context.SectionAttempt.SubtestCode == "listening"), context =>
        {
            Assert.Equal("listening_attempt", resolved[context.SectionAttempt.Id].EvidenceSource);
            Assert.Equal(resolved[context.SectionAttempt.Id].RawScore, context.SectionAttempt.RawScore);
        });
    }

    [Fact]
    public async Task ReadingAnalytics_DictionaryLookup_PreservesDistractorOutputContract()
    {
        await using var database = await TestDatabase.CreateAsync();
        var db = database.Db;
        var now = DateTimeOffset.UtcNow;
        db.ContentPapers.Add(new ContentPaper
        {
            Id = "analytics-paper",
            SubtestCode = "reading",
            Title = "Analytics Paper",
            Slug = "analytics-paper",
            AppliesToAllProfessions = true,
            Status = ContentStatus.Published
        });
        db.ReadingParts.Add(new ReadingPart
        {
            Id = "analytics-part",
            PaperId = "analytics-paper",
            PartCode = ReadingPartCode.B,
            TimeLimitMinutes = 45,
            MaxRawScore = 2,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.ReadingQuestions.AddRange(
            CreateQuestion("question-one", 1, """{"B":"Opposite"}"""),
            CreateQuestion("question-two", 2, """{"D":"TooBroad"}"""));
        for (var index = 0; index < 5; index++)
        {
            var attemptId = $"analytics-attempt-{index}";
            db.ReadingAttempts.Add(new ReadingAttempt
            {
                Id = attemptId,
                UserId = $"analytics-user-{index}",
                PaperId = "analytics-paper",
                Status = ReadingAttemptStatus.Submitted,
                StartedAt = now.AddMinutes(-30),
                SubmittedAt = now,
                LastActivityAt = now,
                RawScore = index,
                ScaledScore = OetScoring.OetRawToScaled(index),
                MaxRawScore = 42
            });
            db.ReadingAnswers.AddRange(
                CreateAnswer($"answer-one-{index}", attemptId, "question-one", ReadingDistractorCategory.Opposite, now),
                CreateAnswer($"answer-two-{index}", attemptId, "question-two", ReadingDistractorCategory.TooBroad, now));
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var analytics = await new ReadingAnalyticsService(db)
            .GetPaperAnalyticsAsync("analytics-paper", CancellationToken.None);

        Assert.Equal("analytics-paper", analytics.PaperId);
        Assert.Equal(5, analytics.TotalAttempts);
        Assert.Equal(5, analytics.SubmittedAttempts);
        Assert.Equal(2, analytics.HardestQuestions.Count);
        Assert.Equal(2, analytics.DistractorHistogram.Count);
        var byQuestion = analytics.DistractorHistogram.ToDictionary(row => row.QuestionId, StringComparer.Ordinal);
        Assert.Equal(("B", 5), (byQuestion["question-one"].OptionKey, byQuestion["question-one"].SelectedCount));
        Assert.Equal(("D", 5), (byQuestion["question-two"].OptionKey, byQuestion["question-two"].SelectedCount));
    }

    private static Attempt CreateAttempt(string id, string userId, DateTimeOffset? submittedAt) => new()
    {
        Id = id,
        UserId = userId,
        ContentId = "content",
        SubtestCode = "reading",
        Context = "practice",
        Mode = "timed",
        State = submittedAt.HasValue ? AttemptState.Completed : AttemptState.InProgress,
        StartedAt = submittedAt?.AddMinutes(-30) ?? DateTimeOffset.UtcNow,
        SubmittedAt = submittedAt
    };

    private static PaymentTransaction CreatePayment(
        string id,
        string status,
        decimal amount,
        DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(),
        LearnerUserId = "billing-user",
        Gateway = "test",
        GatewayTransactionId = id,
        TransactionType = "test",
        Status = status,
        Amount = amount,
        Currency = "USD",
        CreatedAt = createdAt,
        UpdatedAt = createdAt
    };

    private static void AssertMetric(
        IReadOnlyCollection<BillingMetricDaily> metrics,
        string code,
        decimal expectedValue)
        => Assert.Equal(expectedValue, Assert.Single(metrics, metric => metric.MetricCode == code).Value);

    private static MockSectionResultContext CreateMockSectionContext(
        LearnerDbContext db,
        MockAttempt mockAttempt,
        string sectionId,
        string subtestCode,
        string paperId,
        string contentAttemptId,
        int order)
    {
        var bundleSection = new MockBundleSection
        {
            Id = $"bundle-{sectionId}",
            MockBundleId = mockAttempt.MockBundleId!,
            SectionOrder = order,
            SubtestCode = subtestCode,
            ContentPaperId = paperId,
            TimeLimitMinutes = 60
        };
        var sectionAttempt = new MockSectionAttempt
        {
            Id = sectionId,
            MockAttemptId = mockAttempt.Id,
            MockBundleSectionId = bundleSection.Id,
            SubtestCode = subtestCode,
            ContentPaperId = paperId,
            LaunchRoute = "/mocks",
            ContentAttemptId = contentAttemptId,
            State = AttemptState.Completed
        };
        return new MockSectionResultContext(db, mockAttempt, sectionAttempt, bundleSection);
    }

    private static ReadingQuestion CreateQuestion(string id, int displayOrder, string distractorsJson) => new()
    {
        Id = id,
        ReadingPartId = "analytics-part",
        DisplayOrder = displayOrder,
        QuestionType = ReadingQuestionType.MultipleChoice4,
        Stem = id,
        OptionsJson = """["A","B","C","D"]""",
        CorrectAnswerJson = "\"A\"",
        OptionDistractorsJson = distractorsJson
    };

    private static ReadingAnswer CreateAnswer(
        string id,
        string attemptId,
        string questionId,
        ReadingDistractorCategory category,
        DateTimeOffset answeredAt) => new()
    {
        Id = id,
        ReadingAttemptId = attemptId,
        ReadingQuestionId = questionId,
        UserAnswerJson = "\"wrong\"",
        IsCorrect = false,
        PointsEarned = 0,
        SelectedDistractorCategory = category,
        AnsweredAt = answeredAt
    };

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

        public void Clear() => _commands.Clear();

        public IReadOnlyList<string> SelectCommandsFor(string tableName)
            => _commands
                .Where(command =>
                    command.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                    command.Contains($"FROM \"{tableName}\"", StringComparison.OrdinalIgnoreCase))
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
