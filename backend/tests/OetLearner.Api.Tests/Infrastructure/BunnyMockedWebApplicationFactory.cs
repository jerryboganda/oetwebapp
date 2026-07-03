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

    // ── Collections (live Bunny management) ──────────────────────────────────
    public ConcurrentQueue<string> CreatedCollectionNames { get; } = new();
    public ConcurrentQueue<string> DeletedCollectionIds { get; } = new();
    public ConcurrentQueue<(string VideoId, string? CollectionId)> Moves { get; } = new();

    /// <summary>Seedable in-memory Bunny collections.</summary>
    public List<BunnyCollectionInfo> Collections { get; } = new();

    /// <summary>Seedable in-memory videos keyed by collection guid.</summary>
    public Dictionary<string, List<BunnyVideoListItem>> CollectionVideos { get; } = new();

    /// <summary>When true, every collection method throws as if Bunny is dormant.</summary>
    public bool ThrowNotConfigured { get; set; }

    private void GuardConfigured()
    {
        if (ThrowNotConfigured) throw new BunnyNotConfiguredException();
    }

    /// <summary>Metadata returned by GetVideoAsync. Mutable per test.</summary>
    public BunnyVideoInfo NextVideoInfo { get; set; } = new(
        VideoId: "unset",
        Status: 3,
        EncodeProgress: 100,
        LengthSeconds: 720,
        StorageSizeBytes: 0,
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

    // ── Collections ──────────────────────────────────────────────────────────

    public Task<BunnyCollectionListPage> ListCollectionsAsync(
        int page, int itemsPerPage, string? search, string? orderBy, CancellationToken ct)
    {
        GuardConfigured();
        var filtered = Collections
            .Where(c => string.IsNullOrWhiteSpace(search) || c.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var pageItems = filtered.Skip((page - 1) * itemsPerPage).Take(itemsPerPage).ToList();
        return Task.FromResult(new BunnyCollectionListPage(filtered.Count, page, itemsPerPage, pageItems));
    }

    public Task<BunnyCollectionInfo> GetCollectionAsync(string collectionId, CancellationToken ct)
        => Task.FromResult(Collections.FirstOrDefault(c => c.Guid == collectionId)
            ?? new BunnyCollectionInfo(collectionId, collectionId, 0, 0, Array.Empty<string>()));

    public Task<BunnyCollectionInfo> CreateCollectionAsync(string name, CancellationToken ct)
    {
        GuardConfigured();
        CreatedCollectionNames.Enqueue(name);
        var created = new BunnyCollectionInfo($"col-{Guid.NewGuid():N}", name, 0, 0, Array.Empty<string>());
        Collections.Add(created);
        return Task.FromResult(created);
    }

    public Task<BunnyCollectionInfo> UpdateCollectionAsync(string collectionId, string name, CancellationToken ct)
    {
        GuardConfigured();
        var idx = Collections.FindIndex(c => c.Guid == collectionId);
        var updated = (idx >= 0 ? Collections[idx] : new BunnyCollectionInfo(collectionId, name, 0, 0, Array.Empty<string>()))
            with { Name = name };
        if (idx >= 0) Collections[idx] = updated; else Collections.Add(updated);
        return Task.FromResult(updated);
    }

    public Task DeleteCollectionAsync(string collectionId, CancellationToken ct)
    {
        GuardConfigured();
        DeletedCollectionIds.Enqueue(collectionId);
        Collections.RemoveAll(c => c.Guid == collectionId);
        CollectionVideos.Remove(collectionId);
        return Task.CompletedTask;
    }

    public Task<BunnyVideoListPage> ListCollectionVideosAsync(
        string collectionId, int page, int itemsPerPage, string? search, string? orderBy, CancellationToken ct)
    {
        GuardConfigured();
        var all = CollectionVideos.TryGetValue(collectionId, out var list) ? list : new List<BunnyVideoListItem>();
        var filtered = all
            .Where(v => string.IsNullOrWhiteSpace(search) || v.Title.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var pageItems = filtered.Skip((page - 1) * itemsPerPage).Take(itemsPerPage).ToList();
        return Task.FromResult(new BunnyVideoListPage(filtered.Count, page, itemsPerPage, pageItems));
    }

    public Task MoveVideoToCollectionAsync(string bunnyVideoId, string? collectionId, CancellationToken ct)
    {
        GuardConfigured();
        Moves.Enqueue((bunnyVideoId, collectionId));
        var sourceKey = CollectionVideos.Keys.FirstOrDefault(k => CollectionVideos[k].Any(v => v.VideoId == bunnyVideoId));
        if (sourceKey is not null)
        {
            var list = CollectionVideos[sourceKey];
            var idx = list.FindIndex(v => v.VideoId == bunnyVideoId);
            var moved = list[idx] with { CollectionId = collectionId };
            list.RemoveAt(idx);
            if (!string.IsNullOrWhiteSpace(collectionId))
            {
                if (!CollectionVideos.TryGetValue(collectionId, out var dest))
                {
                    dest = new List<BunnyVideoListItem>();
                    CollectionVideos[collectionId] = dest;
                }
                dest.Add(moved);
            }
        }
        return Task.CompletedTask;
    }
}
