using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.VideoLibrary;

namespace OetLearner.Api.Tests;

/// <summary>
/// Mutual Full Course ↔ Crash Course visibility. Package ProductCategory
/// (catalog) decides the course family automatically. Course family is
/// tag-only (batch:*); unclassified premium videos are invisible to every
/// learner (deny-by-default, plan_excludes_course_family) and an explicit
/// per-plan video include never beats the family gate. Shared content stays
/// visible on both families.
/// </summary>
public class CourseFamilyEntitlementTests
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
        string title = "Lesson",
        string? tagsCsv = null,
        string subtestCode = "writing",
        string accessTier = "premium",
        string professionIdsJson = "[\"medicine\"]") => new()
    {
        Id = id,
        Title = title,
        AccessTier = accessTier,
        SubtestCode = subtestCode,
        TagsCsv = tagsCsv,
        Status = ContentStatus.Published,
        DurationSeconds = 600,
        ProfessionIdsJson = professionIdsJson,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static void SeedPlan(
        LearnerDbContext db,
        string userId,
        string planCode,
        string productCategory,
        string dashboardModulesJson = """["VideoLibrary"]""",
        bool tutorBookUnlocked = false,
        int writingAssessmentsRemaining = 0,
        string? professionId = "medicine")
    {
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new LearnerUser
        {
            Id = userId,
            Email = $"{userId}@test.dev",
            DisplayName = userId,
            Role = ApplicationUserRoles.Learner,
            ActiveProfessionId = professionId,
            CreatedAt = now,
            LastActiveAt = now,
        });
        db.BillingPlans.Add(new BillingPlan
        {
            Id = planCode,
            Code = planCode,
            Name = planCode,
            EntitlementsJson = "{}",
            DashboardModulesJson = dashboardModulesJson,
            ProductCategory = productCategory,
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = userId,
            PlanId = planCode,
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-1),
            ChangedAt = now,
            TutorBookUnlocked = tutorBookUnlocked,
            WritingAssessmentsRemaining = writingAssessmentsRemaining,
        });
    }

    [Theory]
    [InlineData("full_course", "full-condensed-medicine", true, false)]
    [InlineData("full_course_bundle", "full-condensed-medicine-tbook", true, false)]
    [InlineData("crash_course", "crash-course", false, true)]
    [InlineData("crash_course_bundle", "crash-3letters", false, true)]
    [InlineData("crash_course_bundle", "crash-5letters", false, true)]
    [InlineData("writing_crash", "writing-crash", false, true)]
    [InlineData("combo_double", "double-special", true, false)]
    public void Resolve_UsesProductCategoryNotDisplayName(
        string category, string planCode, bool full, bool crash)
    {
        var access = CourseFamilyPolicy.Resolve(category, planCode);
        Assert.Equal(full, access.FullCourse);
        Assert.Equal(crash, access.CrashCourse);
        Assert.True(access.IsRestricted);
    }

    [Fact]
    public void ClassifyLabel_FullCrashCourseTitle_IsCrashNotFull()
    {
        Assert.Equal(CourseFamily.CrashCourse, CourseFamilyPolicy.ClassifyLabel("Full Crash Course - General OET"));
        Assert.Equal(
            CourseFamily.CrashCourse,
            CourseFamilyPolicy.ClassifyLabel("Arabic / New Medicine Crash Course / Sessions / Day 1"));
        Assert.Equal(CourseFamily.FullCourse, CourseFamilyPolicy.ClassifyLabel("Writing / Full Course / December"));
        Assert.Equal(CourseFamily.Shared, CourseFamilyPolicy.ClassifyLabel("Listening / Benchmark"));
    }

    [Fact]
    public async Task FullCourse_ReturnsFullContent_HidesCrashCollectionVideo()
    {
        await using var db = CreateDb();
        SeedPlan(db, "learner-1", "full-condensed-medicine", "full_course");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var fullVideo = Video("vid-full", tagsCsv: CourseFamilyPolicy.FullCourseOnlyTag);
        var crashVideo = Video("vid-crash", tagsCsv: CourseFamilyPolicy.CrashCourseOnlyTags.First());
        var sharedVideo = Video("vid-shared", tagsCsv: CourseFamilyPolicy.SharedTag, subtestCode: "listening");

        var full = await service.AllowAccessAsync("learner-1", fullVideo, default);
        var crash = await service.AllowAccessAsync("learner-1", crashVideo, default);
        var shared = await service.AllowAccessAsync("learner-1", sharedVideo, default);

        Assert.True(full.Allowed);
        Assert.Equal("plan_grants_video_library", full.Reason);
        Assert.False(crash.Allowed);
        Assert.Equal("plan_excludes_course_family", crash.Reason);
        Assert.True(shared.Allowed);
    }

    [Fact]
    public async Task FullCourse_ProfessionFilterStillApplies()
    {
        await using var db = CreateDb();
        SeedPlan(db, "learner-1", "full-nursing", "full_course", professionId: "nursing");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var medicineOnly = Video(
            "vid-med",
            tagsCsv: CourseFamilyPolicy.FullCourseOnlyTag,
            professionIdsJson: "[\"medicine\"]");
        var result = await service.AllowAccessAsync("learner-1", medicineOnly, default);

        Assert.False(result.Allowed);
        Assert.Equal("profession_mismatch", result.Reason);
    }

    [Fact]
    public async Task FullCoursePlusTutorBook_HidesCrash_KeepsTutorBookFlag()
    {
        await using var db = CreateDb();
        SeedPlan(
            db,
            "learner-1",
            "full-condensed-medicine-tbook",
            "full_course_bundle",
            tutorBookUnlocked: true);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var entitlement = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-1", default);

        Assert.True(entitlement.TutorBookUnlocked);
        Assert.Equal(CourseFamilyAccess.FullOnly, entitlement.CourseFamilies);

        var crash = await service.AllowAccessAsync(
            "learner-1",
            Video("vid-crash", tagsCsv: "batch:crash-course-workshops"),
            default);
        Assert.False(crash.Allowed);
        Assert.Equal("plan_excludes_course_family", crash.Reason);
    }

    [Theory]
    [InlineData("crash-3letters", "crash_course_bundle", 3)]
    [InlineData("crash-5letters", "crash_course_bundle", 5)]
    public async Task CrashCourseLetters_SeesCrash_HidesFullOnly(
        string planCode, string category, int letters)
    {
        await using var db = CreateDb();
        SeedPlan(db, "learner-1", planCode, category, writingAssessmentsRemaining: letters);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var entitlement = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-1", default);

        Assert.Equal(letters, entitlement.WritingAssessmentsRemaining);
        Assert.Equal(CourseFamilyAccess.CrashOnly, entitlement.CourseFamilies);

        var crash = await service.AllowAccessAsync(
            "learner-1",
            Video("vid-crash", tagsCsv: "batch:crash-course-arabic-writing"),
            default);
        var fullOnly = await service.AllowAccessAsync(
            "learner-1",
            Video("vid-full", tagsCsv: CourseFamilyPolicy.FullCourseOnlyTag),
            default);

        Assert.True(crash.Allowed);
        Assert.False(fullOnly.Allowed);
        Assert.Equal("plan_excludes_course_family", fullOnly.Reason);
    }

    [Fact]
    public async Task PackageTransition_FullToCrash_FlipsVisibilityAutomatically()
    {
        await using var db = CreateDb();
        SeedPlan(db, "learner-1", "full-condensed-medicine", "full_course");
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var crashVideo = Video("vid-crash", tagsCsv: CourseFamilyPolicy.CrashCourseOnlyTags.First());
        var fullVideo = Video("vid-full", tagsCsv: CourseFamilyPolicy.FullCourseOnlyTag);

        Assert.False((await service.AllowAccessAsync("learner-1", crashVideo, default)).Allowed);
        Assert.True((await service.AllowAccessAsync("learner-1", fullVideo, default)).Allowed);

        var sub = await db.Subscriptions.FirstAsync(s => s.UserId == "learner-1");
        sub.Status = SubscriptionStatus.Cancelled;
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "crash-course",
            Code = "crash-course",
            Name = "crash-course",
            EntitlementsJson = "{}",
            DashboardModulesJson = """["VideoLibrary"]""",
            ProductCategory = "crash_course",
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = "learner-1",
            PlanId = "crash-course",
            Status = SubscriptionStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            ChangedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        Assert.True((await service.AllowAccessAsync("learner-1", crashVideo, default)).Allowed);
        var after = await service.AllowAccessAsync("learner-1", fullVideo, default);
        Assert.False(after.Allowed);
        Assert.Equal("plan_excludes_course_family", after.Reason);
    }

    [Fact]
    public async Task PackageTransition_CrashToFull_FlipsVisibilityAutomatically()
    {
        await using var db = CreateDb();
        SeedPlan(db, "learner-1", "crash-course", "crash_course");
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var crashVideo = Video("vid-crash", tagsCsv: CourseFamilyPolicy.CrashCourseOnlyTags.First());
        var fullVideo = Video("vid-full", tagsCsv: CourseFamilyPolicy.FullCourseOnlyTag);

        Assert.True((await service.AllowAccessAsync("learner-1", crashVideo, default)).Allowed);
        Assert.False((await service.AllowAccessAsync("learner-1", fullVideo, default)).Allowed);

        var sub = await db.Subscriptions.FirstAsync(s => s.UserId == "learner-1");
        sub.Status = SubscriptionStatus.Cancelled;
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "full-nursing",
            Code = "full-nursing",
            Name = "full-nursing",
            EntitlementsJson = "{}",
            DashboardModulesJson = """["VideoLibrary"]""",
            ProductCategory = "full_course",
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = "learner-1",
            PlanId = "full-nursing",
            Status = SubscriptionStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            ChangedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        Assert.False((await service.AllowAccessAsync("learner-1", crashVideo, default)).Allowed);
        Assert.True((await service.AllowAccessAsync("learner-1", fullVideo, default)).Allowed);
    }

    [Fact]
    public async Task ExplicitInclude_DoesNotBeatCourseFamily()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new LearnerUser
        {
            Id = "learner-1",
            Email = "learner-1@test.dev",
            DisplayName = "learner",
            Role = ApplicationUserRoles.Learner,
            ActiveProfessionId = "medicine",
            CreatedAt = now,
            LastActiveAt = now,
        });
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "full-condensed-medicine",
            Code = "full-condensed-medicine",
            Name = "full-condensed-medicine",
            EntitlementsJson = "{}",
            DashboardModulesJson = """["VideoLibrary"]""",
            ProductCategory = "full_course",
            ContentOverridesJson = """{"videos":{"include":["vid-crash-carved"]}}""",
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = "learner-1",
            PlanId = "full-condensed-medicine",
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-1),
            ChangedAt = now,
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        // Even an explicit per-plan include cannot resurrect an out-of-family video.
        var result = await service.AllowAccessAsync(
            "learner-1",
            Video("vid-crash-carved", tagsCsv: CourseFamilyPolicy.CrashCourseOnlyTag, title: "Crash Course / Day 1"),
            default);
        Assert.False(result.Allowed);
        Assert.Equal("plan_excludes_course_family", result.Reason);
    }

    [Fact]
    public async Task CrashFolder_HiddenFromFullCourseMaterialsTree()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        const string userId = "learner-1";
        db.Users.Add(new LearnerUser
        {
            Id = userId,
            Email = "learner-1@test.dev",
            DisplayName = "learner",
            Role = ApplicationUserRoles.Learner,
            ActiveProfessionId = "medicine",
            CreatedAt = now,
            LastActiveAt = now,
        });
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "full-condensed-medicine",
            Code = "full-condensed-medicine",
            Name = "full-condensed-medicine",
            EntitlementsJson = "{}",
            DashboardModulesJson = """["MaterialsLibrary"]""",
            ProductCategory = "full_course",
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = userId,
            PlanId = "full-condensed-medicine",
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-1),
            ChangedAt = now,
        });
        db.MaterialFolders.AddRange(
            new MaterialFolder
            {
                Id = "f-shared",
                Name = "Listening",
                AudienceMode = MaterialAudienceMode.Everyone,
                Status = ContentStatus.Published,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new MaterialFolder
            {
                Id = "f-crash",
                Name = "Crash Course",
                AudienceMode = MaterialAudienceMode.Everyone,
                Status = ContentStatus.Published,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new MaterialFolder
            {
                Id = "f-crash-day1",
                Name = "Day 1",
                ParentFolderId = "f-crash",
                AudienceMode = MaterialAudienceMode.Inherit,
                Status = ContentStatus.Published,
                CreatedAt = now,
                UpdatedAt = now,
            });
        await db.SaveChangesAsync();

        var tree = await new MaterialAccessService(db, new EffectiveEntitlementResolver(db))
            .GetVisibleTreeAsync(
                new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity(
                        [
                            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId),
                            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, ApplicationUserRoles.Learner),
                        ],
                        "test")),
                default);

        var json = System.Text.Json.JsonSerializer.Serialize(tree);
        Assert.Contains("Listening", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Crash Course", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Day 1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Union_FullPlusCrash_IsUnrestricted()
    {
        var union = CourseFamilyPolicy.Union(
            [CourseFamilyAccess.FullOnly, CourseFamilyAccess.CrashOnly]);
        Assert.False(union.IsRestricted);
        Assert.True(union.Allows(CourseFamily.FullCourse));
        Assert.True(union.Allows(CourseFamily.CrashCourse));
    }

    [Fact]
    public async Task DirectAccess_OppositeFamily_IsInvisibleNotPremium()
    {
        await using var db = CreateDb();
        SeedPlan(db, "learner-1", "full-condensed-medicine", "full_course");
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var crashVideo = Video("vid-crash", tagsCsv: CourseFamilyPolicy.CrashCourseOnlyTags.First());

        var allow = await service.AllowAccessAsync("learner-1", crashVideo, default);
        Assert.False(allow.Allowed);
        Assert.Equal("plan_excludes_course_family", allow.Reason);

        var ex = await Assert.ThrowsAsync<OetLearner.Api.Services.ApiException>(
            () => service.RequireAccessAsync("learner-1", crashVideo, default));
        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("video_not_found", ex.ErrorCode);
    }

    [Fact]
    public void Evaluate_TagsOnly_IgnoresCollectionTitle()
    {
        using var db = CreateDb();
        var service = CreateService(db);
        var context = new VideoAccessContext(
            IsAdmin: false,
            Authenticated: true,
            HasEligibleSubscription: true,
            Frozen: false,
            Expired: false,
            PlanGrantsPremium: true,
            AddOnGrantsPremium: false,
            CurrentTier: "premium",
            ModuleEnabled: true,
            AllSubtestsGranted: true,
            ProfessionId: "medicine",
            CourseFamilies: CourseFamilyAccess.FullOnly);

        // Crash-tagged video is denied regardless of collection title.
        var tagged = Video("vid-tagged", tagsCsv: CourseFamilyPolicy.CrashCourseOnlyTag);
        Assert.False(service.Evaluate(context, tagged, ["Arabic / New Medicine Crash Course / Sessions"]).Allowed);
        Assert.False(service.Evaluate(context, tagged, extraLabels: null).Allowed);

        // Untagged premium is deny-by-default — crash labels/titles are ignored.
        var untagged = Video("vid-untagged", title: "Day 1 Session");
        var deniedWithLabels = service.Evaluate(context, untagged, ["Arabic / New Medicine Crash Course / Sessions"]);
        Assert.False(deniedWithLabels.Allowed);
        Assert.Equal("plan_excludes_course_family", deniedWithLabels.Reason);
        var deniedWithoutLabels = service.Evaluate(context, untagged, extraLabels: null);
        Assert.False(deniedWithoutLabels.Allowed);
        Assert.Equal("plan_excludes_course_family", deniedWithoutLabels.Reason);

        // Shared-tagged video stays visible even when collection title looks like crash.
        var shared = Video("vid-shared", tagsCsv: CourseFamilyPolicy.SharedTag);
        Assert.True(service.Evaluate(context, shared, ["Arabic / New Medicine Crash Course / Sessions"]).Allowed);
        Assert.True(service.Evaluate(context, shared, extraLabels: null).Allowed);
    }

    [Fact]
    public async Task UnclassifiedPremium_IsDeniedEvenForUnrestricted()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new LearnerUser
        {
            Id = "learner-1",
            Email = "learner-1@test.dev",
            DisplayName = "learner",
            Role = ApplicationUserRoles.Learner,
            ActiveProfessionId = "medicine",
            CreatedAt = now,
            LastActiveAt = now,
        });
        // Custom/legacy category → Unrestricted course-family access — still deny-by-default.
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "custom-plan",
            Code = "custom-plan",
            Name = "custom-plan",
            EntitlementsJson = "{}",
            DashboardModulesJson = """["VideoLibrary"]""",
            ProductCategory = "custom",
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = "learner-1",
            PlanId = "custom-plan",
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-1),
            ChangedAt = now,
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var entitlement = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-1", default);
        Assert.False(entitlement.CourseFamilies.IsRestricted);

        var untagged = await service.AllowAccessAsync(
            "learner-1",
            Video("vid-untagged", tagsCsv: null),
            default);
        Assert.False(untagged.Allowed);
        Assert.Equal("plan_excludes_course_family", untagged.Reason);

        var shared = await service.AllowAccessAsync(
            "learner-1",
            Video("vid-shared", tagsCsv: CourseFamilyPolicy.SharedTag),
            default);
        Assert.True(shared.Allowed);
    }

    [Fact]
    public void ClassifyVideo_IsTagOnlyDenyByDefault()
    {
        Assert.Equal(
            CourseFamily.CrashCourse,
            CourseFamilyPolicy.ClassifyVideo(Video("v", tagsCsv: CourseFamilyPolicy.CrashCourseOnlyTag)));
        Assert.Equal(
            CourseFamily.FullCourse,
            CourseFamilyPolicy.ClassifyVideo(Video("v", tagsCsv: CourseFamilyPolicy.FullCourseOnlyTag)));
        Assert.Equal(
            CourseFamily.Shared,
            CourseFamilyPolicy.ClassifyVideo(Video("v", tagsCsv: CourseFamilyPolicy.SharedTag)));
        Assert.Equal(
            CourseFamily.Shared,
            CourseFamilyPolicy.ClassifyVideo(
                Video("v", tagsCsv: $"{CourseFamilyPolicy.FullCourseOnlyTag}, {CourseFamilyPolicy.CrashCourseOnlyTag}")));
        // Titles/labels are ignored — only batch:* tags classify.
        Assert.Equal(
            CourseFamily.None,
            CourseFamilyPolicy.ClassifyVideo(Video("v", title: "Crash Course / Day 1"), ["Arabic / New Medicine Crash Course"]));
        Assert.Equal(CourseFamily.None, CourseFamilyPolicy.ClassifyVideo(Video("v")));
        Assert.Equal(
            CourseFamily.None,
            CourseFamilyPolicy.ClassifyVideo(Video("v", title: "Day 1"), ["Full Crash Course - General OET"]));
        Assert.Equal(
            CourseFamily.None,
            CourseFamilyPolicy.ClassifyVideo(Video("v", title: "Day 1 Session"), ["Arabic / New Medicine Crash Course / Sessions"]));
    }

    [Fact]
    public void Allows_None_IsAlwaysDenied()
    {
        Assert.False(CourseFamilyAccess.Unrestricted.Allows(CourseFamily.None));
        Assert.False(CourseFamilyAccess.FullOnly.Allows(CourseFamily.None));
        Assert.False(CourseFamilyAccess.CrashOnly.Allows(CourseFamily.None));
        Assert.False(CourseFamilyPolicy.Allows(CourseFamilyAccess.Unrestricted, CourseFamily.None));
    }
}
