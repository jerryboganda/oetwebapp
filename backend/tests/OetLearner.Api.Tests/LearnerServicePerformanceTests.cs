using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests;

public sealed class LearnerServicePerformanceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset SeedTime =
        new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly SqlCaptureInterceptor _sql = new();
    private DbContextOptions<LearnerDbContext> _options = default!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_sql)
            .Options;

        await using var db = new LearnerDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task GetDashboardAsync_UsesAtMostSevenQueriesAndPreservesGoldenDtoForLongHistory()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedLearnerAsync(db, "dashboard-user", attemptCount: 140);
        db.ChangeTracker.Clear();
        _sql.Commands.Clear();

        var json = JsonSerializer.SerializeToElement(
            await CreateLearnerService(db).GetDashboardAsync("dashboard-user", CancellationToken.None));

        AssertPropertyNames(json,
            "cards", "engagement", "freeze", "primaryActions", "partialData", "lastUpdatedAt");
        var cards = json.GetProperty("cards");
        AssertPropertyNames(cards,
            "readiness", "examDate", "todaysTasks", "latestEvaluatedSubmission",
            "weakCriteria", "momentum", "nextMockRecommendation", "pendingExpertReviews");
        Assert.Equal("evaluation-000", cards.GetProperty("latestEvaluatedSubmission").GetProperty("evaluationId").GetString());
        Assert.Equal(47, cards.GetProperty("pendingExpertReviews").GetProperty("count").GetInt32());
        Assert.False(json.GetProperty("partialData").GetBoolean());
        Assert.True(_sql.Commands.Count <= 7, DumpCommands());
        Assert.DoesNotContain(_sql.Commands, command =>
            command.Contains("IN (", StringComparison.OrdinalIgnoreCase)
            && command.Length > 2_000);
    }

    [Fact]
    public async Task GetMeAndBootstrap_ReuseLoadedProfileAndFreezeState()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedLearnerAsync(db, "bootstrap-user", attemptCount: 1);
        db.ChangeTracker.Clear();
        var service = CreateLearnerService(db);

        _sql.Commands.Clear();
        var me = JsonSerializer.SerializeToElement(
            await service.GetMeAsync("bootstrap-user", CancellationToken.None));

        AssertPropertyNames(me,
            "userId", "role", "displayName", "email", "timezone", "locale", "createdAt",
            "lastActiveAt", "currentPlanId", "activeProfessionId", "freeze", "goals");
        Assert.Equal("bootstrap-user", me.GetProperty("userId").GetString());
        Assert.True(_sql.Commands.Count <= 3, DumpCommands());

        db.ChangeTracker.Clear();
        _sql.Commands.Clear();
        var bootstrap = JsonSerializer.SerializeToElement(
            await service.GetBootstrapAsync("bootstrap-user", CancellationToken.None));

        AssertPropertyNames(bootstrap,
            "user", "onboarding", "goals", "readiness", "freeze",
            "permissions", "reference", "links", "lastUpdatedAt");
        Assert.Equal(
            bootstrap.GetProperty("freeze").GetRawText(),
            bootstrap.GetProperty("user").GetProperty("freeze").GetRawText());
        Assert.Equal("oet", bootstrap.GetProperty("goals").GetProperty("examFamilyCode").GetString());
        Assert.True(_sql.Commands.Count <= 7, DumpCommands());
        Assert.Single(_sql.Commands.Where(command =>
            command.Contains("FROM \"Users\"", StringComparison.OrdinalIgnoreCase)
            && command.Contains("Goals", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Bootstrap_CreatesMissingProfileRowsForOtherwiseEmptyLearner()
    {
        await using var db = new LearnerDbContext(_options);
        AddUser(db, "empty-profile-user");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        _sql.Commands.Clear();

        var json = JsonSerializer.SerializeToElement(
            await CreateLearnerService(db).GetBootstrapAsync("empty-profile-user", CancellationToken.None));

        Assert.Equal("empty-profile-user", json.GetProperty("user").GetProperty("userId").GetString());
        Assert.Equal("Australia", json.GetProperty("goals").GetProperty("targetCountry").GetString());
        Assert.True(await db.Goals.AnyAsync(goal => goal.UserId == "empty-profile-user"));
        Assert.True(await db.Settings.AnyAsync(settings => settings.UserId == "empty-profile-user"));
        Assert.True(await db.Wallets.AnyAsync(wallet => wallet.UserId == "empty-profile-user"));
    }

    [Fact]
    public async Task GetProgressAsync_UsesFourNarrowQueriesAndBoundsOnlyHistorySeries()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedLearnerAsync(db, "progress-user", attemptCount: 140);
        db.ChangeTracker.Clear();
        _sql.Commands.Clear();

        var json = JsonSerializer.SerializeToElement(
            await CreateLearnerService(db).GetProgressAsync("progress-user", CancellationToken.None));

        AssertPropertyNames(json,
            "trend", "subtestTrend", "criterionTrend", "completion", "submissionVolume",
            "reviewUsage", "totals", "freshness");
        Assert.Equal(52, json.GetProperty("trend").GetArrayLength());
        Assert.Equal(52, json.GetProperty("subtestTrend").GetArrayLength());
        Assert.Equal(52, json.GetProperty("criterionTrend").GetArrayLength());
        Assert.Equal(140, json.GetProperty("totals").GetProperty("completedAttempts").GetInt32());
        Assert.Equal(140, json.GetProperty("totals").GetProperty("completedEvaluations").GetInt32());
        Assert.Equal(140, json.GetProperty("reviewUsage").GetProperty("totalRequests").GetInt32());
        Assert.Equal(2.0, json.GetProperty("reviewUsage").GetProperty("averageTurnaroundHours").GetDouble());
        Assert.Equal(4, _sql.Commands.Count);
        Assert.Contains(_sql.Commands, command =>
            command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)
            && command.Contains("Evaluations", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_sql.Commands, command =>
            command.Contains("SELECT \"a\".*", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetProgressAsync_ReturnsGoldenEmptySeriesWithoutMaterializingIds()
    {
        await using var db = new LearnerDbContext(_options);
        AddUser(db, "empty-progress-user");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        _sql.Commands.Clear();

        var json = JsonSerializer.SerializeToElement(
            await CreateLearnerService(db).GetProgressAsync("empty-progress-user", CancellationToken.None));

        Assert.Empty(json.GetProperty("trend").EnumerateArray());
        Assert.Empty(json.GetProperty("criterionTrend").EnumerateArray());
        Assert.Equal(0, json.GetProperty("totals").GetProperty("completedAttempts").GetInt32());
        Assert.Equal(0, json.GetProperty("reviewUsage").GetProperty("totalRequests").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("reviewUsage").GetProperty("averageTurnaroundHours").ValueKind);
        Assert.True(json.GetProperty("freshness").GetProperty("usesFallbackSeries").GetBoolean());
        Assert.Equal(4, _sql.Commands.Count);
    }

    [Fact]
    public async Task GetWritingHomeAsync_LimitsAttemptsInSqlAndLoadsEvaluationsOnce()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedLearnerAsync(db, "writing-home-user", attemptCount: 140);
        db.ChangeTracker.Clear();
        _sql.Commands.Clear();

        var json = JsonSerializer.SerializeToElement(
            await CreateLearnerService(db).GetWritingHomeAsync("writing-home-user", CancellationToken.None));

        AssertPropertyNames(json,
            "recommendedTask", "practiceLibrary", "criterionDrillLibrary", "pastSubmissions",
            "reviewCredits", "fullMockEntry", "featuredTasks", "latestEvaluation", "actions");
        var submissions = json.GetProperty("pastSubmissions").EnumerateArray().ToArray();
        Assert.Equal(4, submissions.Length);
        Assert.Equal(
            new[] { "attempt-000", "attempt-001", "attempt-002", "attempt-003" },
            submissions.Select(item => item.GetProperty("attemptId").GetString()).ToArray());
        Assert.Equal(
            "evaluation-000",
            json.GetProperty("latestEvaluation").GetProperty("evaluationId").GetString());
        Assert.True(_sql.Commands.Count <= 5, DumpCommands());
        Assert.Single(_sql.Commands.Where(command =>
            command.Contains("Evaluations", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(_sql.Commands, command =>
            command.Contains("Attempts", StringComparison.OrdinalIgnoreCase)
            && command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CompareSubmissionsAsync_UsesNarrowOwnedRowsAndOneEvaluationBatch()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedLearnerAsync(db, "compare-user", attemptCount: 12);
        db.ChangeTracker.Clear();
        _sql.Commands.Clear();

        var json = JsonSerializer.SerializeToElement(
            await CreateLearnerService(db).CompareSubmissionsAsync(
                "compare-user",
                leftId: null,
                rightId: null,
                CancellationToken.None));

        AssertPropertyNames(json, "canCompare", "left", "right", "summary", "comparisonGroupId");
        Assert.True(json.GetProperty("canCompare").GetBoolean());
        Assert.Equal("attempt-000", json.GetProperty("left").GetProperty("attemptId").GetString());
        Assert.Equal("attempt-001", json.GetProperty("right").GetProperty("attemptId").GetString());
        Assert.True(_sql.Commands.Count <= 4, DumpCommands());
        Assert.Single(_sql.Commands.Where(command =>
            command.Contains("Evaluations", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(
            _sql.Commands.Where(command => command.Contains("Attempts", StringComparison.OrdinalIgnoreCase)),
            command => command.Contains("DraftContent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CompareSubmissionsAsync_ReturnsGoldenEmptyResponse()
    {
        await using var db = new LearnerDbContext(_options);
        AddUser(db, "empty-compare-user");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        _sql.Commands.Clear();

        var json = JsonSerializer.SerializeToElement(
            await CreateLearnerService(db).CompareSubmissionsAsync(
                "empty-compare-user",
                leftId: null,
                rightId: null,
                CancellationToken.None));

        AssertPropertyNames(json, "canCompare", "reason");
        Assert.False(json.GetProperty("canCompare").GetBoolean());
        Assert.Equal("Need at least two submissions to compare.", json.GetProperty("reason").GetString());
        Assert.Equal(2, _sql.Commands.Count);
    }

    [Fact]
    public async Task InterleavedPractice_UsesBoundedRandomOffsetInsteadOfFullRandomSort()
    {
        await using var db = new LearnerDbContext(_options);
        AddUser(db, "interleaved-user");
        foreach (var subtest in new[] { "reading", "listening", "writing", "speaking" })
        {
            for (var index = 0; index < 20; index++)
            {
                db.ContentItems.Add(CreateContent($"{subtest}-{index:D2}", subtest, $"{subtest} task {index:D2}"));
            }
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        _sql.Commands.Clear();

        var json = JsonSerializer.SerializeToElement(
            await CreateLearnerService(db).GetInterleavedPracticeSessionAsync(
                "interleaved-user",
                durationMinutes: 20,
                CancellationToken.None));

        AssertPropertyNames(json,
            "sessionId", "targetDurationMinutes", "actualDurationMinutes", "taskCount",
            "tasks", "scienceBasis", "tips");
        Assert.NotEqual(0, json.GetProperty("taskCount").GetInt32());
        Assert.DoesNotContain(_sql.Commands, command =>
            command.Contains("RANDOM()", StringComparison.OrdinalIgnoreCase)
            || command.Contains("NEWID()", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(_sql.Commands, command =>
            command.Contains("ContentItems", StringComparison.OrdinalIgnoreCase)
            && command.Contains("OFFSET", StringComparison.OrdinalIgnoreCase)
            && command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task SeedLearnerAsync(
        LearnerDbContext db,
        string userId,
        int attemptCount)
    {
        var user = AddUser(db, userId);
        user.CurrentPlanId = $"plan-{userId}";
        db.Goals.Add(new LearnerGoal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProfessionId = "medicine",
            TargetExamDate = new DateOnly(2026, 8, 15),
            OverallGoal = "Golden learner goal",
            TargetWritingScore = 350,
            TargetSpeakingScore = 350,
            TargetReadingScore = 350,
            TargetListeningScore = 350,
            WeakSubtestsJson = "[]",
            StudyHoursPerWeek = 10,
            TargetCountry = "Australia",
            TargetOrganization = "AHPRA",
            DraftStateJson = "{}",
            UpdatedAt = SeedTime,
            ExamFamilyCode = "oet"
        });
        db.Settings.Add(new LearnerSettings
        {
            Id = Guid.NewGuid(),
            UserId = userId
        });
        db.Wallets.Add(new Wallet
        {
            Id = $"wallet-{userId}",
            UserId = userId,
            CreditBalance = 7,
            LastUpdatedAt = SeedTime
        });
        db.ReadinessSnapshots.Add(new ReadinessSnapshot
        {
            Id = $"readiness-{userId}",
            UserId = userId,
            ComputedAt = SeedTime,
            ExpiresAt = SeedTime.AddDays(1),
            PayloadJson = JsonSupport.Serialize(new
            {
                overallRisk = "low",
                recommendedStudyHours = 10
            }),
            Version = 2
        });
        db.StudyPlans.Add(new StudyPlan
        {
            Id = $"plan-{userId}",
            UserId = userId,
            GeneratedAt = SeedTime,
            Checkpoint = "week-1",
            WeakSkillFocus = "writing"
        });
        db.StudyPlanItems.Add(new StudyPlanItem
        {
            Id = $"plan-item-{userId}",
            StudyPlanId = $"plan-{userId}",
            Title = "Complete a writing task",
            SubtestCode = "writing",
            DurationMinutes = 45,
            Rationale = "Improve the weakest criterion.",
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = StudyPlanItemStatus.NotStarted,
            Section = "today",
            ContentId = "writing-content",
            ItemType = "practice"
        });
        db.ContentItems.Add(CreateContent("writing-content", "writing", "Referral letter"));

        for (var index = 0; index < attemptCount; index++)
        {
            var attemptId = $"attempt-{index:D3}";
            var submittedAt = SeedTime.AddMinutes(-index);
            db.Attempts.Add(new Attempt
            {
                Id = attemptId,
                UserId = userId,
                ContentId = "writing-content",
                SubtestCode = "writing",
                Context = "practice",
                Mode = "exam",
                State = AttemptState.Completed,
                StartedAt = submittedAt.AddMinutes(-45),
                SubmittedAt = submittedAt,
                CompletedAt = submittedAt,
                ElapsedSeconds = 2700,
                ComparisonGroupId = "writing-content"
            });
            db.Evaluations.Add(new Evaluation
            {
                Id = $"evaluation-{index:D3}",
                AttemptId = attemptId,
                SubtestCode = "writing",
                State = AsyncState.Completed,
                ScoreRange = $"{300 + index % 100}-{310 + index % 100}",
                GradeRange = "B",
                ConfidenceBand = ConfidenceBand.High,
                StrengthsJson = "[\"purpose\"]",
                IssuesJson = "[\"conciseness\"]",
                CriterionScoresJson = JsonSupport.Serialize(new[]
                {
                    new
                    {
                        criterionCode = "purpose",
                        scoreRange = $"{index % 7}/7",
                        explanation = "Keep the purpose explicit."
                    }
                }),
                ModelExplanationSafe = "Practice estimate.",
                LearnerDisclaimer = "Not an official score.",
                GeneratedAt = submittedAt,
                LastTransitionAt = submittedAt
            });
            db.ReviewRequests.Add(new ReviewRequest
            {
                Id = $"review-{index:D3}",
                AttemptId = attemptId,
                SubtestCode = "writing",
                State = index % 3 == 0
                    ? ReviewRequestState.Submitted
                    : ReviewRequestState.Completed,
                TurnaroundOption = "standard",
                PaymentSource = index % 2 == 0 ? "credits" : "Credits",
                PriceSnapshot = 1m,
                CreatedAt = submittedAt,
                CompletedAt = submittedAt.AddHours(2)
            });
        }

        await db.SaveChangesAsync();
    }

    private static LearnerUser AddUser(LearnerDbContext db, string userId)
    {
        var user = new LearnerUser
        {
            Id = userId,
            Role = ApplicationUserRoles.Learner,
            DisplayName = "Performance Learner",
            Email = $"{userId}@example.test",
            Timezone = "Australia/Sydney",
            Locale = "en-AU",
            ActiveProfessionId = "medicine",
            OnboardingCurrentStep = 4,
            OnboardingStepCount = 4,
            OnboardingCompleted = true,
            OnboardingStartedAt = SeedTime.AddDays(-1),
            OnboardingCompletedAt = SeedTime,
            CreatedAt = SeedTime.AddDays(-30),
            LastActiveAt = SeedTime,
            AccountStatus = "active",
            CurrentStreak = 5,
            LongestStreak = 9,
            TotalPracticeMinutes = 300,
            TotalPracticeSessions = 12
        };
        db.Users.Add(user);
        return user;
    }

    private static ContentItem CreateContent(string id, string subtest, string title) => new()
    {
        Id = id,
        ContentType = $"{subtest}_task",
        SubtestCode = subtest,
        ProfessionId = "medicine",
        Title = title,
        Difficulty = "medium",
        EstimatedDurationMinutes = 45,
        CriteriaFocusJson = "[]",
        ScenarioType = "referral",
        ModeSupportJson = "[\"practice\",\"exam\"]",
        PublishedRevisionId = $"{id}-r1",
        Status = ContentStatus.Published,
        CreatedAt = SeedTime,
        UpdatedAt = SeedTime,
        PublishedAt = SeedTime
    };

    private static void AssertPropertyNames(JsonElement element, params string[] expected)
        => Assert.Equal(expected, element.EnumerateObject().Select(property => property.Name));

    private string DumpCommands() => string.Join("\n---\n", _sql.Commands);

    private static LearnerService CreateLearnerService(LearnerDbContext db)
    {
        var billingOptions = Options.Create(new BillingOptions { AllowSandboxFallbacks = true });
        var platformLinks = new PlatformLinkService(
            TestRuntimeSettingsProvider.FromPlatformOptions(new PlatformOptions
            {
                FallbackEmailDomain = "example.test"
            }),
            billingOptions);
        var storageRoot = Path.Combine(Path.GetTempPath(), $"oet-learner-performance-{Guid.NewGuid():N}");
        var storageOptions = Options.Create(new StorageOptions { LocalRootPath = storageRoot });
        var fileStorage = new LocalFileStorage(
            new TestHostEnvironment(storageRoot),
            storageOptions);
        var paymentGateways = CreatePaymentGatewayService(billingOptions);
        var walletService = new WalletService(
            db,
            paymentGateways,
            platformLinks,
            billingOptions);
        return new LearnerService(
            db,
            fileStorage,
            new NoOpPdfTextExtractor(),
            platformLinks,
            null!,
            walletService,
            paymentGateways,
            null!,
            billingOptions,
            storageOptions);
    }

    private static PaymentGatewayService CreatePaymentGatewayService(
        IOptions<BillingOptions> billingOptions)
    {
        var runtimeSettings = TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value);
        return new PaymentGatewayService(
            new StripeGateway(
                new HttpClient(),
                billingOptions,
                runtimeSettings,
                NullLogger<StripeGateway>.Instance),
            new PayPalGateway(new HttpClient(), billingOptions),
            new OetLearner.Api.Services.Billing.Gateways.PayTabsGateway(
                new HttpClient(),
                billingOptions,
                runtimeSettings),
            new OetLearner.Api.Services.Billing.Gateways.PaymobGateway(
                new HttpClient(),
                billingOptions,
                runtimeSettings),
            new OetLearner.Api.Services.Billing.Gateways.CheckoutComGateway(
                new HttpClient(),
                billingOptions,
                runtimeSettings),
            new OetLearner.Api.Services.Billing.Gateways.EasyKashGateway(
                new HttpClient(),
                billingOptions,
                runtimeSettings));
    }

    private sealed class SqlCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TestHostEnvironment(string contentRootPath)
        : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = contentRootPath;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
