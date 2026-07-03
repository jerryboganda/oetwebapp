using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Settings;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class VideoLibraryAdminFlowsTests(BunnyMockedWebApplicationFactory factory)
    : IClassFixture<BunnyMockedWebApplicationFactory>
{
    private const string WebhookSecret = "hook-secret-1234567890";
    private const string LibraryId = "123456";

    [Fact]
    public async Task FullFlow_Draft_Upload_WebhookReady_Gate_Publish_LearnerSeesIt()
    {
        await ConfigureBunnyAsync();
        using var admin = CreateAdminClient();

        // 1. Create draft.
        var createResponse = await admin.PostAsJsonAsync(
            "/v1/admin/video-library/videos", new { title = "Flow test video" });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var videoId = (await ReadJsonAsync(createResponse)).GetProperty("videoId").GetString()!;

        // 2. Presign the browser→Bunny TUS upload (mocked Bunny assigns a guid).
        var authResponse = await admin.PostAsync(
            $"/v1/admin/video-library/videos/{videoId}/upload-authorization", content: null);
        Assert.Equal(HttpStatusCode.OK, authResponse.StatusCode);
        var auth = await ReadJsonAsync(authResponse);
        var bunnyVideoId = auth.GetProperty("bunnyVideoId").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(auth.GetProperty("authorizationSignature").GetString()));
        Assert.Equal("https://video.bunnycdn.com/tusupload", auth.GetProperty("tusEndpoint").GetString());

        // 3. Bunny reports the encode finished via webhook → metadata pulled.
        factory.Bunny.NextVideoInfo = factory.Bunny.NextVideoInfo with { Status = 3, LengthSeconds = 654 };
        using var anonymous = factory.CreateClient();
        var webhookResponse = await anonymous.PostAsJsonAsync(
            $"/v1/webhooks/bunny-stream?secret={WebhookSecret}",
            new { VideoLibraryId = LibraryId, VideoGuid = bunnyVideoId, Status = 3 });
        Assert.Equal(HttpStatusCode.OK, webhookResponse.StatusCode);

        // 4. Publish gate passes.
        var gateResponse = await admin.GetAsync($"/v1/admin/video-library/videos/{videoId}/publish-gate");
        Assert.Equal(HttpStatusCode.OK, gateResponse.StatusCode);
        var gate = await ReadJsonAsync(gateResponse);
        Assert.True(gate.GetProperty("canPublish").GetBoolean(),
            $"Gate errors: {gate.GetProperty("errors").GetRawText()}");

        // 5. Publish.
        var publishResponse = await admin.PostAsJsonAsync(
            $"/v1/admin/video-library/videos/{videoId}/publish", new { });
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        Assert.True((await ReadJsonAsync(publishResponse)).GetProperty("published").GetBoolean());

        // 6. Learner sees it in the library home (no playback URL anywhere).
        using var learner = CreateLearnerClient($"learner-{Guid.NewGuid():N}");
        var homeResponse = await learner.GetAsync("/v1/video-library");
        Assert.Equal(HttpStatusCode.OK, homeResponse.StatusCode);
        var homePayload = await homeResponse.Content.ReadAsStringAsync();
        Assert.Contains(videoId, homePayload, StringComparison.Ordinal);
        Assert.DoesNotContain("playlist.m3u8", homePayload, StringComparison.Ordinal);

        var detailResponse = await learner.GetAsync($"/v1/video-library/videos/{videoId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.DoesNotContain("playlist.m3u8", await detailResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishAtInFuture_HidesVideoFromLearner()
    {
        await ConfigureBunnyAsync();
        var videoId = await SeedReadyVideoAsync(status: ContentStatus.Published,
            publishAt: DateTimeOffset.UtcNow.AddDays(2));

        using var learner = CreateLearnerClient($"learner-{Guid.NewGuid():N}");
        var homeResponse = await learner.GetAsync("/v1/video-library");
        Assert.Equal(HttpStatusCode.OK, homeResponse.StatusCode);
        Assert.DoesNotContain(videoId, await homeResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var detailResponse = await learner.GetAsync($"/v1/video-library/videos/{videoId}");
        Assert.Equal(HttpStatusCode.NotFound, detailResponse.StatusCode);
    }

    [Fact]
    public async Task LegacyVideoLessons_Return410WithSuccessorPointer()
    {
        using var learner = CreateLearnerClient($"learner-{Guid.NewGuid():N}");

        foreach (var url in new[]
        {
            "/v1/lessons/",
            "/v1/lessons/some-lesson-id",
            "/v1/lessons/programs/some-program-id",
        })
        {
            var response = await learner.GetAsync(url);
            Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
            var payload = await response.Content.ReadAsStringAsync();
            Assert.Contains("feature_retired", payload, StringComparison.Ordinal);
            Assert.Contains("/v1/video-library", payload, StringComparison.Ordinal);
        }

        var progress = await learner.PostAsJsonAsync("/v1/lessons/some-lesson-id/progress", new { watchedSeconds = 10 });
        Assert.Equal(HttpStatusCode.Gone, progress.StatusCode);
    }

    [Fact]
    public async Task ForceDelete_WorksFromAnyStatus_WithoutArchivingFirst()
    {
        await ConfigureBunnyAsync();
        // A permanent delete must not require archiving first — it works from any
        // status (Published here), removing the row outright.
        var videoId = await SeedReadyVideoAsync(status: ContentStatus.Published, publishAt: null);
        using var admin = CreateAdminClient();

        var response = await admin.PostAsJsonAsync(
            $"/v1/admin/video-library/videos/{videoId}/force-delete",
            new { force = true, reason = "cleanup" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.False(await db.LibraryVideos.AnyAsync(v => v.Id == videoId));
    }

    [Fact]
    public async Task ForceDelete_Archived_PurgesDependentsAndDeletesBunnyVideo()
    {
        await ConfigureBunnyAsync();
        var videoId = await SeedReadyVideoAsync(status: ContentStatus.Archived, publishAt: null);
        string bunnyVideoId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var video = await db.LibraryVideos.SingleAsync(v => v.Id == videoId);
            video.ArchivedAt = DateTimeOffset.UtcNow;
            bunnyVideoId = video.BunnyVideoId!;

            var now = DateTimeOffset.UtcNow;
            var userId = $"learner-{Guid.NewGuid():N}";
            db.LearnerVideoLibraryProgress.Add(new LearnerVideoLibraryProgress
            {
                Id = Guid.NewGuid(), UserId = userId, VideoId = videoId,
                PositionSeconds = 30, WatchedSeconds = 30, LastWatchedAt = now,
            });
            db.LearnerVideoBookmarks.Add(new LearnerVideoBookmark
            {
                Id = Guid.NewGuid(), UserId = userId, VideoId = videoId, CreatedAt = now,
            });
            db.VideoPlaybackSessions.Add(new VideoPlaybackSession
            {
                Id = Guid.NewGuid().ToString("N"), UserId = userId, VideoId = videoId,
                Platform = "tauri", AttestationKeyId = "v1", IssuedAt = now, ExpiresAt = now.AddHours(1),
            });
            db.VideoPlaybackEvents.Add(new VideoPlaybackEvent
            {
                Id = Guid.NewGuid(), UserId = userId, VideoId = videoId,
                EventType = "play", PositionSeconds = 0, OccurredAt = now, PayloadJson = "{}",
            });
            var category = new VideoCategory
            {
                Id = $"vcat-{Guid.NewGuid():N}", Title = "Purge shelf", Slug = $"purge-{Guid.NewGuid():N}",
                Status = ContentStatus.Published, CreatedAt = now, UpdatedAt = now,
            };
            db.VideoCategories.Add(category);
            db.VideoCategoryItems.Add(new VideoCategoryItem
            {
                Id = Guid.NewGuid(), CategoryId = category.Id, VideoId = videoId, SortOrder = 0,
            });
            await db.SaveChangesAsync();
        }

        using var admin = CreateAdminClient();
        var response = await admin.PostAsJsonAsync(
            $"/v1/admin/video-library/videos/{videoId}/force-delete",
            new { force = true, reason = "test purge" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(bunnyVideoId, factory.Bunny.DeletedVideoIds);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.False(await verifyDb.LibraryVideos.AnyAsync(v => v.Id == videoId));
        Assert.False(await verifyDb.LearnerVideoLibraryProgress.AnyAsync(p => p.VideoId == videoId));
        Assert.False(await verifyDb.LearnerVideoBookmarks.AnyAsync(b => b.VideoId == videoId));
        Assert.False(await verifyDb.VideoPlaybackSessions.AnyAsync(s => s.VideoId == videoId));
        Assert.False(await verifyDb.VideoPlaybackEvents.AnyAsync(e => e.VideoId == videoId));
        Assert.False(await verifyDb.VideoCategoryItems.AnyAsync(i => i.VideoId == videoId));
    }

    // ── Fixture helpers ─────────────────────────────────────────────────────

    private HttpClient CreateAdminClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", $"admin-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
        client.DefaultRequestHeaders.Add("X-Debug-Email", "video-admin@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", "Video Admin");
        client.DefaultRequestHeaders.Add(
            "X-Debug-AdminPermissions",
            string.Join(",", AdminPermissions.All));
        return client;
    }

    private HttpClient CreateLearnerClient(string learnerId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", learnerId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{learnerId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", learnerId);
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

    private async Task<string> SeedReadyVideoAsync(ContentStatus status, DateTimeOffset? publishAt)
    {
        var videoId = $"vid-{Guid.NewGuid():N}";
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        db.LibraryVideos.Add(new LibraryVideo
        {
            Id = videoId,
            Title = "Seeded ready video",
            AccessTier = "free",
            Status = status,
            PublishAt = publishAt,
            PublishedAt = now,
            EncodeStatus = VideoEncodeStatus.Ready,
            BunnyVideoId = $"bunny-{Guid.NewGuid():N}",
            BunnyLibraryId = LibraryId,
            DurationSeconds = 600,
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
