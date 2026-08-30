using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.VideoLibrary;

namespace OetLearner.Api.Tests;

/// <summary>
/// Video Library visibility-scope gate (spec: video-visibility-rules). Every non-admin
/// premium evaluation is decided by the single VideoEntitlementService.Evaluate check:
/// SHARED videos stay open to every entitled learner, FULL_*/CRASH videos require the
/// learner's package-derived scope set to contain the video's scope, and videos that
/// predate the column fall back to the legacy course-family tag gate.
/// </summary>
public sealed class VideoVisibilityScopeTests
{
    [Fact]
    public void SharedVideo_EmptyScopeSet_IsAllowed()
    {
        using var db = CreateDb();
        var service = new VideoEntitlementService(db, new EffectiveEntitlementResolver(db));

        var result = service.Evaluate(
            Ctx(),
            V("shared-lr", VideoVisibilityScopes.Shared));

        Assert.True(result.Allowed);
    }

    [Fact]
    public void SharedVideo_MedicineScopeSet_IsStillAllowed()
    {
        // SHARED is implicit for every Full/Crash learner: it is never stored in
        // PackageScopes, so the gate must not demand membership for it.
        using var db = CreateDb();
        var service = new VideoEntitlementService(db, new EffectiveEntitlementResolver(db));

        var result = service.Evaluate(
            Ctx(VideoVisibilityScopes.FullMedicine),
            V("shared-lr", VideoVisibilityScopes.Shared));

        Assert.True(result.Allowed);
    }

    [Fact]
    public void MedicineVideo_MedicineScopeSet_IsAllowedWithPlanGrantReason()
    {
        using var db = CreateDb();
        var service = new VideoEntitlementService(db, new EffectiveEntitlementResolver(db));

        var result = service.Evaluate(
            Ctx(VideoVisibilityScopes.FullMedicine),
            V("writing-med", VideoVisibilityScopes.FullMedicine));

        Assert.True(result.Allowed);
        Assert.Equal("plan_grants_video_library", result.Reason);
    }

    [Theory]
    [InlineData(VideoVisibilityScopes.FullNursing)]
    [InlineData(VideoVisibilityScopes.Crash)]
    public void MedicineVideo_ForeignScopeSet_IsDeniedWithScopeMismatch(string videoScope)
    {
        using var db = CreateDb();
        var service = new VideoEntitlementService(db, new EffectiveEntitlementResolver(db));

        var result = service.Evaluate(
            Ctx(VideoVisibilityScopes.FullMedicine),
            V("writing-other", videoScope));

        Assert.False(result.Allowed);
        Assert.Equal("visibility_scope_mismatch", result.Reason);
    }

    [Fact]
    public void MedicineVideo_EmptyScopeSet_IsDeniedWithScopeMismatch()
    {
        // An entitled learner whose packages grant no isolated scope (shared L/R only)
        // must not see profession-isolated Writing/Speaking videos.
        using var db = CreateDb();
        var service = new VideoEntitlementService(db, new EffectiveEntitlementResolver(db));

        var result = service.Evaluate(
            Ctx(),
            V("writing-med", VideoVisibilityScopes.FullMedicine));

        Assert.False(result.Allowed);
        Assert.Equal("visibility_scope_mismatch", result.Reason);
    }

    [Fact]
    public void CrashVideo_CrashScopeSet_IsAllowed()
    {
        using var db = CreateDb();
        var service = new VideoEntitlementService(db, new EffectiveEntitlementResolver(db));

        var result = service.Evaluate(
            Ctx(VideoVisibilityScopes.Crash),
            V("crash-writing", VideoVisibilityScopes.Crash));

        Assert.True(result.Allowed);
    }

    [Fact]
    public void MultiPackage_UnionSet_GrantsEachMemberScopeOnly()
    {
        // OQ-1: multiple effective packages union their scopes; the union grants
        // FULL_MEDICINE and CRASH but never a scope no package purchased.
        using var db = CreateDb();
        var service = new VideoEntitlementService(db, new EffectiveEntitlementResolver(db));
        var union = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            VideoVisibilityScopes.FullMedicine,
            VideoVisibilityScopes.Crash,
        };

        var medicine = service.Evaluate(Ctx(union.ToArray()), V("w-med", VideoVisibilityScopes.FullMedicine));
        var crash = service.Evaluate(Ctx(union.ToArray()), V("w-crash", VideoVisibilityScopes.Crash));
        var nursing = service.Evaluate(Ctx(union.ToArray()), V("w-nur", VideoVisibilityScopes.FullNursing));

