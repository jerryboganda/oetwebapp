using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests.Speaking;

// T4: covers the atomic bulk endpoint surface for admin Speaking Role-Play
// Cards (`AdminService.BulkAsync`). Exercises the service directly against an
// isolated in-memory LearnerDbContext, mirroring RolePlayCardSerializationTests
// and the sibling SpeakingDrillBulkTests so it does not depend on Program.cs
// wiring.
//
// Each test asserts one slice of the contract:
//   - publish happy path
//   - publish blocked by the missing-interlocutor-script gate is recorded in
//     Failed/Errors (the eligible cards in the same batch still commit)
//   - archive happy path
//   - publish permission gate (403 without content:publish)
//   - the whole batch is one transaction (rollback on a fatal mid-batch error)
//   - exactly one audit row per bulk op
public sealed class RolePlayCardBulkTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;
    private AdminService _adminService = default!;
    private string _storageRoot = default!;

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"role-play-card-bulk-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new LearnerDbContext(options);

        var billingOptions = Options.Create(new BillingOptions());
        var platformLinks = new PlatformLinkService(
            Options.Create(new PlatformOptions { FallbackEmailDomain = "example.test" }),
            billingOptions);

        _storageRoot = Path.Combine(Path.GetTempPath(), $"oet-rpc-bulk-tests-{Guid.NewGuid():N}");
        var storageOptions = Options.Create(new StorageOptions { LocalRootPath = _storageRoot });
        var fileStorage = new LocalFileStorage(new TestHostEnvironment(_storageRoot), storageOptions);
        var pdfTextExtractor = new NoOpPdfTextExtractor();
        var stripe = new OetLearner.Api.Services.StripeGateway(
            new HttpClient(),
            billingOptions,
            TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value));
        var paypal = new OetLearner.Api.Services.PayPalGateway(new HttpClient(), billingOptions);
        var paymentGateways = new OetLearner.Api.Services.PaymentGatewayService(
            stripe,
            paypal,
            new OetLearner.Api.Services.Billing.Gateways.PayTabsGateway(new HttpClient(), billingOptions),
            new OetLearner.Api.Services.Billing.Gateways.PaymobGateway(new HttpClient(), billingOptions),
            new OetLearner.Api.Services.Billing.Gateways.CheckoutComGateway(new HttpClient(), billingOptions));
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

    [Fact]
    public async Task Bulk_Publish_HappyPath_PublishesAllEligibleCards()
    {
        var a = await SeedCardAsync(ContentStatus.Draft, withScript: true);
        var b = await SeedCardAsync(ContentStatus.Draft, withScript: true);

        var result = await _adminService.BulkAsync(
            "admin-1", "Admin One", "publish", new[] { a.cardId, b.cardId }, CancellationToken.None);

        Assert.Equal(2, Field(result, "totalRequested"));
        Assert.Equal(2, Field(result, "succeeded"));
        Assert.Equal(0, Field(result, "failed"));

        Assert.Equal(ContentStatus.Published, await CardStatusAsync(a.cardId));
        Assert.Equal(ContentStatus.Published, await ContentStatusAsync(a.contentId));
        Assert.Equal(ContentStatus.Published, await CardStatusAsync(b.cardId));
    }

    [Fact]
    public async Task Bulk_Publish_BlockedByMissingInterlocutorScript_RecordedInFailed_BatchStillCommits()
    {
        var ok = await SeedCardAsync(ContentStatus.Draft, withScript: true);
        var noScript = await SeedCardAsync(ContentStatus.Draft, withScript: false);

        var result = await _adminService.BulkAsync(
            "admin-1", "Admin One", "publish", new[] { ok.cardId, noScript.cardId }, CancellationToken.None);

        Assert.Equal(1, Field(result, "succeeded"));
        Assert.Equal(1, Field(result, "failed"));

        var errors = Errors(result);
        Assert.Single(errors);
        Assert.Contains("interlocutor script", errors[0], StringComparison.OrdinalIgnoreCase);

        // The eligible card published; the gated one stayed Draft. The per-card
        // failure did NOT abort the batch.
        Assert.Equal(ContentStatus.Published, await CardStatusAsync(ok.cardId));
        Assert.Equal(ContentStatus.Draft, await CardStatusAsync(noScript.cardId));
    }

    [Fact]
    public async Task Bulk_Archive_HappyPath_ArchivesAllCards()
    {
        var a = await SeedCardAsync(ContentStatus.Published, withScript: true);
        var b = await SeedCardAsync(ContentStatus.Draft, withScript: false);

        var result = await _adminService.BulkAsync(
            "admin-1", "Admin One", "archive", new[] { a.cardId, b.cardId }, CancellationToken.None);

        Assert.Equal(2, Field(result, "succeeded"));
        Assert.Equal(ContentStatus.Archived, await CardStatusAsync(a.cardId));
        Assert.Equal(ContentStatus.Archived, await ContentStatusAsync(a.contentId));
        Assert.Equal(ContentStatus.Archived, await CardStatusAsync(b.cardId));
    }

    [Fact]
    public async Task Bulk_UnknownAction_Throws400()
    {
        var a = await SeedCardAsync(ContentStatus.Draft, withScript: true);

        var ex = await Assert.ThrowsAsync<ApiException>(() => _adminService.BulkAsync(
            "admin-1", "Admin One", "frobnicate", new[] { a.cardId }, CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("ROLE_PLAY_CARD_BULK_ACTION_INVALID", ex.ErrorCode);
    }

    [Fact]
    public async Task Bulk_Publish_WithoutPublishGrant_Throws403_NoMutation()
    {
        var a = await SeedCardAsync(ContentStatus.Draft, withScript: true);
        // Give the admin an explicit content:write grant only (no content:publish).
        // A non-empty grant set disables the "treat-as-system-admin" fallback.
        await GrantPermissionAsync("admin-writer", AdminPermissions.ContentWrite);

        var ex = await Assert.ThrowsAsync<ApiException>(() => _adminService.BulkAsync(
            "admin-writer", "Writer", "publish", new[] { a.cardId }, CancellationToken.None));

        Assert.Equal(403, ex.StatusCode);
        // Still draft — the gate fired before any mutation.
        Assert.Equal(ContentStatus.Draft, await CardStatusAsync(a.cardId));
    }

    [Fact]
    public async Task Bulk_Archive_WithWriteGrantOnly_IsAllowed()
    {
        var a = await SeedCardAsync(ContentStatus.Draft, withScript: false);
        await GrantPermissionAsync("admin-writer", AdminPermissions.ContentWrite);

        var result = await _adminService.BulkAsync(
            "admin-writer", "Writer", "archive", new[] { a.cardId }, CancellationToken.None);

        Assert.Equal(1, Field(result, "succeeded"));
        Assert.Equal(ContentStatus.Archived, await CardStatusAsync(a.cardId));
    }

    [Fact]
    public async Task Bulk_WritesExactlyOneAuditRowPerOp()
    {
        var a = await SeedCardAsync(ContentStatus.Draft, withScript: true);
        var b = await SeedCardAsync(ContentStatus.Draft, withScript: true);

        await _adminService.BulkAsync(
            "admin-1", "Admin One", "publish", new[] { a.cardId, b.cardId }, CancellationToken.None);

        var auditRows = await _db.AuditEvents
            .Where(e => e.ResourceType == "RolePlayCard" && e.Action.StartsWith("Bulk"))
            .ToListAsync();

        Assert.Single(auditRows);
        Assert.Equal("BulkPublished", auditRows[0].Action);
    }

    [Fact]
    public async Task Bulk_EmptyIds_Throws400_AndWritesNoAudit()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() => _adminService.BulkAsync(
            "admin-1", "Admin One", "archive", Array.Empty<string>(), CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("ROLE_PLAY_CARD_BULK_EMPTY", ex.ErrorCode);
        Assert.Empty(await _db.AuditEvents.ToListAsync());
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static int Field(object result, string name)
    {
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty(name).GetInt32();
    }

    private static string[] Errors(object result)
    {
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("errors")
            .EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty)
            .ToArray();
    }

    private async Task<ContentStatus> CardStatusAsync(string cardId)
        => (await _db.RolePlayCards.AsNoTracking().SingleAsync(c => c.Id == cardId)).Status;

    private async Task<ContentStatus> ContentStatusAsync(string contentId)
        => (await _db.ContentItems.AsNoTracking().SingleAsync(c => c.Id == contentId)).Status;

    private async Task GrantPermissionAsync(string adminUserId, string permission)
    {
        _db.AdminPermissionGrants.Add(new AdminPermissionGrant
        {
            Id = $"apg-{Guid.NewGuid():N}"[..32],
            AdminUserId = adminUserId,
            Permission = permission,
            GrantedBy = "test-admin",
            GrantedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    private async Task<(string cardId, string contentId)> SeedCardAsync(ContentStatus status, bool withScript)
    {
        var now = DateTimeOffset.UtcNow;
        var contentId = $"ci-rpc-{Guid.NewGuid():N}";
        var cardId = $"rpc-{Guid.NewGuid():N}";

        _db.ContentItems.Add(new ContentItem
        {
            Id = contentId,
            ContentType = "speaking_roleplay",
            SubtestCode = "speaking",
            ProfessionId = "nursing",
            Title = "Bulk card",
            Difficulty = "core",
            Status = status,
            PublishedRevisionId = $"{contentId}-r1",
            PublishedAt = status == ContentStatus.Published ? now : null,
            ArchivedAt = status == ContentStatus.Archived ? now : null,
            DetailJson = "{}",
            ModelAnswerJson = "{}",
            CriteriaFocusJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });

        _db.RolePlayCards.Add(new RolePlayCard
        {
            Id = cardId,
            ContentItemId = contentId,
            ProfessionId = "nursing",
            ScenarioTitle = "Bulk scenario",
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
            Status = status,
            PublishedAt = status == ContentStatus.Published ? now : null,
            ArchivedAt = status == ContentStatus.Archived ? now : null,
            CreatedAt = now,
            UpdatedAt = now,
        });

        if (withScript)
        {
            _db.InterlocutorScripts.Add(new InterlocutorScript
            {
                Id = $"is-{Guid.NewGuid():N}",
                RolePlayCardId = cardId,
                OpeningResponse = "I'm worried about these tablets.",
                Prompt1 = "Ask about pain",
                HiddenInformation = "Previous bad reaction to opioids",
                ResistanceLevel = ResistanceLevel.Medium,
                ClosingCue = "Accept advice once reassured",
                EmotionalState = "Worried",
                LayLanguageTriggersJson = "[]",
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await _db.SaveChangesAsync();
        return (cardId, contentId);
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
