using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.VideoLibrary;

namespace OetLearner.Api.Tests;

/// <summary>
/// Covers the per-user Video Library scope (<see cref="UserVideoAccess"/>): explicit rows keep
/// older selections restricted, while videos first published after the initial scope are
/// automatically included. No rows remains fail-open. Enforced consistently in both
/// the playback gate (<see cref="VideoEntitlementService"/> → reason "not_in_user_allocation")
/// and the listing/detail path (<see cref="VideoLibraryLearnerService.FindVisibleVideoAsync"/>).
/// Admins bypass. Owner directive 2026-07-18, forward-compatible content behavior 2026-08-08.
/// </summary>
public class UserVideoAccessTests
{
    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static VideoEntitlementService CreateGate(LearnerDbContext db)
        => new(db, new EffectiveEntitlementResolver(db));

    private static LibraryVideo Video(
        string id,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? publishedAt = null)
    {
        var created = createdAt ?? DateTimeOffset.UtcNow;
        return new LibraryVideo
        {
            Id = id,
            Title = $"Video {id}",
            AccessTier = "premium",
            Status = ContentStatus.Published,
            DurationSeconds = 600,
            ProfessionIdsJson = "[]",
            CreatedAt = created,
            PublishedAt = publishedAt,
            UpdatedAt = created,
        };
    }

