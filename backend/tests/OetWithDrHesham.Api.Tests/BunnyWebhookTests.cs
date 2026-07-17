using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Settings;
using OetWithDrHesham.Api.Tests.Infrastructure;

namespace OetWithDrHesham.Api.Tests;

public class BunnyWebhookTests(BunnyMockedWebApplicationFactory factory)
    : IClassFixture<BunnyMockedWebApplicationFactory>
{
    private const string WebhookSecret = "hook-secret-1234567890";
    private const string LibraryId = "123456";

    [Fact]
    public async Task Webhook_BadSecret_Returns401()
    {
        await ConfigureAsync(webhookSecretConfigured: true);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/v1/webhooks/bunny-stream?secret=wrong-secret",
            new { VideoLibraryId = LibraryId, VideoGuid = "any", Status = 3 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_AbsentSecret_Returns401()
    {
        await ConfigureAsync(webhookSecretConfigured: true);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/v1/webhooks/bunny-stream",
            new { VideoLibraryId = LibraryId, VideoGuid = "any", Status = 3 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_DormantConfiguration_Returns401()
    {
        await ConfigureAsync(webhookSecretConfigured: false);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/v1/webhooks/bunny-stream?secret={WebhookSecret}",
            new { VideoLibraryId = LibraryId, VideoGuid = "any", Status = 3 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_LibraryMismatch_Returns200AndIgnores()
    {
        await ConfigureAsync(webhookSecretConfigured: true);
        var (videoId, bunnyVideoId) = await SeedVideoAsync(VideoEncodeStatus.Uploading);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/v1/webhooks/bunny-stream?secret={WebhookSecret}",
            new { VideoLibraryId = "999999", VideoGuid = bunnyVideoId, Status = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("library_mismatch", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(VideoEncodeStatus.Uploading, await ReadEncodeStatusAsync(videoId));
    }

    [Theory]
    // Bunny status 0 (Created — object exists, bytes not yet received) maps to
    // Uploading, not Queued: MapBunnyStatus(0) keeps an interrupted/never-started
    // upload recoverable in the admin card. See the "incomplete upload recovery"
    // hardening in VideoLibraryAdminService.
    [InlineData(0, VideoEncodeStatus.Uploading)]
    [InlineData(1, VideoEncodeStatus.Processing)]
    [InlineData(2, VideoEncodeStatus.Encoding)]
    [InlineData(5, VideoEncodeStatus.Failed)]
    public async Task Webhook_StatusTransitions_PersistMappedEncodeStatus(int bunnyStatus, VideoEncodeStatus expected)
    {
        await ConfigureAsync(webhookSecretConfigured: true);
        var (videoId, bunnyVideoId) = await SeedVideoAsync(VideoEncodeStatus.Uploading);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/v1/webhooks/bunny-stream?secret={WebhookSecret}",
            new { VideoLibraryId = LibraryId, VideoGuid = bunnyVideoId, Status = bunnyStatus });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, await ReadEncodeStatusAsync(videoId));
    }

    [Fact]
    public async Task Webhook_Ready_PullsMetadataFromBunny()
    {
        await ConfigureAsync(webhookSecretConfigured: true);
        var (videoId, bunnyVideoId) = await SeedVideoAsync(VideoEncodeStatus.Encoding);
        factory.Bunny.NextVideoInfo = factory.Bunny.NextVideoInfo with
        {
            Status = 3,
            EncodeProgress = 100,
            LengthSeconds = 987,
            // Non-zero stored bytes: a real finished encode. Without this the
            // ApplyBunnyInfo guard (<=0 bytes => Uploading, for interrupted
            // uploads) would override Ready and the metadata pull would be moot.
            StorageSizeBytes = 5_000_000,
            Width = 1280,
            Height = 720,
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/v1/webhooks/bunny-stream?secret={WebhookSecret}",
            new { VideoLibraryId = LibraryId, VideoGuid = bunnyVideoId, Status = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var video = await db.LibraryVideos.AsNoTracking().SingleAsync(v => v.Id == videoId);
        Assert.Equal(VideoEncodeStatus.Ready, video.EncodeStatus);
        Assert.Equal(987, video.DurationSeconds);
        Assert.Equal(1280, video.Width);
        Assert.Equal(720, video.Height);
        Assert.NotNull(video.BunnyThumbnailUrl);
        Assert.Contains(bunnyVideoId, video.BunnyThumbnailUrl!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Webhook_UnknownVideo_Returns200()
    {
        await ConfigureAsync(webhookSecretConfigured: true);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/v1/webhooks/bunny-stream?secret={WebhookSecret}",
            new { VideoLibraryId = LibraryId, VideoGuid = $"missing-{Guid.NewGuid():N}", Status = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("unknown_video", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // ── Fixture helpers ─────────────────────────────────────────────────────

    private async Task ConfigureAsync(bool webhookSecretConfigured)
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
        row.BunnyStreamLibraryId = LibraryId;
        row.BunnyStreamWebhookSecretEncrypted = webhookSecretConfigured ? provider.Protect(WebhookSecret) : null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        provider.Invalidate();
    }

    private async Task<(string VideoId, string BunnyVideoId)> SeedVideoAsync(VideoEncodeStatus encodeStatus)
    {
        var videoId = $"vid-{Guid.NewGuid():N}";
        var bunnyVideoId = $"bunny-{Guid.NewGuid():N}";
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        db.LibraryVideos.Add(new LibraryVideo
        {
            Id = videoId,
            Title = "Webhook test video",
            BunnyVideoId = bunnyVideoId,
            BunnyLibraryId = LibraryId,
            EncodeStatus = encodeStatus,
            Status = ContentStatus.Draft,
            ProfessionIdsJson = "[]",
            ChaptersJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (videoId, bunnyVideoId);
    }

    private async Task<VideoEncodeStatus> ReadEncodeStatusAsync(string videoId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        return await db.LibraryVideos.AsNoTracking()
            .Where(v => v.Id == videoId)
            .Select(v => v.EncodeStatus)
            .SingleAsync();
    }
}
