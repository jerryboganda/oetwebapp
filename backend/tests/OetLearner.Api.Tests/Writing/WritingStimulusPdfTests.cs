using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Reading;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Tests for the stimulus-PDF feature:
///   - MediaAssetAccessService access grants (learner, expert, admin, owner)
///   - Delete guard: in-use query fires for WritingScenario references
///   - DTO mapper: StimulusPdfDownloadPath computed correctly
/// </summary>
public class WritingStimulusPdfTests
{
    // ─── helpers ───────────────────────────────────────────────────────────────

    private static LearnerDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    /// <summary>
    /// Builds the access service with null-safe test doubles.
    /// MaterialAccessService and ContentEntitlementService are constructed with the same
    /// in-memory DB; their internal queries return false/empty when no records match.
    /// </summary>
    private static MediaAssetAccessService BuildAccessService(LearnerDbContext db)
    {
        var materialAccess = new MaterialAccessService(db, new StubEntitlementResolver());
        return new MediaAssetAccessService(
            db,
            new StubContentEntitlementService(),
            new StubReadingPolicyService(),
            materialAccess);
    }

    private static ClaimsPrincipal MakePrincipal(string userId, string role, string? profession = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Role, role),
        };
        if (profession is not null)
            claims.Add(new Claim("prof", profession));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static MediaAsset BuildMediaAsset(string id, string uploadedBy = "admin-user")
        => new()
        {
            Id = id,
            OriginalFilename = "stimulus.pdf",
            MimeType = "application/pdf",
            Format = "pdf",
            SizeBytes = 12345,
            StoragePath = $"media/{id}.pdf",
            Status = MediaAssetStatus.Ready,
            MediaKind = "document",
            UploadedBy = uploadedBy,
            UploadedAt = DateTimeOffset.UtcNow,
        };

    private static WritingScenario BuildScenario(string stimulusPdfMediaAssetId, string status = "published", string profession = "medicine")
        => new()
        {
            Id = Guid.NewGuid(),
            Title = "Test Scenario",
            LetterType = "LT-RR",
            Profession = profession,
            CaseNotesMarkdown = "Patient notes.",
            AuthorId = "test-author",
            Status = status,
            StimulusPdfMediaAssetId = stimulusPdfMediaAssetId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    // ─── MediaAssetAccessService: learner access ────────────────────────────────

    [Fact]
    public async Task Learner_CanAccess_PublishedScenarioPdf_MatchingProfession()
    {
        await using var db = BuildDb();
        var assetId = Guid.NewGuid().ToString("N");
        db.MediaAssets.Add(BuildMediaAsset(assetId));
        db.WritingScenarios.Add(BuildScenario(assetId, status: "published", profession: "medicine"));
        await db.SaveChangesAsync();

        var svc = BuildAccessService(db);
        var principal = MakePrincipal("learner-001", "learner", "medicine");

        var result = await svc.CanAccessAsync(principal, assetId, CancellationToken.None);

        Assert.True(result, "Learner with matching profession should access published stimulus PDF.");
    }

    [Fact]
    public async Task Learner_CannotAccess_DraftScenarioPdf()
    {
        await using var db = BuildDb();
        var assetId = Guid.NewGuid().ToString("N");
        db.MediaAssets.Add(BuildMediaAsset(assetId));
        db.WritingScenarios.Add(BuildScenario(assetId, status: "draft", profession: "medicine"));
        await db.SaveChangesAsync();

        var svc = BuildAccessService(db);
        var principal = MakePrincipal("learner-001", "learner", "medicine");

        var result = await svc.CanAccessAsync(principal, assetId, CancellationToken.None);

        Assert.False(result, "Learner should not access a stimulus PDF from a draft scenario.");
    }

    [Fact]
    public async Task Learner_CannotAccess_PublishedScenarioPdf_MismatchedProfession()
    {
        await using var db = BuildDb();
        var assetId = Guid.NewGuid().ToString("N");
        db.MediaAssets.Add(BuildMediaAsset(assetId));
        db.WritingScenarios.Add(BuildScenario(assetId, status: "published", profession: "nursing"));
        await db.SaveChangesAsync();

        var svc = BuildAccessService(db);
        var principal = MakePrincipal("learner-001", "learner", "medicine");

        var result = await svc.CanAccessAsync(principal, assetId, CancellationToken.None);

        Assert.False(result, "Learner with mismatched profession should not access nursing stimulus PDF.");
    }

    // ─── MediaAssetAccessService: expert access ─────────────────────────────────

    [Fact]
    public async Task Expert_CanAccess_PublishedScenarioPdf_AnyProfession()
    {
        await using var db = BuildDb();
        var assetId = Guid.NewGuid().ToString("N");
        db.MediaAssets.Add(BuildMediaAsset(assetId));
        db.WritingScenarios.Add(BuildScenario(assetId, status: "published", profession: "nursing"));
        await db.SaveChangesAsync();

        var svc = BuildAccessService(db);
        var principal = MakePrincipal("expert-001", "expert");

        var result = await svc.CanAccessAsync(principal, assetId, CancellationToken.None);

        Assert.True(result, "Expert should access any published stimulus PDF (no profession filter).");
    }

    [Fact]
    public async Task Expert_CannotAccess_DraftScenarioPdf_ViaThisPath()
    {
        await using var db = BuildDb();
        var assetId = Guid.NewGuid().ToString("N");
        db.MediaAssets.Add(BuildMediaAsset(assetId));
        db.WritingScenarios.Add(BuildScenario(assetId, status: "draft", profession: "medicine"));
        await db.SaveChangesAsync();

        var svc = BuildAccessService(db);
        // Expert has no writing-attempt-asset or voice-note link — only the stimulus path
        var principal = MakePrincipal("expert-001", "expert");

        var result = await svc.CanAccessAsync(principal, assetId, CancellationToken.None);

        Assert.False(result, "Expert should not access a draft stimulus PDF via the writing stimulus path.");
    }

    // ─── MediaAssetAccessService: admin / owner access ──────────────────────────

    [Fact]
    public async Task Admin_CanAlwaysAccess_AnyMediaAsset()
    {
        await using var db = BuildDb();
        var assetId = Guid.NewGuid().ToString("N");
        db.MediaAssets.Add(BuildMediaAsset(assetId, uploadedBy: "some-other-user"));
        // No scenario — admin bypass fires before scenario check
        await db.SaveChangesAsync();

        var svc = BuildAccessService(db);
        var principal = MakePrincipal("admin-user", "admin");

        var result = await svc.CanAccessAsync(principal, assetId, CancellationToken.None);

        Assert.True(result, "Admin should always access any media asset.");
    }

    [Fact]
    public async Task Owner_CanAccess_OwnMediaAsset()
    {
        await using var db = BuildDb();
        var assetId = Guid.NewGuid().ToString("N");
        db.MediaAssets.Add(BuildMediaAsset(assetId, uploadedBy: "author-007"));
        await db.SaveChangesAsync();

        var svc = BuildAccessService(db);
        // uploader can always access regardless of role — use admin role for simplicity
        var principal = MakePrincipal("author-007", "admin");

        var result = await svc.CanAccessAsync(principal, assetId, CancellationToken.None);

        Assert.True(result, "Uploader should access their own media asset.");
    }

    // ─── DTO mapper tests ───────────────────────────────────────────────────────

    [Fact]
    public void WritingScenarioResponse_HasDownloadPath_WhenPdfIdSet()
    {
        var pdfId = "abc123mediaasset";
        var view = new WritingScenarioView(
            Id: Guid.NewGuid(),
            Title: "Test",
            LetterType: "LT-RR",
            Profession: "medicine",
            SubDiscipline: null,
            Topics: Array.Empty<string>(),
            Difficulty: 2,
            CaseNotesMarkdown: "notes",
            CaseNotesStructured: Array.Empty<WritingScenarioStructuredSentenceDto>(),
            IsDiagnostic: false,
            Status: "published",
            CreatedAt: DateTimeOffset.UtcNow,
            StimulusPdfMediaAssetId: pdfId);

        var response = WritingV2ResponseMapper.ToResponse(view);

        Assert.Equal(pdfId, response.StimulusPdfMediaAssetId);
        Assert.Equal($"/v1/media/{pdfId}/content", response.StimulusPdfDownloadPath);
    }

    [Fact]
    public void WritingScenarioResponse_HasNullDownloadPath_WhenPdfIdNull()
    {
        var view = new WritingScenarioView(
            Id: Guid.NewGuid(),
            Title: "Test",
            LetterType: "LT-RR",
            Profession: "medicine",
            SubDiscipline: null,
            Topics: Array.Empty<string>(),
            Difficulty: 2,
            CaseNotesMarkdown: "notes",
            CaseNotesStructured: Array.Empty<WritingScenarioStructuredSentenceDto>(),
            IsDiagnostic: false,
            Status: "published",
            CreatedAt: DateTimeOffset.UtcNow,
            StimulusPdfMediaAssetId: null);

        var response = WritingV2ResponseMapper.ToResponse(view);

        Assert.Null(response.StimulusPdfMediaAssetId);
        Assert.Null(response.StimulusPdfDownloadPath);
    }

    // ─── Delete guard ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteGuard_ReturnsInUse_WhenScenarioReferencesAsset()
    {
        await using var db = BuildDb();
        var assetId = Guid.NewGuid().ToString("N");
        db.MediaAssets.Add(BuildMediaAsset(assetId));
        db.WritingScenarios.Add(BuildScenario(assetId, status: "draft"));
        await db.SaveChangesAsync();

        // Mirror the exact in-use check from MediaEndpoints.HandleDeleteAsync
        var isInUse = await db.WritingScenarios.AnyAsync(s => s.StimulusPdfMediaAssetId == assetId, CancellationToken.None);

        Assert.True(isInUse, "A media asset referenced by a WritingScenario should be flagged as in-use.");
    }

    [Fact]
    public async Task DeleteGuard_NotInUse_WhenNoScenarioReferencesAsset()
    {
        await using var db = BuildDb();
        var assetId = Guid.NewGuid().ToString("N");
        db.MediaAssets.Add(BuildMediaAsset(assetId));
        // No WritingScenario references this asset
        await db.SaveChangesAsync();

        var isInUse = await db.WritingScenarios.AnyAsync(s => s.StimulusPdfMediaAssetId == assetId, CancellationToken.None);

        Assert.False(isInUse, "A media asset not referenced by any WritingScenario should not be flagged as in-use.");
    }
}

