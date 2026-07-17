using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Settings;
using OetWithDrHesham.Api.Services.VideoLibrary;
using OetWithDrHesham.Api.Tests.Infrastructure;

namespace OetWithDrHesham.Api.Tests;

/// <summary>
/// Admin "Collections" console flows: live Bunny collection browse/CRUD, the
/// annotation join (imported vs not), the import bridge, video move, and the
/// system-admin-gated direct Bunny delete. Bunny is mocked via
/// <see cref="FakeBunnyStreamClient"/>; its seedable stores stand in for the
/// live library.
/// </summary>
public class VideoLibraryCollectionFlowsTests(BunnyMockedWebApplicationFactory factory)
    : IClassFixture<BunnyMockedWebApplicationFactory>
{
    private const string LibraryId = "123456";
    private const string WebhookSecret = "hook-secret-1234567890";

    [Fact]
    public async Task ListCollections_ReturnsSeededBunnyCollectionsWithCounts()
    {
        await ConfigureBunnyAsync();
        ResetBunny();
        factory.Bunny.Collections.Add(new BunnyCollectionInfo("col-1", "Speaking drills", 4, 4096, Array.Empty<string>()));
        factory.Bunny.Collections.Add(new BunnyCollectionInfo("col-2", "Listening", 2, 2048, Array.Empty<string>()));
        using var admin = CreateAdminClient();

        var response = await admin.GetAsync("/v1/admin/video-library/collections");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await ReadJsonAsync(response);

        Assert.Equal(2, payload.GetProperty("totalItems").GetInt32());
        var items = payload.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        var first = items.Single(i => i.GetProperty("collectionId").GetString() == "col-1");
        Assert.Equal("Speaking drills", first.GetProperty("name").GetString());
        Assert.Equal(4, first.GetProperty("videoCount").GetInt32());
        Assert.Equal(4096, first.GetProperty("totalSizeBytes").GetInt64());
    }

    [Fact]
    public async Task CreateCollection_RecordsName()
    {
        await ConfigureBunnyAsync();
        ResetBunny();
        using var admin = CreateAdminClient();

        var response = await admin.PostAsJsonAsync(
            "/v1/admin/video-library/collections", new { name = "New shelf" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("New shelf", (await ReadJsonAsync(response)).GetProperty("name").GetString());
        Assert.Contains("New shelf", factory.Bunny.CreatedCollectionNames);
    }

    [Fact]
    public async Task RenameCollection_UpdatesName()
    {
        await ConfigureBunnyAsync();
        ResetBunny();
        factory.Bunny.Collections.Add(new BunnyCollectionInfo("col-rename", "Old name", 0, 0, Array.Empty<string>()));
        using var admin = CreateAdminClient();

        var response = await admin.PostAsJsonAsync(
            "/v1/admin/video-library/collections/col-rename", new { name = "New name" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("New name", (await ReadJsonAsync(response)).GetProperty("name").GetString());
        Assert.Equal("New name", factory.Bunny.Collections.Single(c => c.Guid == "col-rename").Name);
    }

    [Fact]
    public async Task DeleteCollection_RequiresSystemAdmin()
    {
        await ConfigureBunnyAsync();
        ResetBunny();
        factory.Bunny.Collections.Add(new BunnyCollectionInfo("col-del", "Doomed", 0, 0, Array.Empty<string>()));

        // Content-write only → 403 (delete is system-admin gated).
        using var contentAdmin = CreateAdminClient(AdminPermissions.ContentRead, AdminPermissions.ContentWrite);
        var forbidden = await contentAdmin.DeleteAsync("/v1/admin/video-library/collections/col-del");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // Full admin (incl. system_admin) → 200 + recorded.
        using var admin = CreateAdminClient();
        var ok = await admin.DeleteAsync("/v1/admin/video-library/collections/col-del");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("col-del", factory.Bunny.DeletedCollectionIds);
    }

    [Fact]
    public async Task ListCollectionVideos_AnnotatesImportedLinkage()
    {
        await ConfigureBunnyAsync();
        ResetBunny();
        const string importedGuid = "bunny-imported-x";
        const string freshGuid = "bunny-fresh-x";
        factory.Bunny.CollectionVideos["col-annot"] =
        [
            MakeItem(importedGuid, "Already in catalog"),
            MakeItem(freshGuid, "Not yet imported"),
        ];
        var localId = await SeedLibraryVideoAsync(importedGuid, ContentStatus.Draft);

        using var admin = CreateAdminClient();
        var response = await admin.GetAsync("/v1/admin/video-library/collections/col-annot/videos");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray().ToList();

        var imported = items.Single(i => i.GetProperty("bunnyVideoId").GetString() == importedGuid);
        Assert.True(imported.GetProperty("isImported").GetBoolean());
        Assert.Equal(localId, imported.GetProperty("localVideoId").GetString());
        Assert.Equal("Draft", imported.GetProperty("localStatus").GetString());

        var fresh = items.Single(i => i.GetProperty("bunnyVideoId").GetString() == freshGuid);
        Assert.False(fresh.GetProperty("isImported").GetBoolean());
        Assert.Equal(JsonValueKind.Null, fresh.GetProperty("localVideoId").ValueKind);
    }

    [Fact]
    public async Task ImportFromBunny_CreatesLinkedDraft_ThenRejectsReimport()
    {
        await ConfigureBunnyAsync();
        ResetBunny();
        const string guid = "bunny-import-me";
        factory.Bunny.NextVideoInfo = factory.Bunny.NextVideoInfo with
        {
            LengthSeconds = 432,
            StorageSizeBytes = 123_456,
            Status = 4,
        };
        using var admin = CreateAdminClient();

        var response = await admin.PostAsJsonAsync(
            $"/v1/admin/video-library/collections/videos/{guid}/import",
            new { title = "Imported clip" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await ReadJsonAsync(response);
        var videoId = detail.GetProperty("videoId").GetString()!;
        Assert.Equal(guid, detail.GetProperty("bunnyVideoId").GetString());
        Assert.Equal(432, detail.GetProperty("durationSeconds").GetInt32());
        Assert.Equal("Draft", detail.GetProperty("status").GetString());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var row = await db.LibraryVideos.SingleAsync(v => v.Id == videoId);
            Assert.Equal(guid, row.BunnyVideoId);
            Assert.Equal(LibraryId, row.BunnyLibraryId);
            Assert.Equal("Imported clip", row.Title);
            Assert.Equal(ContentStatus.Draft, row.Status);
        }

        // Re-import the same Bunny guid → 409.
        var reimport = await admin.PostAsJsonAsync(
            $"/v1/admin/video-library/collections/videos/{guid}/import", new { });
        Assert.Equal(HttpStatusCode.Conflict, reimport.StatusCode);
        Assert.Equal("already_imported", (await ReadJsonAsync(reimport)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task MoveVideo_RecordsMove_AndNullClearsMembership()
    {
        await ConfigureBunnyAsync();
        ResetBunny();
        using var admin = CreateAdminClient();

        var moved = await admin.PostAsJsonAsync(
            "/v1/admin/video-library/collections/videos/bunny-move-1/move",
            new { collectionId = "col-target" });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        var cleared = await admin.PostAsJsonAsync(
            "/v1/admin/video-library/collections/videos/bunny-move-2/move",
            new { collectionId = (string?)null });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);

        var recorded = factory.Bunny.Moves.ToArray();
        Assert.Contains(recorded, m => m.VideoId == "bunny-move-1" && m.CollectionId == "col-target");
        Assert.Contains(recorded, m => m.VideoId == "bunny-move-2" && m.CollectionId is null);
    }

    [Fact]
    public async Task BunnyDelete_RefusesImported_ButDeletesUnimported_WithAudit()
    {
        await ConfigureBunnyAsync();
        ResetBunny();
        using var admin = CreateAdminClient();

        // force=false → 400.
        var needsForce = await admin.PostAsJsonAsync(
            "/v1/admin/video-library/collections/videos/bunny-nf/bunny-delete", new { force = false });
        Assert.Equal(HttpStatusCode.BadRequest, needsForce.StatusCode);

        // Imported → 409 (must use catalog force-delete).
        const string importedGuid = "bunny-imported-del";
        await SeedLibraryVideoAsync(importedGuid, ContentStatus.Published);
        var refused = await admin.PostAsJsonAsync(
            $"/v1/admin/video-library/collections/videos/{importedGuid}/bunny-delete", new { force = true });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("imported_use_force_delete", (await ReadJsonAsync(refused)).GetProperty("code").GetString());

        // Not imported → 200 + Bunny delete recorded + audit row.
        const string freshGuid = "bunny-only-del";
        var deleted = await admin.PostAsJsonAsync(
            $"/v1/admin/video-library/collections/videos/{freshGuid}/bunny-delete", new { force = true });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Contains(freshGuid, factory.Bunny.DeletedVideoIds);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.True(await db.AuditEvents.AnyAsync(a =>
            a.Action == "BunnyCollectionVideoDeleted" && a.ResourceId == freshGuid));
    }

    [Fact]
    public async Task NotConfigured_Returns503()
    {
        await ConfigureBunnyAsync();
        ResetBunny();
        factory.Bunny.ThrowNotConfigured = true;
        try
        {
            using var admin = CreateAdminClient();
            var response = await admin.GetAsync("/v1/admin/video-library/collections");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("bunny_not_configured", (await ReadJsonAsync(response)).GetProperty("code").GetString());
        }
        finally
        {
            factory.Bunny.ThrowNotConfigured = false;
        }
    }

    // ── Fixture helpers ─────────────────────────────────────────────────────

    private static BunnyVideoListItem MakeItem(string guid, string title) => new(
        VideoId: guid,
        Title: title,
        CollectionId: null,
        Status: 4,
        EncodeProgress: 100,
        LengthSeconds: 300,
        StorageSizeBytes: 10_000,
        ThumbnailUrl: $"https://vz-test.b-cdn.net/{guid}/thumbnail.jpg",
        Width: 1920,
        Height: 1080,
        AvailableResolutions: ["720p", "1080p"]);

    private void ResetBunny()
    {
        factory.Bunny.ThrowNotConfigured = false;
        factory.Bunny.Collections.Clear();
        factory.Bunny.CollectionVideos.Clear();
        factory.Bunny.CreatedCollectionNames.Clear();
        factory.Bunny.DeletedCollectionIds.Clear();
        factory.Bunny.Moves.Clear();
        factory.Bunny.DeletedVideoIds.Clear();
    }

    private HttpClient CreateAdminClient(params string[] permissions)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", $"admin-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
        client.DefaultRequestHeaders.Add("X-Debug-Email", "collection-admin@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", "Collection Admin");
        var perms = permissions.Length == 0 ? AdminPermissions.All : permissions;
        client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", string.Join(",", perms));
        return client;
    }

    private async Task ConfigureBunnyAsync()
    {
        var provider = factory.Services.GetRequiredService<IRuntimeSettingsProvider>();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var row = await db.RuntimeSettings.FirstOrDefaultAsync(r => r.Id == "default");
        if (row is null)
        {
            row = new RuntimeSettingsRow { Id = "default", UpdatedAt = DateTimeOffset.UtcNow };
            db.RuntimeSettings.Add(row);
        }
        row.BunnyStreamEnabled = true;
        row.BunnyStreamLibraryId = LibraryId;
        row.BunnyStreamApiKeyEncrypted = provider.Protect("test-api-key");
        row.BunnyStreamCdnHostname = "vz-test.b-cdn.net";
        row.BunnyStreamTokenAuthKeyEncrypted = provider.Protect("token-auth-key");
        row.BunnyStreamWebhookSecretEncrypted = provider.Protect(WebhookSecret);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        provider.Invalidate();
    }

    private async Task<string> SeedLibraryVideoAsync(string bunnyVideoId, ContentStatus status)
    {
        var videoId = $"vid-{Guid.NewGuid():N}";
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        db.LibraryVideos.Add(new LibraryVideo
        {
            Id = videoId,
            Title = "Linked catalog video",
            AccessTier = "premium",
            Status = status,
            EncodeStatus = VideoEncodeStatus.Ready,
            BunnyVideoId = bunnyVideoId,
            BunnyLibraryId = LibraryId,
            DurationSeconds = 300,
            ProfessionIdsJson = "[]",
            ChaptersJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return videoId;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }
}
