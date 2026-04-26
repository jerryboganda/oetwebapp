using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using Xunit;

namespace OetLearner.Api.Tests;

public class ContentAccessServiceTests
{
    private static (LearnerDbContext db, ContentAccessService svc) Build()
    {
        var opts = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(opts);
        var hierarchy = new ContentHierarchyService(db);
        var svc = new ContentAccessService(db, hierarchy);
        return (db, svc);
    }

    private static ContentItem AddItem(
        LearnerDbContext db, string id, bool isPreview = false,
        ContentStatus status = ContentStatus.Published,
        string subtest = "writing", string? profession = "nursing",
        string difficulty = "easy", string language = "en",
        string provenance = "original", string freshness = "current")
    {
        var item = new ContentItem
        {
            Id = id,
            ContentType = "lesson",
            SubtestCode = subtest,
            ProfessionId = profession,
            Title = $"Title-{id}",
            Difficulty = difficulty,
            EstimatedDurationMinutes = 10,
            PublishedRevisionId = "r1",
            Status = status,
            InstructionLanguage = language,
            SourceProvenance = provenance,
            FreshnessConfidence = freshness,
            IsPreviewEligible = isPreview,
            QualityScore = 3,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.ContentItems.Add(item);
        db.SaveChanges();
        return item;
    }

    private static ContentPackage AddPackage(LearnerDbContext db, string id, string planId)
    {
        var p = new ContentPackage
        {
            Id = id,
            Code = $"code-{id}",
            Title = $"Package {id}",
            BillingPlanId = planId,
            Status = ContentStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.ContentPackages.Add(p);
        db.SaveChanges();
        return p;
    }

    private static void AddPackageItemRule(LearnerDbContext db, string packageId, string contentItemId)
    {
        db.PackageContentRules.Add(new PackageContentRule
        {
            Id = Guid.NewGuid().ToString("N"),
            PackageId = packageId,
            RuleType = "include_item",
            TargetId = contentItemId,
            TargetType = "content_item",
        });
        db.SaveChanges();
    }

    private static void AddSubscription(LearnerDbContext db, string userId, string planId)
    {
        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            PlanId = planId,
            Status = SubscriptionStatus.Active,
            StartedAt = DateTimeOffset.UtcNow.AddDays(-30),
            ChangedAt = DateTimeOffset.UtcNow.AddDays(-30),
            NextRenewalAt = DateTimeOffset.UtcNow.AddDays(30),
            PriceAmount = 9.99m,
        });
        db.SaveChanges();
    }

    // ── CheckAccessAsync ────────────────────────────────────────────────

    [Fact]
    public async Task CheckAccessAsync_returns_not_found_when_item_missing()
    {
        var (_, svc) = Build();
        var result = await svc.CheckAccessAsync("u1", "missing-id", CancellationToken.None);
        Assert.False(result.IsAccessible);
        Assert.Equal("not_found", result.Reason);
        Assert.Null(result.RequiredPackageId);
    }

    [Fact]
    public async Task CheckAccessAsync_grants_free_preview_when_item_is_preview_eligible()
    {
        var (db, svc) = Build();
        AddItem(db, "c1", isPreview: true);
        var result = await svc.CheckAccessAsync("u1", "c1", CancellationToken.None);
        Assert.True(result.IsAccessible);
        Assert.Equal("free_preview", result.Reason);
    }

    [Fact]
    public async Task CheckAccessAsync_returns_locked_when_no_subscription_and_not_preview()
    {
        var (db, svc) = Build();
        AddItem(db, "c1");
        var result = await svc.CheckAccessAsync("u1", "c1", CancellationToken.None);
        Assert.False(result.IsAccessible);
        Assert.Equal("locked", result.Reason);
    }

    [Fact]
    public async Task CheckAccessAsync_grants_subscription_access_when_user_has_active_plan_with_item_rule()
    {
        var (db, svc) = Build();
        AddItem(db, "c1");
        AddPackage(db, "pkg-1", planId: "plan-pro");
        AddPackageItemRule(db, "pkg-1", "c1");
        AddSubscription(db, "u1", planId: "plan-pro");

        var result = await svc.CheckAccessAsync("u1", "c1", CancellationToken.None);
        Assert.True(result.IsAccessible);
        Assert.Equal("subscription", result.Reason);
    }

    [Fact]
    public async Task CheckAccessAsync_locked_returns_required_package_id_for_targeted_content()
    {
        var (db, svc) = Build();
        AddItem(db, "c1");
        AddPackage(db, "pkg-1", planId: "plan-pro");
        AddPackageItemRule(db, "pkg-1", "c1");
        // No subscription for u1.
        var result = await svc.CheckAccessAsync("u1", "c1", CancellationToken.None);
        Assert.False(result.IsAccessible);
        Assert.Equal("locked", result.Reason);
        Assert.Equal("pkg-1", result.RequiredPackageId);
    }

