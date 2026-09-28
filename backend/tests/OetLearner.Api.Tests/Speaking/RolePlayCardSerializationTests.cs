using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Speaking;

// Phase 1 (J) of the OET Speaking module roadmap.
//
// These tests pin two contractual invariants from the plan:
//   1. The learner-facing `RolePlayCard` serializer MUST NOT include
//      any field that originates from the hidden `InterlocutorScript`
//      row. Even a single leaked property would defeat the whole
//      two-card design.
//   2. The publish gate on `RolePlayCard` MUST refuse to promote a
//      card to Published unless an `InterlocutorScript` has been
//      authored for it.
//
// Both tests target the service surface directly against an isolated
// in-memory `LearnerDbContext` so they don't depend on Program.cs
// wiring.
public sealed class RolePlayCardSerializationTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;
    private LearnerService _learnerService = default!;
    private AdminService _adminService = default!;
    private string _storageRoot = default!;

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"role-play-card-serialization-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new LearnerDbContext(options);

        var billingOptions = Options.Create(new BillingOptions());
        var platformLinks = new PlatformLinkService(
            TestRuntimeSettingsProvider.FromPlatformOptions(new PlatformOptions { FallbackEmailDomain = "example.test" }),
            billingOptions);

        _storageRoot = Path.Combine(
            Path.GetTempPath(), $"oet-role-play-card-tests-{Guid.NewGuid():N}");
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

        _adminService = new AdminService(
            _db,
            emailOtpService: null!,
            passwordHasher: null!,
            passwordPolicyService: null!,
            timeProvider: TimeProvider.System,
            notifications: null!,
            learnerService: _learnerService);

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        try
        {
            if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup — temp directory is fine if it lingers.
        }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task LearnerProjection_DoesNotIncludeInterlocutorFields()
    {
        const string userId = "learner-serializer-1";
        var (cardId, _) = await SeedPublishedCardWithInterlocutorAsync();
        await SeedLearnerProfileAsync(userId);

        var projection = await _learnerService.GetSpeakingRolePlayCardForLearnerAsync(
            userId, cardId, CancellationToken.None);

        // Serialize via System.Text.Json so we exercise the actual on-the-wire
        // shape — this catches both explicit property names and any
        // anonymous-object property that might be added later.
        var json = JsonSerializer.Serialize(projection);

        Assert.DoesNotContain("Prompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HiddenInformation", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ClosingCue", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ResistanceLevel", json, StringComparison.OrdinalIgnoreCase);

        // Sanity: the candidate-side scenario title IS in the payload —
        // proves the serializer ran and returned something meaningful.
        Assert.Contains("Test scenario", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LearnerProjections_NeverIncludeEmotionGoalTopic()
    {
        // Owner, 23 Sep 2026: Emotion / Goal / Topic are internal (AI patient
        // prompt only) and never reach a learner — neither the card detail
        // nor the speaking task payload (which also feeds attempt reads and
        // evaluation summary/review roleCard).
        const string userId = "learner-no-emotion";
        var (cardId, _) = await SeedPublishedCardWithInterlocutorAsync();
        await SeedLearnerProfileAsync(userId);

        var detail = JsonSerializer.Serialize(await _learnerService.GetSpeakingRolePlayCardForLearnerAsync(
            userId, cardId, CancellationToken.None));
        var task = JsonSerializer.Serialize(await _learnerService.GetSpeakingTaskAsync(
            userId, cardId, CancellationToken.None));

        foreach (var json in new[] { detail, task })
        {
            Assert.DoesNotContain("patientEmotion", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("communicationGoal", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("clinicalTopic", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Reassure", json, StringComparison.Ordinal);
            Assert.DoesNotContain("pain management", json, StringComparison.Ordinal);
            Assert.Contains("Test scenario", json, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("medicine")]
    [InlineData(null)]
    public async Task CardDetail_OtherProfessionOrNoProfession_Is404(string? learnerProfession)
    {
        var userId = $"learner-locked-{learnerProfession ?? "none"}";
        var (cardId, _) = await SeedPublishedCardWithInterlocutorAsync();
        await SeedLearnerProfileAsync(userId, learnerProfession);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            _learnerService.GetSpeakingRolePlayCardForLearnerAsync(userId, cardId, CancellationToken.None));
        Assert.Equal("role_play_card_not_found", ex.ErrorCode);
    }

    [Fact]
    public async Task CardDetail_OrphanCardWithoutContentItem_IsNotUniversal()
    {
        // A card whose ContentItem row is missing used to count as
        // "applies to all professions". Now only its own profession sees it.
        var cardId = $"rpc-orphan-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        _db.RolePlayCards.Add(new RolePlayCard
        {
            Id = cardId,
            ContentItemId = $"ci-missing-{Guid.NewGuid():N}",
            ProfessionId = "nursing",
            ScenarioTitle = "Orphan scenario",
            Setting = "Ward",
            CandidateRole = "Nurse",
            InterlocutorRole = "Patient",
            Background = "Background",
            Task1 = "Task 1",
            Difficulty = "core",
            CriteriaFocusJson = "[]",
            Disclaimer = "Practice estimate only.",
            Status = ContentStatus.Published,
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });
        await _db.SaveChangesAsync();
        await SeedLearnerProfileAsync("orphan-medicine", "medicine");
        await SeedLearnerProfileAsync("orphan-nursing", "nursing");

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            _learnerService.GetSpeakingRolePlayCardForLearnerAsync("orphan-medicine", cardId, CancellationToken.None));
        Assert.Equal("role_play_card_not_found", ex.ErrorCode);

        var own = JsonSerializer.Serialize(await _learnerService.GetSpeakingRolePlayCardForLearnerAsync(
            "orphan-nursing", cardId, CancellationToken.None));
        Assert.Contains("Orphan scenario", own, StringComparison.Ordinal);
        Assert.Contains("\"appliesToAllProfessions\":false", own, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishGate_RequiresInterlocutorScript()
    {
        var cardId = await SeedDraftCardWithoutInterlocutorAsync();

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            _adminService.PublishSpeakingRolePlayCardAsync(
                "admin-1", "Admin One", cardId, CancellationToken.None));

        Assert.Equal("role_play_card_missing_interlocutor", exception.ErrorCode);
    }

    // ── Fixture helpers ──────────────────────────────────────────────────

    private async Task<(string cardId, string scriptId)> SeedPublishedCardWithInterlocutorAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var contentItemId = $"ci-{Guid.NewGuid():N}";
        _db.ContentItems.Add(new ContentItem
        {
            Id = contentItemId,
            ContentType = "speaking_roleplay",
            SubtestCode = "speaking",
            ProfessionId = "nursing",
            Title = "Test scenario",
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
            ProfessionId = "nursing",
            ScenarioTitle = "Test scenario",
            Setting = "Surgical ward",
            CandidateRole = "Nurse",
            InterlocutorRole = "Patient",
            Background = "Test background",
            Task1 = "Task 1",
            Task2 = "Task 2",
            Task3 = "Task 3",
            PatientEmotion = "worried",
            CommunicationGoal = "Reassure",
            ClinicalTopic = "pain management",
            Difficulty = "core",
            CriteriaFocusJson = "[]",
            Disclaimer = "Practice estimate only.",
            Status = ContentStatus.Published,
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });

        var scriptId = $"is-{Guid.NewGuid():N}";
        _db.InterlocutorScripts.Add(new InterlocutorScript
        {
            Id = scriptId,
            RolePlayCardId = cardId,
            OpeningResponse = "I'm worried about these tablets.",
            Prompt1 = "Ask about pain at injection site",
            Prompt2 = "Mention nausea concerns",
            Prompt3 = "Push back on dosage",
            HiddenInformation = "Patient had a previous bad reaction to opioids",
            ResistanceLevel = ResistanceLevel.Medium,
            ClosingCue = "Accept advice once reassured about addiction risk",
            EmotionalState = "Worried about taking opioids",
            LayLanguageTriggersJson = JsonSerializer.Serialize(new[] { "NSAIDs", "PRN" }),
            CreatedAt = now,
            UpdatedAt = now,
        });

        await _db.SaveChangesAsync();
        return (cardId, scriptId);
    }

    private async Task<string> SeedDraftCardWithoutInterlocutorAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var contentItemId = $"ci-{Guid.NewGuid():N}";
        _db.ContentItems.Add(new ContentItem
        {
            Id = contentItemId,
            ContentType = "speaking_roleplay",
            SubtestCode = "speaking",
            ProfessionId = "nursing",
            Title = "Draft card",
            Difficulty = "core",
            Status = ContentStatus.Draft,
            PublishedRevisionId = $"{contentItemId}-r1",
            CreatedAt = now,
            UpdatedAt = now,
            DetailJson = "{}",
            ModelAnswerJson = "{}",
        });

        var cardId = $"rpc-{Guid.NewGuid():N}";
        _db.RolePlayCards.Add(new RolePlayCard
        {
            Id = cardId,
            ContentItemId = contentItemId,
            ProfessionId = "nursing",
            ScenarioTitle = "Draft scenario",
            Setting = "Surgical ward",
            CandidateRole = "Nurse",
            InterlocutorRole = "Patient",
            Background = "Background detail",
            Task1 = "Task 1",
            Task2 = "Task 2",
            Task3 = "Task 3",
            PatientEmotion = "worried",
            CommunicationGoal = "Reassure",
            ClinicalTopic = "pain management",
            Difficulty = "core",
            CriteriaFocusJson = "[]",
            Disclaimer = "Practice estimate only.",
            Status = ContentStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await _db.SaveChangesAsync();
        return cardId;
    }

    // Profession lock (23 Sep 2026): a learner with no profession fails
    // closed, so the default learner is of the seeded cards' profession.
    private async Task SeedLearnerProfileAsync(string userId, string? profession = "nursing")
    {
        var now = DateTimeOffset.UtcNow;
        _db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = "Test Learner",
            Email = $"{userId}@example.test",
            ActiveProfessionId = profession,
            CreatedAt = now,
            LastActiveAt = now,
        });
        // EnsureLearnerProfileAsync seeds default Goal/Settings/StudyPlan
        // rows as needed; we only need the user row to satisfy the
        // initial profile-exists guard.
        await _db.SaveChangesAsync();
    }
}
