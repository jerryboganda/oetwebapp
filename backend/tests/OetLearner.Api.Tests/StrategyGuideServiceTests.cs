using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public sealed class StrategyGuideServiceTests
{
    private const string UserId = "learner-1";

    [Fact]
    public async Task Feature_flag_disables_strategy_surface()
    {
        var (db, service) = Build();
        db.FeatureFlags.Add(new FeatureFlag
        {
            Id = "flag-strategy-guides",
            Key = "strategy_guides",
            Name = "Strategy Guides",
            Enabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.False(await service.IsEnabledAsync(default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task ListGuides_returns_progress_buckets_and_weak_subtest_recommendations()
    {
        var (db, service) = Build();
        await SeedStrategyGuidesAsync(db);
        db.Goals.Add(new LearnerGoal
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            ProfessionId = "nursing",
            WeakSubtestsJson = "[\"writing\"]",
            StudyHoursPerWeek = 8,
            UpdatedAt = DateTimeOffset.UtcNow,
            ExamTypeCode = "oet",
            ExamFamilyCode = "oet"
        });
        db.LearnerStrategyProgress.Add(new LearnerStrategyProgress
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            StrategyGuideId = "strategy-writing",
            ReadPercent = 45,
            Bookmarked = true,
            StartedAt = DateTimeOffset.UtcNow.AddDays(-1),
            LastReadAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var library = await service.ListGuidesAsync(UserId, "oet", null, null, null, recommendedOnly: false, default);

        Assert.Equal(2, library.Items.Count);
        var recommended = Assert.Single(library.Recommended);
        Assert.Equal("strategy-writing", recommended.Id);
        Assert.Equal("Matches your Writing focus.", recommended.RecommendedReason);
        Assert.Equal("strategy-writing", Assert.Single(library.ContinueReading).Id);
        Assert.Equal("strategy-writing", Assert.Single(library.Bookmarked).Id);
        Assert.Contains(library.Categories, category => category.Code == "writing");
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Progress_and_bookmark_updates_are_idempotent_and_non_regressive()
    {
        var (db, service) = Build();
        await SeedStrategyGuidesAsync(db);

        var first = await service.UpdateProgressAsync(UserId, "strategy-writing", 65, default);
        var second = await service.UpdateProgressAsync(UserId, "strategy-writing", 20, default);
        var bookmark = await service.SetBookmarkAsync(UserId, "strategy-writing", true, default);
        var complete = await service.UpdateProgressAsync(UserId, "strategy-writing", 100, default);

        Assert.NotNull(first);
        Assert.Equal(65, first!.Progress.ReadPercent);
        Assert.NotNull(second);
        Assert.Equal(65, second!.Progress.ReadPercent);
        Assert.NotNull(bookmark);
        Assert.True(bookmark!.Progress.Bookmarked);
        Assert.NotNull(complete);
        Assert.True(complete!.Progress.Completed);
        Assert.Equal(100, complete.Progress.ReadPercent);

        var stored = await db.LearnerStrategyProgress.SingleAsync(progress => progress.UserId == UserId && progress.StrategyGuideId == "strategy-writing");
        Assert.True(stored.Bookmarked);
        Assert.True(stored.Completed);
        Assert.NotNull(stored.CompletedAt);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task PublishGuide_rejects_missing_source_body_and_title()
    {
        var (db, service) = Build();
        db.StrategyGuides.Add(new StrategyGuide
        {
            Id = "strategy-draft",
            ExamTypeCode = "oet",
            Title = "",
            Summary = "Draft",
            Category = "writing",
            ReadingTimeMinutes = 3,
            SortOrder = 1,
            Status = "draft",
            ContentHtml = "",
            ContentJson = "",
            SourceProvenance = "",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await service.PublishGuideAsync("admin-1", "Admin User", "strategy-draft", default);

        Assert.False(result.Published);
        Assert.False(result.Validation.CanPublish);
        Assert.Contains(result.Validation.Errors, error => error.Field == "title");
        Assert.Contains(result.Validation.Errors, error => error.Field == "sourceProvenance");
        Assert.Contains(result.Validation.Errors, error => error.Field == "content");
        Assert.Equal("draft", (await db.StrategyGuides.SingleAsync(guide => guide.Id == "strategy-draft")).Status);
        Assert.Empty(db.AuditEvents);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task PublishGuide_sets_status_and_writes_audit_event_when_valid()
    {
        var (db, service) = Build();
        db.StrategyGuides.Add(new StrategyGuide
        {
            Id = "strategy-valid",
            ExamTypeCode = "oet",
            Title = "Valid Strategy",
            Summary = "A valid guide.",
            Category = "writing",
            ReadingTimeMinutes = 4,
            SortOrder = 1,
            Status = "draft",
            ContentHtml = "<p>Use a clear plan.</p>",
            ContentJson = "{\"overview\":\"Use a clear plan.\"}",
            SourceProvenance = "Original OET Prep editorial content",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await service.PublishGuideAsync("admin-1", "Admin User", "strategy-valid", default);

        Assert.True(result.Published);
        Assert.True(result.Validation.CanPublish);
        var stored = await db.StrategyGuides.SingleAsync(guide => guide.Id == "strategy-valid");
        Assert.Equal("active", stored.Status);
        Assert.NotEqual(default, stored.PublishedAt);
        var audit = Assert.Single(db.AuditEvents);
        Assert.Equal("Published", audit.Action);
        Assert.Equal("StrategyGuide", audit.ResourceType);
        Assert.Equal("strategy-valid", audit.ResourceId);
        await db.DisposeAsync();
    }

    private static (LearnerDbContext Db, StrategyGuideService Service) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var service = new StrategyGuideService(db);
        return (db, service);
    }

    private static async Task SeedStrategyGuidesAsync(LearnerDbContext db)
    {
        db.StrategyGuides.AddRange(
            new StrategyGuide
            {
                Id = "strategy-overview",
                Slug = "oet-overview-strategy",
                ExamTypeCode = "oet",
                Title = "OET Strategy Overview",
                Summary = "Build a simple weekly plan around the four subtests.",
                Category = "exam_overview",
                ReadingTimeMinutes = 5,
                SortOrder = 1,
                Status = "active",
                ContentHtml = "<p>Start with your weakest subtest.</p>",
                ContentJson = "{\"overview\":\"Start with your weakest subtest.\"}",
                SourceProvenance = "Original OET Prep editorial content",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                PublishedAt = DateTimeOffset.UtcNow
            },
            new StrategyGuide
            {
                Id = "strategy-writing",
                Slug = "writing-letter-structure",
                ExamTypeCode = "oet",
                SubtestCode = "writing",
                Title = "Writing Letter Structure",
                Summary = "Plan a purposeful referral letter before you draft.",
                Category = "writing",
                ReadingTimeMinutes = 6,
                SortOrder = 2,
                Status = "active",
                ContentHtml = "<p>Group case notes by reader need.</p>",
                ContentJson = "{\"overview\":\"Group case notes by reader need.\"}",
                SourceProvenance = "Original OET Prep editorial content",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                PublishedAt = DateTimeOffset.UtcNow
            },
            new StrategyGuide
            {
                Id = "strategy-archived",
                Slug = "archived",
                ExamTypeCode = "oet",
                Title = "Archived",
                Summary = "Hidden.",
                Category = "exam_overview",
                ReadingTimeMinutes = 2,
                SortOrder = 3,
                Status = "archived",
                ContentHtml = "<p>Hidden.</p>",
                ContentJson = "{\"overview\":\"Hidden.\"}",
                SourceProvenance = "Original OET Prep editorial content",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                PublishedAt = DateTimeOffset.UtcNow
            });

        await db.SaveChangesAsync();
    }
}
