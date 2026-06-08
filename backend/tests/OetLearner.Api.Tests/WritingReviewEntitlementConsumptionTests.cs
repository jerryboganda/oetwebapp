using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests;

/// <summary>
/// Phase 3 acceptance ("Decrement writing_assessments_remaining on submission"):
/// when a learner submits a WRITING letter for expert review and has a bundled/add-on
/// writing assessment available, <see cref="LearnerService.CreateReviewRequestAsync"/>
/// consumes one entitlement (free, <c>PaymentSource == "entitlement"</c>,
/// <c>PriceSnapshot == 0</c>) instead of charging wallet credits; otherwise it falls
/// through to the unchanged wallet/credits path. The admin cancel hook
/// (<see cref="AdminService.CancelReviewAsync"/>) best-effort restores one entitlement.
///
/// Uses a SQLite in-memory database (mirrors <c>LearnerSubmissionsPaginationTests</c>)
/// because the service exercises real SQL (wallet load, transactions, concurrency retry).
/// The seeded learner deliberately has no <c>AuthAccountId</c> and no admin accounts are
/// seeded, so the end-to-end notification fan-out short-circuits and the null
/// NotificationService collaborators are never dereferenced.
/// </summary>
public sealed class WritingReviewEntitlementConsumptionTests : IAsyncLifetime
{
    private const string LearnerId = "learner-entitlement";
    private const string WritingAttemptId = "attempt-writing-1";
    private const string AdminAuthAccountId = "adm-1";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private DbContextOptions<LearnerDbContext> _options = default!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .Options;
        await using var db = new LearnerDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task WritingReview_WithEligibleEntitlement_ConsumesOneAndSkipsWallet()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedLearnerWithWritingAttemptAsync(db, walletCredits: 5);
        var subscription = await SeedSubscriptionAsync(db, writingRemaining: 3, expiresAt: DateTimeOffset.UtcNow.AddDays(30));
        var service = CreateLearnerService(db);

        var response = await service.CreateReviewRequestAsync(LearnerId, BuildRequest(), CancellationToken.None);
        var element = JsonSerializer.SerializeToElement(response);

        Assert.Equal("entitlement", element.GetProperty("paymentSource").GetString());
        Assert.Equal(0m, element.GetProperty("priceSnapshot").GetDecimal());

        await using var verify = new LearnerDbContext(_options);
        var refreshedSub = await verify.Subscriptions.SingleAsync(s => s.Id == subscription.Id);
        Assert.Equal(2, refreshedSub.WritingAssessmentsRemaining); // 3 - 1

        var wallet = await verify.Wallets.SingleAsync(w => w.UserId == LearnerId);
        Assert.Equal(5, wallet.CreditBalance); // untouched

        var review = await verify.ReviewRequests.SingleAsync(r => r.AttemptId == WritingAttemptId);
        Assert.Equal("entitlement", review.PaymentSource);
        Assert.Equal(0m, review.PriceSnapshot);
        Assert.Equal(ReviewRequestState.Queued, review.State);

