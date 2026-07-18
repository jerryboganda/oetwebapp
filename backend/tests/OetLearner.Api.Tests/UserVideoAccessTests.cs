using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.VideoLibrary;

namespace OetLearner.Api.Tests;

/// <summary>
/// Covers the per-user Video Library allow-list (<see cref="UserVideoAccess"/>): a learner
/// with ANY rows is RESTRICTED to those video ids (fail-open when no rows). Enforced in both
/// the playback gate (<see cref="VideoEntitlementService"/> → reason "not_in_user_allocation")
/// and the listing/detail path (<see cref="VideoLibraryLearnerService.FindVisibleVideoAsync"/>).
/// Admins bypass. Owner directive 2026-07-18.
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

    private static LibraryVideo Video(string id) => new()
    {
        Id = id,
        Title = $"Video {id}",
        AccessTier = "premium",
        Status = ContentStatus.Published,
        DurationSeconds = 600,
        ProfessionIdsJson = "[]",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Grants the learner the Video Library via the admin "Videos" module toggle
    /// (DashboardModulesJson) so premium videos unlock — the allocation is then the ONLY
    /// thing that can withhold a video.</summary>
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

    private static void Allow(LearnerDbContext db, string userId, string videoId)
        => db.UserVideoAccesses.Add(new UserVideoAccess
        {
            Id = $"uva-{Guid.NewGuid():N}",
            UserId = userId,
            VideoId = videoId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

    // ── Playback gate ───────────────────────────────────────────────────────

    [Fact]
    public async Task Allocation_ExcludesVideo_GateDenies()
    {
        await using var db = CreateDb();
        SeedVideoModuleSubscription(db, "learner-1");
        db.LibraryVideos.AddRange(Video("vid-a"), Video("vid-b"));
        Allow(db, "learner-1", "vid-a"); // only vid-a is allocated
        await db.SaveChangesAsync();

        var result = await CreateGate(db).AllowAccessAsync("learner-1", Video("vid-b"), default);

        Assert.False(result.Allowed);
        Assert.Equal("not_in_user_allocation", result.Reason);
    }

    [Fact]
    public async Task Allocation_IncludesVideo_GateAllows()
    {
        await using var db = CreateDb();
        SeedVideoModuleSubscription(db, "learner-1");
        db.LibraryVideos.AddRange(Video("vid-a"), Video("vid-b"));
        Allow(db, "learner-1", "vid-a");
        await db.SaveChangesAsync();

        var result = await CreateGate(db).AllowAccessAsync("learner-1", Video("vid-a"), default);

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
        db.LibraryVideos.AddRange(Video("vid-a"), Video("vid-b"));
        Allow(db, "learner-1", "vid-a");
        await db.SaveChangesAsync();

        // FindVisibleVideoAsync only touches the db (profession + allow-list); the
        // entitlement/settings deps are never dereferenced on this path.
        var service = new VideoLibraryLearnerService(db, entitlements: null!, settingsProvider: null!);
        var now = DateTimeOffset.UtcNow;

        Assert.NotNull(await service.FindVisibleVideoAsync("learner-1", "vid-a", now, default));
        Assert.Null(await service.FindVisibleVideoAsync("learner-1", "vid-b", now, default));
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
