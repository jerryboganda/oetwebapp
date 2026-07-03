using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.VideoLibrary;

// ── Collection admin contracts ────────────────────────────────────────────────

/// <summary>A Bunny collection as surfaced to the admin console.</summary>
public sealed record AdminCollectionDto(
    string CollectionId,
    string Name,
    int VideoCount,
    long TotalSizeBytes);

/// <summary>One page of admin collections.</summary>
public sealed record AdminCollectionListDto(
    int TotalItems,
    int Page,
    int ItemsPerPage,
    IReadOnlyList<AdminCollectionDto> Items);

/// <summary>
/// A Bunny video inside a collection, annotated with local-catalog linkage so
/// the admin can tell at a glance whether it has been imported (and jump to it).
/// </summary>
public sealed record AdminCollectionVideoDto(
    string BunnyVideoId,
    string Title,
    string EncodeStatus,
    int EncodeProgress,
    int DurationSeconds,
    long StorageSizeBytes,
    string? ThumbnailUrl,
    int? Width,
    int? Height,
    bool IsImported,
    string? LocalVideoId,
    string? LocalStatus);

/// <summary>One page of annotated collection videos.</summary>
public sealed record AdminCollectionVideoPageDto(
    int TotalItems,
    int Page,
    int ItemsPerPage,
    IReadOnlyList<AdminCollectionVideoDto> Items);

