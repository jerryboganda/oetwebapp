using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.VideoLibrary;

namespace OetLearner.Api.Tests;

/// <summary>
/// Owner directive 2026-08-26: two-way Writing video isolation between
/// Full Course and Crash Course / Fast Track. The tag-driven family model
/// (CourseFamilyPolicy) supersedes the per-plan excludeTags / include /
/// subtest-restriction machinery: a premium video's family comes from the
/// admin-assigned "batch:*" tags first, then title/collection labels, and
/// neutral content falls back to Shared. Explicit per-id includes preserve
/// intentional cross-family catalog carve-outs.
///
/// These tests lock the four core guarantees:
///   1. Full-course plans deny every video tagged with any Crash Course
///      batch tag — including the legacy excludeTags-covered batches.
///   2. Full-course plans still allow ordinary December/February Writing
///      videos tagged batch:full-course-only and neutral untagged content.
///   3. Crash Course plans allow the crash batch tags with no include
///      carve-out and no video_library.subtests restriction — the family
///      tag is the sole gate.
///   4. Explicit per-id include beats the family tag for that exact video.
/// </summary>
public class VideoEntitlementTwoWayWritingIsolationTests
{
    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static VideoEntitlementService CreateService(LearnerDbContext db)
        => new(db, new EffectiveEntitlementResolver(db));

