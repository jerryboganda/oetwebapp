using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Tests;

/// <summary>
/// Verifies that <see cref="StrategyGuideService"/> consults
/// <see cref="ILearnerEntitlementResolver"/> for paid / sponsored access
/// instead of duplicating direct subscription checks. Premium guides
/// (those whose hierarchy is published but not in any package) must be
/// gated for free learners but unlocked for paid / trial / sponsor /
/// active-add-on / freeze entitlements.
/// </summary>
public sealed class StrategyGuideEntitlementTests
{
    private const string PremiumGuideId = "strategy-premium";

    [Fact]
    public async Task FreeLearner_PremiumGuide_IsLocked()
    {
        var (db, service) = Build();
        await SeedPremiumHierarchyAsync(db);
        await db.SaveChangesAsync();

        var detail = await service.GetGuideAsync("learner-free", PremiumGuideId, default);

        Assert.NotNull(detail);
        Assert.False(detail!.IsAccessible);
        Assert.Equal("locked", detail.AccessReason);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task PaidLearner_PremiumGuide_IsAccessible()
    {
        var (db, service) = Build();
        await SeedPremiumHierarchyAsync(db);
        db.Subscriptions.Add(NewSubscription("sub-paid", "learner-paid", SubscriptionStatus.Active));
        await db.SaveChangesAsync();

        var detail = await service.GetGuideAsync("learner-paid", PremiumGuideId, default);

        Assert.NotNull(detail);
        Assert.True(detail!.IsAccessible);
        Assert.Equal("entitled", detail.AccessReason);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task TrialLearner_PremiumGuide_IsAccessible()
    {
        var (db, service) = Build();
        await SeedPremiumHierarchyAsync(db);
        db.Subscriptions.Add(NewSubscription("sub-trial", "learner-trial", SubscriptionStatus.Trial));
        await db.SaveChangesAsync();

        var detail = await service.GetGuideAsync("learner-trial", PremiumGuideId, default);

        Assert.True(detail!.IsAccessible);
        Assert.Equal("entitled", detail.AccessReason);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SponsorSeatLearner_PremiumGuide_IsAccessible()
    {
        var (db, service) = Build();
        await SeedPremiumHierarchyAsync(db);
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

        var detail = await service.GetGuideAsync("learner-sponsored", PremiumGuideId, default);

        Assert.True(detail!.IsAccessible);
        Assert.Equal("entitled", detail.AccessReason);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task LearnerWithActiveAddOn_AndActiveSubscription_PremiumGuide_IsAccessible()
    {
        var (db, service) = Build();
        await SeedPremiumHierarchyAsync(db);
        var now = DateTimeOffset.UtcNow;
        db.Subscriptions.Add(NewSubscription("sub-addon", "learner-addon", SubscriptionStatus.Active));
        db.SubscriptionItems.Add(new SubscriptionItem
        {
            Id = "item-priority",
            SubscriptionId = "sub-addon",
            ItemCode = "priority-review",
            ItemType = "addon",
            Status = SubscriptionItemStatus.Active,
            StartsAt = now.AddDays(-1),
            CreatedAt = now.AddDays(-1),
            UpdatedAt = now.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var detail = await service.GetGuideAsync("learner-addon", PremiumGuideId, default);

        Assert.True(detail!.IsAccessible);
        Assert.Equal("entitled", detail.AccessReason);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task LearnerOnFreeze_PremiumGuide_IsAccessibleViaActiveSubscription()
    {
        // A freeze pauses billing but the underlying subscription remains
        // Active in our schema, so paid access is preserved through the
        // resolver's HasPaidOrSponsoredAccess flag.
        var (db, service) = Build();
        await SeedPremiumHierarchyAsync(db);
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

        var detail = await service.GetGuideAsync("learner-frozen", PremiumGuideId, default);

        Assert.True(detail!.IsAccessible);
        Assert.Equal("entitled", detail.AccessReason);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task LegacyGuideWithoutHierarchy_IsAccessibleForEveryone()
    {
        var (db, service) = Build();
        db.StrategyGuides.Add(new StrategyGuide
        {
            Id = "strategy-legacy",
            Slug = "legacy",
            ExamTypeCode = "oet",
            Title = "Legacy",
            Summary = "No hierarchy.",
            Category = "exam_overview",
            ReadingTimeMinutes = 3,
            SortOrder = 1,
            Status = "active",
            ContentHtml = "<p>Legacy.</p>",
            ContentJson = "{}",
            SourceProvenance = "Original",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            PublishedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var detail = await service.GetGuideAsync("learner-free", "strategy-legacy", default);

        Assert.NotNull(detail);
        Assert.True(detail!.IsAccessible);
        Assert.Equal("legacy_access", detail.AccessReason);
        await db.DisposeAsync();
    }

    private static (LearnerDbContext Db, StrategyGuideService Service) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var service = new StrategyGuideService(db, new LearnerEntitlementResolver(db));
        return (db, service);
    }

    private static async Task SeedPremiumHierarchyAsync(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        db.ContentPrograms.Add(new ContentProgram
        {
            Id = "prog-premium",
            Code = "premium-program",
            Title = "Premium Program",
            ExamTypeCode = "oet",
            ExamFamilyCode = "oet",
            Status = ContentStatus.Published,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.ContentTracks.Add(new ContentTrack
        {
            Id = "track-premium",
            ProgramId = "prog-premium",
            Title = "Premium Track",
            SubtestCode = "writing",
            Status = ContentStatus.Published
        });
        db.ContentModules.Add(new ContentModule
        {
            Id = "module-premium",
            TrackId = "track-premium",
            Title = "Premium Module",
            Status = ContentStatus.Published
        });
        db.ContentLessons.Add(new ContentLesson
        {
            Id = "lesson-premium",
            ModuleId = "module-premium",
            Title = "Premium Lesson",
            LessonType = "strategy_guide",
            Status = ContentStatus.Published
        });
        db.StrategyGuides.Add(new StrategyGuide
        {
            Id = PremiumGuideId,
            Slug = "premium-guide",
            ExamTypeCode = "oet",
            SubtestCode = "writing",
            Title = "Premium Strategy",
            Summary = "Premium guide gated by package.",
            Category = "writing",
            ReadingTimeMinutes = 6,
            SortOrder = 1,
            Status = "active",
            ContentHtml = "<p>premium</p>",
            ContentJson = "{}",
            SourceProvenance = "Original",
            ContentLessonId = "lesson-premium",
            IsPreviewEligible = false,
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now
        });
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
