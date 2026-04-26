using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using Xunit;

namespace OetLearner.Api.Tests;

public class MediaNormalizationServiceTests
{
    private static (LearnerDbContext db, MediaNormalizationService svc) Build()
    {
        var opts = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(opts);
        return (db, new MediaNormalizationService(db));
    }

    private static MediaAsset AddAsset(
        LearnerDbContext db, string id,
        MediaAssetStatus status = MediaAssetStatus.Ready,
        string mimeType = "video/mp4",
        string? thumbnail = null,
        string? transcript = null,
        string storagePath = "s3://bucket/file")
    {
        var asset = new MediaAsset
        {
            Id = id,
            OriginalFilename = $"{id}.mp4",
            MimeType = mimeType,
            Format = "mp4",
            SizeBytes = 1024,
            StoragePath = storagePath,
            ThumbnailPath = thumbnail,
            TranscriptPath = transcript,
            Status = status,
            UploadedAt = DateTimeOffset.UtcNow,
        };
        db.MediaAssets.Add(asset);
        db.SaveChanges();
        return asset;
    }

    // ── GetSignedMediaUrlAsync ──────────────────────────────────────

    [Fact]
    public async Task GetSignedMediaUrlAsync_returns_failure_when_asset_missing()
    {
        var (_, svc) = Build();
        var result = await svc.GetSignedMediaUrlAsync("does-not-exist", "u1", CancellationToken.None);
        Assert.False(result.Success);
        Assert.Null(result.Url);
        Assert.Equal("Media asset not found.", result.Error);
    }

