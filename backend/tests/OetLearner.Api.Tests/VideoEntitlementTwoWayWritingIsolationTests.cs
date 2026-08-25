using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.VideoLibrary;

namespace OetLearner.Api.Tests;

/// <summary>
/// Owner directive 2026-08-26: two-way Writing video isolation between
/// Full Course and Crash Course / Fast Track. The previous narrower rule
/// (20260822090000, 18 Arabic Writing videos) is superseded by this
/// tag-driven approach. A new admin-managed "batch:*" tag picker makes
/// the rule live the moment an admin publishes a video, with no
/// migration required.
///
/// These tests lock the four core guarantees:
///   1. Full-course plans deny every video tagged with one of the four
///      Crash Course Writing batch tags.
///   2. Full-course plans still allow ordinary December/February Writing
///      videos (no regression for shared content).
///   3. Crash Course plans allow the four batch tags (no regression on
///      the 18-id include carved in by 20260822090000).
///   4. The new tag-based exclusion composes correctly with the existing
///      explicit-per-id include rule (include beats exclude-tag).
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
        string accessTier = "premium") => new()
    {
        Id = id,
        Title = $"Video {id}",
        AccessTier = accessTier,
        SubtestCode = subtestCode,
        TagsCsv = tagsCsv,
        Status = ContentStatus.Published,
        DurationSeconds = 600,
        ProfessionIdsJson = "[]",
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
        var fullCourseOverrides = """
            {
              "videos": {
                "excludeTags": [
                  "batch:crash-course-arabic-writing",
                  "batch:writing-sessions-crash-course-old",
                  "batch:fast-track-crash-course",
                  "batch:crash-course-workshops"
                ]
              }
            }
            """;
        SeedPlan(db, "learner-1", "full-condensed-medicine", fullCourseOverrides);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var video = Video("vid_crash_arabic_1", tagsCsv: "batch:crash-course-arabic-writing,language:ar");
        var result = await service.AllowAccessAsync("learner-1", video, default);

        Assert.False(result.Allowed);
        Assert.Equal("plan_excludes_video_tag", result.Reason);
    }

    [Fact]
    public async Task FullCourse_DeniesVideoTaggedWithFastTrackCrashCourseBatch()
    {
        await using var db = CreateDb();
        var fullCourseOverrides = """
            {
              "videos": {
                "excludeTags": [
                  "batch:crash-course-arabic-writing",
                  "batch:writing-sessions-crash-course-old",
                  "batch:fast-track-crash-course",
                  "batch:crash-course-workshops"
                ]
              }
            }
            """;
        SeedPlan(db, "learner-1", "full-condensed-medicine", fullCourseOverrides);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var video = Video("vid_fast_track_1", tagsCsv: "batch:fast-track-crash-course");
        var result = await service.AllowAccessAsync("learner-1", video, default);

        Assert.False(result.Allowed);
        Assert.Equal("plan_excludes_video_tag", result.Reason);
    }

    [Fact]
    public async Task FullCourse_AllowsOrdinaryDecemberFullCourseWritingVideo()
    {
        await using var db = CreateDb();
        var fullCourseOverrides = """
            {
              "videos": {
                "excludeTags": [
                  "batch:crash-course-arabic-writing",
                  "batch:writing-sessions-crash-course-old",
                  "batch:fast-track-crash-course",
                  "batch:crash-course-workshops"
                ]
              }
            }
            """;
        SeedPlan(db, "learner-1", "full-condensed-medicine", fullCourseOverrides);
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
    public async Task FullCourse_AllowsUntaggedWritingVideo()
    {
        await using var db = CreateDb();
        var fullCourseOverrides = """
            {
              "videos": {
                "excludeTags": [
                  "batch:crash-course-arabic-writing",
                  "batch:writing-sessions-crash-course-old",
                  "batch:fast-track-crash-course",
                  "batch:crash-course-workshops"
                ]
              }
            }
            """;
        SeedPlan(db, "learner-1", "full-condensed-medicine", fullCourseOverrides);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var video = Video("vid_untagged_1", tagsCsv: null);
        var result = await service.AllowAccessAsync("learner-1", video, default);

        Assert.True(result.Allowed);
    }

    [Fact]
    public async Task CrashCourse_AllowsVideoTaggedWithCrashCourseArabicWritingBatch()
    {
        await using var db = CreateDb();
        // Crash Course plan: video_library restricted to L/R/S only (existing rule from
        // 20260822090000), with the 18 crash-course videos carved back in via per-id include.
        // The new batch tags MUST NOT additionally exclude the video — the include still wins,
        // and no excludeTags entry points at these batches on the crash plan.
        var crashCourseOverrides = """
            {
              "videos": {
                "include": [
                  "vid_crash_arabic_1"
                ]
              }
            }
            """;
        SeedPlan(
            db,
            "learner-1",
            "crash-course",
            crashCourseOverrides,
            dashboardModulesJson: """["VideoLibrary"]""");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        // Manually restrict the plan to L/R/S only (matches the existing migration's
        // entitlements_json structure). Writing is excluded at the entitlement layer but
        // the explicit per-id include wins.
        var plan = await db.BillingPlans.FirstAsync(p => p.Code == "crash-course");
        plan.EntitlementsJson = """{"video_library":{"tier":"premium","subtests":["listening","reading","speaking"]}}""";
        await db.SaveChangesAsync();

        var video = Video("vid_crash_arabic_1", tagsCsv: "batch:crash-course-arabic-writing");
        var result = await service.AllowAccessAsync("learner-1", video, default);

        Assert.True(result.Allowed);
    }

    [Fact]
    public async Task FullCourse_ExplicitIncludeBeatsExcludeTag()
    {
        await using var db = CreateDb();
        // The previous 18-id include from migration 20260822090000 still wins even when
        // a tag-based exclude covers the same video. This keeps the legacy carve-out intact.
        var fullCourseOverrides = """
            {
              "videos": {
                "excludeTags": [
                  "batch:crash-course-arabic-writing"
                ],
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
