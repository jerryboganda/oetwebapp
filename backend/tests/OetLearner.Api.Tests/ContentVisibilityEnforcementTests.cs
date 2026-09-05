using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests;

/// <summary>
/// Hidden-denied proofs for the V2 straggler fixes (#197).
/// A Published-but-hidden paper must behave exactly like a missing one
/// on every learner path.
/// </summary>
public sealed class ContentVisibilityEnforcementTests
{
    private static LearnerDbContext NewInMemory()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static void SeedLearner(LearnerDbContext db, string userId)
    {
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new LearnerUser
        {
            Id = userId,
            AuthAccountId = $"{userId}-auth",
            DisplayName = "Hidden Gate Learner",
            Email = $"{userId}@example.test",
            Role = ApplicationUserRoles.Learner,
            AccountStatus = "active",
            CreatedAt = now,
            LastActiveAt = now,
        });
    }

    private static ContentPaper ListeningPaper(string id, bool visible) => new()
    {
        Id = id,
        SubtestCode = "listening",
        Title = id,
        Slug = id,
        AppliesToAllProfessions = true,
        Difficulty = "standard",
        EstimatedDurationMinutes = 45,
        Status = ContentStatus.Published,
        CandidateVisible = visible,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        PublishedAt = DateTimeOffset.UtcNow,
    };

    private sealed class AllowAllContentEntitlementService : IContentEntitlementService
    {
        public Task<ContentEntitlementResult> AllowAccessAsync(string? userId, ContentPaper paper, CancellationToken ct)
            => Task.FromResult(new ContentEntitlementResult(true, "test_allowed", null, null));

        public Task RequireAccessAsync(string? userId, ContentPaper paper, CancellationToken ct)
            => Task.CompletedTask;

        public bool IsAdmin(ClaimsPrincipal? principal) => false;
    }

    /// <summary>
    /// Resolve-level gate: hidden papers must fail at source resolution with
    /// the not-found code — not merely at a downstream access check — so the
    /// dozen other ResolveSourceAsync callers (results, resume, review) are
    /// protected too. (StartAttempt was already denied downstream by the V1
    /// gate; this pins the denial at the choke point.)
    /// </summary>
    [Fact]
    public async Task HiddenListeningPaper_StartAttempt_DeniesNotFound()
    {
        await using var db = NewInMemory();
        SeedLearner(db, "hidden-listener");
        db.ContentPapers.Add(ListeningPaper("hidden-listening-paper", visible: false));
        db.ListeningPolicies.Add(new ListeningPolicy
        {
            Id = "global",
            FullPaperTimerMinutes = 45,
            GracePeriodSeconds = 10,
        });
        await db.SaveChangesAsync();

        var svc = new ListeningLearnerService(db, new AllowAllContentEntitlementService());

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.StartAttemptAsync(
            "hidden-listener",
            "hidden-listening-paper",
            "practice",
            null,
            forceNewAttempt: true,
            CancellationToken.None));

        Assert.Equal("listening_paper_not_found", ex.Code);
    }

    [Fact]
    public async Task VisibleListeningPaper_StartAttempt_PassesVisibilityGate()
    {
        await using var db = NewInMemory();
        SeedLearner(db, "visible-listener");
        db.ContentPapers.Add(ListeningPaper("visible-listening-paper", visible: true));
        db.ListeningPolicies.Add(new ListeningPolicy
        {
            Id = "global",
            FullPaperTimerMinutes = 45,
            GracePeriodSeconds = 10,
        });
        await db.SaveChangesAsync();

        var svc = new ListeningLearnerService(db, new AllowAllContentEntitlementService());

        // Visible papers travel PAST source resolution; whatever the attempt
        // setup demands next (sound check, policy, questions) is not a
        // visibility denial.
        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.StartAttemptAsync(
            "visible-listener",
            "visible-listening-paper",
            "practice",
            null,
            forceNewAttempt: true,
            CancellationToken.None));

        Assert.NotEqual("listening_paper_not_found", ex.Code);
    }

    private static ClaimsPrincipal LearnerPrincipal(string userId)
        => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, ApplicationUserRoles.Learner),
            new Claim("profession", "medicine"),
        ]));

    private static string SeedPaperMedia(LearnerDbContext db, string mediaId, bool visible)
    {
        var now = DateTimeOffset.UtcNow;
        db.MediaAssets.Add(new MediaAsset
        {
            Id = mediaId,
            OriginalFilename = $"{mediaId}.jpg",
            MimeType = "image/jpeg",
            Format = "jpg",
            MediaKind = "image",
            SizeBytes = 4,
            StoragePath = $"media/{mediaId}.jpg",
            Status = MediaAssetStatus.Ready,
            UploadedBy = "admin-user",
            UploadedAt = now,
        });
        var paper = new ContentPaper
        {
            Id = $"paper-{mediaId}",
            SubtestCode = "listening",
            Title = $"Paper {mediaId}",
            Slug = $"paper-{mediaId}",
            AppliesToAllProfessions = true,
            Difficulty = "standard",
            EstimatedDurationMinutes = 60,
            Status = ContentStatus.Published,
            CandidateVisible = visible,
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        };
        db.ContentPapers.Add(paper);
        db.ContentPaperAssets.Add(new ContentPaperAsset
        {
            Id = $"paper-asset-{mediaId}",
            PaperId = paper.Id,
            Role = PaperAssetRole.QuestionPaper,
            MediaAssetId = mediaId,
            IsPrimary = true,
            CreatedAt = now,
        });
        return mediaId;
    }

    /// <summary>
    /// Classifier hardening (not a verdict flip): the enforcement query
    /// already denied hidden-paper media, so hidden→false holds before and
    /// after. The classifier fix skips needless entitlement/policy work for
    /// hidden attachments instead of discovering that at enforcement time.
    /// </summary>
    [Fact]
    public async Task HiddenPaperMedia_LearnerAccess_Denied()
    {
        await using var db = NewInMemory();
        const string mediaId = "hidden-paper-media";
        SeedPaperMedia(db, mediaId, visible: false);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var svc = new MediaAssetAccessService(
            db,
            new MediaPerformanceContentEntitlementService { Allowed = true },
            new MaterialAccessService(db, new MediaPerformanceEffectiveEntitlementResolver()),
            new MediaPerformanceVideoEntitlementService());

        Assert.False(await svc.CanAccessAsync(LearnerPrincipal("learner-1"), mediaId, CancellationToken.None));
    }

    [Fact]
    public async Task VisiblePaperMedia_LearnerAccess_Allowed()
    {
        await using var db = NewInMemory();
        const string mediaId = "visible-paper-media";
        SeedPaperMedia(db, mediaId, visible: true);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var svc = new MediaAssetAccessService(
            db,
            new MediaPerformanceContentEntitlementService { Allowed = true },
            new MaterialAccessService(db, new MediaPerformanceEffectiveEntitlementResolver()),
            new MediaPerformanceVideoEntitlementService());

        Assert.True(await svc.CanAccessAsync(LearnerPrincipal("learner-1"), mediaId, CancellationToken.None));
    }
}
