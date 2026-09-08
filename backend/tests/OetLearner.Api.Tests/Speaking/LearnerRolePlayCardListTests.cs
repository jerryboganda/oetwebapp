using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests.Speaking;

// 2026-09-09 classifier repair — the learner catalogue list
// (`LearnerService.ListSpeakingRolePlayCardsForLearnerAsync`) had no direct
// test coverage. Pins: profession scoping (incl. universal cards),
// primaryCategory filtering, publication-state gating (Published only —
// unlike the single-card detail endpoint, which also allows Archived), the
// server-derived totalCount matching the filtered set, and the existing
// interlocutor-leakage guard extended to the LIST projection. Mirrors the
// isolated in-memory LearnerDbContext harness used by
// RolePlayCardSerializationTests.
public sealed class LearnerRolePlayCardListTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;
    private LearnerService _learnerService = default!;
    private string _storageRoot = default!;

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"learner-rpc-list-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new LearnerDbContext(options);

        var billingOptions = Options.Create(new BillingOptions());
        var platformLinks = new PlatformLinkService(
            TestRuntimeSettingsProvider.FromPlatformOptions(new PlatformOptions { FallbackEmailDomain = "example.test" }),
            billingOptions);

        _storageRoot = Path.Combine(Path.GetTempPath(), $"oet-learner-rpc-list-tests-{Guid.NewGuid():N}");
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
    public async Task List_ScopesToTheLearnersActiveProfession_ButAlwaysIncludesUniversalCards()
    {
        await SeedLearnerAsync("learner-1", activeProfessionId: "nursing");
        await SeedCardAsync(profession: "nursing", contentProfession: "nursing", status: ContentStatus.Published, title: "Nursing card");
        await SeedCardAsync(profession: "medicine", contentProfession: "medicine", status: ContentStatus.Published, title: "Medicine card");
        await SeedCardAsync(profession: "nursing", contentProfession: null, status: ContentStatus.Published, title: "Universal card");

        var result = await _learnerService.ListSpeakingRolePlayCardsForLearnerAsync(
            "learner-1", professionId: null, primaryCategory: null, CancellationToken.None);

        var titles = Titles(result);
        Assert.Contains("Nursing card", titles);
        Assert.Contains("Universal card", titles);
        Assert.DoesNotContain("Medicine card", titles);
        Assert.Equal(titles.Count, TotalCount(result));
    }

    [Fact]
    public async Task List_ExplicitProfessionId_OverridesTheLearnersActiveProfession()
    {
        await SeedLearnerAsync("learner-2", activeProfessionId: "nursing");
        await SeedCardAsync(profession: "medicine", contentProfession: "medicine", status: ContentStatus.Published, title: "Medicine card");

        var result = await _learnerService.ListSpeakingRolePlayCardsForLearnerAsync(
            "learner-2", professionId: "medicine", primaryCategory: null, CancellationToken.None);

        Assert.Contains("Medicine card", Titles(result));
    }

    [Fact]
    public async Task List_FiltersByPrimaryCategory()
    {
        await SeedLearnerAsync("learner-3", activeProfessionId: "nursing");
        await SeedCardAsync(profession: "nursing", contentProfession: "nursing", status: ContentStatus.Published,
            title: "First visit card", primaryCategory: "First Visit");
        await SeedCardAsync(profession: "nursing", contentProfession: "nursing", status: ContentStatus.Published,
            title: "Follow-up card", primaryCategory: "Second Visit / Follow-up");

        var result = await _learnerService.ListSpeakingRolePlayCardsForLearnerAsync(
            "learner-3", professionId: null, primaryCategory: "First Visit", CancellationToken.None);

        var titles = Titles(result);
        Assert.Contains("First visit card", titles);
        Assert.DoesNotContain("Follow-up card", titles);
        Assert.Equal(1, TotalCount(result));
    }

    [Fact]
    public async Task List_OnlyReturnsPublished_DraftAndArchivedAreExcluded()
    {
        await SeedLearnerAsync("learner-4", activeProfessionId: "nursing");
        await SeedCardAsync(profession: "nursing", contentProfession: "nursing", status: ContentStatus.Published, title: "Published card");
        await SeedCardAsync(profession: "nursing", contentProfession: "nursing", status: ContentStatus.Draft, title: "Draft card");
        await SeedCardAsync(profession: "nursing", contentProfession: "nursing", status: ContentStatus.Archived, title: "Archived card");

        var result = await _learnerService.ListSpeakingRolePlayCardsForLearnerAsync(
            "learner-4", professionId: null, primaryCategory: null, CancellationToken.None);

        var titles = Titles(result);
        Assert.Contains("Published card", titles);
        Assert.DoesNotContain("Draft card", titles);
        Assert.DoesNotContain("Archived card", titles);
        Assert.Equal(1, TotalCount(result));
    }

    [Fact]
    public async Task List_TotalCountIsExactlyTheFilteredSetSize()
    {
        await SeedLearnerAsync("learner-5", activeProfessionId: "nursing");
        for (var i = 0; i < 3; i++)
        {
            await SeedCardAsync(profession: "nursing", contentProfession: "nursing", status: ContentStatus.Published, title: $"Card {i}");
        }

        var result = await _learnerService.ListSpeakingRolePlayCardsForLearnerAsync(
            "learner-5", professionId: null, primaryCategory: null, CancellationToken.None);

        Assert.Equal(3, TotalCount(result));
        Assert.Equal(3, Titles(result).Count);
    }

    [Fact]
    public async Task List_ProjectionNeverIncludesInterlocutorFields()
    {
        await SeedLearnerAsync("learner-6", activeProfessionId: "nursing");
        await SeedCardAsync(
            profession: "nursing", contentProfession: "nursing", status: ContentStatus.Published,
            title: "Leakage check card", withInterlocutorScript: true);

        var result = await _learnerService.ListSpeakingRolePlayCardsForLearnerAsync(
            "learner-6", professionId: null, primaryCategory: null, CancellationToken.None);

        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("Prompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HiddenInformation", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ClosingCue", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ResistanceLevel", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Leakage check card", json, StringComparison.Ordinal);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static List<string> Titles(object result)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        return doc.RootElement.GetProperty("rolePlayCards")
            .EnumerateArray()
            .Select(e => e.GetProperty("scenarioTitle").GetString()!)
            .ToList();
    }

    private static int TotalCount(object result)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        return doc.RootElement.GetProperty("totalCount").GetInt32();
    }

    private async Task SeedLearnerAsync(string userId, string activeProfessionId)
    {
        var now = DateTimeOffset.UtcNow;
        _db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = "Test Learner",
            Email = $"{userId}@example.test",
            ActiveProfessionId = activeProfessionId,
            CreatedAt = now,
            LastActiveAt = now,
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedCardAsync(
        string profession,
        string? contentProfession,
        ContentStatus status,
        string title,
        string primaryCategory = "First Visit",
        bool withInterlocutorScript = false)
    {
        var now = DateTimeOffset.UtcNow;
        var contentItemId = $"ci-{Guid.NewGuid():N}";
        _db.ContentItems.Add(new ContentItem
        {
            Id = contentItemId,
            ContentType = "speaking_roleplay",
            SubtestCode = "speaking",
            ProfessionId = contentProfession,
            Title = title,
            Difficulty = "core",
            Status = status,
            PublishedRevisionId = $"{contentItemId}-r1",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = status == ContentStatus.Published ? now : null,
            DetailJson = "{}",
            ModelAnswerJson = "{}",
        });

        var cardId = $"rpc-{Guid.NewGuid():N}";
        _db.RolePlayCards.Add(new RolePlayCard
        {
            Id = cardId,
            ContentItemId = contentItemId,
            ProfessionId = profession,
            ScenarioTitle = title,
            Setting = "Clinic",
            CandidateRole = "Nurse",
            InterlocutorRole = "Patient",
            Background = "Background detail",
            Task1 = "Task 1",
            PatientEmotion = "neutral",
            CommunicationGoal = "Inform",
            ClinicalTopic = "general",
            Difficulty = "core",
            PrimaryCategory = primaryCategory,
            CriteriaFocusJson = "[]",
            Disclaimer = "Practice estimate only.",
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = status == ContentStatus.Published ? now : null,
        });

        if (withInterlocutorScript)
        {
            _db.InterlocutorScripts.Add(new InterlocutorScript
            {
                Id = $"is-{Guid.NewGuid():N}",
                RolePlayCardId = cardId,
                OpeningResponse = "Opening line.",
                Prompt1 = "Secret prompt",
                HiddenInformation = "Secret info",
                ResistanceLevel = ResistanceLevel.Low,
                ClosingCue = "Secret closing cue",
                EmotionalState = "worried",
                LayLanguageTriggersJson = "[]",
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

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