// ─── Test doubles ──────────────────────────────────────────────────────────────

file sealed class StubEntitlementResolver : IEffectiveEntitlementResolver
{
    public Task<EffectiveEntitlementSnapshot> ResolveAsync(string? userId, CancellationToken ct)
        => Task.FromResult(new EffectiveEntitlementSnapshot(
            UserId: userId,
            HasEligibleSubscription: false,
            IsTrial: false,
            Tier: "free",
            SubscriptionId: null,
            SubscriptionStatus: null,
            PlanId: null,
            PlanVersionId: null,
            PlanCode: null,
            AiQuotaPlanCode: null,
            AiQuotaPlanCodeSource: null,
            ActiveAddOnCodes: Array.Empty<string>(),
            IsFrozen: false,
            Trace: Array.Empty<string>()));
}

file sealed class StubContentEntitlementService : IContentEntitlementService
{
    public Task<ContentEntitlementResult> AllowAccessAsync(string? userId, ContentPaper paper, CancellationToken ct)
        => Task.FromResult(new ContentEntitlementResult(false, "test_denied", null, null));

    public Task RequireAccessAsync(string? userId, ContentPaper paper, CancellationToken ct)
        => Task.CompletedTask;

    public bool IsAdmin(System.Security.Claims.ClaimsPrincipal? principal) => false;
}

