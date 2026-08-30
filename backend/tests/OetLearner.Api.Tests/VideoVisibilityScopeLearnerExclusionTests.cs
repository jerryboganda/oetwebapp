using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.VideoLibrary;

namespace OetLearner.Api.Tests;

/// <summary>
/// Learner-surface exclusion (spec §6): a medicine Full Course learner sees their own
/// FULL_MEDICINE Writing videos plus every SHARED one, while FULL_NURSING / CRASH
/// videos are invisible (null → endpoint 404) through the real resolver chain.
/// </summary>
public sealed class VideoVisibilityScopeLearnerExclusionTests
{
    [Fact]
    public async Task FindVisibleVideoAsync_FullMedicineScope_IsVisibleToMedicineLearner()
    {
        var (db, service, video) = await SeedAsync(VideoVisibilityScopes.FullMedicine);
        await using (db)
        {
            var found = await service.FindVisibleVideoAsync("learner-1", video.Id, DateTimeOffset.UtcNow, default);
            Assert.NotNull(found);
        }
    }

    [Fact]
    public async Task FindVisibleVideoAsync_SharedScope_IsVisibleToMedicineLearner()
    {
        var (db, service, video) = await SeedAsync(VideoVisibilityScopes.Shared);
        await using (db)
        {
            var found = await service.FindVisibleVideoAsync("learner-1", video.Id, DateTimeOffset.UtcNow, default);
            Assert.NotNull(found);
        }
    }

    [Fact]
    public async Task FindVisibleVideoAsync_FullNursingScope_IsInvisibleToMedicineLearner()
    {
        var (db, service, video) = await SeedAsync(VideoVisibilityScopes.FullNursing);
        await using (db)
        {
            var found = await service.FindVisibleVideoAsync("learner-1", video.Id, DateTimeOffset.UtcNow, default);
            Assert.Null(found);
        }
    }

    [Fact]
    public async Task FindVisibleVideoAsync_CrashScope_IsInvisibleToMedicineLearner()
    {
        var (db, service, video) = await SeedAsync(VideoVisibilityScopes.Crash);
        await using (db)
        {
            var found = await service.FindVisibleVideoAsync("learner-1", video.Id, DateTimeOffset.UtcNow, default);
            Assert.Null(found);
        }
    }

    /// <summary>Seeds a medicine Full Course learner (Professions + user + plan + sub)
    /// exactly as the resolver chain requires, plus one published Writing video carrying
    /// the scope under test. Language stays null so profession visibility falls back to
    /// the ProfessionIdsJson check (no CourseContentMatrix coupling).</summary>
    private static async Task<(LearnerDbContext Db, VideoLibraryLearnerService Service, LibraryVideo Video)> SeedAsync(
        string scope)
    {
        var db = new LearnerDbContext(
            new DbContextOptionsBuilder<LearnerDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
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
        var video = new LibraryVideo
        {
            Id = $"video-{scope.ToLowerInvariant()}",
            Title = "Writing video",
            AccessTier = "premium",
            SubtestCode = "writing",
            ProfessionIdsJson = """["medicine"]""",
            TagsCsv = CourseFamilyPolicy.SharedTag,
            VisibilityScope = scope,
            Status = ContentStatus.Published,
        };
        db.LibraryVideos.Add(video);
        await db.SaveChangesAsync();

        var entitlements = new VideoEntitlementService(db, new EffectiveEntitlementResolver(db));
        // FindVisibleVideoAsync never touches Bunny settings — null provider is safe.
        var service = new VideoLibraryLearnerService(db, entitlements, null!);
        return (db, service, video);
    }
}