        Assert.True(medicine.Allowed);
        Assert.True(crash.Allowed);
        Assert.False(nursing.Allowed);
        Assert.Equal("visibility_scope_mismatch", nursing.Reason);
    }

    [Fact]
    public void ScopeComparison_IsCaseInsensitiveOnBothSides()
    {
        // Scopes persist verbatim, but the gate compares OrdinalIgnoreCase so a
        // lower-cased legacy value or a mixed-case set entry cannot lock a learner out.
        using var db = CreateDb();
        var service = new VideoEntitlementService(db, new EffectiveEntitlementResolver(db));

        var result = service.Evaluate(
            Ctx(VideoVisibilityScopes.FullMedicine),
            V("w-med-lower", "full_medicine"));

        Assert.True(result.Allowed);
    }

    [Fact]
    public void ExplicitVideoInclude_BypassesScopeMismatch()
    {
        // Per-plan video include beats the scope gate (same precedence as subtest/
        // profession excludes) but never the subscription/module gate above it.
        using var db = CreateDb();
        var service = new VideoEntitlementService(db, new EffectiveEntitlementResolver(db));
        var context = Ctx(VideoVisibilityScopes.FullMedicine) with
        {
            VideoIncludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "w-nur-include" },
        };

        var result = service.Evaluate(
            context,
            V("w-nur-include", VideoVisibilityScopes.FullNursing));

        Assert.True(result.Allowed);
    }

    [Fact]
    public void NullScopeVideo_CrashTagFullOnlyPlan_FallsBackToLegacyFamilyGate()
    {
        // Pre-migration rows (VisibilityScope null) keep the legacy tag gate: a
        // batch:crash-course-only video on a Full-only plan is denied by family.
        using var db = CreateDb();
        var service = new VideoEntitlementService(db, new EffectiveEntitlementResolver(db));
        var context = Ctx() with { CourseFamilies = CourseFamilyAccess.FullOnly };
        var video = V("legacy-crash", scope: null, tagsCsv: CourseFamilyPolicy.CrashCourseOnlyTag);

        var result = service.Evaluate(context, video);

        Assert.False(result.Allowed);
        Assert.Equal("plan_excludes_course_family", result.Reason);
    }

    [Fact]
    public async Task RequireAccessAsync_ScopeMismatch_SurfacesAs404VideoNotFound()
    {
        // The learner-facing lookup path must not leak that a locked video exists:
        // visibility_scope_mismatch maps to the same 404 video_not_found as any denial.
        // Scopes arrive through the real resolver chain — the seeded full_course medicine
        // plan resolves PackageScopes = {FULL_MEDICINE}, which cannot unlock a
        // FULL_NURSING Writing video.
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.Professions.Add(new ProfessionReference
        {
            Id = "medicine",
            Code = "medicine",
            Label = "Medicine",
        });
        db.Users.Add(new LearnerUser
        {
            Id = "learner-1",
            Role = ApplicationUserRoles.Learner,
            DisplayName = "Dr. Test Medicine",
            Email = "learner-1@test.dev",
            ActiveProfessionId = "medicine",
            AccountStatus = "active",
            CreatedAt = now,
            LastActiveAt = now,
        });
        const string planCode = "full-condensed-medicine";
        db.BillingPlans.Add(new BillingPlan
        {
            Id = planCode,
            Code = planCode,
            Name = "Full Course Medicine",
            ProductCategory = "full_course",
            Profession = "medicine",
            DurationMonths = 6,
            AccessDurationDays = 180,
            DashboardModulesJson = """["Recalls","MaterialsLibrary","VideoLibrary"]""",
            IncludedSubtestsJson = "[]",
            EntitlementsJson = "{}",
            Status = BillingPlanStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = "learner-1",
            PlanId = planCode,
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-1),
            ChangedAt = now,
        });
        var video = V("w-nur-locked", VideoVisibilityScopes.FullNursing);
        db.LibraryVideos.Add(video);
        await db.SaveChangesAsync();

        var service = new VideoEntitlementService(db, new EffectiveEntitlementResolver(db));

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.RequireAccessAsync("learner-1", video, default));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("video_not_found", ex.ErrorCode);
    }

    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static VideoAccessContext Ctx(params string[] scopes)
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
            PackageScopes: scopes.Length == 0
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(scopes, StringComparer.OrdinalIgnoreCase));

    private static LibraryVideo V(string id, string? scope, string subtest = "writing", string? tagsCsv = null) => new()
    {
        Id = id,
        Title = id,
        AccessTier = "premium",
        SubtestCode = subtest,
        ProfessionIdsJson = "[\"medicine\"]",
        TagsCsv = tagsCsv ?? CourseFamilyPolicy.SharedTag,
        VisibilityScope = scope,
        Status = ContentStatus.Published,
    };
}