    [Fact]
    public async Task GetSignedMediaUrlAsync_returns_failure_when_asset_not_ready()
    {
        var (db, svc) = Build();
        AddAsset(db, "a1", status: MediaAssetStatus.Processing);
        var result = await svc.GetSignedMediaUrlAsync("a1", "u1", CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("Media asset is not ready for playback.", result.Error);
    }

    [Fact]
    public async Task GetSignedMediaUrlAsync_returns_storage_path_when_ready()
    {
        var (db, svc) = Build();
        AddAsset(db, "a1", storagePath: "s3://bucket/path/to/file.mp4");
        var result = await svc.GetSignedMediaUrlAsync("a1", "u1", CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal("s3://bucket/path/to/file.mp4", result.Url);
        Assert.Null(result.Error);
    }

    // ── EnqueueForProcessingAsync ───────────────────────────────────

    [Fact]
    public async Task EnqueueForProcessingAsync_throws_when_asset_missing()
    {
        var (_, svc) = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.EnqueueForProcessingAsync("missing", CancellationToken.None));
    }

    [Fact]
    public async Task EnqueueForProcessingAsync_marks_asset_processing()
    {
        var (db, svc) = Build();
        AddAsset(db, "a1", status: MediaAssetStatus.Ready);
        var asset = await svc.EnqueueForProcessingAsync("a1", CancellationToken.None);
        Assert.Equal(MediaAssetStatus.Processing, asset.Status);
        var refetched = await db.MediaAssets.FindAsync("a1");
        Assert.Equal(MediaAssetStatus.Processing, refetched!.Status);
    }

    // ── CompleteProcessingAsync ─────────────────────────────────────

    [Fact]
    public async Task CompleteProcessingAsync_returns_null_when_asset_missing()
    {
        var (_, svc) = Build();
        var result = await svc.CompleteProcessingAsync(
            "missing", "thumb.jpg", null, null, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task CompleteProcessingAsync_marks_ready_and_sets_paths()
    {
        var (db, svc) = Build();
        AddAsset(db, "a1", status: MediaAssetStatus.Processing);
        var result = await svc.CompleteProcessingAsync(
            "a1", "thumb.jpg", "captions.vtt", "transcript.txt", CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(MediaAssetStatus.Ready, result!.Status);
        Assert.Equal("thumb.jpg", result.ThumbnailPath);
        Assert.Equal("captions.vtt", result.CaptionPath);
        Assert.Equal("transcript.txt", result.TranscriptPath);
        Assert.NotNull(result.ProcessedAt);
    }

    [Fact]
    public async Task CompleteProcessingAsync_keeps_existing_paths_when_args_null()
    {
        var (db, svc) = Build();
        AddAsset(db, "a1", status: MediaAssetStatus.Processing, thumbnail: "old-thumb.jpg");
        var result = await svc.CompleteProcessingAsync(
            "a1", thumbnailPath: null, captionPath: null, transcriptPath: null, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("old-thumb.jpg", result!.ThumbnailPath);
    }

    // ── GetNormalizationPlan (static) ───────────────────────────────

    [Fact]
    public void GetNormalizationPlan_returns_video_plan_for_video_mime()
    {
        var plan = MediaNormalizationService.GetNormalizationPlan("video/mp4");
        Assert.Equal("mp4", plan.TargetFormat);
        Assert.True(plan.ShouldGenerateThumbnail);
        Assert.True(plan.ShouldExtractCaptions);
        Assert.True(plan.ShouldGenerateTranscript);
        Assert.Contains("720p", plan.QualityPresets);
    }

    [Fact]
    public void GetNormalizationPlan_returns_audio_plan_for_audio_mime()
    {
        var plan = MediaNormalizationService.GetNormalizationPlan("audio/wav");
        Assert.Equal("mp3", plan.TargetFormat);
        Assert.False(plan.ShouldGenerateThumbnail);
        Assert.False(plan.ShouldExtractCaptions);
        Assert.True(plan.ShouldGenerateTranscript);
    }

    [Fact]
    public void GetNormalizationPlan_returns_pdf_plan_for_application_pdf()
    {
        var plan = MediaNormalizationService.GetNormalizationPlan("application/pdf");
        Assert.Equal("pdf", plan.TargetFormat);
        Assert.True(plan.ShouldGenerateThumbnail);
        Assert.False(plan.ShouldGenerateTranscript);
    }

    [Fact]
    public void GetNormalizationPlan_returns_image_plan_for_image_mime()
    {
        var plan = MediaNormalizationService.GetNormalizationPlan("image/png");
        Assert.Equal("webp", plan.TargetFormat);
        Assert.False(plan.ShouldGenerateThumbnail);
        Assert.NotEmpty(plan.QualityPresets);
    }

    [Fact]
    public void GetNormalizationPlan_returns_unknown_plan_for_unrecognized_mime()
    {
        var plan = MediaNormalizationService.GetNormalizationPlan("application/x-weird");
        Assert.Null(plan.TargetFormat);
        Assert.Contains("manual review", plan.Notes ?? "");
    }

    // ── AuditMediaAssetsAsync ───────────────────────────────────────

    [Fact]
    public async Task AuditMediaAssetsAsync_returns_zero_counts_for_empty_db()
    {
        var (_, svc) = Build();
        var result = await svc.AuditMediaAssetsAsync(CancellationToken.None);
        Assert.Equal(0, result.TotalAssets);
        Assert.Equal(0, result.Ready);
        Assert.Equal(0, result.Processing);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.MissingThumbnails);
        Assert.Equal(0, result.MissingTranscripts);
    }

    [Fact]
    public async Task AuditMediaAssetsAsync_aggregates_status_counts()
    {
        var (db, svc) = Build();
        AddAsset(db, "ready1", status: MediaAssetStatus.Ready,
            thumbnail: "t.jpg", transcript: "tr.txt");
        AddAsset(db, "ready2", status: MediaAssetStatus.Ready,
            thumbnail: "t.jpg", transcript: "tr.txt");
        AddAsset(db, "proc1", status: MediaAssetStatus.Processing);
        AddAsset(db, "fail1", status: MediaAssetStatus.Failed);

        var result = await svc.AuditMediaAssetsAsync(CancellationToken.None);
        Assert.Equal(4, result.TotalAssets);
        Assert.Equal(2, result.Ready);
        Assert.Equal(1, result.Processing);
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public async Task AuditMediaAssetsAsync_counts_video_assets_missing_thumbnail()
    {
        var (db, svc) = Build();
        AddAsset(db, "v1", status: MediaAssetStatus.Ready, mimeType: "video/mp4",
            thumbnail: null, transcript: "tr.txt");
        AddAsset(db, "v2", status: MediaAssetStatus.Ready, mimeType: "video/mp4",
            thumbnail: "t.jpg", transcript: "tr.txt");

        var result = await svc.AuditMediaAssetsAsync(CancellationToken.None);
        Assert.Equal(1, result.MissingThumbnails);
    }

    [Fact]
    public async Task AuditMediaAssetsAsync_counts_audio_and_video_missing_transcripts()
    {
        var (db, svc) = Build();
        AddAsset(db, "v1", status: MediaAssetStatus.Ready, mimeType: "video/mp4",
            thumbnail: "t.jpg", transcript: null);
        AddAsset(db, "a1", status: MediaAssetStatus.Ready, mimeType: "audio/mp3",
            thumbnail: null, transcript: null);
        AddAsset(db, "img1", status: MediaAssetStatus.Ready, mimeType: "image/png",
            thumbnail: null, transcript: null); // images don't count toward missing transcripts

        var result = await svc.AuditMediaAssetsAsync(CancellationToken.None);
        Assert.Equal(2, result.MissingTranscripts);
    }

    [Fact]
    public async Task AuditMediaAssetsAsync_excludes_non_ready_assets_from_missing_counts()
    {
        var (db, svc) = Build();
        AddAsset(db, "p1", status: MediaAssetStatus.Processing, mimeType: "video/mp4",
            thumbnail: null, transcript: null);

        var result = await svc.AuditMediaAssetsAsync(CancellationToken.None);
        Assert.Equal(0, result.MissingThumbnails);
        Assert.Equal(0, result.MissingTranscripts);
    }
}