    private static LibraryVideo Video(
        string id,
        string subtestCode = "writing",
        string? tagsCsv = null,
        string accessTier = "premium",
        string? title = null,
        string professionIdsJson = "[]") => new()
    {
        Id = id,
        Title = title ?? $"Video {id}",
        AccessTier = accessTier,
        SubtestCode = subtestCode,
        TagsCsv = tagsCsv,
        Status = ContentStatus.Published,
        DurationSeconds = 600,
        ProfessionIdsJson = professionIdsJson,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Plant a plan with explicit ContentOverridesJson + dashboard module list, then
    /// attach an active subscription for the given user. Mirrors the seeding pattern in
    /// <see cref="VideoEntitlementServiceTests"/> but with ContentOverridesJson wired in.</summary>
    private static void SeedPlan(
        LearnerDbContext db,
        string userId,
        string planCode,
        string contentOverridesJson,
        string dashboardModulesJson = """["VideoLibrary"]""",
        DateTimeOffset? expiresAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan
        {
            Id = planCode,
            Code = planCode,
            Name = planCode,
            EntitlementsJson = "{}",
            ContentOverridesJson = contentOverridesJson,
            DashboardModulesJson = dashboardModulesJson,
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = userId,
            PlanId = planCode,
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-1),
            ChangedAt = now,
            ExpiresAt = expiresAt,
        });
    }

    [Fact]
    public async Task FullCourse_DeniesVideoTaggedWithCrashCourseArabicWritingBatch()
    {
        await using var db = CreateDb();
        SeedPlan(db, "learner-1", "full-condensed-medicine", "{}");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var video = Video("vid_crash_arabic_1", tagsCsv: "batch:crash-course-arabic-writing,language:ar");
        var result = await service.AllowAccessAsync("learner-1", video, default);

        Assert.False(result.Allowed);
        Assert.Equal("plan_excludes_course_family", result.Reason);
    }

    [Fact]
    public async Task FullCourse_DeniesVideoTaggedWithFastTrackCrashCourseBatch()
    {
        await using var db = CreateDb();
        SeedPlan(db, "learner-1", "full-condensed-medicine", "{}");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var video = Video("vid_fast_track_1", tagsCsv: "batch:fast-track-crash-course");
        var result = await service.AllowAccessAsync("learner-1", video, default);

        Assert.False(result.Allowed);
        Assert.Equal("plan_excludes_course_family", result.Reason);
    }

    [Fact]
    public async Task FullCourse_AllowsOrdinaryDecemberFullCourseWritingVideo()
    {
        await using var db = CreateDb();
        SeedPlan(db, "learner-1", "full-condensed-medicine", "{}");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var video = Video(
            "vid_december_full_course_1",
            tagsCsv: "batch:full-course-only,month:december,profession:medicine");
        var result = await service.AllowAccessAsync("learner-1", video, default);

        Assert.True(result.Allowed);
        Assert.Equal("plan_grants_video_library", result.Reason);
    }

    [Fact]
    public async Task FullCourse_AllowsUntaggedPremiumWritingVideo_WhenNeutral()
    {
        await using var db = CreateDb();
        SeedPlan(db, "learner-1", "full-condensed-medicine", "{}");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var video = Video("vid_untagged_1", tagsCsv: null);
        var result = await service.AllowAccessAsync("learner-1", video, default);

        Assert.True(result.Allowed);
        Assert.Equal("plan_grants_video_library", result.Reason);
    }

    [Fact]
    public async Task CrashCourse_AllowsVideoTaggedWithCrashCourseArabicWritingBatch()
    {
        await using var db = CreateDb();
        // Crash Course plan, plain: no video_library.subtests restriction and no
        // per-id include. Writing is granted like every other subtest, and the
        // 31 Aug 2026 isolation spec §4 additionally requires the video's own
        // path/title to name the Crash Course / Fast Track folder it lives in.
        SeedPlan(db, "learner-1", "crash-course", "{}");
        // Writing content is profession-targeted by contract, so the learner needs a
        // profession for the Medicine-only crash rules (§1.1/§5) to resolve.
        db.Users.Add(new LearnerUser
        {
            Id = "learner-1",
            Email = "learner-1@test.dev",
            DisplayName = "learner-1",
            Role = ApplicationUserRoles.Learner,
            ActiveProfessionId = "medicine",
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var video = Video(
            "vid_crash_arabic_1",
            tagsCsv: "batch:crash-course-arabic-writing",
            title: "Writing / Medicine / Arabic / Fast-Track Crash Course / Session 1",
            professionIdsJson: """["medicine"]""");
        var result = await service.AllowAccessAsync("learner-1", video, default);

        Assert.True(result.Allowed);
        Assert.Equal("plan_grants_video_library", result.Reason);
    }

    [Fact]
    public async Task FullCourse_ExplicitIncludeBeatsCrashTag()
    {
        await using var db = CreateDb();
        // An explicit per-id include intentionally carves this crash-tagged video into
        // the full-course plan without exposing the rest of the Crash Course catalog.
        var fullCourseOverrides = """
            {
              "videos": {
                "include": [
                  "vid_crash_arabic_legacy_included"
                ]
              }
            }
            """;
        SeedPlan(db, "learner-1", "full-condensed-medicine", fullCourseOverrides);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var video = Video(
            "vid_crash_arabic_legacy_included",
            tagsCsv: "batch:crash-course-arabic-writing");
        var result = await service.AllowAccessAsync("learner-1", video, default);

        Assert.True(result.Allowed);
        Assert.Equal("plan_grants_video_library", result.Reason);
    }

    [Fact]
    public void MergeContentOverrides_PicksUpExcludeTagsFromVideosNode()
    {
        // Pure resolver test: the excludeTags under "videos" should be picked up by
        // MergeContentOverrides, case-insensitive, and survive a second plan in the list.
        var plan1 = new BillingPlan
        {
            Id = "full-medicine",
            Code = "full-medicine",
            ContentOverridesJson = """
                {"videos":{"excludeTags":["batch:crash-course-arabic-writing","BATCH:FAST-TRACK-CRASH-COURSE"]}}
                """,
        };
        var plan2 = new BillingPlan
        {
            Id = "addon-extra",
            Code = "addon-extra",
            ContentOverridesJson = """{"videos":{"excludeTags":["batch:crash-course-workshops"]}}""",
        };
        var plan3 = new BillingPlan
        {
            Id = "no-overrides",
            Code = "no-overrides",
            ContentOverridesJson = null,
        };

        var merged = EffectiveEntitlementResolver.MergeContentOverrides(new[] { plan1, plan2, plan3 });

        Assert.NotNull(merged.VideoExcludeTags);
        Assert.Contains("batch:crash-course-arabic-writing", merged.VideoExcludeTags);
        Assert.Contains("batch:fast-track-crash-course", merged.VideoExcludeTags);
        Assert.Contains("batch:crash-course-workshops", merged.VideoExcludeTags);
    }

    [Fact]
    public void MergeContentOverrides_EmptyWhenNoExcludeTags()
    {
        var plan = new BillingPlan
        {
            Id = "only-id-overrides",
            Code = "only-id-overrides",
            ContentOverridesJson = """{"videos":{"exclude":["vid_x"]}}""",
        };
        var merged = EffectiveEntitlementResolver.MergeContentOverrides(new[] { plan });

        Assert.Null(merged.VideoExcludeTags);
    }

    [Fact]
    public void VideoMatchesAnyTag_HandlesNullAndEmptyInputs()
    {
        var video = Video("v1", tagsCsv: "batch:crash-course-arabic-writing, language:ar");
        Assert.True(VideoEntitlementService.VideoMatchesAnyTag(
            video,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "batch:crash-course-arabic-writing" }));
        Assert.False(VideoEntitlementService.VideoMatchesAnyTag(
            video,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "batch:unrelated" }));
        Assert.False(VideoEntitlementService.VideoMatchesAnyTag(
            Video("v2", tagsCsv: null),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "batch:crash-course-arabic-writing" }));
        Assert.False(VideoEntitlementService.VideoMatchesAnyTag(
            video,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
    }
}