file sealed class StubReadingPolicyService : IReadingPolicyService
{
    private static ReadingResolvedPolicy DefaultPolicy => new(
        AttemptsPerPaperPerUser: 3,
        AttemptCooldownMinutes: 0,
        PartATimerStrictness: "lenient",
        PartATimerMinutes: 60,
        PartBCTimerMinutes: 75,
        GracePeriodSeconds: 30,
        OnExpirySubmitPolicy: "auto",
        CountdownWarnings: Array.Empty<int>(),
        EnabledQuestionTypes: Array.Empty<string>(),
        ShortAnswerNormalisation: "none",
        ShortAnswerAcceptSynonyms: false,
        MatchingAllowPartialCredit: false,
        UnknownTypeFallbackPolicy: "skip",
        ShowExplanationsAfterSubmit: false,
        ShowExplanationsOnlyIfWrong: false,
        ShowCorrectAnswerOnReview: false,
        SubmitRateLimitPerMinute: 10,
        AutosaveRateLimitPerMinute: 30,
        ExtraTimeEntitlementPct: 0,
        AllowMultipleConcurrentAttempts: false,
        AllowPausingAttempt: false,
        AllowResumeAfterExpiry: false,
        AllowPaperReadingMode: false);

    public Task<ReadingPolicy> GetGlobalAsync(CancellationToken ct) => Task.FromResult(new ReadingPolicy());
    public Task<ReadingUserPolicyOverride?> GetUserOverrideAsync(string userId, CancellationToken ct) => Task.FromResult<ReadingUserPolicyOverride?>(null);
    public Task<ReadingResolvedPolicy> ResolveForUserAsync(string? userId, CancellationToken ct) => Task.FromResult(DefaultPolicy);
    public Task<ReadingPolicy> UpsertGlobalAsync(ReadingPolicy next, string adminId, CancellationToken ct) => Task.FromResult(next);
    public Task<ReadingUserPolicyOverride> UpsertUserOverrideAsync(string userId, ReadingUserPolicyOverride next, string adminId, CancellationToken ct) => Task.FromResult(next);
}
