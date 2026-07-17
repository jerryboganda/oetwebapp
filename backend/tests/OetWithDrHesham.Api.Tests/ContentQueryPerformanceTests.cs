using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;

namespace OetWithDrHesham.Api.Tests;

public sealed class ContentQueryPerformanceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly SqlCaptureInterceptor _sql = new();
    private DbContextOptions<LearnerDbContext> _options = default!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_sql)
            .Options;

        await using var db = new LearnerDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    public async Task ResolveAccessibleContentIdsAsync_UsesTwoCommandsRegardlessOfProgramRuleCount(
        int ruleCount)
    {
        await using var db = new LearnerDbContext(_options);
        AddEligibility(db, "learner", "package", "plan");

        for (var index = 0; index < ruleCount; index++)
        {
            var suffix = index.ToString("D2");
            AddHierarchy(
                db,
                $"program-{suffix}",
                $"track-{suffix}",
                $"module-{suffix}",
                $"item-{suffix}");
            db.PackageContentRules.Add(CreateRule(
                $"rule-{suffix}",
                "package",
                "include_program",
                "program",
                $"program-{suffix}"));
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        _sql.Commands.Clear();

        var result = await new ContentHierarchyService(db)
            .ResolveAccessibleContentIdsAsync("learner", CancellationToken.None);

        Assert.Equal(ruleCount, result.Count);
        Assert.Equal(2, _sql.Commands.Count);
        Assert.All(Enumerable.Range(0, ruleCount), index =>
            Assert.Contains($"item-{index:D2}", result));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ResolveAccessibleContentIdsAsync_BatchesTargetTypesAndAppliesExcludesAndPublication()
    {
        await using var db = new LearnerDbContext(_options);
        AddEligibility(db, "learner", "package", "plan");

        AddHierarchy(db, "program-target", "track-program", "module-program", "item-program");
        AddHierarchy(db, "program-track", "track-target", "module-track", "item-track");
        AddHierarchy(db, "program-module", "track-module", "module-target", "item-module");
        AddHierarchy(
            db,
            "program-draft-module",
            "track-draft-module",
            "module-draft",
            "item-under-draft-module",
            moduleStatus: ContentStatus.Draft);
        AddHierarchy(
            db,
            "program-draft-lesson",
            "track-draft-lesson",
            "module-draft-lesson",
            "item-under-draft-lesson",
            lessonStatus: ContentStatus.Draft);

        db.ContentItems.AddRange(
            CreateItem("item-direct", "Direct"),
            CreateItem("item-draft", "Draft", status: ContentStatus.Draft),
            CreateItem("item-superseded", "Superseded", freshness: "superseded"));
        db.PackageContentRules.AddRange(
            CreateRule("rule-01", "package", "include_program", "program", "program-target"),
            CreateRule("rule-02", "package", "include_track", "track", "track-target"),
            CreateRule("rule-03", "package", "include_module", "module", "module-target"),
            CreateRule("rule-04", "package", "exclude_module", "module", "module-target"),
            CreateRule("rule-05", "package", "include_content_item", "content_item", "item-direct"),
            CreateRule("rule-06", "package", "exclude_content_item", "content_item", "item-direct"),
            CreateRule("rule-07", "package", "include_content_item", "content_item", "item-draft"),
            CreateRule("rule-08", "package", "include_module", "module", "module-draft"),
            CreateRule("rule-09", "package", "include_module", "module", "module-draft-lesson"),
            CreateRule("rule-10", "package", "include_content_item", "content_item", "item-superseded"));

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        _sql.Commands.Clear();

        var result = await new ContentHierarchyService(db)
            .ResolveAccessibleContentIdsAsync("learner", CancellationToken.None);

        Assert.Equal(
            new[] { "item-program", "item-track" },
            result.OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(5, _sql.Commands.Count);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ResolveAccessibleContentIdsAsync_UnionsPackagesAfterPackageLocalExcludes()
    {
        await using var db = new LearnerDbContext(_options);
        AddEligibility(db, "learner", "package-a", "plan-a");
        AddEligibility(db, "learner", "package-b", "plan-b");
        AddHierarchy(db, "program", "track", "module", "item");
        db.PackageContentRules.AddRange(
            CreateRule("rule-a-include", "package-a", "include_program", "program", "program"),
            CreateRule("rule-a-exclude", "package-a", "exclude_content_item", "content_item", "item"),
            CreateRule("rule-b-include", "package-b", "include_content_item", "content_item", "item"));

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        _sql.Commands.Clear();

        var result = await new ContentHierarchyService(db)
            .ResolveAccessibleContentIdsAsync("learner", CancellationToken.None);

        Assert.Equal(new[] { "item" }, result);
        Assert.Equal(3, _sql.Commands.Count);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ResolveAccessibleContentIdsAsync_EmptyEligibilityUsesOneCommand()
    {
        await using var db = new LearnerDbContext(_options);
        _sql.Commands.Clear();

        var result = await new ContentHierarchyService(db)
            .ResolveAccessibleContentIdsAsync("learner-without-subscription", CancellationToken.None);

        Assert.Empty(result);
        Assert.Single(_sql.Commands);
    }

    [Fact]
    public async Task ResolveAccessibleContentIdsAsync_ObservesPreCancelledTokenWithoutQuerying()
    {
        await using var db = new LearnerDbContext(_options);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _sql.Commands.Clear();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ContentHierarchyService(db)
                .ResolveAccessibleContentIdsAsync("learner", cancellation.Token));

        Assert.Empty(_sql.Commands);
    }

    [Fact]
    public async Task BrowseProgramsWithAccessAsync_FiltersRulesByUserPackagesAndPreservesDto()
    {
        await using var db = new LearnerDbContext(_options);
        AddEligibility(db, "learner", "package-user", "plan-user");
        db.ContentPackages.Add(CreatePackage("package-other", "plan-other"));
        db.ContentPrograms.AddRange(
            CreateProgram("program-user", displayOrder: 1),
            CreateProgram("program-other", displayOrder: 2));
        db.ContentTracks.Add(CreateTrack("track-user", "program-user"));
        db.PackageContentRules.AddRange(
            CreateRule("rule-user", "package-user", "include_program", "program", "program-user"),
            CreateRule("rule-other", "package-other", "include_program", "program", "program-other"));

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        _sql.Commands.Clear();

        var response = await new ContentAccessService(db, new ContentHierarchyService(db))
            .BrowseProgramsWithAccessAsync(
                "learner",
                type: null,
                language: null,
                page: 1,
                pageSize: 20,
                CancellationToken.None);
        var json = JsonSerializer.SerializeToElement(response);
        var items = json.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(2, json.GetProperty("total").GetInt32());
        Assert.Equal(1, json.GetProperty("page").GetInt32());
        Assert.Equal(20, json.GetProperty("pageSize").GetInt32());
        Assert.Equal(
            new[]
            {
                "Id", "Code", "Title", "Description", "ProfessionId",
                "InstructionLanguage", "ProgramType", "ThumbnailUrl",
                "DisplayOrder", "EstimatedDurationMinutes", "isAccessible", "trackCount"
            },
            items[0].EnumerateObject().Select(property => property.Name));
        Assert.Equal("program-user", items[0].GetProperty("Id").GetString());
        Assert.True(items[0].GetProperty("isAccessible").GetBoolean());
        Assert.Equal(1, items[0].GetProperty("trackCount").GetInt32());
        Assert.Equal("program-other", items[1].GetProperty("Id").GetString());
        Assert.False(items[1].GetProperty("isAccessible").GetBoolean());
        Assert.Equal(0, items[1].GetProperty("trackCount").GetInt32());

        Assert.Equal(5, _sql.Commands.Count);
        var ruleCommand = Assert.Single(_sql.Commands.Where(command =>
            command.Contains("PackageContentRules", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("PackageId", ruleCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RuleType", ruleCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task CheckAccessAsync_ReturnsPackageForCanonicalDirectItemRule()
    {
        await using var db = new LearnerDbContext(_options);
        db.ContentItems.AddRange(
            CreateItem("item-locked", "Locked"),
            CreateItem(
                "item-draft-preview",
                "Draft preview",
                status: ContentStatus.Draft,
                isPreviewEligible: true));
        db.PackageContentRules.Add(CreateRule(
            "rule-direct",
            "package-required",
            "include_content_item",
            "content_item",
            "item-locked"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new ContentAccessService(db, new ContentHierarchyService(db))
            .CheckAccessAsync("learner", "item-locked", CancellationToken.None);

        Assert.False(result.IsAccessible);
        Assert.Equal("locked", result.Reason);
        Assert.Equal("package-required", result.RequiredPackageId);

        var draftPreview = await new ContentAccessService(db, new ContentHierarchyService(db))
            .CheckAccessAsync("learner", "item-draft-preview", CancellationToken.None);
        Assert.False(draftPreview.IsAccessible);
        Assert.Equal("not_found", draftPreview.Reason);
        Assert.Null(draftPreview.RequiredPackageId);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task BrowseContentAsync_ClampsPaginationAndPreservesDtoProjection()
    {
        await using var db = new LearnerDbContext(_options);
        db.ContentItems.Add(CreateItem(
            "item-preview",
            "Preview",
            detailJson: """{"section":"sample"}""",
            isPreviewEligible: true));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        _sql.Commands.Clear();

        var response = await new ContentAccessService(db, new ContentHierarchyService(db))
            .BrowseContentAsync(
                "learner-without-subscription",
                subtestCode: null,
                professionId: null,
                difficulty: null,
                language: null,
                provenance: null,
                page: 0,
                pageSize: 1_000,
                CancellationToken.None);
        var json = JsonSerializer.SerializeToElement(response);
        var item = Assert.Single(json.GetProperty("items").EnumerateArray());

        Assert.Equal(1, json.GetProperty("page").GetInt32());
        Assert.Equal(100, json.GetProperty("pageSize").GetInt32());
        Assert.Equal(
            new[]
            {
                "contentId", "contentType", "subtest", "professionId", "title",
                "difficulty", "estimatedDurationMinutes", "scenarioType",
                "instructionLanguage", "sourceProvenance", "qualityScore",
                "isAccessible", "isPreview", "requiresUpgrade", "noSubscription"
            },
            item.EnumerateObject().Select(property => property.Name));
        Assert.Equal("item-preview", item.GetProperty("contentId").GetString());
        Assert.False(item.GetProperty("isAccessible").GetBoolean());
        Assert.True(item.GetProperty("isPreview").GetBoolean());
        Assert.False(item.GetProperty("noSubscription").GetBoolean());
        Assert.Equal(3, _sql.Commands.Count);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task SearchContentAsync_UsesSqliteSafeContainsSemanticsAndPreservesDto()
    {
        await using var db = new LearnerDbContext(_options);
        db.ContentItems.AddRange(
            CreateItem("item-title", "HEART care"),
            CreateItem("item-detail", "Other", detailJson: """{"note":"NURSING keyword"}"""),
            CreateItem("item-percent", "Rate 100% Ready"),
            CreateItem("item-wildcard", "Rate 100X Ready"),
            CreateItem("item-draft", "keyword", status: ContentStatus.Draft),
            CreateItem("item-superseded", "keyword", freshness: "superseded"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = new ContentSearchService(db);
        var titleResult = JsonSerializer.SerializeToElement(await service.SearchContentAsync(
            new ContentSearchQuery { Text = "heart" },
            CancellationToken.None));
        Assert.Equal(
            new[] { "item-title" },
            titleResult.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("Id").GetString()));

        _sql.Commands.Clear();
        var detailResult = JsonSerializer.SerializeToElement(await service.SearchContentAsync(
            new ContentSearchQuery { Text = "keyword", Page = 0, PageSize = 1_000 },
            CancellationToken.None));
        var detailItem = Assert.Single(detailResult.GetProperty("items").EnumerateArray());

        Assert.Equal("item-detail", detailItem.GetProperty("Id").GetString());
        Assert.Equal(1, detailResult.GetProperty("page").GetInt32());
        Assert.Equal(100, detailResult.GetProperty("pageSize").GetInt32());
        Assert.Equal(
            new[]
            {
                "Id", "Title", "SubtestCode", "ContentType", "ProfessionId",
                "Difficulty", "DifficultyRating", "EstimatedDurationMinutes",
                "ScenarioType", "InstructionLanguage", "SourceProvenance",
                "QualityScore", "IsPreviewEligible", "IsMockEligible",
                "IsDiagnosticEligible", "CreatedAt"
            },
            detailItem.EnumerateObject().Select(property => property.Name));
        Assert.Equal(2, _sql.Commands.Count);
        Assert.DoesNotContain(_sql.Commands, command =>
            command.Contains("lower(", StringComparison.OrdinalIgnoreCase));

        var literalResult = JsonSerializer.SerializeToElement(await service.SearchContentAsync(
            new ContentSearchQuery { Text = "%" },
            CancellationToken.None));
        Assert.Equal(
            new[] { "item-percent" },
            literalResult.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("Id").GetString()));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task SearchContentAsync_LimitsOversizedPageToOneHundredRows()
    {
        await using var db = new LearnerDbContext(_options);
        db.ContentItems.AddRange(Enumerable.Range(0, 105)
            .Select(index => CreateItem($"item-{index:D3}", $"Item {index:D3}")));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        _sql.Commands.Clear();

        var result = JsonSerializer.SerializeToElement(await new ContentSearchService(db)
            .SearchContentAsync(
                new ContentSearchQuery { Page = int.MinValue, PageSize = int.MaxValue },
                CancellationToken.None));

        Assert.Equal(105, result.GetProperty("total").GetInt32());
        Assert.Equal(100, result.GetProperty("items").GetArrayLength());
        Assert.Equal(1, result.GetProperty("page").GetInt32());
        Assert.Equal(100, result.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, _sql.Commands.Count);
    }

    [Fact]
    public async Task GetRecommendationsAsync_LimitsOversizedCount()
    {
        await using var db = new LearnerDbContext(_options);
        db.ContentItems.AddRange(Enumerable.Range(0, 105)
            .Select(index => CreateItem($"item-{index:D3}", $"Item {index:D3}")));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = JsonSerializer.SerializeToElement(await new ContentSearchService(db)
            .GetRecommendationsAsync("learner", int.MaxValue, CancellationToken.None));

        Assert.Equal(100, result.GetProperty("recommended").GetArrayLength());
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private static void AddEligibility(
        LearnerDbContext db,
        string userId,
        string packageId,
        string planId)
    {
        db.Subscriptions.Add(new Subscription
        {
            Id = $"subscription-{packageId}",
            UserId = userId,
            PlanId = planId,
            Status = SubscriptionStatus.Active
        });
        db.ContentPackages.Add(CreatePackage(packageId, planId));
    }

    private static ContentPackage CreatePackage(string id, string planId)
        => new()
        {
            Id = id,
            Code = id,
            Title = id,
            BillingPlanId = planId,
            Status = ContentStatus.Published
        };

    private static PackageContentRule CreateRule(
        string id,
        string packageId,
        string ruleType,
        string targetType,
        string targetId)
        => new()
        {
            Id = id,
            PackageId = packageId,
            RuleType = ruleType,
            TargetType = targetType,
            TargetId = targetId
        };

    private static void AddHierarchy(
        LearnerDbContext db,
        string programId,
        string trackId,
        string moduleId,
        string itemId,
        ContentStatus programStatus = ContentStatus.Published,
        ContentStatus trackStatus = ContentStatus.Published,
        ContentStatus moduleStatus = ContentStatus.Published,
        ContentStatus lessonStatus = ContentStatus.Published,
        ContentStatus itemStatus = ContentStatus.Published)
    {
        db.ContentPrograms.Add(CreateProgram(programId, status: programStatus));
        db.ContentTracks.Add(CreateTrack(trackId, programId, trackStatus));
        db.ContentModules.Add(new ContentModule
        {
            Id = moduleId,
            TrackId = trackId,
            Title = moduleId,
            Status = moduleStatus
        });
        db.ContentLessons.Add(new ContentLesson
        {
            Id = $"lesson-{moduleId}",
            ModuleId = moduleId,
            ContentItemId = itemId,
            Title = $"Lesson {moduleId}",
            Status = lessonStatus
        });
        db.ContentItems.Add(CreateItem(itemId, itemId, status: itemStatus));
    }

    private static ContentProgram CreateProgram(
        string id,
        int displayOrder = 0,
        ContentStatus status = ContentStatus.Published)
        => new()
        {
            Id = id,
            Code = id,
            Title = id,
            Status = status,
            DisplayOrder = displayOrder
        };

    private static ContentTrack CreateTrack(
        string id,
        string programId,
        ContentStatus status = ContentStatus.Published)
        => new()
        {
            Id = id,
            ProgramId = programId,
            Title = id,
            Status = status
        };

    private static ContentItem CreateItem(
        string id,
        string title,
        string detailJson = "{}",
        ContentStatus status = ContentStatus.Published,
        string freshness = "current",
        bool isPreviewEligible = false)
        => new()
        {
            Id = id,
            ContentType = "practice",
            SubtestCode = "reading",
            Title = title,
            Difficulty = "intermediate",
            PublishedRevisionId = $"revision-{id}",
            DetailJson = detailJson,
            Status = status,
            FreshnessConfidence = freshness,
            InstructionLanguage = "en",
            SourceProvenance = "original",
            QualityScore = 3,
            CreatedAt = CreatedAt,
            UpdatedAt = CreatedAt,
            IsPreviewEligible = isPreviewEligible
        };

    private sealed class SqlCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
