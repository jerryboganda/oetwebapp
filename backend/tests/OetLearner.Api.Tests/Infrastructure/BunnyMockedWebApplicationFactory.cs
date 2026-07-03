using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Services.VideoLibrary;

namespace OetLearner.Api.Tests.Infrastructure;

/// <summary>
/// Test host with <see cref="IBunnyStreamClient"/> replaced by an in-memory
/// fake so admin upload / webhook / force-delete flows never reach the real
/// Bunny API. The fake instance is exposed for call assertions.
/// </summary>
public sealed class BunnyMockedWebApplicationFactory : TestWebApplicationFactory
{
    public FakeBunnyStreamClient Bunny { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IBunnyStreamClient>(Bunny);
        });
    }
}

public sealed class FakeBunnyStreamClient : IBunnyStreamClient
{
    public ConcurrentQueue<string> CreatedTitles { get; } = new();
    public ConcurrentQueue<string> DeletedVideoIds { get; } = new();
    public ConcurrentQueue<(string VideoId, string LanguageCode)> UploadedCaptions { get; } = new();

    /// <summary>Metadata returned by GetVideoAsync. Mutable per test.</summary>
    public BunnyVideoInfo NextVideoInfo { get; set; } = new(
        VideoId: "unset",
        Status: 3,
        EncodeProgress: 100,
        LengthSeconds: 720,
        ThumbnailUrl: "https://vz-test.b-cdn.net/unset/thumbnail.jpg",
        Width: 1920,
        Height: 1080,
        AvailableResolutions: ["720p", "1080p"]);

    public Task<string> CreateVideoAsync(string title, string? collectionId, CancellationToken ct)
    {
        CreatedTitles.Enqueue(title);
        return Task.FromResult($"bunny-{Guid.NewGuid():N}");
    }

    public Task<BunnyTusAuthorization> CreateTusUploadAuthorizationAsync(
        string bunnyVideoId, long expiresUnix, CancellationToken ct)
        => Task.FromResult(new BunnyTusAuthorization(
            bunnyVideoId,
            "123456",
            BunnyStreamClient.TusEndpoint,
            BunnyStreamClient.ComputeTusSignature("123456", "test-api-key", expiresUnix, bunnyVideoId),
            expiresUnix));

    public Task<BunnyVideoInfo> GetVideoAsync(string bunnyVideoId, CancellationToken ct)
        => Task.FromResult(NextVideoInfo with
        {
            VideoId = bunnyVideoId,
            ThumbnailUrl = $"https://vz-test.b-cdn.net/{bunnyVideoId}/thumbnail.jpg",
        });

    public Task DeleteVideoAsync(string bunnyVideoId, CancellationToken ct)
    {
        DeletedVideoIds.Enqueue(bunnyVideoId);
        return Task.CompletedTask;
    }

    public Task UploadCaptionAsync(string bunnyVideoId, string languageCode, string label, byte[] vttBytes, CancellationToken ct)
    {
        UploadedCaptions.Enqueue((bunnyVideoId, languageCode));
        return Task.CompletedTask;
    }

    public Task SetThumbnailAsync(string bunnyVideoId, string thumbnailUrl, CancellationToken ct)
        => Task.CompletedTask;

    public Task<string> SignPlaybackUrlAsync(string bunnyVideoId, long expiresUnix, CancellationToken ct)
    {
        var tokenPath = $"/{bunnyVideoId}/";
        var token = BunnyStreamClient.ComputeCdnToken("token-auth-key", tokenPath, expiresUnix);
        return Task.FromResult(BunnyStreamClient.BuildSignedPlaybackUrl(
            "vz-test.b-cdn.net", bunnyVideoId, token, expiresUnix, tokenPath));
    }
}
