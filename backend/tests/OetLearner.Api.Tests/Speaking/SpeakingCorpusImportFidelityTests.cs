using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
// Unqualified `Services.*` would bind to `OetLearner.Api.Tests.Services` from
// inside this namespace, so the gateway namespace is imported explicitly.
using OetLearner.Api.Services.Billing.Gateways;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests.Speaking;

// End-to-end fidelity cover for the bulk import of the owner's transcribed OET
// Speaking corpus (374 cards from 20 source PDFs, staged by
// `scripts/import-speaking-cards.mjs`).
//
// The importer's own --self-check proves the *mapping* is lossless. It cannot
// prove the *write path* is, because it stops at the HTTP boundary. These tests
// close that gap: they push the exact shapes the real corpus contains through
// `AdminService.CreateSpeakingRolePlayCardAsync` +
// `UpsertInterlocutorScriptAsync`, read them back through the admin projection,
// and assert the text that comes out is byte-identical to what went in.
//
// Every constant below is a measured property of the real corpus, not a
// hypothetical:
//   * 9 task bullets   — the widest card found (the old schema capped at 5)
//   * 602-char bullet  — the longest found (the legacy mirror column is 500)
//   * embedded "\n  - " sub-items — printed dashed sub-bullets, which the
//     transcription deliberately keeps inside their parent bullet
//   * a Cambridge Boxhill rights notice — carried by 291 of 382 records
//
// The brief for this import was "100% same to same, no deviations". A silent
// truncation anywhere on this path would break that promise invisibly, on
// content that has already been verified by hand against the source scans.
public sealed class SpeakingCorpusImportFidelityTests : IAsyncLifetime
{
    // Measured from the corpus. See scripts/import-speaking-cards.mjs.
    private const int LongestRealBulletLength = 602;
    private const string RealRightsNotice =
        "\u00a9 Cambridge Boxhill Language Assessment";

    private LearnerDbContext _db = default!;
    private AdminService _adminService = default!;
    private LearnerService _learnerService = default!;
    private string _storageRoot = default!;

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-corpus-import-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new LearnerDbContext(options);

        var billingOptions = Options.Create(new BillingOptions());
        var platformLinks = new PlatformLinkService(
            TestRuntimeSettingsProvider.FromPlatformOptions(
                new PlatformOptions { FallbackEmailDomain = "example.test" }),
            billingOptions);

