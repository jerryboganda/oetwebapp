using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests.Mocks;

/// <summary>
/// Tests for the atomic bulk action over admin mock bundles
/// (<see cref="MockService.BulkAsync"/>) backing
/// <c>POST /v1/admin/mock-bundles/bulk</c>.
///
/// The in-memory EF provider is non-relational, so <c>BulkAsync</c> skips the
/// explicit transaction (guarded by <c>Database.IsRelational()</c>) and relies
/// on the single <c>SaveChangesAsync</c>. The single-audit-row and
/// per-id-failure-recording guarantees are still fully exercised here; the
/// transaction wrapping itself is identical to the well-trodden
/// <c>ListeningAuthoringService</c> pattern.
/// </summary>
public class MockBundleBulkActionTests
{
    private const string AdminId = "admin-1";

    private static LearnerDbContext NewDb(string? name = null) =>
        new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString("N"))
            .Options);

    // ── helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Seed a diagnostic bundle. When <paramref name="publishable"/> is true the
    /// bundle carries provenance + one published-paper section so it clears the
    /// publish gate; otherwise it has neither and will fail the gate.
    /// </summary>
    private static void SeedBundle(LearnerDbContext db, string id, ContentStatus status, bool publishable)
    {
        var now = DateTimeOffset.UtcNow;
        db.MockBundles.Add(new MockBundle
        {
            Id = id,
            Title = $"Bundle {id}",
            Slug = $"bundle-{id}",
            MockType = MockTypes.Diagnostic,
            AppliesToAllProfessions = true,
            Status = status,
            EstimatedDurationMinutes = 60,
            ReleasePolicy = MockReleasePolicies.Instant,
            SourceStatus = MockSourceStatuses.Original,
            QualityStatus = MockQualityStatuses.Approved,
            SourceProvenance = publishable ? "Bulk action test seed." : null,
            CreatedAt = now,
            UpdatedAt = now,
        });

        if (publishable)
        {
            var paperId = $"paper-{id}";
            db.ContentPapers.Add(new ContentPaper
            {
                Id = paperId,
                SubtestCode = "reading",
                Title = $"Paper {id}",
                Slug = $"{paperId}-slug",
                AppliesToAllProfessions = true,
                Difficulty = "standard",
                EstimatedDurationMinutes = 60,
                Status = ContentStatus.Published,
                SourceProvenance = "Bulk action test paper.",
                CreatedAt = now,
                UpdatedAt = now,
                PublishedAt = now,
            });
            db.MockBundleSections.Add(new MockBundleSection
            {
                Id = $"section-{id}",
                MockBundleId = id,
                SectionOrder = 1,
                SubtestCode = "reading",
                ContentPaperId = paperId,
                TimeLimitMinutes = 60,
                ReviewEligible = false,
                IsRequired = true,
                CreatedAt = now,
            });
        }
    }

    private static async Task<int> AuditCountAsync(LearnerDbContext db) =>
        await db.AuditEvents.CountAsync();

    private static async Task GrantPermissionAsync(LearnerDbContext db, string adminUserId, string permission)
    {
        db.AdminPermissionGrants.Add(new AdminPermissionGrant
        {
            Id = $"apg-{Guid.NewGuid():N}"[..32],
            AdminUserId = adminUserId,
            Permission = permission,
            GrantedBy = "test-admin",
            GrantedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    // ── happy paths ─────────────────────────────────────────────────────

    [Fact]
    public async Task Bulk_Publish_PublishesDraftBundles()
    {
        await using var db = NewDb();
        SeedBundle(db, "b1", ContentStatus.Draft, publishable: true);
        SeedBundle(db, "b2", ContentStatus.Draft, publishable: true);
        await db.SaveChangesAsync();

        var result = await new MockService(db).BulkAsync("publish", ["b1", "b2"], AdminId, CancellationToken.None);

        Assert.Equal(2, GetInt(result, "succeeded"));
        Assert.Equal(0, GetInt(result, "failed"));
        Assert.Equal(0, GetInt(result, "skipped"));
        Assert.Equal(2, GetInt(result, "totalRequested"));

        var statuses = await db.MockBundles.Select(b => b.Status).ToListAsync();
        Assert.All(statuses, s => Assert.Equal(ContentStatus.Published, s));
        Assert.Equal(1, await AuditCountAsync(db));
    }

    [Fact]
    public async Task Bulk_Archive_ArchivesBundles()
    {
        await using var db = NewDb();
        SeedBundle(db, "b1", ContentStatus.Published, publishable: true);
        SeedBundle(db, "b2", ContentStatus.Draft, publishable: false);
        await db.SaveChangesAsync();

        var result = await new MockService(db).BulkAsync("archive", ["b1", "b2"], AdminId, CancellationToken.None);

        Assert.Equal(2, GetInt(result, "succeeded"));
        Assert.Equal(0, GetInt(result, "failed"));
        var statuses = await db.MockBundles.Select(b => b.Status).ToListAsync();
        Assert.All(statuses, s => Assert.Equal(ContentStatus.Archived, s));
        Assert.Equal(1, await AuditCountAsync(db));
    }

    [Fact]
    public async Task Bulk_Delete_SoftArchivesBundles()
    {
        await using var db = NewDb();
        SeedBundle(db, "b1", ContentStatus.Draft, publishable: false);
        await db.SaveChangesAsync();

        var result = await new MockService(db).BulkAsync("delete", ["b1"], AdminId, CancellationToken.None);

        Assert.Equal(1, GetInt(result, "succeeded"));
        var bundle = await db.MockBundles.SingleAsync(b => b.Id == "b1");
        Assert.Equal(ContentStatus.Archived, bundle.Status);
        Assert.NotNull(bundle.ArchivedAt);
        Assert.Equal(1, await AuditCountAsync(db));
    }

    // ── skipped (already in target state) ───────────────────────────────

    [Fact]
    public async Task Bulk_Publish_AlreadyPublished_CountsAsSkipped()
    {
        await using var db = NewDb();
        SeedBundle(db, "b1", ContentStatus.Published, publishable: true);
        await db.SaveChangesAsync();

        var result = await new MockService(db).BulkAsync("publish", ["b1"], AdminId, CancellationToken.None);

        Assert.Equal(0, GetInt(result, "succeeded"));
        Assert.Equal(1, GetInt(result, "skipped"));
        Assert.Equal(0, GetInt(result, "failed"));
    }

    // ── status-gate / not-found failures recorded, batch continues ──────

    [Fact]
    public async Task Bulk_Publish_GateFailure_RecordedAndBatchContinues()
    {
        await using var db = NewDb();
        SeedBundle(db, "ok", ContentStatus.Draft, publishable: true);
        SeedBundle(db, "bad", ContentStatus.Draft, publishable: false); // fails publish gate
        await db.SaveChangesAsync();

        var result = await new MockService(db).BulkAsync("publish", ["ok", "bad"], AdminId, CancellationToken.None);

        Assert.Equal(1, GetInt(result, "succeeded"));
        Assert.Equal(1, GetInt(result, "failed"));
        var errors = GetErrors(result);
        Assert.Single(errors);
        Assert.Contains("bad", errors[0]);

        // The good one was still published, the bad one stayed draft.
        Assert.Equal(ContentStatus.Published, (await db.MockBundles.SingleAsync(b => b.Id == "ok")).Status);
        Assert.Equal(ContentStatus.Draft, (await db.MockBundles.SingleAsync(b => b.Id == "bad")).Status);

        // Exactly one audit row for the whole op.
        Assert.Equal(1, await AuditCountAsync(db));
    }

    [Fact]
    public async Task Bulk_Publish_MissingId_RecordedAsFailure()
    {
        await using var db = NewDb();
        SeedBundle(db, "ok", ContentStatus.Draft, publishable: true);
        await db.SaveChangesAsync();

        var result = await new MockService(db).BulkAsync("publish", ["ok", "does-not-exist"], AdminId, CancellationToken.None);

        Assert.Equal(1, GetInt(result, "succeeded"));
        Assert.Equal(1, GetInt(result, "failed"));
    }

    // ── single audit row even across many items ─────────────────────────

    [Fact]
    public async Task Bulk_EmitsExactlyOneAuditRow()
    {
        await using var db = NewDb();
        SeedBundle(db, "b1", ContentStatus.Published, publishable: true);
        SeedBundle(db, "b2", ContentStatus.Draft, publishable: false);
        SeedBundle(db, "b3", ContentStatus.Draft, publishable: false);
        await db.SaveChangesAsync();

        await new MockService(db).BulkAsync("archive", ["b1", "b2", "b3"], AdminId, CancellationToken.None);

        Assert.Equal(1, await AuditCountAsync(db));
        var audit = await db.AuditEvents.SingleAsync();
        Assert.Equal("MockBundle", audit.ResourceType);
        Assert.Equal("bulk", audit.ResourceId);
        Assert.Equal(AdminId, audit.ActorId);
    }

    // ── permission gate (publish requires content:publish) ──────────────

    [Fact]
    public async Task Bulk_Publish_WithoutPublishGrant_Throws403()
    {
        await using var db = NewDb();
        SeedBundle(db, "b1", ContentStatus.Draft, publishable: true);
        // Explicit content:write grant only (no content:publish). A non-empty
        // grant set disables the treat-as-system-admin fallback.
        await GrantPermissionAsync(db, "admin-writer", AdminPermissions.ContentWrite);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            new MockService(db).BulkAsync("publish", ["b1"], "admin-writer", CancellationToken.None));

        Assert.Equal(403, ex.StatusCode);
        // Still draft — the gate fired before any mutation, and no audit row.
        Assert.Equal(ContentStatus.Draft, (await db.MockBundles.SingleAsync(b => b.Id == "b1")).Status);
        Assert.Equal(0, await AuditCountAsync(db));
    }

    [Fact]
    public async Task Bulk_Archive_WithWriteGrantOnly_IsAllowed()
    {
        await using var db = NewDb();
        SeedBundle(db, "b1", ContentStatus.Draft, publishable: false);
        await GrantPermissionAsync(db, "admin-writer", AdminPermissions.ContentWrite);

        var result = await new MockService(db).BulkAsync("archive", ["b1"], "admin-writer", CancellationToken.None);

        Assert.Equal(1, GetInt(result, "succeeded"));
        Assert.Equal(ContentStatus.Archived, (await db.MockBundles.SingleAsync(b => b.Id == "b1")).Status);
    }

    // ── validation: unknown action / empty ──────────────────────────────

    [Fact]
    public async Task Bulk_UnknownAction_Throws()
    {
        await using var db = NewDb();
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            new MockService(db).BulkAsync("frobnicate", ["b1"], AdminId, CancellationToken.None));
        Assert.Equal("mock_bundle_bulk_action_invalid", ex.ErrorCode);
    }

    [Fact]
    public async Task Bulk_EmptyIds_Throws()
    {
        await using var db = NewDb();
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            new MockService(db).BulkAsync("publish", [], AdminId, CancellationToken.None));
        Assert.Equal("mock_bundle_bulk_empty", ex.ErrorCode);
    }

    // ── force-delete (true purge of bundle + all learner data) ──────────

    [Fact]
    public async Task Bulk_ForceDelete_PurgesArchivedBundleAndAllLearnerData()
    {
        await using var db = NewDb();
        SeedBundle(db, "b1", ContentStatus.Archived, publishable: true); // archived + section-b1 + paper-b1
        db.MockAttempts.Add(new MockAttempt { Id = "att-1", UserId = "u1", MockBundleId = "b1" });
        db.MockSectionAttempts.Add(new MockSectionAttempt { Id = "sa-1", MockAttemptId = "att-1", MockBundleSectionId = "section-b1", SubtestCode = "reading", ContentPaperId = "paper-b1", LaunchRoute = "/r" });
        db.MockReviewReservations.Add(new MockReviewReservation { Id = "rr-1", UserId = "u1", MockAttemptId = "att-1", WalletId = "w1" });
        db.MockProctoringEvents.Add(new MockProctoringEvent { Id = "pe-1", MockAttemptId = "att-1", Kind = "focus_lost" });
        db.MockBookings.Add(new MockBooking { Id = "bk-1", UserId = "u1", MockBundleId = "b1", MockAttemptId = "att-1" });
        await db.SaveChangesAsync();

        var result = await new MockService(db).BulkAsync("force-delete", ["b1"], AdminId, CancellationToken.None);

        Assert.Equal(1, GetInt(result, "succeeded"));
        Assert.Equal(0, GetInt(result, "failed"));
        Assert.False(await db.MockBundles.AnyAsync(b => b.Id == "b1"));
        Assert.False(await db.MockBundleSections.AnyAsync(s => s.MockBundleId == "b1"));
        Assert.False(await db.MockAttempts.AnyAsync(a => a.Id == "att-1"));
        Assert.False(await db.MockSectionAttempts.AnyAsync(a => a.Id == "sa-1"));
        Assert.False(await db.MockReviewReservations.AnyAsync(r => r.Id == "rr-1"));
        Assert.False(await db.MockProctoringEvents.AnyAsync(e => e.Id == "pe-1"));
        Assert.False(await db.MockBookings.AnyAsync(b => b.Id == "bk-1"));
        Assert.Equal(1, await AuditCountAsync(db));
    }

    [Fact]
    public async Task Bulk_ForceDelete_NotArchived_RecordedAsFailure()
    {
        await using var db = NewDb();
        SeedBundle(db, "b1", ContentStatus.Draft, publishable: false);
        await db.SaveChangesAsync();

        var result = await new MockService(db).BulkAsync("force-delete", ["b1"], AdminId, CancellationToken.None);

        Assert.Equal(0, GetInt(result, "succeeded"));
        Assert.Equal(1, GetInt(result, "failed"));
        Assert.True(await db.MockBundles.AnyAsync(b => b.Id == "b1"));
        Assert.Contains("archived", GetErrors(result)[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bulk_ForceDelete_WithoutSystemAdmin_Throws403()
    {
        await using var db = NewDb();
        SeedBundle(db, "b1", ContentStatus.Archived, publishable: false);
        // Explicit content:write grant only disables the treat-as-system-admin fallback.
        await GrantPermissionAsync(db, "admin-writer", AdminPermissions.ContentWrite);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            new MockService(db).BulkAsync("force-delete", ["b1"], "admin-writer", CancellationToken.None));

        Assert.Equal(403, ex.StatusCode);
        Assert.True(await db.MockBundles.AnyAsync(b => b.Id == "b1"));
    }

    // ── result-shape helpers (anonymous wire-compatible object) ─────────

    private static int GetInt(object result, string field) =>
        Convert.ToInt32(result.GetType().GetProperty(field)!.GetValue(result));

    private static string[] GetErrors(object result) =>
        (string[])result.GetType().GetProperty("errors")!.GetValue(result)!;
}
