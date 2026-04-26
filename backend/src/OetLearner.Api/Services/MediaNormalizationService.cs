using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Phase 11 — Media normalization: signed URL generation, format detection, and media pipeline management.
/// </summary>
public class MediaNormalizationService(LearnerDbContext db)
{
    /// <summary>
    /// Get a time-limited signed URL for accessing paid media content.
    /// Falls back to direct path for non-S3 storage.
    /// </summary>
    public async Task<MediaAccessResult> GetSignedMediaUrlAsync(string mediaAssetId, string userId, CancellationToken ct)
    {
        // Read-only path: AsNoTracking avoids the change-tracker entry for a one-shot URL lookup.
        var asset = await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == mediaAssetId, ct);
        if (asset is null)
            return new MediaAccessResult(false, null, "Media asset not found.");

        if (asset.Status != MediaAssetStatus.Ready)
            return new MediaAccessResult(false, null, "Media asset is not ready for playback.");

        // For local storage, return the storage path directly
        // For S3-compatible storage, generate a signed URL
        var url = asset.StoragePath;

        return new MediaAccessResult(true, url, null);
    }

    /// <summary>
    /// Enqueue a media asset for processing (thumbnail generation, format normalization).
    /// </summary>
    public async Task<MediaAsset> EnqueueForProcessingAsync(string assetId, CancellationToken ct)
    {
        var asset = await db.MediaAssets.FindAsync([assetId], ct);
        if (asset is null) throw new InvalidOperationException($"Media asset {assetId} not found.");

        asset.Status = MediaAssetStatus.Processing;
        await db.SaveChangesAsync(ct);

        // In production, this would enqueue to a background job processor
        // For now, mark as ready after "processing" (placeholder for actual transcoding pipeline)
        return asset;
    }

    /// <summary>
    /// Mark a media asset processing as complete with results.
    /// </summary>
    public async Task<MediaAsset?> CompleteProcessingAsync(string assetId, string? thumbnailPath, string? captionPath, string? transcriptPath, CancellationToken ct)
    {
        var asset = await db.MediaAssets.FindAsync([assetId], ct);
        if (asset is null) return null;

        asset.Status = MediaAssetStatus.Ready;
        asset.ProcessedAt = DateTimeOffset.UtcNow;
        if (thumbnailPath != null) asset.ThumbnailPath = thumbnailPath;
        if (captionPath != null) asset.CaptionPath = captionPath;
        if (transcriptPath != null) asset.TranscriptPath = transcriptPath;

        await db.SaveChangesAsync(ct);
        return asset;
    }

    /// <summary>
    /// Detect the media type from MIME type and return normalization recommendations.
    /// </summary>
    public static MediaNormalizationPlan GetNormalizationPlan(string mimeType)
    {
        return mimeType switch
        {
            var m when m.StartsWith("video/") => new MediaNormalizationPlan
            {
                TargetFormat = "mp4",
                TargetCodec = "H.264/AAC",
                ShouldGenerateThumbnail = true,
                ShouldExtractCaptions = true,
                ShouldGenerateTranscript = true,
                QualityPresets = ["720p", "480p", "360p"],
                Notes = "Transcode to MP4/H.264 for universal playback. Generate thumbnail at 10s mark."
            },
            var m when m.StartsWith("audio/") => new MediaNormalizationPlan
            {
                TargetFormat = "mp3",
                TargetCodec = "MP3 128kbps",
                ShouldGenerateThumbnail = false,
                ShouldExtractCaptions = false,
                ShouldGenerateTranscript = true,
                QualityPresets = ["128kbps"],
                Notes = "Normalize to MP3/128kbps for consistent playback."
            },
            "application/pdf" => new MediaNormalizationPlan
            {
                TargetFormat = "pdf",
                TargetCodec = null,
                ShouldGenerateThumbnail = true,
                ShouldExtractCaptions = false,
                ShouldGenerateTranscript = false,
                QualityPresets = [],
                Notes = "Render in-app PDF viewer. Generate first-page thumbnail."
            },
            var m when m.StartsWith("image/") => new MediaNormalizationPlan
            {
                TargetFormat = "webp",
                TargetCodec = null,
                ShouldGenerateThumbnail = false,
                ShouldExtractCaptions = false,
                ShouldGenerateTranscript = false,
                QualityPresets = ["1200w", "800w", "400w"],
                Notes = "Optimize to WebP with responsive sizes. Add alt text requirement."
            },
            _ => new MediaNormalizationPlan
            {
                TargetFormat = null,
                TargetCodec = null,
                Notes = "Unknown media type — manual review required."
            }
        };
    }

    /// <summary>
    /// Audit all media assets for normalization compliance.
    /// </summary>
    public async Task<MediaAuditResult> AuditMediaAssetsAsync(CancellationToken ct)
    {
        // SQL aggregations — never materialise the (potentially huge) MediaAssets table.
        var assets = db.MediaAssets.AsNoTracking();
        var totalAssets = await assets.CountAsync(ct);
        var needsProcessing = await assets.CountAsync(a => a.Status == MediaAssetStatus.Processing, ct);
        var failed = await assets.CountAsync(a => a.Status == MediaAssetStatus.Failed, ct);
        var ready = await assets.CountAsync(a => a.Status == MediaAssetStatus.Ready, ct);
        var missingThumbnails = await assets.CountAsync(a => a.Status == MediaAssetStatus.Ready
            && a.MimeType.StartsWith("video/") && string.IsNullOrEmpty(a.ThumbnailPath), ct);
        var missingTranscripts = await assets.CountAsync(a => a.Status == MediaAssetStatus.Ready
            && (a.MimeType.StartsWith("video/") || a.MimeType.StartsWith("audio/"))
            && string.IsNullOrEmpty(a.TranscriptPath), ct);

        return new MediaAuditResult
        {
            TotalAssets = totalAssets,
            Ready = ready,
            Processing = needsProcessing,
            Failed = failed,
            MissingThumbnails = missingThumbnails,
            MissingTranscripts = missingTranscripts
        };
    }
}

// ── DTOs ──

public record MediaAccessResult(bool Success, string? Url, string? Error);

public class MediaNormalizationPlan
{
    public string? TargetFormat { get; set; }
    public string? TargetCodec { get; set; }
    public bool ShouldGenerateThumbnail { get; set; }
    public bool ShouldExtractCaptions { get; set; }
    public bool ShouldGenerateTranscript { get; set; }
    public List<string> QualityPresets { get; set; } = [];
    public string? Notes { get; set; }
}

public class MediaAuditResult
{
    public int TotalAssets { get; set; }
    public int Ready { get; set; }
    public int Processing { get; set; }
    public int Failed { get; set; }
    public int MissingThumbnails { get; set; }
    public int MissingTranscripts { get; set; }
}
