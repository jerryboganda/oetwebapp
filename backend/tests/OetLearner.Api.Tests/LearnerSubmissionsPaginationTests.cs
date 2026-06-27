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

public sealed class LearnerSubmissionsPaginationTests : IAsyncLifetime
{
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
    public async Task GetSubmissionsAsync_UsesDatabaseCursorPageAndBatchLoadsRelatedRows()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedSubmissionsAsync(db, "learner-submissions");
        var service = CreateLearnerService(db);
        _sql.Commands.Clear();

        var firstPage = JsonSerializer.SerializeToElement(await service.GetSubmissionsAsync(
            "learner-submissions",
            cursor: null,
            limit: 3,
            CancellationToken.None));

        AssertSubmissionPage(firstPage, expectedIds: ["attempt-00", "attempt-01", "attempt-02"], expectNextCursor: true);
        AssertSqlSidePageAndBatchLoad();

        var nextCursor = firstPage.GetProperty("nextCursor").GetString();
        _sql.Commands.Clear();

        var secondPage = JsonSerializer.SerializeToElement(await service.GetSubmissionsAsync(
            "learner-submissions",
            cursor: nextCursor,
            limit: 3,
            CancellationToken.None));

        AssertSubmissionPage(secondPage, expectedIds: ["attempt-03", "attempt-04", "attempt-05"], expectNextCursor: true);
        AssertSqlSidePageAndBatchLoad();
    }

    [Fact]
    public async Task GetSubmissionsAsync_RendersOrphanedAttempt_WhenContentMissing()
    {
        await using var db = new LearnerDbContext(_options);
        var now = DateTimeOffset.UtcNow;
        const string userId = "learner-orphan";

        db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = "Orphan Learner",
            Email = "orphan@example.test",
            CreatedAt = now,
            LastActiveAt = now,
            AccountStatus = "active",
        });

        db.ContentItems.Add(new ContentItem
        {
            Id = "content-valid",
            ContentType = "writing_task",
            SubtestCode = "writing",
            Title = "Valid Writing Task",
            Difficulty = "medium",
            EstimatedDurationMinutes = 45,
            CriteriaFocusJson = "[]",
            ScenarioType = "referral",
            ModeSupportJson = "[]",
            PublishedRevisionId = "content-valid-r1",
            Status = ContentStatus.Published,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Attempts.Add(new Attempt
        {
            Id = "attempt-valid",
            UserId = userId,
            ContentId = "content-valid",
            SubtestCode = "writing",
            Context = "practice",
            Mode = "exam",
            State = AttemptState.Completed,
            StartedAt = now.AddMinutes(-2),
            SubmittedAt = now.AddMinutes(-1),
            CompletedAt = now.AddMinutes(-1),
            ElapsedSeconds = 900,
        });

        // Orphaned attempt: ContentId has NO matching ContentItem (content was
        // force-deleted). Seed it as the NEWEST attempt so the response builder
        // hits it first — exactly where the unguarded dictionary indexer threw.
        db.Attempts.Add(new Attempt
        {
            Id = "attempt-orphan",
            UserId = userId,
            ContentId = "content-deleted",
            SubtestCode = "writing",
            Context = "practice",
            Mode = "exam",
            State = AttemptState.Completed,
            StartedAt = now.AddMinutes(-1),
            SubmittedAt = now,
            CompletedAt = now,
            ElapsedSeconds = 900,
        });
        await db.SaveChangesAsync();

        var service = CreateLearnerService(db);

        // Must NOT throw KeyNotFoundException for the orphaned attempt.
        var page = JsonSerializer.SerializeToElement(await service.GetSubmissionsAsync(
            userId,
            cursor: null,
            limit: 10,
            CancellationToken.None));

        var items = page.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);

        var ids = items.Select(item => item.GetProperty("submissionId").GetString()).ToArray();
        Assert.Contains("attempt-valid", ids);
        Assert.Contains("attempt-orphan", ids);

        var orphan = items.Single(item => item.GetProperty("submissionId").GetString() == "attempt-orphan");
        Assert.Equal("content-deleted", orphan.GetProperty("contentId").GetString());
        Assert.Equal("Removed practice item", orphan.GetProperty("taskName").GetString());
        Assert.Equal("writing", orphan.GetProperty("subtest").GetString());
        Assert.Equal("not_requested", orphan.GetProperty("reviewStatus").GetString());
    }

    private void AssertSqlSidePageAndBatchLoad()
    {
        var attemptQueries = _sql.Commands
            .Where(command => command.Contains("Attempts", StringComparison.OrdinalIgnoreCase)
                              && command.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Contains(attemptQueries, command => command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(attemptQueries, command => command.Contains("SELECT \"a\".", StringComparison.OrdinalIgnoreCase)
                                                        && !command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        Assert.True(_sql.Commands.Count <= 6, string.Join("\n---\n", _sql.Commands));
    }

    private static void AssertSubmissionPage(JsonElement page, string[] expectedIds, bool expectNextCursor)
    {
        var items = page.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(expectedIds.Length, items.Length);
        Assert.Equal(expectedIds, items.Select(item => item.GetProperty("submissionId").GetString()).ToArray());
        Assert.All(items, item => Assert.Equal(1, item.GetProperty("voiceNoteCount").GetInt32()));
        Assert.Equal(expectNextCursor, page.GetProperty("nextCursor").ValueKind == JsonValueKind.String);
    }

    private static async Task SeedSubmissionsAsync(LearnerDbContext db, string userId)
    {
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = "Submissions Learner",
            Email = "submissions@example.test",
            CreatedAt = now,
            LastActiveAt = now,
            AccountStatus = "active",
        });

        for (var index = 0; index < 8; index++)
        {
            var contentId = $"content-{index}";
            var attemptId = $"attempt-{index:D2}";
            db.ContentItems.Add(new ContentItem
            {
                Id = contentId,
                ContentType = "writing_task",
                SubtestCode = "writing",
                Title = $"Writing Task {index}",
                Difficulty = "medium",
                EstimatedDurationMinutes = 45,
                CriteriaFocusJson = "[]",
                ScenarioType = "referral",
                ModeSupportJson = "[]",
                PublishedRevisionId = $"{contentId}-r1",
                Status = ContentStatus.Published,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Attempts.Add(new Attempt
            {
                Id = attemptId,
                UserId = userId,
                ContentId = contentId,
                SubtestCode = "writing",
                Context = "practice",
                Mode = "exam",
                State = AttemptState.Completed,
                StartedAt = now.AddMinutes(-index - 1),
                SubmittedAt = now.AddMinutes(-index),
                CompletedAt = now.AddMinutes(-index),
                ElapsedSeconds = 900,
            });
            db.Evaluations.Add(new Evaluation
            {
                Id = $"evaluation-{index}",
                AttemptId = attemptId,
                SubtestCode = "writing",
                State = AsyncState.Completed,
                ScoreRange = $"{index + 1}/5",
                GradeRange = "B",
                ConfidenceBand = ConfidenceBand.High,
                ModelExplanationSafe = "ok",
                LearnerDisclaimer = "practice",
                GeneratedAt = now.AddMinutes(-index).AddSeconds(10),
                LastTransitionAt = now.AddMinutes(-index).AddSeconds(10),
            });
            db.ReviewRequests.Add(new ReviewRequest
            {
                Id = $"review-{index}",
                AttemptId = attemptId,
                SubtestCode = "writing",
                State = ReviewRequestState.Completed,
                TurnaroundOption = "standard",
                PaymentSource = "credits",
                PriceSnapshot = 1m,
                CreatedAt = now.AddMinutes(-index).AddSeconds(20),
                CompletedAt = now.AddMinutes(-index).AddSeconds(30),
            });
            db.MediaAssets.Add(new MediaAsset
            {
                Id = $"media-{index}",
                OriginalFilename = $"voice-note-{index}.mp3",
                MimeType = "audio/mpeg",
                Format = "mp3",
                SizeBytes = 1024,
                StoragePath = $"review-notes/{index}.mp3",
                Status = MediaAssetStatus.Ready,
                MediaKind = "audio",
                UploadedBy = "reviewer-1",
                UploadedAt = now.AddMinutes(-index).AddSeconds(34),
                ProcessedAt = now.AddMinutes(-index).AddSeconds(34),
            });
            db.ReviewVoiceNotes.Add(new ReviewVoiceNote
            {
                Id = $"voice-note-{index}",
                ReviewRequestId = $"review-{index}",
                UploadedByReviewerId = "reviewer-1",
                MediaAssetId = $"media-{index}",
                Status = "ready",
                CreatedAt = now.AddMinutes(-index).AddSeconds(35),
                UpdatedAt = now.AddMinutes(-index).AddSeconds(35),
            });
        }

        await db.SaveChangesAsync();
    }

    private static LearnerService CreateLearnerService(LearnerDbContext db)
    {
        var billingOptions = Options.Create(new BillingOptions { AllowSandboxFallbacks = true });
        var platformLinks = new PlatformLinkService(
            TestRuntimeSettingsProvider.FromPlatformOptions(new PlatformOptions { FallbackEmailDomain = "example.test" }),
            billingOptions);
        var storageRoot = Path.Combine(Path.GetTempPath(), $"oet-submissions-{Guid.NewGuid():N}");
        var storageOptions = Options.Create(new StorageOptions { LocalRootPath = storageRoot });
        var fileStorage = new LocalFileStorage(new TestHostEnvironment(storageRoot), storageOptions);
        var paymentGateways = CreatePaymentGatewayService(billingOptions);
        var walletService = new WalletService(db, paymentGateways, platformLinks, billingOptions);
        return new LearnerService(db, fileStorage, new NoOpPdfTextExtractor(), platformLinks, null!, walletService, paymentGateways, null!, billingOptions, storageOptions);
    }

    private static PaymentGatewayService CreatePaymentGatewayService(IOptions<BillingOptions> billingOptions)
    {
        var stripe = new StripeGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value), NullLogger<StripeGateway>.Instance);
        var paypal = new PayPalGateway(new HttpClient(), billingOptions);
        return new PaymentGatewayService(
            stripe,
            paypal,
            new OetLearner.Api.Services.Billing.Gateways.PayTabsGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value)),
            new OetLearner.Api.Services.Billing.Gateways.PaymobGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value)),
            new OetLearner.Api.Services.Billing.Gateways.CheckoutComGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value)));
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

    private sealed class TestHostEnvironment(string contentRootPath) : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = contentRootPath;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
