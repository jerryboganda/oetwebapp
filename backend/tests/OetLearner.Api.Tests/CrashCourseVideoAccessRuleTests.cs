using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.VideoLibrary;

namespace OetLearner.Api.Tests;

public sealed class CrashCourseVideoAccessRuleTests
{
    [Fact]
    public void FullCourse_ExcludedCrashWritingVideo_IsDeniedByPlanMapping()
    {
        using var db = CreateDb();
        var service = new VideoEntitlementService(db, new EffectiveEntitlementResolver(db));

        var result = service.Evaluate(
            FullCourseContext(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "crash-writing" }),
            FullCourseVideo("crash-writing"));

        Assert.False(result.Allowed);
        Assert.Equal("plan_excludes_video", result.Reason);
    }

    [Fact]
    public void FullCourse_NormalWritingVideo_RemainsAllowed()
    {
        using var db = CreateDb();
        var service = new VideoEntitlementService(db, new EffectiveEntitlementResolver(db));

        var result = service.Evaluate(FullCourseContext(), FullCourseVideo("december-writing"));

        Assert.True(result.Allowed);
        Assert.Equal("plan_grants_video_library", result.Reason);
    }

    [Fact]
    public void CrashCourse_ExplicitCrashWritingInclude_IsAllowed()
    {
        using var db = CreateDb();
        var service = new VideoEntitlementService(db, new EffectiveEntitlementResolver(db));

        var result = service.Evaluate(CrashCourseContext("crash-writing"), CrashVideo("crash-writing"));

        Assert.True(result.Allowed);
        Assert.Equal("plan_grants_video_library", result.Reason);
    }

    [Fact]
    public void CrashCourse_NonIncludedWritingVideo_IsDeniedBySubtestScope()
    {
        using var db = CreateDb();
        var service = new VideoEntitlementService(db, new EffectiveEntitlementResolver(db));

        var result = service.Evaluate(CrashCourseContext("crash-writing"), CrashVideo("february-writing"));

        Assert.False(result.Allowed);
        Assert.Equal("plan_does_not_grant_subtest", result.Reason);
    }

    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static LibraryVideo FullCourseVideo(string id) => new()
    {
        Id = id,
        Title = id,
        AccessTier = "premium",
        SubtestCode = "writing",
        ProfessionIdsJson = "[\"medicine\"]",
        TagsCsv = "batch:full-course-only",
        Status = ContentStatus.Published,
    };

    private static LibraryVideo CrashVideo(string id) => new()
    {
        Id = id,
        Title = id,
        AccessTier = "premium",
        SubtestCode = "writing",
        ProfessionIdsJson = "[\"medicine\"]",
        TagsCsv = "batch:crash-course-only",
        Status = ContentStatus.Published,
    };

    private static VideoAccessContext FullCourseContext(IReadOnlySet<string>? excludes = null)
        => new(
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
            VideoExcludes: excludes);

    private static VideoAccessContext CrashCourseContext(string includedVideoId)
        => new(
            IsAdmin: false,
            Authenticated: true,
            HasEligibleSubscription: true,
            Frozen: false,
            Expired: false,
            PlanGrantsPremium: true,
            AddOnGrantsPremium: false,
            CurrentTier: "premium",
            ModuleEnabled: true,
            AllSubtestsGranted: false,
            GrantedSubtests: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "listening", "reading", "speaking",
            },
            ProfessionId: "medicine",
            VideoIncludes: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { includedVideoId });
}
