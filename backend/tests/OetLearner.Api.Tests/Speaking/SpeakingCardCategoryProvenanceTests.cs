using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

// 2026-09-09 classifier repair — persisted lifecycle coverage for the
// category-provenance fields (CategorySource/CategoryClassifierVersion/
// CategoryClassifiedAt) that SpeakingCardClassifierTests (pure function,
// no DB) cannot exercise: automatic classification on create, manual
// overrides, content-edit reclassification, versioned reclassification,
// secondary-tag whitelist validation, and reviewed Other Cards. Mirrors the
// isolated in-memory LearnerDbContext + direct AdminService setup used by
// RolePlayCardBulkTests.
public sealed class SpeakingCardCategoryProvenanceTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;
    private AdminService _adminService = default!;
    private string _storageRoot = default!;

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"role-play-card-provenance-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new LearnerDbContext(options);

        var billingOptions = Options.Create(new BillingOptions());
        var platformLinks = new PlatformLinkService(
            TestRuntimeSettingsProvider.FromPlatformOptions(new PlatformOptions { FallbackEmailDomain = "example.test" }),
            billingOptions);

        _storageRoot = Path.Combine(Path.GetTempPath(), $"oet-rpc-provenance-tests-{Guid.NewGuid():N}");
        var storageOptions = Options.Create(new StorageOptions { LocalRootPath = _storageRoot });
        var fileStorage = new LocalFileStorage(new TestHostEnvironment(_storageRoot), storageOptions);
        var pdfTextExtractor = new NoOpPdfTextExtractor();
        var stripe = new OetLearner.Api.Services.StripeGateway(
            new HttpClient(),
            billingOptions,
            TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OetLearner.Api.Services.StripeGateway>.Instance);
        var paypal = new OetLearner.Api.Services.PayPalGateway(new HttpClient(), billingOptions);
        var paymentGateways = new OetLearner.Api.Services.PaymentGatewayService(
            stripe,
            paypal,
            new OetLearner.Api.Services.Billing.Gateways.PayTabsGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value)),
            new OetLearner.Api.Services.Billing.Gateways.PaymobGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value)),
            new OetLearner.Api.Services.Billing.Gateways.CheckoutComGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value)),
            new OetLearner.Api.Services.Billing.Gateways.EasyKashGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value)),
            new OetLearner.Api.Services.Billing.Gateways.WhopGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value)),
            new OetLearner.Api.Services.Billing.Gateways.FawaterakGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value)));
        var walletService = new WalletService(_db, paymentGateways, platformLinks, billingOptions);

        var learnerService = new LearnerService(
            _db, fileStorage, pdfTextExtractor, platformLinks,
            notifications: null!, walletService, paymentGateways,
            disputeService: null!, billingOptions, storageOptions);

        _adminService = new AdminService(
            _db,
            emailOtpService: null!,
            passwordHasher: null!,
            passwordPolicyService: null!,
            timeProvider: TimeProvider.System,
            notifications: null!,
            learnerService: learnerService);

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        try
        {
            if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
        }
        catch { /* best-effort */ }
        return Task.CompletedTask;
    }

    private static AdminRolePlayCardCreateRequest BaseRequest(
        string scenarioTitle,
        string background,
        string[] tasks,
        string? primaryCategory = null,
        string[]? secondaryTags = null,
        bool? categoryNeedsReview = null) => new(
        ProfessionId: "nursing",
        ScenarioTitle: scenarioTitle,
        Setting: "Clinic",
        CandidateRole: "Nurse",
        InterlocutorRole: "Patient",
        PatientName: null,
        PatientAge: null,
        Background: background,
        Task1: null, Task2: null, Task3: null, Task4: null, Task5: null,
        AllowedNotes: true,
        PrepTimeSeconds: 180,
        RolePlayTimeSeconds: 300,
        PatientEmotion: "neutral",
        CommunicationGoal: "Inform",
        ClinicalTopic: "general",
        Difficulty: "core",
        CriteriaFocus: Array.Empty<string>(),
        Disclaimer: null,
        IsLiveTutorEligible: false,
        PrimaryCategory: primaryCategory,
        SecondaryTags: secondaryTags,
        CategoryNeedsReview: categoryNeedsReview,
        Tasks: tasks);

    [Fact]
    public async Task Create_WithoutExplicitCategory_IsClassifierSourced()
    {
        var detail = await _adminService.CreateSpeakingRolePlayCardAsync(
            "admin-1", "Admin One",
            BaseRequest(
                "Follow-up review",
                "Mr Lee is returning for a follow-up of his asthma control.",
                new[] { "Review inhaler technique" }),
            CancellationToken.None);

        Assert.Equal("Second Visit / Follow-up", detail.PrimaryCategory);
        Assert.Equal("classifier", detail.CategorySource);
        Assert.Equal(SpeakingCardClassifier.ClassifierVersion, detail.CategoryClassifierVersion);
        Assert.NotNull(detail.CategoryClassifiedAt);
    }

    [Fact]
    public async Task Create_WithExplicitCategory_IsManualSourced_NoClassifierVersion()
    {
        var detail = await _adminService.CreateSpeakingRolePlayCardAsync(
            "admin-1", "Admin One",
            BaseRequest(
                "Nursing home visit",
                "You are visiting a nursing home resident to review the medication chart.",
                new[] { "Go through the chart" },
                primaryCategory: "Already Known Patient",
                categoryNeedsReview: false),
            CancellationToken.None);

        Assert.Equal("Already Known Patient", detail.PrimaryCategory);
        Assert.Equal("manual", detail.CategorySource);
        Assert.Null(detail.CategoryClassifierVersion);
        Assert.Null(detail.CategoryClassifiedAt);
    }

    [Fact]
    public async Task Update_ExplicitCategoryPick_BecomesManual_AndClearsStaleReviewFlag()
    {
        var created = await _adminService.CreateSpeakingRolePlayCardAsync(
            "admin-1", "Admin One",
            BaseRequest("Unclear scenario", "Talk to the patient about their health.", Array.Empty<string>()),
            CancellationToken.None);
        Assert.Equal("Other Cards", created.PrimaryCategory);
        Assert.True(created.CategoryNeedsReview);

        var updated = await _adminService.UpdateSpeakingRolePlayCardAsync(
            "admin-1", "Admin One", created.CardId,
            new AdminRolePlayCardUpdateRequest(PrimaryCategory: "Reluctant Patient"),
            CancellationToken.None);

        Assert.Equal("Reluctant Patient", updated.PrimaryCategory);
        Assert.Equal("manual", updated.CategorySource);
        Assert.False(updated.CategoryNeedsReview); // explicit pick clears stale review state
    }

    [Fact]
    public async Task Update_ContentEditOnClassifierRow_ReRunsClassifier()
    {
        var created = await _adminService.CreateSpeakingRolePlayCardAsync(
            "admin-1", "Admin One",
            BaseRequest(
                "First visit",
                "Mrs Allen attends the clinic for the first time with headaches.",
                new[] { "Take a history" }),
            CancellationToken.None);
        Assert.Equal("First Visit", created.PrimaryCategory);
        Assert.Equal("classifier", created.CategorySource);

        // Edit the background so it now reads as a follow-up — no PrimaryCategory
        // supplied, so the classifier/legacy row is re-run, not preserved as-is.
        var updated = await _adminService.UpdateSpeakingRolePlayCardAsync(
            "admin-1", "Admin One", created.CardId,
            new AdminRolePlayCardUpdateRequest(
                Background: "Mrs Allen is returning for a follow-up of her headaches."),
            CancellationToken.None);

        Assert.Equal("Second Visit / Follow-up", updated.PrimaryCategory);
        Assert.Equal("classifier", updated.CategorySource);
        Assert.Equal(SpeakingCardClassifier.ClassifierVersion, updated.CategoryClassifierVersion);
    }

    [Fact]
    public async Task Update_ContentEditOnManualRow_PreservesCategory_ButFlagsForReview()
    {
        var created = await _adminService.CreateSpeakingRolePlayCardAsync(
            "admin-1", "Admin One",
            BaseRequest(
                "Discharge advice",
                "Mr Hill was admitted three days ago. You are discharging him today.",
                new[] { "Explain wound care" },
                primaryCategory: "Already Known Patient",
                categoryNeedsReview: false),
            CancellationToken.None);
        Assert.Equal("manual", created.CategorySource);
        Assert.False(created.CategoryNeedsReview);

        // Content changes but no PrimaryCategory supplied — a human-confirmed
        // row must never be silently reclassified; it should surface for
        // re-review instead, keeping the admin's chosen category.
        var updated = await _adminService.UpdateSpeakingRolePlayCardAsync(
            "admin-1", "Admin One", created.CardId,
            new AdminRolePlayCardUpdateRequest(
                Background: "Mr Hill was admitted three days ago for observation. You are discharging him today with new advice."),
            CancellationToken.None);

        Assert.Equal("Already Known Patient", updated.PrimaryCategory); // unchanged
        Assert.Equal("manual", updated.CategorySource); // unchanged
        Assert.True(updated.CategoryNeedsReview); // flagged for a human to re-check
    }

    [Fact]
    public async Task Update_SecondaryTags_DropsAnyTagOutsideTheThreeAllowed()
    {
        var created = await _adminService.CreateSpeakingRolePlayCardAsync(
            "admin-1", "Admin One",
            BaseRequest("First visit", "Mrs Allen attends the clinic for the first time.", Array.Empty<string>()),
            CancellationToken.None);

        var updated = await _adminService.UpdateSpeakingRolePlayCardAsync(
            "admin-1", "Admin One", created.CardId,
            new AdminRolePlayCardUpdateRequest(SecondaryTags: new[] { "Angry", "Urgent", "VIP" }),
            CancellationToken.None);

        Assert.Equal(new[] { "Angry" }, updated.SecondaryTags);
    }

    [Fact]
    public async Task ManuallyConfirmedOtherCards_CanBeMarkedReviewed_WithoutForcingAnotherCategory()
    {
        var detail = await _adminService.CreateSpeakingRolePlayCardAsync(
            "admin-1", "Admin One",
            BaseRequest(
                "Genuinely uncommon scenario",
                "A profession-specific scenario with no common-framework match.",
                Array.Empty<string>(),
                primaryCategory: "Other Cards",
                categoryNeedsReview: false),
            CancellationToken.None);

        // A human explicitly confirmed Other Cards is correct — it must not
        // be force-flagged for review the way an automatic fallback is.
        Assert.Equal("Other Cards", detail.PrimaryCategory);
        Assert.Equal("manual", detail.CategorySource);
        Assert.False(detail.CategoryNeedsReview);
    }

    [Fact]
    public async Task AutomaticOtherCards_AlwaysCarriesTheReviewFlag()
    {
        var detail = await _adminService.CreateSpeakingRolePlayCardAsync(
            "admin-1", "Admin One",
            BaseRequest("Unclear scenario", "Talk to the patient about their health.", Array.Empty<string>()),
            CancellationToken.None);

        Assert.Equal("Other Cards", detail.PrimaryCategory);
        Assert.Equal("classifier", detail.CategorySource);
        Assert.True(detail.CategoryNeedsReview);
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
