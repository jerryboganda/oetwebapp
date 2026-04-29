using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Tests;

/// <summary>
/// Verifies that <see cref="VideoLessonService"/> consults
/// <see cref="ILearnerEntitlementResolver"/> for paid / sponsored access
/// instead of duplicating direct subscription checks. Premium video
/// lessons (those whose hierarchy is published but not in any package
/// rule and not preview-eligible) must be locked for free learners but
/// unlocked for paid / trial / sponsor / active-add-on entitlements.
/// </summary>
public sealed class VideoLessonEntitlementTests
{
    private const string LessonId = "lsn-premium-video";

    [Fact]
    public async Task FreeLearner_PremiumLesson_IsLocked()
    {
        var (db, service) = Build();
        await SeedPremiumLessonAsync(db);

        var detail = await service.GetLessonAsync("learner-free", LessonId, default);

        Assert.NotNull(detail);
        Assert.False(detail!.IsAccessible);
        Assert.Equal("locked", detail.AccessReason);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task PaidLearner_PremiumLesson_IsAccessible()
    {
        var (db, service) = Build();
        await SeedPremiumLessonAsync(db);
        db.Subscriptions.Add(NewSubscription("sub-paid", "learner-paid", SubscriptionStatus.Active));
        await db.SaveChangesAsync();

        var detail = await service.GetLessonAsync("learner-paid", LessonId, default);

        Assert.True(detail!.IsAccessible);
        Assert.Equal("entitled", detail.AccessReason);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task TrialLearner_PremiumLesson_IsAccessible()
    {
        var (db, service) = Build();
        await SeedPremiumLessonAsync(db);
        db.Subscriptions.Add(NewSubscription("sub-trial", "learner-trial", SubscriptionStatus.Trial));
        await db.SaveChangesAsync();

        var detail = await service.GetLessonAsync("learner-trial", LessonId, default);

        Assert.True(detail!.IsAccessible);
        Assert.Equal("entitled", detail.AccessReason);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SponsorSeatLearner_PremiumLesson_IsAccessible()
    {
        var (db, service) = Build();
        await SeedPremiumLessonAsync(db);
        db.Sponsorships.Add(new Sponsorship
        {
            Id = Guid.NewGuid(),
            SponsorUserId = "sponsor-user",
            LearnerUserId = "learner-sponsored",
            LearnerEmail = "learner@example.test",
            Status = "Active",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var detail = await service.GetLessonAsync("learner-sponsored", LessonId, default);

        Assert.True(detail!.IsAccessible);
        Assert.Equal("entitled", detail.AccessReason);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task AddOnPlusActiveSubscription_PremiumLesson_IsAccessible()
    {
        var (db, service) = Build();
        await SeedPremiumLessonAsync(db);
        var now = DateTimeOffset.UtcNow;
        db.Subscriptions.Add(NewSubscription("sub-addon", "learner-addon", SubscriptionStatus.Active));
        db.SubscriptionItems.Add(new SubscriptionItem
        {
            Id = "item-addon-vid",
            SubscriptionId = "sub-addon",
            ItemCode = "video-pack",
            ItemType = "addon",
            Status = SubscriptionItemStatus.Active,
            StartsAt = now.AddDays(-1),
            CreatedAt = now.AddDays(-1),
            UpdatedAt = now.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var detail = await service.GetLessonAsync("learner-addon", LessonId, default);

        Assert.True(detail!.IsAccessible);
        Assert.Equal("entitled", detail.AccessReason);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task LearnerOnFreeze_PremiumLesson_IsAccessibleViaActiveSubscription()
    {
        var (db, service) = Build();
        await SeedPremiumLessonAsync(db);
        var now = DateTimeOffset.UtcNow;
        db.Subscriptions.Add(NewSubscription("sub-frozen", "learner-frozen", SubscriptionStatus.Active));
        db.AccountFreezeRecords.Add(new AccountFreezeRecord
        {
            Id = "freeze-current",
            UserId = "learner-frozen",
            Status = FreezeStatus.Active,
            IsCurrent = true,
            RequestedAt = now.AddDays(-2),
            StartedAt = now.AddDays(-1),
            DurationDays = 7,
            UpdatedAt = now.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var detail = await service.GetLessonAsync("learner-frozen", LessonId, default);

        Assert.True(detail!.IsAccessible);
        Assert.Equal("entitled", detail.AccessReason);
        await db.DisposeAsync();
    }

    private static (LearnerDbContext Db, VideoLessonService Service) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var service = new VideoLessonService(db, new MediaNormalizationService(db), new LearnerEntitlementResolver(db));
        return (db, service);
    }

    private static async Task SeedPremiumLessonAsync(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        db.ContentPrograms.Add(new ContentProgram
        {
            Id = "prg-premium-video",
            Code = "premium-video",
            Title = "Premium Video Program",
            Status = ContentStatus.Published,
            ExamTypeCode = "oet",
            ExamFamilyCode = "oet",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.ContentTracks.Add(new ContentTrack
        {
            Id = "trk-premium-video",
            ProgramId = "prg-premium-video",
            Title = "Premium Track",
            SubtestCode = "writing",
            Status = ContentStatus.Published
        });
        db.ContentModules.Add(new ContentModule
        {
            Id = "mod-premium-video",
            TrackId = "trk-premium-video",
            Title = "Premium Module",
            EstimatedDurationMinutes = 10,
            Status = ContentStatus.Published
        });
        db.ContentItems.Add(new ContentItem
        {
            Id = "content-premium-video",
            ContentType = "video_lesson",
            SubtestCode = "writing",
            Title = "Premium Video",
            Difficulty = "intermediate",
            EstimatedDurationMinutes = 10,
            PublishedRevisionId = "rev-premium-video",
            Status = ContentStatus.Published,
            ExamTypeCode = "oet",
            ExamFamilyCode = "oet",
            SourceProvenance = "original",
            RightsStatus = "owned",
            FreshnessConfidence = "current",
            IsPreviewEligible = false,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.MediaAssets.Add(new MediaAsset
        {
            Id = "media-premium-video",
            OriginalFilename = "premium.mp4",
            MimeType = "video/mp4",
            Format = "mp4",
            SizeBytes = 1024,
            DurationSeconds = 600,
            StoragePath = "/media/premium.mp4",
            Status = MediaAssetStatus.Ready,
            UploadedAt = now
        });
        db.ContentLessons.Add(new ContentLesson
        {
            Id = LessonId,
            ModuleId = "mod-premium-video",
            ContentItemId = "content-premium-video",
            MediaAssetId = "media-premium-video",
            Title = "Premium Video Lesson",
            LessonType = "video_lesson",
            Status = ContentStatus.Published,
            DisplayOrder = 1
        });
        await db.SaveChangesAsync();
    }

    private static Subscription NewSubscription(string id, string userId, SubscriptionStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        return new Subscription
        {
            Id = id,
            UserId = userId,
            PlanId = "premium-monthly",
            Status = status,
            StartedAt = now.AddDays(-10),
            ChangedAt = now.AddDays(-10),
            NextRenewalAt = now.AddDays(20)
        };
    }
}
