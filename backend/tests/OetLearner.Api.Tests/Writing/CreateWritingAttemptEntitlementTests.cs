using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Writing Rule Enforcement Addendum Rev5 (10 Sep 2026), §12 + §14 mandatory
/// regression/acceptance tests: <c>LearnerService.CreateWritingAttemptAsync</c>
/// is the legacy "Practice this" gate for the content-item Writing Tasks flow.
/// It used to charge via <c>AiPackageCreditService.DeductGradingCreditAsync</c>
/// directly (no free-tier fallback, and not atomic with attempt creation).
/// It now routes the DECISION through the same canonical
/// <see cref="WritingEntitlementService"/> used by Dashboard and
/// Submit-for-Grading, and performs the finite-credit deduction (when one
/// applies) and the attempt row creation in one DB transaction.
/// </summary>
public sealed class CreateWritingAttemptEntitlementTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;
    private LearnerService _learnerService = default!;
    private AiPackageCreditService _credits = default!;
    private string _storageRoot = default!;
    private const string ContentId = "content-writing-1";

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"writing-attempt-entitlement-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new LearnerDbContext(options);

        var billingOptions = Options.Create(new BillingOptions());
        var platformLinks = new PlatformLinkService(
            TestRuntimeSettingsProvider.FromPlatformOptions(new PlatformOptions { FallbackEmailDomain = "example.test" }),
            billingOptions);

        _storageRoot = Path.Combine(Path.GetTempPath(), $"oet-writing-attempt-entitlement-tests-{Guid.NewGuid():N}");
        var storageOptions = Options.Create(new StorageOptions { LocalRootPath = _storageRoot });
        var fileStorage = new LocalFileStorage(new TestHostEnvironment(_storageRoot), storageOptions);
        var pdfTextExtractor = new NoOpPdfTextExtractor();
        var stripe = new StripeGateway(
            new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value),
            NullLogger<StripeGateway>.Instance);
        var paypal = new PayPalGateway(new HttpClient(), billingOptions);
        var paymentGateways = new PaymentGatewayService(
            stripe, paypal,
            new OetLearner.Api.Services.Billing.Gateways.PayTabsGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value)),
            new OetLearner.Api.Services.Billing.Gateways.PaymobGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value)),
            new OetLearner.Api.Services.Billing.Gateways.CheckoutComGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value)),
            new OetLearner.Api.Services.Billing.Gateways.EasyKashGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value)),
            new OetLearner.Api.Services.Billing.Gateways.WhopGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value)),
            new OetLearner.Api.Services.Billing.Gateways.FawaterakGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value)));
        var walletService = new WalletService(_db, paymentGateways, platformLinks, billingOptions);

        _credits = new AiPackageCreditService(_db, NullLogger<AiPackageCreditService>.Instance);
        var writingOptions = new WritingOptionsProvider(_db, new MemoryCache(new MemoryCacheOptions()));
        var resolver = new EffectiveEntitlementResolver(_db);
        IWritingEntitlementService writingEntitlement = new WritingEntitlementService(_db, resolver, writingOptions, _credits);

        _learnerService = new LearnerService(
            _db, fileStorage, pdfTextExtractor, platformLinks,
            notifications: null!, walletService, paymentGateways,
            disputeService: null!, billingOptions, storageOptions,
            writingEntitlement: writingEntitlement,
            aiPackageCreditService: _credits);

        await SeedContentAsync();
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        try { if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true); }
        catch { /* best-effort */ }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task FiniteBalance_CreatesAttemptAndDeductsExactlyOnce()
    {
        var userId = await SeedLearnerAsync();
        await GrantFiniteWritingCreditsAsync(userId, activities: 3);

        var result = await _learnerService.CreateWritingAttemptAsync(
            userId, new CreateAttemptRequest(ContentId, Context: null, Mode: null, DeviceType: null, ParentAttemptId: null),
            CancellationToken.None);

        var attemptId = ExtractAttemptId(result);
        Assert.True(await _db.Attempts.AnyAsync(a => a.Id == attemptId && a.UserId == userId));

        var deducts = await _db.AiPackageCreditTransactions
            .Where(t => t.Reason == AiPackageCreditReason.GradingDeduct)
            .ToListAsync();
        Assert.Single(deducts);

        var snapshot = await _credits.GetSnapshotAsync(userId, 0, CancellationToken.None);
        Assert.Equal(4, snapshot.WritingOnlyCredits); // 6 - 2
    }

    [Fact]
    public async Task DoubleTap_BeforeAttemptExists_ResumesWithoutSecondDeduction()
    {
        var userId = await SeedLearnerAsync();
        await GrantFiniteWritingCreditsAsync(userId, activities: 3);
        var request = new CreateAttemptRequest(ContentId, Context: null, Mode: null, DeviceType: null, ParentAttemptId: null);

        var first = await _learnerService.CreateWritingAttemptAsync(userId, request, CancellationToken.None);
        var second = await _learnerService.CreateWritingAttemptAsync(userId, request, CancellationToken.None);

        Assert.Equal(ExtractAttemptId(first), ExtractAttemptId(second)); // same attempt resumed
        Assert.Equal(1, await _db.Attempts.CountAsync(a => a.UserId == userId && a.SubtestCode == "writing"));
        Assert.Equal(1, await _db.AiPackageCreditTransactions.CountAsync(t => t.Reason == AiPackageCreditReason.GradingDeduct));
    }

    [Fact]
    public async Task ZeroBalanceNoFreeTier_BlocksBeforeAttemptCreated_NoAttemptNoDeduction()
    {
        var userId = await SeedLearnerAsync();
        // No grant at all, free tier disabled by default.

        var ex = await Assert.ThrowsAsync<ApiException>(() => _learnerService.CreateWritingAttemptAsync(
            userId, new CreateAttemptRequest(ContentId, Context: null, Mode: null, DeviceType: null, ParentAttemptId: null),
            CancellationToken.None));

        Assert.Equal(402, ex.StatusCode);
        Assert.False(await _db.Attempts.AnyAsync(a => a.UserId == userId));
        Assert.Empty(await _db.AiPackageCreditTransactions.ToListAsync());
    }

    [Fact]
    public async Task UnlimitedGrant_CreatesAttemptWithoutAnyDeduction()
    {
        var userId = await SeedLearnerAsync();
        await _credits.GrantPackageAsync(
            userId,
            AddOn("pkg_oet_mastery", 180, 0, """{"package_type":"full","unlimited_grading":true,"listening_tests":null,"reading_tests":null}"""),
            1, $"cs_mastery_{userId}", null, CancellationToken.None);
        await SeedActiveMasterySubscriptionAsync(userId);

        var result = await _learnerService.CreateWritingAttemptAsync(
            userId, new CreateAttemptRequest(ContentId, Context: null, Mode: null, DeviceType: null, ParentAttemptId: null),
            CancellationToken.None);

        Assert.True(await _db.Attempts.AnyAsync(a => a.Id == ExtractAttemptId(result)));
        Assert.Empty(await _db.AiPackageCreditTransactions.Where(t => t.Reason == AiPackageCreditReason.GradingDeduct).ToListAsync());
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static string ExtractAttemptId(object result)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(result));
        return doc.RootElement.GetProperty("attemptId").GetString()!;
    }

    private async Task GrantFiniteWritingCreditsAsync(string userId, int activities)
    {
        // 2 raw credits per activity (AiGradingCreditCost.CreditsPerWritingOrSpeakingActivity).
        var rawCredits = activities * 2;
        await _credits.GrantPackageAsync(
            userId,
            AddOn("pkg_writing_starter", 30, activities, $$"""{"package_type":"writing","writing_only_credits":{{rawCredits}}}"""),
            1, $"cs_writing_starter_{userId}", null, CancellationToken.None);
    }

    private async Task SeedActiveMasterySubscriptionAsync(string userId)
    {
        var now = DateTimeOffset.UtcNow;
        var subId = $"sub-mastery-{userId}";
        _db.Subscriptions.Add(new Subscription
        {
            Id = subId,
            UserId = userId,
            PlanId = "plan-free",
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddDays(180),
            ExpiresAt = now.AddDays(180),
            PriceAmount = 0,
            Currency = "GBP",
            Interval = "one_time",
        });
        _db.SubscriptionItems.Add(new SubscriptionItem
        {
            Id = $"item-mastery-{userId}",
            SubscriptionId = subId,
            ItemCode = "pkg_oet_mastery",
            ItemType = "addon",
            Status = SubscriptionItemStatus.Active,
            StartsAt = now,
            EndsAt = now.AddDays(180),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await _db.SaveChangesAsync();
    }

    private static BillingAddOn AddOn(string code, int durationDays, int grantCredits, string grantJson)
        => new()
        {
            Id = $"addon_{code}_{Guid.NewGuid():N}",
            Code = code,
            Name = code,
            Price = 1m,
            Currency = "GBP",
            Interval = "one_time",
            Status = BillingAddOnStatus.Active,
            DurationDays = durationDays,
            GrantCredits = grantCredits,
            GrantEntitlementsJson = grantJson,
            AddonKind = "ai_package",
            AppliesToAllPlans = true,
            IsStackable = true,
            QuantityStep = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private async Task<string> SeedLearnerAsync()
    {
        var userId = $"learner-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        _db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = "Test Learner",
            Email = $"{userId}@example.test",
            AccountStatus = "active",
            CreatedAt = now,
            LastActiveAt = now,
        });
        await _db.SaveChangesAsync();
        return userId;
    }

    private async Task SeedContentAsync()
    {
        var now = DateTimeOffset.UtcNow;
        _db.ContentItems.Add(new ContentItem
        {
            Id = ContentId,
            ContentType = "writing_task",
            SubtestCode = "writing",
            Title = "Writing Task",
            Difficulty = "medium",
            EstimatedDurationMinutes = 45,
            CriteriaFocusJson = "[]",
            ScenarioType = "referral",
            ModeSupportJson = "[]",
            PublishedRevisionId = $"{ContentId}-r1",
            Status = ContentStatus.Published,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await _db.SaveChangesAsync();
    }

    private sealed class TestHostEnvironment(string contentRootPath)
        : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = contentRootPath;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