/// <summary>
/// Admin management of the live Bunny Stream library: browse collections, browse
/// the videos inside them, and bridge Bunny-native videos into the app catalog.
/// Bunny is the source of truth for collection membership — nothing is mirrored
/// into the DB by this service except the <see cref="LibraryVideo"/> created on
/// import. Composes <see cref="VideoLibraryAdminService"/> for that import bridge.
/// This surface never mints or exposes a playback URL — playback stays attested.
/// </summary>
public sealed class BunnyCollectionAdminService(
    LearnerDbContext db,
    IBunnyStreamClient bunny,
    VideoLibraryAdminService videos,
    Settings.IRuntimeSettingsProvider settingsProvider,
    ILogger<BunnyCollectionAdminService> logger)
{
    public async Task<AdminCollectionListDto> ListAsync(int page, int itemsPerPage, string? search, CancellationToken ct)
    {
        var result = await bunny.ListCollectionsAsync(page, itemsPerPage, search, orderBy: null, ct);
        // Cheap by design: no per-collection round-trip. Imported-vs-total is
        // computed only when a collection is opened (ListVideosAsync).
        var items = result.Items
            .Select(c => new AdminCollectionDto(c.Guid, c.Name, c.VideoCount, c.TotalSize))
            .ToList();
        return new AdminCollectionListDto(result.TotalItems, result.CurrentPage, result.ItemsPerPage, items);
    }

    public async Task<AdminCollectionDto> CreateAsync(string name, CancellationToken ct)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw ApiException.Validation("collection_name_required", "A collection name is required.");
        }
        var created = await bunny.CreateCollectionAsync(trimmed, ct);
        return new AdminCollectionDto(created.Guid, created.Name, created.VideoCount, created.TotalSize);
    }

    public async Task<AdminCollectionDto> RenameAsync(string collectionId, string name, CancellationToken ct)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw ApiException.Validation("collection_name_required", "A collection name is required.");
        }
        var updated = await bunny.UpdateCollectionAsync(collectionId, trimmed, ct);
        return new AdminCollectionDto(updated.Guid, updated.Name, updated.VideoCount, updated.TotalSize);
    }

    public Task DeleteAsync(string collectionId, CancellationToken ct)
        => bunny.DeleteCollectionAsync(collectionId, ct);

    public async Task<AdminCollectionVideoPageDto> ListVideosAsync(
        string collectionId, int page, int itemsPerPage, string? search, CancellationToken ct)
    {
        var result = await bunny.ListCollectionVideosAsync(collectionId, page, itemsPerPage, search, orderBy: "date", ct);

        // Annotation join: which of these Bunny videos already exist in the catalog?
        var guids = result.Items.Select(i => i.VideoId).Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
        var local = guids.Count == 0
            ? new List<LocalLink>()
            : (await db.LibraryVideos.AsNoTracking()
                .Where(v => v.BunnyVideoId != null && guids.Contains(v.BunnyVideoId))
                .Select(v => new { v.Id, v.BunnyVideoId, v.Status })
                .ToListAsync(ct))
                .Select(v => new LocalLink(v.Id, v.BunnyVideoId!, v.Status))
                .ToList();
        var byBunnyId = local
            .GroupBy(l => l.BunnyVideoId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var items = result.Items.Select(i =>
        {
            byBunnyId.TryGetValue(i.VideoId, out var link);
            return new AdminCollectionVideoDto(
                BunnyVideoId: i.VideoId,
                Title: i.Title,
                EncodeStatus: VideoLibraryAdminService.EncodeStatusLabel(VideoLibraryAdminService.MapBunnyStatus(i.Status)),
                EncodeProgress: i.EncodeProgress,
                DurationSeconds: i.LengthSeconds,
                StorageSizeBytes: i.StorageSizeBytes,
                ThumbnailUrl: i.ThumbnailUrl,
                Width: i.Width,
                Height: i.Height,
                IsImported: link is not null,
                LocalVideoId: link?.Id,
                LocalStatus: link is null ? null : VideoLibraryAdminService.StatusLabel(link.Status));
        }).ToList();

        return new AdminCollectionVideoPageDto(result.TotalItems, result.CurrentPage, result.ItemsPerPage, items);
    }

    /// <summary>
    /// Bridge a live Bunny video into the learner catalog: create a linked
    /// <see cref="LibraryVideo"/> draft pointing at the existing Bunny guid and
    /// pull its encode metadata. Idempotency-guarded — re-import throws 409.
    /// </summary>
    public async Task<AdminVideoDetailDto> ImportFromBunnyAsync(
        string adminId, string bunnyVideoId, string? titleOverride, string? collectionId, CancellationToken ct)
    {
        if (await db.LibraryVideos.AnyAsync(v => v.BunnyVideoId == bunnyVideoId, ct))
        {
            throw ApiException.Conflict("already_imported", "This Bunny video is already in the catalog.");
        }

        // Fetch first — validates the video exists on Bunny and gives us metadata
        // in a single call (no second RefreshStatus round-trip).
        var info = await bunny.GetVideoAsync(bunnyVideoId, ct);
        var settings = (await settingsProvider.GetAsync(ct)).BunnyStream;

        var title = string.IsNullOrWhiteSpace(titleOverride)
            ? $"Imported {bunnyVideoId[..Math.Min(8, bunnyVideoId.Length)]}"
            : titleOverride.Trim();

        var draft = await videos.CreateDraftAsync(adminId, title, ct);
        draft.BunnyVideoId = bunnyVideoId;
        draft.BunnyLibraryId = settings.LibraryId;
        draft.BunnyCollectionId = string.IsNullOrWhiteSpace(collectionId) ? null : collectionId.Trim();
        VideoLibraryAdminService.ApplyBunnyInfo(draft, info);
        draft.UpdatedAt = DateTimeOffset.UtcNow;
        draft.UpdatedByAdminId = adminId;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Imported Bunny video {BunnyVideoId} into catalog as {VideoId} by admin {AdminId}.",
            bunnyVideoId, draft.Id, adminId);
        return await videos.BuildDetailAsync(draft, ct);
    }

    public Task MoveVideoAsync(string bunnyVideoId, string? targetCollectionId, CancellationToken ct)
        => bunny.MoveVideoToCollectionAsync(bunnyVideoId, targetCollectionId, ct);

    /// <summary>
    /// Permanently delete a video directly on Bunny. Refuses videos that are
    /// imported into the catalog — deleting those would orphan a
    /// <see cref="LibraryVideo"/> pointing at a dead guid; the caller must use
    /// the catalog force-delete instead (which cascades local dependents).
    /// </summary>
    public async Task DeleteBunnyVideoAsync(string bunnyVideoId, CancellationToken ct)
    {
        var localId = await db.LibraryVideos.AsNoTracking()
            .Where(v => v.BunnyVideoId == bunnyVideoId)
            .Select(v => v.Id)
            .FirstOrDefaultAsync(ct);
        if (localId is not null)
        {
            throw ApiException.Conflict("imported_use_force_delete",
                "This video is in the catalog. Delete it from the Video Library (force delete) instead of Bunny directly.");
        }
        await bunny.DeleteVideoAsync(bunnyVideoId, ct);
        logger.LogWarning("Permanently deleted Bunny video {BunnyVideoId} (not in catalog).", bunnyVideoId);
    }

    private sealed record LocalLink(string Id, string BunnyVideoId, ContentStatus Status);
}
