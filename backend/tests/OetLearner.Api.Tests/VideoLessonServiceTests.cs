using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public sealed class VideoLessonServiceTests
{
    private const string UserId = "learner-1";

    [Fact]
    public async Task ListLessons_returns_hierarchy_video_fields_with_zero_progress()
    {
        var (db, service) = Build();
        await SeedHierarchyVideoLessonAsync(db);

        var lessons = await service.ListLessonsAsync(UserId, "oet", "writing", null, default);

        var lesson = Assert.Single(lessons);
        Assert.Equal("lsn-video-1", lesson.Id);
        Assert.Equal("content_hierarchy", lesson.Source);
        Assert.Equal("oet", lesson.ExamTypeCode);
        Assert.Equal("writing", lesson.SubtestCode);
        Assert.Equal("video_lesson", lesson.Category);
        Assert.Equal("intermediate", lesson.DifficultyLevel);
        Assert.Equal(600, lesson.DurationSeconds);
        Assert.False(lesson.IsAccessible);
        Assert.True(lesson.RequiresUpgrade);
        Assert.NotNull(lesson.Progress);
        Assert.Equal(0, lesson.Progress!.PercentComplete);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Detail_hides_signed_video_url_for_locked_lessons()
    {
        var (db, service) = Build();
        await SeedHierarchyVideoLessonAsync(db);

        var lesson = await service.GetLessonAsync(UserId, "lsn-video-1", default);

        Assert.NotNull(lesson);
        Assert.False(lesson!.IsAccessible);
        Assert.True(lesson.RequiresUpgrade);
        Assert.Null(lesson.VideoUrl);
        Assert.Equal("locked", lesson.AccessReason);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Detail_exposes_signed_video_url_for_preview_lessons()
    {
        var (db, service) = Build();
        await SeedHierarchyVideoLessonAsync(db);
        db.FreePreviewAssets.Add(new FreePreviewAsset
        {
            Id = "preview-video-1",
            Title = "Preview",
            PreviewType = "sample_lesson",
            MediaAssetId = "media-video-1",
            Status = ContentStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var lesson = await service.GetLessonAsync(UserId, "lsn-video-1", default);

        Assert.NotNull(lesson);
        Assert.True(lesson!.IsAccessible);
        Assert.True(lesson.IsPreviewEligible);
        Assert.False(lesson.RequiresUpgrade);
        Assert.Equal("/media/video-1.mp4", lesson.VideoUrl);
        Assert.Equal("/media/video-1.vtt", lesson.CaptionUrl);
        Assert.Equal("/media/video-1.txt", lesson.TranscriptUrl);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Detail_exposes_signed_video_url_for_entitled_lessons()
    {
        var (db, service) = Build();
        await SeedHierarchyVideoLessonAsync(db);
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-1",
            UserId = UserId,
            PlanId = "plan-1",
            Status = SubscriptionStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            ChangedAt = DateTimeOffset.UtcNow,
            NextRenewalAt = DateTimeOffset.UtcNow.AddMonths(1),
            PriceAmount = 99
        });
        db.ContentPackages.Add(new ContentPackage
        {
            Id = "pkg-1",
            Code = "full",
            Title = "Full",
            BillingPlanId = "plan-1",
            Status = ContentStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.PackageContentRules.Add(new PackageContentRule
        {
            Id = "rule-1",
            PackageId = "pkg-1",
            RuleType = "include_module",
            TargetType = "module",
            TargetId = "mod-video-1"
        });
        await db.SaveChangesAsync();

        var lesson = await service.GetLessonAsync(UserId, "content-video-1", default);

        Assert.NotNull(lesson);
        Assert.True(lesson!.IsAccessible);
        Assert.False(lesson.RequiresUpgrade);
        Assert.Equal("entitled", lesson.AccessReason);
        Assert.Equal("/media/video-1.mp4", lesson.VideoUrl);
        Assert.Equal("lsn-video-1", lesson.Id);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Progress_clamps_seconds_and_never_regresses()
    {
        var (db, service) = Build();
        await SeedHierarchyVideoLessonAsync(db);

        var first = await service.UpdateProgressAsync(UserId, "lsn-video-1", 700, default);
        var second = await service.UpdateProgressAsync(UserId, "lsn-video-1", 120, default);

        Assert.NotNull(first);
        Assert.Equal(600, first!.WatchedSeconds);
        Assert.True(first.Completed);
        Assert.NotNull(second);
        Assert.Equal(600, second!.WatchedSeconds);
        Assert.True(second.Completed);
        Assert.Equal(100, second.PercentComplete);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Feature_flag_disables_video_lesson_surface()
    {
        var (db, service) = Build();
        db.FeatureFlags.Add(new FeatureFlag
        {
            Id = "flag-video-lessons",
            Key = "video_lessons",
            Name = "Video Lessons",
            Enabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.False(await service.IsEnabledAsync(default));
        await db.DisposeAsync();
    }

    private static (LearnerDbContext Db, VideoLessonService Service) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var service = new VideoLessonService(db, new MediaNormalizationService(db));
        return (db, service);
    }

    private static async Task SeedHierarchyVideoLessonAsync(LearnerDbContext db)
    {
        db.ContentPrograms.Add(new ContentProgram
        {
            Id = "prg-video-1",
            Code = "oet-video",
            Title = "OET Video Course",
            Description = "Expert video course",
            Status = ContentStatus.Published,
            ExamTypeCode = "oet",
            ExamFamilyCode = "oet",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.ContentTracks.Add(new ContentTrack
        {
            Id = "trk-video-1",
            ProgramId = "prg-video-1",
            Title = "Writing Track",
            SubtestCode = "writing",
            Status = ContentStatus.Published
        });
        db.ContentModules.Add(new ContentModule
        {
            Id = "mod-video-1",
            TrackId = "trk-video-1",
            Title = "Referral Letters",
            Description = "Plan high-scoring referral letters.",
            EstimatedDurationMinutes = 10,
            Status = ContentStatus.Published
        });
        db.ContentItems.Add(new ContentItem
        {
            Id = "content-video-1",
            ContentType = "video_lesson",
            SubtestCode = "writing",
            Title = "Referral Letter Structure",
            Difficulty = "intermediate",
            EstimatedDurationMinutes = 10,
            PublishedRevisionId = "rev-video-1",
            Status = ContentStatus.Published,
            ExamTypeCode = "oet",
            ExamFamilyCode = "oet",
            SourceProvenance = "original",
            RightsStatus = "owned",
            FreshnessConfidence = "current",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.MediaAssets.Add(new MediaAsset
        {
            Id = "media-video-1",
            OriginalFilename = "video-1.mp4",
            MimeType = "video/mp4",
            Format = "mp4",
            SizeBytes = 1024,
            DurationSeconds = 600,
            StoragePath = "/media/video-1.mp4",
            ThumbnailPath = "/media/video-1.jpg",
            CaptionPath = "/media/video-1.vtt",
            TranscriptPath = "/media/video-1.txt",
            Status = MediaAssetStatus.Ready,
            UploadedAt = DateTimeOffset.UtcNow
        });
        db.ContentLessons.Add(new ContentLesson
        {
            Id = "lsn-video-1",
            ModuleId = "mod-video-1",
            ContentItemId = "content-video-1",
            MediaAssetId = "media-video-1",
            Title = "Referral Letter Structure",
            LessonType = "video_lesson",
            Status = ContentStatus.Published,
            DisplayOrder = 1
        });

        await db.SaveChangesAsync();
    }
}