    /// <summary>Grants the learner the Video Library via the admin "Videos" module toggle
    /// (DashboardModulesJson) so premium videos unlock — the allocation is then the only
    /// additional restriction for existing content.</summary>
    private static void SeedVideoModuleSubscription(LearnerDbContext db, string userId)
    {
        var now = DateTimeOffset.UtcNow;
        var planCode = $"plan-{Guid.NewGuid():N}"[..24];
        db.BillingPlans.Add(new BillingPlan
        {
            Id = planCode,
            Code = planCode,
            Name = "Test plan",
            EntitlementsJson = "{}",
            DashboardModulesJson = """["VideoLibrary"]""",
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = userId,
            PlanId = planCode,
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-1),
            ChangedAt = now,
        });
    }

    private static void Allow(
        LearnerDbContext db,
        string userId,
        string videoId,
        DateTimeOffset? createdAt = null)
        => db.UserVideoAccesses.Add(new UserVideoAccess
        {
            Id = $"uva-{Guid.NewGuid():N}",
            UserId = userId,
            VideoId = videoId,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        });

    // ── Playback gate ───────────────────────────────────────────────────────

    [Fact]
    public async Task Allocation_ExcludesVideo_GateDenies()
    {
        await using var db = CreateDb();
        SeedVideoModuleSubscription(db, "learner-1");
        var scopeCreatedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var allocated = Video("vid-a", scopeCreatedAt.AddHours(-2));
        var excluded = Video("vid-b", scopeCreatedAt.AddHours(-1));
        db.LibraryVideos.AddRange(allocated, excluded);
        Allow(db, "learner-1", "vid-a", scopeCreatedAt); // only vid-a is allocated
        await db.SaveChangesAsync();

        var result = await CreateGate(db).AllowAccessAsync("learner-1", excluded, default);

        Assert.False(result.Allowed);
        Assert.Equal("not_in_user_allocation", result.Reason);
    }

    [Fact]
    public async Task Allocation_AutomaticallyIncludesVideosPublishedAfterTheScopeWasCreated()
    {
        await using var db = CreateDb();
        var scopeCreatedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var existing = Video("vid-a", scopeCreatedAt.AddHours(-1), scopeCreatedAt.AddHours(-1));
        var newlyPublished = Video("vid-new", scopeCreatedAt.AddHours(1), scopeCreatedAt.AddHours(1));
        db.LibraryVideos.AddRange(existing, newlyPublished);
        Allow(db, "learner-1", existing.Id, scopeCreatedAt);
        await db.SaveChangesAsync();

        var service = new VideoLibraryLearnerService(db, entitlements: null!, settingsProvider: null!);

        Assert.NotNull(await service.FindVisibleVideoAsync("learner-1", newlyPublished.Id, scopeCreatedAt.AddDays(1), default));
    }

    [Fact]
    public async Task Allocation_AutomaticallyIncludesNewVideoInPlaybackGate()
    {
        await using var db = CreateDb();
        var scopeCreatedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        SeedVideoModuleSubscription(db, "learner-1");
        var existing = Video("vid-a", scopeCreatedAt.AddHours(-1), scopeCreatedAt.AddHours(-1));
        var newlyPublished = Video("vid-new", scopeCreatedAt.AddHours(1), scopeCreatedAt.AddHours(1));
        db.LibraryVideos.AddRange(existing, newlyPublished);
        Allow(db, "learner-1", existing.Id, scopeCreatedAt);
        await db.SaveChangesAsync();

        var result = await CreateGate(db).AllowAccessAsync("learner-1", newlyPublished, default);

        Assert.True(result.Allowed);
        Assert.Equal("plan_grants_video_library", result.Reason);
    }

    [Fact]
    public async Task Allocation_IncludesVideo_GateAllows()
    {
        await using var db = CreateDb();
        SeedVideoModuleSubscription(db, "learner-1");
        var scopeCreatedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var allocated = Video("vid-a", scopeCreatedAt.AddHours(-2));
        db.LibraryVideos.AddRange(allocated, Video("vid-b", scopeCreatedAt.AddHours(-1)));
        Allow(db, "learner-1", "vid-a", scopeCreatedAt);
        await db.SaveChangesAsync();

        var result = await CreateGate(db).AllowAccessAsync("learner-1", allocated, default);

        Assert.True(result.Allowed);
        Assert.Equal("plan_grants_video_library", result.Reason);
    }

    [Fact]
    public async Task NoAllocation_GateUnchanged_FailOpen()
    {
        await using var db = CreateDb();
        SeedVideoModuleSubscription(db, "learner-1");
        db.LibraryVideos.Add(Video("vid-a"));
        // No UserVideoAccess rows.
        await db.SaveChangesAsync();

        var result = await CreateGate(db).AllowAccessAsync("learner-1", Video("vid-a"), default);

        Assert.True(result.Allowed);
        Assert.Equal("plan_grants_video_library", result.Reason);
    }

    [Fact]
    public async Task Admin_BypassesAllocation()
    {
        await using var db = CreateDb();
        Allow(db, "admin-1", "vid-a"); // allow-list would exclude vid-b for a learner
        await db.SaveChangesAsync();

        var gate = CreateGate(db);
        var context = await gate.ResolveContextAsync("admin-1", isAdmin: true, default);
        var result = gate.Evaluate(context, Video("vid-b"));

        Assert.True(result.Allowed);
        Assert.Equal("admin", result.Reason);
    }

    // ── Listing / detail path ───────────────────────────────────────────────

    [Fact]
    public async Task FindVisibleVideo_WithAllocation_HidesNonAllocated()
    {
        await using var db = CreateDb();
        var scopeCreatedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var allocated = Video("vid-a", scopeCreatedAt.AddHours(-2));
        var excluded = Video("vid-b", scopeCreatedAt.AddHours(-1));
        db.LibraryVideos.AddRange(allocated, excluded);
        Allow(db, "learner-1", "vid-a", scopeCreatedAt);
        await db.SaveChangesAsync();

        // FindVisibleVideoAsync only touches the db (profession + video scope); the
        // entitlement/settings deps are never dereferenced on this path.
        var service = new VideoLibraryLearnerService(db, entitlements: null!, settingsProvider: null!);
        var now = DateTimeOffset.UtcNow;

        Assert.NotNull(await service.FindVisibleVideoAsync("learner-1", allocated.Id, now, default));
        Assert.Null(await service.FindVisibleVideoAsync("learner-1", excluded.Id, now, default));
    }

    [Fact]
    public async Task FindVisibleVideo_WithoutAllocation_ShowsAll()
    {
        await using var db = CreateDb();
        db.LibraryVideos.AddRange(Video("vid-a"), Video("vid-b"));
        // No UserVideoAccess rows.
        await db.SaveChangesAsync();

        var service = new VideoLibraryLearnerService(db, entitlements: null!, settingsProvider: null!);
        var now = DateTimeOffset.UtcNow;

        Assert.NotNull(await service.FindVisibleVideoAsync("learner-1", "vid-a", now, default));
        Assert.NotNull(await service.FindVisibleVideoAsync("learner-1", "vid-b", now, default));
    }
}