        // No wallet debit transaction should have been written on the prepaid path.
        Assert.False(await verify.WalletTransactions.AnyAsync(t => t.WalletId == wallet.Id));
    }

    [Fact]
    public async Task WritingReview_WithZeroEntitlement_FallsBackToWalletCredits()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedLearnerWithWritingAttemptAsync(db, walletCredits: 5);
        await SeedSubscriptionAsync(db, writingRemaining: 0, expiresAt: DateTimeOffset.UtcNow.AddDays(30));
        var service = CreateLearnerService(db);

        var response = await service.CreateReviewRequestAsync(LearnerId, BuildRequest(), CancellationToken.None);
        var element = JsonSerializer.SerializeToElement(response);

        Assert.Equal("credits", element.GetProperty("paymentSource").GetString());

        await using var verify = new LearnerDbContext(_options);
        var wallet = await verify.Wallets.SingleAsync(w => w.UserId == LearnerId);
        Assert.Equal(4, wallet.CreditBalance); // 5 - 1 (standard turnaround)

        var review = await verify.ReviewRequests.SingleAsync(r => r.AttemptId == WritingAttemptId);
        Assert.Equal("credits", review.PaymentSource);
        Assert.Equal(1m, review.PriceSnapshot);

        Assert.True(await verify.WalletTransactions.AnyAsync(
            t => t.WalletId == wallet.Id && t.TransactionType == "review_deduction" && t.Amount == -1));
    }

    [Fact]
    public async Task WritingReview_WithExpiredEntitlement_IsIneligible_FallsBackToWalletCredits()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedLearnerWithWritingAttemptAsync(db, walletCredits: 5);
        var subscription = await SeedSubscriptionAsync(db, writingRemaining: 3, expiresAt: DateTimeOffset.UtcNow.AddDays(-1));
        var service = CreateLearnerService(db);

        var response = await service.CreateReviewRequestAsync(LearnerId, BuildRequest(), CancellationToken.None);
        var element = JsonSerializer.SerializeToElement(response);

        Assert.Equal("credits", element.GetProperty("paymentSource").GetString());

        await using var verify = new LearnerDbContext(_options);
        var refreshedSub = await verify.Subscriptions.SingleAsync(s => s.Id == subscription.Id);
        Assert.Equal(3, refreshedSub.WritingAssessmentsRemaining); // expired sub NOT decremented

        var wallet = await verify.Wallets.SingleAsync(w => w.UserId == LearnerId);
        Assert.Equal(4, wallet.CreditBalance); // wallet charged instead
    }

    [Fact]
    public async Task WritingReview_RetriedWithSameIdempotencyKey_DecrementsOnlyOnce()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedLearnerWithWritingAttemptAsync(db, walletCredits: 5);
        var subscription = await SeedSubscriptionAsync(db, writingRemaining: 3, expiresAt: DateTimeOffset.UtcNow.AddDays(30));
        var service = CreateLearnerService(db);
        var request = BuildRequest(idempotencyKey: "idem-writing-entitlement-1");

        var first = JsonSerializer.SerializeToElement(
            await service.CreateReviewRequestAsync(LearnerId, request, CancellationToken.None));
        var second = JsonSerializer.SerializeToElement(
            await service.CreateReviewRequestAsync(LearnerId, request, CancellationToken.None));

        // The idempotency cache short-circuits the retried submit and returns the same review id.
        Assert.Equal(
            first.GetProperty("reviewRequestId").GetString(),
            second.GetProperty("reviewRequestId").GetString());

        await using var verify = new LearnerDbContext(_options);
        var refreshedSub = await verify.Subscriptions.SingleAsync(s => s.Id == subscription.Id);
        Assert.Equal(2, refreshedSub.WritingAssessmentsRemaining); // decremented exactly once, not twice

        Assert.Equal(1, await verify.ReviewRequests.CountAsync(r => r.AttemptId == WritingAttemptId));
    }

    [Fact]
    public async Task AdminCancel_OfEntitlementReview_RestoresOneEntitlement()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedLearnerWithWritingAttemptAsync(db, walletCredits: 5);
        var subscription = await SeedSubscriptionAsync(db, writingRemaining: 3, expiresAt: DateTimeOffset.UtcNow.AddDays(30));
        var learnerService = CreateLearnerService(db);

        await learnerService.CreateReviewRequestAsync(LearnerId, BuildRequest(), CancellationToken.None);
        var reviewId = await db.ReviewRequests.Where(r => r.AttemptId == WritingAttemptId).Select(r => r.Id).SingleAsync();

        // Sanity: consume happened.
        Assert.Equal(2, await db.Subscriptions.Where(s => s.Id == subscription.Id).Select(s => s.WritingAssessmentsRemaining).SingleAsync());

        var adminService = CreateAdminService(db);
        var cancelResult = await adminService.CancelReviewAsync(
            "adm-1", "Admin Tester", reviewId, new AdminReviewCancelRequest("learner requested"), CancellationToken.None);
        var cancelElement = JsonSerializer.SerializeToElement(cancelResult);

        Assert.True(cancelElement.GetProperty("restoredWritingAssessment").GetBoolean());

        await using var verify = new LearnerDbContext(_options);
        var refreshedSub = await verify.Subscriptions.SingleAsync(s => s.Id == subscription.Id);
        Assert.Equal(3, refreshedSub.WritingAssessmentsRemaining); // restored 2 -> 3

        var review = await verify.ReviewRequests.SingleAsync(r => r.Id == reviewId);
        Assert.Equal(ReviewRequestState.Cancelled, review.State);

        // No wallet refund transaction: an entitlement review never debited the wallet.
        var wallet = await verify.Wallets.SingleAsync(w => w.UserId == LearnerId);
        Assert.Equal(5, wallet.CreditBalance);
        Assert.False(await verify.WalletTransactions.AnyAsync(t => t.WalletId == wallet.Id));
    }

    [Fact]
    public async Task AdminReopen_OfCancelledEntitlementReview_ReturnsToQueueNotAwaitingPayment()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedLearnerWithWritingAttemptAsync(db, walletCredits: 5);
        await SeedSubscriptionAsync(db, writingRemaining: 3, expiresAt: DateTimeOffset.UtcNow.AddDays(30));
        var learnerService = CreateLearnerService(db);

        await learnerService.CreateReviewRequestAsync(LearnerId, BuildRequest(), CancellationToken.None);
        var reviewId = await db.ReviewRequests.Where(r => r.AttemptId == WritingAttemptId).Select(r => r.Id).SingleAsync();

        var adminService = CreateAdminService(db);
        await adminService.CancelReviewAsync(
            "adm-1", "Admin Tester", reviewId, new AdminReviewCancelRequest("learner requested"), CancellationToken.None);

        var reopenResult = await adminService.ReopenReviewAsync(
            "adm-1", "Admin Tester", reviewId, new AdminReviewReopenRequest("changed mind"), CancellationToken.None);
        var reopenElement = JsonSerializer.SerializeToElement(reopenResult);

        // A prepaid entitlement review (PaymentSource "entitlement", PriceSnapshot 0)
        // must reopen straight to the queue, never AwaitingPayment.
        Assert.Equal("queued", reopenElement.GetProperty("status").GetString());

        await using var verify = new LearnerDbContext(_options);
        var review = await verify.ReviewRequests.SingleAsync(r => r.Id == reviewId);
        Assert.Equal(ReviewRequestState.Queued, review.State);

        // No wallet recharge for a £0 entitlement review.
        var wallet = await verify.Wallets.SingleAsync(w => w.UserId == LearnerId);
        Assert.Equal(5, wallet.CreditBalance);
        Assert.False(await verify.WalletTransactions.AnyAsync(t => t.WalletId == wallet.Id));
    }

    // ── Seeding ─────────────────────────────────────────────────────────

    private static ReviewRequestCreateRequest BuildRequest(string? idempotencyKey = null)
        => new(
            WritingAttemptId,
            "writing",
            "standard",
            ["OET writing criteria"],
            "Please review.",
            "credits",
            idempotencyKey);

    private static async Task SeedLearnerWithWritingAttemptAsync(LearnerDbContext db, int walletCredits)
    {
        var now = DateTimeOffset.UtcNow;
        if (!await db.ApplicationUserAccounts.AnyAsync(a => a.Id == AdminAuthAccountId))
        {
            db.ApplicationUserAccounts.Add(new ApplicationUserAccount
            {
                Id = AdminAuthAccountId,
                Email = "admin.entitlement@example.test",
                NormalizedEmail = "ADMIN.ENTITLEMENT@EXAMPLE.TEST",
                PasswordHash = "test-only",
                Role = ApplicationUserRoles.Admin,
                EmailVerifiedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        db.Users.Add(new LearnerUser
        {
            Id = LearnerId,
            // Intentionally no AuthAccountId: the learner notification short-circuits.
            DisplayName = "Entitlement Learner",
            Email = "entitlement@example.test",
            CreatedAt = now,
            LastActiveAt = now,
            AccountStatus = "active",
        });

        db.Wallets.Add(new Wallet
        {
            Id = $"wallet-{LearnerId}",
            UserId = LearnerId,
            CreditBalance = walletCredits,
            LastUpdatedAt = now,
        });

        db.ContentItems.Add(new ContentItem
        {
            Id = "content-writing-1",
            ContentType = "writing_task",
            SubtestCode = "writing",
            Title = "Writing Task",
            Difficulty = "medium",
            EstimatedDurationMinutes = 45,
            CriteriaFocusJson = "[]",
            ScenarioType = "referral",
            ModeSupportJson = "[]",
            PublishedRevisionId = "content-writing-1-r1",
            Status = ContentStatus.Published,
            CreatedAt = now,
            UpdatedAt = now,
        });

        db.Attempts.Add(new Attempt
        {
            Id = WritingAttemptId,
            UserId = LearnerId,
            ContentId = "content-writing-1",
            SubtestCode = "writing",
            Context = "practice",
            Mode = "exam",
            State = AttemptState.Completed,
            StartedAt = now.AddMinutes(-30),
            SubmittedAt = now.AddMinutes(-1),
            CompletedAt = now.AddMinutes(-1),
            ElapsedSeconds = 900,
        });

        await db.SaveChangesAsync();
    }

    private static async Task<Subscription> SeedSubscriptionAsync(
        LearnerDbContext db, int writingRemaining, DateTimeOffset? expiresAt)
    {
        var now = DateTimeOffset.UtcNow;
        var sub = new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = LearnerId,
            PlanId = "basic",
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-10),
            ChangedAt = now.AddDays(-10),
            NextRenewalAt = now.AddDays(20),
            PriceAmount = 29.00m,
            Currency = "USD",
            Interval = "month",
            ExpiresAt = expiresAt,
            WritingAssessmentsRemaining = writingRemaining,
            SpeakingSessionsRemaining = 0,
            AiCreditsRemaining = 0,
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();
        return sub;
    }

    // ── Service construction ────────────────────────────────────────────

    private static LearnerService CreateLearnerService(LearnerDbContext db)
    {
        var billingOptions = Options.Create(new BillingOptions { AllowSandboxFallbacks = true });
        var platformLinks = new PlatformLinkService(
            Options.Create(new PlatformOptions { FallbackEmailDomain = "example.test" }),
            billingOptions);
        var storageRoot = Path.Combine(Path.GetTempPath(), $"oet-entitlement-{Guid.NewGuid():N}");
        var storageOptions = Options.Create(new StorageOptions { LocalRootPath = storageRoot });
        var fileStorage = new LocalFileStorage(new TestHostEnvironment(storageRoot), storageOptions);
        var paymentGateways = CreatePaymentGatewayService(billingOptions);
        var walletService = new WalletService(db, paymentGateways, platformLinks, billingOptions);
        var notifications = CreateNotificationService(db);
        return new LearnerService(
            db, fileStorage, new NoOpPdfTextExtractor(), platformLinks, notifications,
            walletService, paymentGateways, null!, billingOptions, storageOptions);
    }

    private static AdminService CreateAdminService(LearnerDbContext db)
        => new(
            db,
            emailOtpService: null!,
            passwordPolicyService: null!,
            passwordHasher: null!,
            timeProvider: TimeProvider.System,
            notifications: null!,
            learnerService: null!);

    private static NotificationService CreateNotificationService(LearnerDbContext db)
        => new(
            db,
            emailSender: null!,
            webPushDispatcher: null!,
            mobilePushDispatcher: null!,
            hubContext: null!,
            platformLinks: null!,
            timeProvider: TimeProvider.System,
            webPushOptions: Options.Create(new WebPushOptions()),
            runtimeSettingsProvider: TestRuntimeSettingsProvider.FromZoomOptions(new ZoomOptions()),
            notificationProofOptions: Options.Create(new NotificationProofHarnessOptions()),
            environment: null!,
            logger: NullLogger<NotificationService>.Instance);

    private static PaymentGatewayService CreatePaymentGatewayService(IOptions<BillingOptions> billingOptions)
    {
        var stripe = new StripeGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value));
        var paypal = new PayPalGateway(new HttpClient(), billingOptions);
        return new PaymentGatewayService(
            stripe,
            paypal,
            new OetLearner.Api.Services.Billing.Gateways.PayTabsGateway(new HttpClient(), billingOptions),
            new OetLearner.Api.Services.Billing.Gateways.PaymobGateway(new HttpClient(), billingOptions),
            new OetLearner.Api.Services.Billing.Gateways.CheckoutComGateway(new HttpClient(), billingOptions));
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
