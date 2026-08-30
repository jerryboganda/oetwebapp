using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Tests;

/// <summary>
/// End-to-end resolver wiring for visibility scopes (spec: video-visibility-rules):
/// the EffectiveEntitlementResolver must surface PackageScopePolicy output on
/// EffectiveEntitlementSnapshot.PackageScopes for the single-package and
/// multi-package aggregation paths, and leave it empty for non-course plans.
/// </summary>
public sealed class PackageScopeResolverTests
{
    [Fact]
    public async Task ResolveAsync_FullMedicinePlan_SurfacesFullMedicineScope()
    {
        await using var db = CreateDb();
        SeedCoursePlan(db, "plan-full-med", "full-medicine", "full_course", "medicine");
        SeedSubscription(db, "learner-med", "plan-full-med");
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-med", default);

        Assert.True(snapshot.HasEligibleSubscription);
        Assert.Equal(
            new[] { VideoVisibilityScopes.FullMedicine },
            snapshot.PackageScopes.ToArray());
    }

    [Fact]
    public async Task ResolveAsync_FullNursingPlan_SurfacesFullNursingScope()
    {
        await using var db = CreateDb();
        SeedCoursePlan(db, "plan-full-nur", "full-nursing", "full_course", "nursing");
        SeedSubscription(db, "learner-nur", "plan-full-nur");
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-nur", default);

        Assert.Equal(
            new[] { VideoVisibilityScopes.FullNursing },
            snapshot.PackageScopes.ToArray());
    }

    [Fact]
    public async Task ResolveAsync_FullPharmacyPlan_SurfacesFullPharmacyScope()
    {
        await using var db = CreateDb();
        SeedCoursePlan(db, "plan-full-phar", "full-pharmacy", "full_course", "pharmacy");
        SeedSubscription(db, "learner-phar", "plan-full-phar");
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-phar", default);

        Assert.Equal(
            new[] { VideoVisibilityScopes.FullPharmacy },
            snapshot.PackageScopes.ToArray());
    }

    [Fact]
    public async Task ResolveAsync_CrashCoursePlan_SurfacesCrashScope()
    {
        await using var db = CreateDb();
        SeedCoursePlan(db, "plan-crash", "crash-course", "crash_course", "all");
        SeedSubscription(db, "learner-crash", "plan-crash");
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-crash", default);

        Assert.Equal(
            new[] { VideoVisibilityScopes.Crash },
            snapshot.PackageScopes.ToArray());
    }

    [Fact]
    public async Task ResolveAsync_NonCourseFoundationPlan_HasEmptyScopeSet()
    {
        await using var db = CreateDb();
        // No eligibility module (Recalls/MaterialsLibrary/VideoLibrary/Mocks or per-skill keys)
        // → the plan is not course-eligible, so no scope set is derived.
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "plan-foundation",
            Code = "foundation-1m",
            Name = "Foundation",
            ProductCategory = "foundation",
            Profession = "medicine",
            DashboardModulesJson = """["BasicEnglish"]""",
        });
        SeedSubscription(db, "learner-foundation", "plan-foundation");
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-foundation", default);

        Assert.Empty(snapshot.PackageScopes);
    }

    [Fact]
    public async Task ResolveAsync_MultiplePackages_UnionScopesAcrossPlans()
    {
        await using var db = CreateDb();
        SeedCoursePlan(db, "plan-full-med", "full-medicine", "full_course", "medicine");
        SeedCoursePlan(db, "plan-writing-crash", "writing-crash-2", "writing_crash", "all");
        SeedSubscription(db, "learner-multi", "plan-full-med");
        SeedSubscription(db, "learner-multi", "plan-writing-crash");
        await db.SaveChangesAsync();

        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-multi", default);

        Assert.True(snapshot.HasEligibleSubscription);
        Assert.Equal(2, snapshot.PackageScopes.Count);
        Assert.Contains(VideoVisibilityScopes.FullMedicine, snapshot.PackageScopes);
        Assert.Contains(VideoVisibilityScopes.Crash, snapshot.PackageScopes);
    }

    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static void SeedCoursePlan(
        LearnerDbContext db,
        string planId,
        string code,
        string productCategory,
        string profession)
    {
        db.BillingPlans.Add(new BillingPlan
        {
            Id = planId,
            Code = code,
            Name = code,
            ProductCategory = productCategory,
            Profession = profession,
            DashboardModulesJson = """["Recalls","MaterialsLibrary","VideoLibrary"]""",
        });
    }

    private static void SeedSubscription(LearnerDbContext db, string userId, string planId)
    {
        var now = DateTimeOffset.UtcNow;
        db.Subscriptions.Add(new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = userId,
            PlanId = planId,
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-5),
            ChangedAt = now.AddDays(-5),
            ExpiresAt = now.AddDays(180),
        });
    }
}
