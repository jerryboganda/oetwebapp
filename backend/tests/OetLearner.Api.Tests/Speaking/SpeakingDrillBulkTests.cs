using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests.Speaking;

// T3: covers the atomic bulk endpoint surface for admin Speaking Drills
// (`AdminService.BulkSpeakingDrillsAsync`). Exercises the service directly
// against an isolated in-memory LearnerDbContext, mirroring
// RolePlayCardSerializationTests so it does not depend on Program.cs wiring.
//
// Each test asserts one slice of the contract:
//   - publish / archive / delete happy paths
//   - per-id recoverable failure is recorded (not fatal)
//   - publish permission gate (403 without content:publish)
//   - the whole batch is one transaction (rollback on a fatal mid-batch error)
//   - exactly one audit row per bulk op
public sealed class SpeakingDrillBulkTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;
    private AdminService _adminService = default!;
    private string _storageRoot = default!;

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-drill-bulk-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new LearnerDbContext(options);

        var billingOptions = Options.Create(new BillingOptions());
        var platformLinks = new PlatformLinkService(
            Options.Create(new PlatformOptions { FallbackEmailDomain = "example.test" }),
            billingOptions);

        _storageRoot = Path.Combine(Path.GetTempPath(), $"oet-drill-bulk-tests-{Guid.NewGuid():N}");
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
    public async Task Bulk_Publish_HappyPath_PublishesAllDraftDrills()
    {
        var a = await SeedDrillAsync(ContentStatus.Draft);
        var b = await SeedDrillAsync(ContentStatus.Draft);

        var result = await _adminService.BulkSpeakingDrillsAsync(
            "admin-1", "Admin One", "publish", new[] { a.drillId, b.drillId }, CancellationToken.None);

        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetProperty("totalRequested").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("succeeded").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("failed").GetInt32());

        Assert.Equal(ContentStatus.Published, await StatusOfAsync(a.contentId));
        Assert.Equal(ContentStatus.Published, await StatusOfAsync(b.contentId));
    }

    [Fact]
    public async Task Bulk_Archive_HappyPath_ArchivesAllDrills()
    {
        var a = await SeedDrillAsync(ContentStatus.Published);
        var b = await SeedDrillAsync(ContentStatus.Draft);

        var result = await _adminService.BulkSpeakingDrillsAsync(
            "admin-1", "Admin One", "archive", new[] { a.drillId, b.drillId }, CancellationToken.None);

        Assert.Equal(2, Field(result, "succeeded"));
        Assert.Equal(ContentStatus.Archived, await StatusOfAsync(a.contentId));
        Assert.Equal(ContentStatus.Archived, await StatusOfAsync(b.contentId));
    }

    [Fact]
    public async Task Bulk_Delete_HappyPath_SoftDeletesViaArchive()
    {
        var a = await SeedDrillAsync(ContentStatus.Published);

        var result = await _adminService.BulkSpeakingDrillsAsync(
            "admin-1", "Admin One", "delete", new[] { a.drillId }, CancellationToken.None);

        Assert.Equal(1, Field(result, "succeeded"));
        Assert.Equal(ContentStatus.Archived, await StatusOfAsync(a.contentId));
    }

    [Fact]
    public async Task Bulk_Publish_RecordsFailureForArchivedDrill_WithoutAbortingBatch()
    {
        var ok = await SeedDrillAsync(ContentStatus.Draft);
        var archived = await SeedDrillAsync(ContentStatus.Archived);

        var result = await _adminService.BulkSpeakingDrillsAsync(
            "admin-1", "Admin One", "publish", new[] { ok.drillId, archived.drillId }, CancellationToken.None);

        Assert.Equal(1, Field(result, "succeeded"));
        Assert.Equal(1, Field(result, "failed"));
        // The good drill still published — the per-id failure did not abort the batch.
        Assert.Equal(ContentStatus.Published, await StatusOfAsync(ok.contentId));
        Assert.Equal(ContentStatus.Archived, await StatusOfAsync(archived.contentId));
    }

    [Fact]
    public async Task Bulk_UnknownAction_Throws400()
    {
        var a = await SeedDrillAsync(ContentStatus.Draft);

        var ex = await Assert.ThrowsAsync<ApiException>(() => _adminService.BulkSpeakingDrillsAsync(
            "admin-1", "Admin One", "frobnicate", new[] { a.drillId }, CancellationToken.None));

        Assert.Equal("SPEAKING_DRILL_BULK_ACTION_INVALID", ex.ErrorCode);
    }

    [Fact]
    public async Task Bulk_Publish_WithoutPublishGrant_Throws403()
    {
        var a = await SeedDrillAsync(ContentStatus.Draft);
        // Give the admin an explicit content:write grant only (no content:publish).
        // A non-empty grant set disables the "treat-as-system-admin" fallback.
        await GrantPermissionAsync("admin-writer", AdminPermissions.ContentWrite);

        var ex = await Assert.ThrowsAsync<ApiException>(() => _adminService.BulkSpeakingDrillsAsync(
            "admin-writer", "Writer", "publish", new[] { a.drillId }, CancellationToken.None));

        Assert.Equal(403, ex.StatusCode);
        // Still draft — the gate fired before any mutation.
        Assert.Equal(ContentStatus.Draft, await StatusOfAsync(a.contentId));
    }

    [Fact]
    public async Task Bulk_Archive_WithWriteGrantOnly_IsAllowed()
    {
        var a = await SeedDrillAsync(ContentStatus.Draft);
        await GrantPermissionAsync("admin-writer", AdminPermissions.ContentWrite);

        var result = await _adminService.BulkSpeakingDrillsAsync(
            "admin-writer", "Writer", "archive", new[] { a.drillId }, CancellationToken.None);

        Assert.Equal(1, Field(result, "succeeded"));
        Assert.Equal(ContentStatus.Archived, await StatusOfAsync(a.contentId));
    }

    [Fact]
    public async Task Bulk_WritesExactlyOneAuditRowPerOp()
    {
        var a = await SeedDrillAsync(ContentStatus.Draft);
        var b = await SeedDrillAsync(ContentStatus.Draft);

        await _adminService.BulkSpeakingDrillsAsync(
            "admin-1", "Admin One", "publish", new[] { a.drillId, b.drillId }, CancellationToken.None);

        var auditRows = await _db.AuditEvents
            .Where(e => e.ResourceType == "SpeakingDrill" && e.Action.StartsWith("Bulk"))
            .ToListAsync();

        Assert.Single(auditRows);
        Assert.Equal("BulkPublished", auditRows[0].Action);
    }

    [Fact]
    public async Task Bulk_EmptyIds_Throws400_AndWritesNoAudit()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() => _adminService.BulkSpeakingDrillsAsync(
            "admin-1", "Admin One", "archive", Array.Empty<string>(), CancellationToken.None));

        Assert.Equal("SPEAKING_DRILL_BULK_EMPTY", ex.ErrorCode);
        Assert.Empty(await _db.AuditEvents.ToListAsync());
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static int Field(object result, string name)
    {
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty(name).GetInt32();
    }

    private async Task<ContentStatus> StatusOfAsync(string contentId)
        => (await _db.ContentItems.AsNoTracking().SingleAsync(c => c.Id == contentId)).Status;

    private async Task GrantPermissionAsync(string adminUserId, string permission)
    {
        _db.AdminPermissionGrants.Add(new AdminPermissionGrant
        {
            Id = $"apg-{Guid.NewGuid():N}"[..32],
            AdminUserId = adminUserId,
            Permission = permission,
        });
        await _db.SaveChangesAsync();
    }

    private async Task<(string drillId, string contentId)> SeedDrillAsync(ContentStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var contentId = $"ci-drill-{Guid.NewGuid():N}";
        var drillId = $"sdi-{Guid.NewGuid():N}";

        _db.ContentItems.Add(new ContentItem
        {
            Id = contentId,
            ContentType = "speaking_drill",
            SubtestCode = "speaking",
            ProfessionId = "nursing",
            Title = "Bulk drill",
            Difficulty = "core",
            Status = status,
            PublishedRevisionId = $"rev-{drillId}",
            PublishedAt = status == ContentStatus.Published ? now : null,
            ArchivedAt = status == ContentStatus.Archived ? now : null,
            DetailJson = "{\"instructionText\":\"Do the drill.\",\"drillKind\":\"Empathy\",\"targetCriteria\":[\"relationshipBuilding\"]}",
            ModelAnswerJson = "{}",
            CriteriaFocusJson = "[\"relationshipBuilding\"]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        _db.SpeakingDrillItems.Add(new SpeakingDrillItem
        {
            Id = drillId,
            ContentItemId = contentId,
            DrillKind = SpeakingDrillKind.Empathy,
            TargetCriteriaJson = "[\"relationshipBuilding\"]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await _db.SaveChangesAsync();
        return (drillId, contentId);
    }
}
