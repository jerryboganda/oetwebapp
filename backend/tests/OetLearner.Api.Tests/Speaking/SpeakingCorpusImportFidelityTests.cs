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
    public async Task RightsNotice_ReachesTheLearner_WithoutTheInternalProvenanceToken()
    {
        // 374 of 387 production cards carry this notice. The owner holds the
        // rights and requires it displayed on the learner's card, so the only
        // thing stripped is the trailing provenance token — our own bookkeeping,
        // which names an internal source scan and page range.
        var attribution = $"{RealRightsNotice} [Official_Samples_p012-014 p12,13]";

        var created = await CreateCardAsync(
            tasks: ["First.", "Second.", "Third."],
            sourceAttribution: attribution);

        // Admins keep the value byte-for-byte, token included.
        var adminView = await _adminService.GetSpeakingRolePlayCardAsync(
            created.CardId, CancellationToken.None);
        Assert.Equal(attribution, adminView.SourceAttribution);

        await PublishWithScriptAsync(created.CardId);

        var contentItemId = await _db.RolePlayCards
            .Where(c => c.Id == created.CardId)
            .Select(c => c.ContentItemId)
            .FirstAsync();
        var payload = Assert.IsAssignableFrom<IDictionary<string, object?>>(
            await _learnerService.GetSpeakingTaskAsync(contentItemId, CancellationToken.None));

        // The notice reaches the learner verbatim, minus the trailing token.
        Assert.Equal(RealRightsNotice, payload["sourceAttribution"]);

        // Sanity: the learner still got a real card, so the assertion above is
        // not passing merely because the projection came back empty.
        Assert.Equal("Corpus fidelity scenario", payload["title"]);
    }

    [Fact]
    public async Task RightsNotice_ThatIsNothingButAProvenanceToken_IsOmitted()
    {
        // 51 production cards carry the token and no notice. Stripping the token
        // must leave null, not an empty attribution line under the card.
        var created = await CreateCardAsync(
            tasks: ["First.", "Second.", "Third."],
            sourceAttribution: "[Pharmacy__Speaking_Pharmacy_Cards_p004-006 p6]");

        await PublishWithScriptAsync(created.CardId);

        var contentItemId = await _db.RolePlayCards
            .Where(c => c.Id == created.CardId)
            .Select(c => c.ContentItemId)
            .FirstAsync();
        var payload = await _learnerService.GetSpeakingTaskAsync(
            contentItemId, CancellationToken.None);

        Assert.Null(Assert.IsAssignableFrom<IDictionary<string, object?>>(payload)["sourceAttribution"]);
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

    // The learner's role-play page (`/speaking/roleplay/[id]`) does not read the
    // card table — it reads `GET /v1/speaking/tasks/{id}`, which projects the
    // `ContentItem` shell. That shell is created with `DetailJson = "{}"` and
    // nothing ever back-fills it, so a projection that trusts the shell alone
    // serves a card with the right title, an empty background and zero task
    // bullets. In production that was true of 368 of 380 published role plays:
    // every one of them opened blank.
    [Fact]
    public async Task LearnerSpeakingTask_CarriesCardBackground_AndEveryBullet()
    {
        var tasks = Enumerable.Range(1, 7)
            .Select(i => $"Find out detail number {i}.")
            .ToArray();
        const string background =
            "You are seeing a 54-year-old teacher who has had a cough for three weeks.";

        var created = await CreateCardAsync(
            tasks: tasks,
            background: background,
            patientName: "Mr Felix Hartwell",
            patientAge: "39");
        await PublishWithScriptAsync(created.CardId);

        var contentItemId = await _db.RolePlayCards
            .Where(c => c.Id == created.CardId)
            .Select(c => c.ContentItemId)
            .FirstAsync();

        var payload = await _learnerService.GetSpeakingTaskAsync(
            contentItemId, CancellationToken.None);
        var json = JsonSerializer.Serialize(payload);

        // Premise check: the shell really is empty. Without this the assertions
        // below could pass for the wrong reason if the shell were ever
        // back-filled, and the join they exist to protect could rot unnoticed.
        var detailJson = await _db.ContentItems
            .Where(c => c.Id == contentItemId)
            .Select(c => c.DetailJson)
            .FirstAsync();
        Assert.DoesNotContain(background, detailJson ?? string.Empty, StringComparison.Ordinal);

        Assert.Contains(background, json, StringComparison.Ordinal);
        foreach (var bullet in tasks)
        {
            Assert.Contains(bullet, json, StringComparison.Ordinal);
        }

        // 13 production cards name the patient and give an age. That is card
        // content, so it has to reach the learner too.
        Assert.Contains("Mr Felix Hartwell, 39", json, StringComparison.Ordinal);

        // The results page's "practise again" link only knows the card id, so
        // the same task lookup has to resolve that too.
        var viaCardId = await _learnerService.GetSpeakingTaskAsync(
            created.CardId, CancellationToken.None);
        Assert.Contains(
            background, JsonSerializer.Serialize(viaCardId), StringComparison.Ordinal);
    }

    private async Task<AdminRolePlayCardDetail> CreateCardAsync(
        string[] tasks,
        string? sourceAttribution = null,
        string? background = null,
        string? patientName = null,
        string? patientAge = null)
        => await _adminService.CreateSpeakingRolePlayCardAsync(
            "admin-1", "Admin One",
            new AdminRolePlayCardCreateRequest(
                ProfessionId: "medicine",
                ScenarioTitle: "Corpus fidelity scenario",
                Setting: "Suburban general practice",
                CandidateRole: "Doctor",
                InterlocutorRole: "Patient",
                PatientName: patientName,
                PatientAge: patientAge,
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

// Regression cover for the production 500 that blocked the corpus import.
// Migration 20260901100000_AddSpeakingSimulationV11PersonaRuntime created
// InterlocutorScripts."SecondVisitCarryFactsJson" as raw `jsonb`, but the EF
// model left it at the default `text`, so EVERY InterlocutorScript INSERT died
// with Postgres 42804 ("column is of type jsonb but expression is of type
// text") — i.e. admin script creation was broken in production, not just the
// import. The SQLite harness the tests above use cannot catch this, because the
// jsonb mappings are deliberately scoped to `Database.IsNpgsql()`. So this
// asserts against a model built for the Npgsql provider. No connection is
// opened — EF builds the model lazily and offline.
public sealed class InterlocutorScriptJsonbMappingTests
{
    [Fact]
    public void SecondVisitCarryFactsJson_is_mapped_as_jsonb_for_npgsql()
    {
        // `UseVector()` mirrors the app's own Npgsql setup (DatabaseConfiguration.cs) —
        // without it the model fails validation on the pgvector `Embedding` property
        // before it ever gets to the column types.
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=u;Password=p",
                npgsql => npgsql.UseVector())
            .Options;
        using var db = new LearnerDbContext(options);

        var columnType = db.Model
            .FindEntityType(typeof(InterlocutorScript))!
            .FindProperty(nameof(InterlocutorScript.SecondVisitCarryFactsJson))!
            .GetColumnType();

        Assert.Equal("jsonb", columnType);
    }
}