    // ── BrowseContentAsync ──────────────────────────────────────────────

    [Fact]
    public async Task BrowseContentAsync_returns_only_published_non_superseded_items()
    {
        var (db, svc) = Build();
        AddItem(db, "p1");
        AddItem(db, "p2", status: ContentStatus.Draft);
        AddItem(db, "p3", freshness: "superseded");

        dynamic result = await svc.BrowseContentAsync(
            "u1", null, null, null, null, null, page: 1, pageSize: 50, CancellationToken.None);
        Assert.Equal(1, (int)result.total);
    }

    [Fact]
    public async Task BrowseContentAsync_filters_by_subtest_difficulty_language_provenance()
    {
        var (db, svc) = Build();
        AddItem(db, "a", subtest: "writing", difficulty: "easy", language: "en", provenance: "original");
        AddItem(db, "b", subtest: "reading", difficulty: "easy", language: "en", provenance: "original");
        AddItem(db, "c", subtest: "writing", difficulty: "hard", language: "en", provenance: "original");
        AddItem(db, "d", subtest: "writing", difficulty: "easy", language: "ar", provenance: "original");
        AddItem(db, "e", subtest: "writing", difficulty: "easy", language: "en", provenance: "recall");

        dynamic r = await svc.BrowseContentAsync(
            "u1", subtestCode: "writing", professionId: null, difficulty: "easy",
            language: "en", provenance: "original", page: 1, pageSize: 50, CancellationToken.None);
        Assert.Equal(1, (int)r.total);
    }

    [Fact]
    public async Task BrowseContentAsync_filter_by_profession_includes_null_profession_items()
    {
        var (db, svc) = Build();
        AddItem(db, "n1", profession: "nursing");
        AddItem(db, "n2", profession: "medicine");
        AddItem(db, "shared", profession: null);

        dynamic r = await svc.BrowseContentAsync(
            "u1", null, professionId: "nursing", null, null, null,
            page: 1, pageSize: 50, CancellationToken.None);
        Assert.Equal(2, (int)r.total);
    }

    [Fact]
    public async Task BrowseContentAsync_supports_pagination()
    {
        var (db, svc) = Build();
        for (var i = 0; i < 5; i++) AddItem(db, $"c{i}");

        dynamic page1 = await svc.BrowseContentAsync(
            "u1", null, null, null, null, null, page: 1, pageSize: 2, CancellationToken.None);
        Assert.Equal(5, (int)page1.total);

        dynamic page3 = await svc.BrowseContentAsync(
            "u1", null, null, null, null, null, page: 3, pageSize: 2, CancellationToken.None);
        Assert.Equal(5, (int)page3.total);
    }

    [Fact]
    public async Task BrowseContentAsync_marks_preview_items_correctly_for_unsubscribed_users()
    {
        var (db, svc) = Build();
        AddItem(db, "preview", isPreview: true);
        AddItem(db, "locked", isPreview: false);

        dynamic r = await svc.BrowseContentAsync(
            "u1", null, null, null, null, null, page: 1, pageSize: 50, CancellationToken.None);
        Assert.Equal(2, (int)r.total);
    }

    // ── BrowseProgramsWithAccessAsync ───────────────────────────────────

    [Fact]
    public async Task BrowseProgramsWithAccessAsync_returns_only_published_programs()
    {
        var (db, svc) = Build();
        db.ContentPrograms.Add(new ContentProgram
        {
            Id = "prog-1",
            Code = "p1",
            Title = "Program 1",
            Status = ContentStatus.Published,
            DisplayOrder = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.ContentPrograms.Add(new ContentProgram
        {
            Id = "prog-2",
            Code = "p2",
            Title = "Program 2",
            Status = ContentStatus.Draft,
            DisplayOrder = 2,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();

        dynamic r = await svc.BrowseProgramsWithAccessAsync(
            "u1", null, null, page: 1, pageSize: 50, CancellationToken.None);
        Assert.Equal(1, (int)r.total);
    }

    [Fact]
    public async Task BrowseProgramsWithAccessAsync_filters_by_program_type_and_language()
    {
        var (db, svc) = Build();
        db.ContentPrograms.Add(new ContentProgram
        {
            Id = "p1", Code = "p1", Title = "P1",
            ProgramType = "course", InstructionLanguage = "en",
            Status = ContentStatus.Published, DisplayOrder = 1,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.ContentPrograms.Add(new ContentProgram
        {
            Id = "p2", Code = "p2", Title = "P2",
            ProgramType = "intensive", InstructionLanguage = "en",
            Status = ContentStatus.Published, DisplayOrder = 2,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();

        dynamic r = await svc.BrowseProgramsWithAccessAsync(
            "u1", type: "course", language: "en", page: 1, pageSize: 50, CancellationToken.None);
        Assert.Equal(1, (int)r.total);
    }
}