        _storageRoot = Path.Combine(
            Path.GetTempPath(), $"oet-corpus-import-tests-{Guid.NewGuid():N}");
        var storageOptions = Options.Create(new StorageOptions { LocalRootPath = _storageRoot });
        var fileStorage = new LocalFileStorage(new TestHostEnvironment(_storageRoot), storageOptions);
        var stripe = new StripeGateway(
            new HttpClient(),
            billingOptions,
            TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StripeGateway>.Instance);
        var paypal = new PayPalGateway(new HttpClient(), billingOptions);
        var settings = TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value);
        var paymentGateways = new PaymentGatewayService(
            stripe,
            paypal,
            new PayTabsGateway(new HttpClient(), billingOptions, settings),
            new PaymobGateway(new HttpClient(), billingOptions, settings),
            new CheckoutComGateway(new HttpClient(), billingOptions, settings),
            new EasyKashGateway(new HttpClient(), billingOptions, settings),
            new WhopGateway(new HttpClient(), billingOptions, settings),
            new FawaterakGateway(new HttpClient(), billingOptions, settings));
        var walletService = new WalletService(_db, paymentGateways, platformLinks, billingOptions);

        _learnerService = new LearnerService(
            _db, fileStorage, new NoOpPdfTextExtractor(), platformLinks,
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
            // Best-effort cleanup.
        }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task NineBulletCard_KeepsEveryBullet_InOrder()
    {
        // The old schema had five positional Task fields. Real printed cards go
        // to nine, so a five-slot write would have silently dropped four
        // bullets of already hand-verified content.
        var tasks = Enumerable.Range(1, 9)
            .Select(i => $"Bullet {i}: ask the patient about their symptoms.")
            .ToArray();

        var created = await CreateCardAsync(tasks: tasks);
        var readBack = await _adminService.GetSpeakingRolePlayCardAsync(
            created.CardId, CancellationToken.None);

        Assert.Equal(9, readBack.Tasks.Length);
        Assert.Equal(tasks, readBack.Tasks);
    }

    [Fact]
    public async Task LongestRealBullet_SurvivesVerbatim_AndClampsOnlyTheLegacyMirror()
    {
        // Regression: `MirrorToLegacyColumns` used to copy the untruncated
        // bullet into the varchar(500) legacy column, so Postgres rejected the
        // whole INSERT and the card could not be saved at all — not by the
        // importer, and not by an admin typing it in by hand.
        var longBullet = "A" + new string('x', LongestRealBulletLength - 1);
        Assert.Equal(LongestRealBulletLength, longBullet.Length);

        var created = await CreateCardAsync(tasks: [longBullet, "Second.", "Third."]);
        var readBack = await _adminService.GetSpeakingRolePlayCardAsync(
            created.CardId, CancellationToken.None);

        // The authoritative list keeps the full text...
        Assert.Equal(longBullet, readBack.Tasks[0]);
        Assert.Equal(LongestRealBulletLength, readBack.Tasks[0]!.Length);

        // ...while the legacy mirror stays inside its column width.
        var row = await _db.RolePlayCards.AsNoTracking()
            .FirstAsync(x => x.Id == created.CardId);
        Assert.Equal(RolePlayCardTasks.LegacyColumnLength, row.Task1!.Length);
        Assert.StartsWith(row.Task1, longBullet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintedSubBullets_StayInsideTheirParentBullet()
    {
        // The transcription keeps printed dashed sub-items nested in the parent
        // bullet rather than promoting them to top-level bullets. Splitting them
        // here would invent structure the source does not have — and would also
        // fake 6 short cards past the >=3-bullet publish gate.
        var withSubItems =
            "Explain the treatment options:\n  - tablets taken twice daily\n  - a topical cream";

        var created = await CreateCardAsync(tasks: [withSubItems, "Second.", "Third."]);
        var readBack = await _adminService.GetSpeakingRolePlayCardAsync(
            created.CardId, CancellationToken.None);

        Assert.Equal(3, readBack.Tasks.Length);
        Assert.Equal(withSubItems, readBack.Tasks[0]);
        Assert.Contains("\n  - a topical cream", readBack.Tasks[0]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RightsNotice_IsKeptForAdmins_AndNeverReachesTheLearner()
    {
        // 291 of 382 corpus records carry this notice. The owner asked for the
        // marker off the learner-facing card — NOT for the content to be
        // dropped, and stripping a rights notice outright would be worse than
        // keeping it. So it rides along in an admin-only column.
        var attribution = $"{RealRightsNotice} [Official_Samples_p012-014 p12,13]";

        var created = await CreateCardAsync(
            tasks: ["First.", "Second.", "Third."],
            sourceAttribution: attribution);

        var adminView = await _adminService.GetSpeakingRolePlayCardAsync(
            created.CardId, CancellationToken.None);
        Assert.Equal(attribution, adminView.SourceAttribution);

        await PublishWithScriptAsync(created.CardId);
        const string userId = "corpus-import-learner";
        await SeedLearnerAsync(userId);

        var learnerView = await _learnerService.GetSpeakingRolePlayCardForLearnerAsync(
            userId, created.CardId, CancellationToken.None);
        var json = JsonSerializer.Serialize(learnerView);

        Assert.DoesNotContain("SourceAttribution", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cambridge Boxhill", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\u00a9", json, StringComparison.Ordinal);

        // Sanity: the learner still got a real card, so the assertions above
        // are not passing merely because the projection came back empty.
        Assert.Contains("Corpus fidelity scenario", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RoleplayerFace_RoundTripsEveryBullet_AndStaysHidden()
    {
        // The roleplayer (patient) face is the other half of every card. It has
        // the same unbounded-list problem and the same must-not-leak rule.
        var patientTasks = Enumerable.Range(1, 7)
            .Select(i => $"When asked, say detail number {i}.")
            .ToArray();

        var created = await CreateCardAsync(tasks: ["First.", "Second.", "Third."]);
        await _adminService.UpsertInterlocutorScriptAsync(
            "admin-1", "Admin One", created.CardId,
            new AdminInterlocutorScriptUpsertRequest(
                OpeningResponse: patientTasks[0],
                PatientBackground: "You are a 54-year-old teacher.",
                PatientTasks: patientTasks),
            CancellationToken.None);

        var readBack = await _adminService.GetSpeakingRolePlayCardAsync(
            created.CardId, CancellationToken.None);

        Assert.NotNull(readBack.InterlocutorScript);
        Assert.Equal(7, readBack.InterlocutorScript!.PatientTasks!.Length);
        Assert.Equal(patientTasks, readBack.InterlocutorScript.PatientTasks);
        // Bullet 1 is COPIED into OpeningResponse, never moved out of the list.
        Assert.Equal(patientTasks[0], readBack.InterlocutorScript.OpeningResponse);
    }

    [Fact]
    public async Task CardBodyText_IsStoredExactlyAsTranscribed()
    {
        // Guards against any well-meaning normalisation (smart quotes, collapsed
        // whitespace, stripped accents) creeping into the write path. These
        // strings were verified character-by-character against the source scans.
        const string background =
            "You are a GP. Your patient, Mr O'Neill, is a 63-year-old man who "
            + "attended A&E last week \u2014 he was told his BP was \u201chigh\u201d "
            + "(180/100 mmHg) and advised to see you.";

        var created = await CreateCardAsync(
            tasks: ["First.", "Second.", "Third."], background: background);
        var readBack = await _adminService.GetSpeakingRolePlayCardAsync(
            created.CardId, CancellationToken.None);

        Assert.Equal(background, readBack.Background);
    }

    // ── Fixture helpers ──────────────────────────────────────────────────

    private async Task<AdminRolePlayCardDetail> CreateCardAsync(
        string[] tasks,
        string? sourceAttribution = null,
        string? background = null)
        => await _adminService.CreateSpeakingRolePlayCardAsync(
            "admin-1", "Admin One",
            new AdminRolePlayCardCreateRequest(
                ProfessionId: "medicine",
                ScenarioTitle: "Corpus fidelity scenario",
                Setting: "Suburban general practice",
                CandidateRole: "Doctor",
                InterlocutorRole: "Patient",
                PatientName: null,
                PatientAge: null,
                Background: background ?? "You are a general practitioner.",
                Task1: null, Task2: null, Task3: null, Task4: null, Task5: null,
                AllowedNotes: true,
                PrepTimeSeconds: 180,
                RolePlayTimeSeconds: 300,
                PatientEmotion: "neutral",
                CommunicationGoal: "Inform",
                ClinicalTopic: "general",
                Difficulty: "core",
                CriteriaFocus: [],
                Disclaimer: null,
                IsLiveTutorEligible: null,
                CardTypeId: null,
                DisplayCardNumber: 12,
                Tasks: tasks,
                SourceAttribution: sourceAttribution),
            CancellationToken.None);

    private async Task PublishWithScriptAsync(string cardId)
    {
        await _adminService.UpsertInterlocutorScriptAsync(
            "admin-1", "Admin One", cardId,
            new AdminInterlocutorScriptUpsertRequest(
                OpeningResponse: "When asked, say you are worried.",
                PatientBackground: "You are a 54-year-old teacher.",
                PatientTasks: ["When asked, say you are worried."]),
            CancellationToken.None);

        await _adminService.PublishSpeakingRolePlayCardAsync(
            "admin-1", "Admin One", cardId, CancellationToken.None);
    }

    private async Task SeedLearnerAsync(string userId)
    {
        var now = DateTimeOffset.UtcNow;
        _db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = "Test Learner",
            Email = $"{userId}@example.test",
            CreatedAt = now,
            LastActiveAt = now,
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
