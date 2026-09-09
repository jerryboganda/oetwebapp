using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// 2026-09-09 acceptance pass — the ONLY learner-reachable Speaking practice
/// UI (Selection -> /speaking/roleplay/[id] -> /speaking/task/[id]) carries
/// RolePlayCard.Id through its URLs and ultimately calls
/// <c>CreateSpeakingAttemptAsync</c> with that id as <c>ContentId</c>. But
/// RolePlayCard.Id and its ContentItem shell's own Id are minted as two
/// distinct, differently-prefixed values (<c>rpc-...</c> vs <c>ci-...</c>),
/// and CreateAttemptAsync's original content lookup was a strict
/// <c>ContentItems.Id == request.ContentId</c> match — so every real
/// Speaking submission 404'd with "content_not_found" before an Attempt
/// could even be created, confirmed live in production. Pins the fix: a
/// RolePlayCard.Id fallback (mirroring the one GetSpeakingTaskAsync already
/// used for card previews) resolves to the canonical ContentItem.Id, which
/// is what gets stored on the Attempt and used for downstream binding
/// checks (CreateSpeakingUploadSessionAsync -> EnsureSpeakingAttemptBindingAsync).
/// </summary>
public sealed class CreateSpeakingAttemptRolePlayCardIdTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;
    private LearnerService _learnerService = default!;
    private string _storageRoot = default!;

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-attempt-cardid-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new LearnerDbContext(options);

        var billingOptions = Options.Create(new BillingOptions());
        var platformLinks = new PlatformLinkService(
            TestRuntimeSettingsProvider.FromPlatformOptions(new PlatformOptions { FallbackEmailDomain = "example.test" }),
            billingOptions);

        _storageRoot = Path.Combine(Path.GetTempPath(), $"oet-speaking-attempt-cardid-tests-{Guid.NewGuid():N}");
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

        _learnerService = new LearnerService(
            _db, fileStorage, pdfTextExtractor, platformLinks,
            notifications: null!, walletService, paymentGateways,
            disputeService: null!, billingOptions, storageOptions);

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

    [Fact]
    public async Task CreateSpeakingAttempt_WithRolePlayCardId_ResolvesToTheContentItem()
    {
        var userId = await SeedLearnerAsync("medicine");
        var (contentItemId, rolePlayCardId) = await SeedCardAsync(profession: "medicine");

        var result = await _learnerService.CreateSpeakingAttemptAsync(
            userId,
            new CreateAttemptRequest(rolePlayCardId, Context: null, Mode: "self", DeviceType: null, ParentAttemptId: null),
            CancellationToken.None);

        var attemptId = ExtractAttemptId(result);
        var attempt = await _db.Attempts.SingleAsync(a => a.Id == attemptId);
        Assert.Equal(contentItemId, attempt.ContentId);
        Assert.NotEqual(rolePlayCardId, attempt.ContentId);
    }

    [Fact]
    public async Task CreateSpeakingAttempt_WithContentItemId_StillWorksDirectly()
    {
        var userId = await SeedLearnerAsync("medicine");
        var (contentItemId, _) = await SeedCardAsync(profession: "medicine");

        var result = await _learnerService.CreateSpeakingAttemptAsync(
            userId,
            new CreateAttemptRequest(contentItemId, Context: null, Mode: "self", DeviceType: null, ParentAttemptId: null),
            CancellationToken.None);

        var attemptId = ExtractAttemptId(result);
        var attempt = await _db.Attempts.SingleAsync(a => a.Id == attemptId);
        Assert.Equal(contentItemId, attempt.ContentId);
    }

    [Fact]
    public async Task CreateSpeakingAttempt_WithUnknownId_StillThrowsContentNotFound()
    {
        var userId = await SeedLearnerAsync("medicine");
        await SeedCardAsync(profession: "medicine");

        var ex = await Assert.ThrowsAsync<ApiException>(() => _learnerService.CreateSpeakingAttemptAsync(
            userId,
            new CreateAttemptRequest("rpc-does-not-exist", Context: null, Mode: "self", DeviceType: null, ParentAttemptId: null),
            CancellationToken.None));
        Assert.Equal("content_not_found", ex.ErrorCode);
    }

    [Fact]
    public async Task UploadSessionBinding_AcceptsTheRolePlayCardIdTheAttemptWasCreatedFrom()
    {
        var userId = await SeedLearnerAsync("medicine");
        var (_, rolePlayCardId) = await SeedCardAsync(profession: "medicine");

        var created = await _learnerService.CreateSpeakingAttemptAsync(
            userId,
            new CreateAttemptRequest(rolePlayCardId, Context: null, Mode: "self", DeviceType: null, ParentAttemptId: null),
            CancellationToken.None);
        var attemptId = ExtractAttemptId(created);

        // The frontend's submitSpeakingRecording() binds every subsequent
        // upload/complete/submit call by the same RolePlayCard.Id the page
        // URL carries — this must not throw speaking_attempt_content_mismatch.
        var upload = await _learnerService.CreateSpeakingUploadSessionAsync(
            userId, attemptId, expectedContentId: rolePlayCardId, expectedMockSessionId: null, CancellationToken.None);
        Assert.NotNull(upload);
    }

    [Fact]
    public async Task UploadSessionBinding_RejectsAGenuinelyDifferentCard()
    {
        var userId = await SeedLearnerAsync("medicine");
        var (_, rolePlayCardId) = await SeedCardAsync(profession: "medicine");
        var (_, otherCardId) = await SeedCardAsync(profession: "medicine");

        var created = await _learnerService.CreateSpeakingAttemptAsync(
            userId,
            new CreateAttemptRequest(rolePlayCardId, Context: null, Mode: "self", DeviceType: null, ParentAttemptId: null),
            CancellationToken.None);
        var attemptId = ExtractAttemptId(created);

        var ex = await Assert.ThrowsAsync<ApiException>(() => _learnerService.CreateSpeakingUploadSessionAsync(
            userId, attemptId, expectedContentId: otherCardId, expectedMockSessionId: null, CancellationToken.None));
        Assert.Equal("speaking_attempt_content_mismatch", ex.ErrorCode);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static string ExtractAttemptId(object result)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(result));
        return doc.RootElement.GetProperty("attemptId").GetString()
            ?? doc.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<string> SeedLearnerAsync(string activeProfessionId)
    {
        var userId = $"learner-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        _db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = "Test Learner",
            Email = $"{userId}@example.test",
            ActiveProfessionId = activeProfessionId,
            AccountStatus = "active",
            CreatedAt = now,
            LastActiveAt = now,
        });
        await _db.SaveChangesAsync();
        return userId;
    }

    private async Task<(string ContentItemId, string RolePlayCardId)> SeedCardAsync(string profession)
    {
        var now = DateTimeOffset.UtcNow;
        var contentItemId = $"ci-{Guid.NewGuid():N}";
        _db.ContentItems.Add(new ContentItem
        {
            Id = contentItemId,
            ContentType = "speaking_roleplay",
            SubtestCode = "speaking",
            ProfessionId = profession,
            Title = "Test role play",
            Difficulty = "core",
            Status = ContentStatus.Published,
            PublishedRevisionId = $"{contentItemId}-r1",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
            DetailJson = "{}",
            ModelAnswerJson = "{}",
        });

        var cardId = $"rpc-{Guid.NewGuid():N}";
        _db.RolePlayCards.Add(new RolePlayCard
        {
            Id = cardId,
            ContentItemId = contentItemId,
            ProfessionId = profession,
            ScenarioTitle = "Test role play",
            Setting = "Clinic",
            CandidateRole = "Nurse",
            InterlocutorRole = "Patient",
            Background = "Background detail",
            Task1 = "Task 1",
            PatientEmotion = "neutral",
            CommunicationGoal = "Inform",
            ClinicalTopic = "general",
            Difficulty = "core",
            PrimaryCategory = "First Visit",
            CriteriaFocusJson = "[]",
            Disclaimer = "Practice estimate only.",
            Status = ContentStatus.Published,
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });

        await _db.SaveChangesAsync();
        return (contentItemId, cardId);
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
