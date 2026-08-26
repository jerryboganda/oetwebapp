using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.VideoLibrary;
using Xunit;

namespace OetLearner.Api.Tests;

public class PackageBasedContentAccessAcceptanceTests
{
    private const string MedicinePlanCode = "full-condensed-medicine-tbook";
    private const string MedicineProfession = "medicine";
    private const string NursingProfession = "nursing";

    private static LearnerDbContext CreateDb()
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NoopAddonProcessor : IAddonGrantProcessor
    {
        public Task<AddonGrantResult> ApplyAsync(string eventId, string subscriptionId, string addOnCode, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<AddonGrantResult> ReverseAsync(string eventId, string subscriptionId, string addOnCode, CancellationToken ct = default)
            => Task.FromResult(new AddonGrantResult(false, false, "noop"));
    }

    private static UserAccessAllocationService CreateAllocationService(LearnerDbContext db)
        => new(db, new NoopAddonProcessor(), TimeProvider.System);

    private static ClaimsPrincipal Principal(string userId, string role = ApplicationUserRoles.Learner)
        => new(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Role, role),
            },
            authenticationType: "test"));

    private static async Task SeedEnvironmentAsync(
        LearnerDbContext db,
        string userId,
        DateTimeOffset now)
    {
        // Reference professions
        db.Professions.AddRange(
            new ProfessionReference { Id = MedicineProfession, Code = MedicineProfession, Label = "Medicine" },
            new ProfessionReference { Id = NursingProfession, Code = NursingProfession, Label = "Nursing" });

        // Medicine Learner
        db.Users.Add(new LearnerUser
        {
            Id = userId,
            Role = ApplicationUserRoles.Learner,
            DisplayName = "Dr. Test Medicine",
            Email = $"{userId}@test.dev",
            ActiveProfessionId = MedicineProfession,
            AccountStatus = "active",
            CreatedAt = now,
            LastActiveAt = now,
        });

        // "Full Course + TutorBook" Plan for Medicine
        db.BillingPlans.Add(new BillingPlan
        {
            Id = MedicinePlanCode,
            Code = MedicinePlanCode,
            Name = "Full Course + TutorBook",
            Profession = MedicineProfession,
            DurationMonths = 6,
            AccessDurationDays = 180,
            DashboardModulesJson = """["Recalls","MaterialsLibrary","VideoLibrary"]""",
            IncludedSubtestsJson = "[]",
            EntitlementsJson = "{}",
            BundledTutorBook = true,
            Status = BillingPlanStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        });

        // Materials folders (Medicine-specific, Shared Reading, Nursing-only)
        db.MaterialFolders.AddRange(
            new MaterialFolder
            {
                Id = "f-med",
                Name = "Medicine",
                ParentFolderId = null,
                AudienceMode = MaterialAudienceMode.Everyone,
                Status = ContentStatus.Published,
                SortOrder = 1,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new MaterialFolder
            {
                Id = "f-shared",
                Name = "Reading",
                ParentFolderId = null,
                AudienceMode = MaterialAudienceMode.Everyone,
                Status = ContentStatus.Published,
                SortOrder = 2,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new MaterialFolder
            {
                Id = "f-nursing",
                Name = "Nursing",
                ParentFolderId = null,
                AudienceMode = MaterialAudienceMode.Everyone,
                Status = ContentStatus.Published,
                SortOrder = 3,
                CreatedAt = now,
                UpdatedAt = now,
            });

        // Media assets and material files
        db.MediaAssets.AddRange(
            new MediaAsset { Id = "asset-med", OriginalFilename = "med.pdf", MimeType = "application/pdf", Format = "pdf", StoragePath = "/store/med.pdf", SizeBytes = 1024, Status = MediaAssetStatus.Ready },
            new MediaAsset { Id = "asset-shared", OriginalFilename = "shared.pdf", MimeType = "application/pdf", Format = "pdf", StoragePath = "/store/shared.pdf", SizeBytes = 1024, Status = MediaAssetStatus.Ready },
            new MediaAsset { Id = "asset-nursing", OriginalFilename = "nursing.pdf", MimeType = "application/pdf", Format = "pdf", StoragePath = "/store/nursing.pdf", SizeBytes = 1024, Status = MediaAssetStatus.Ready });

        db.MaterialFiles.AddRange(
            new MaterialFile { Id = "file-med", FolderId = "f-med", MediaAssetId = "asset-med", SubtestCode = "speaking", Kind = "pdf", Title = "Medicine Guide", Status = ContentStatus.Published, SortOrder = 0, CreatedAt = now, UpdatedAt = now },
            new MaterialFile { Id = "file-shared", FolderId = "f-shared", MediaAssetId = "asset-shared", SubtestCode = "reading", Kind = "pdf", Title = "Reading Guide", Status = ContentStatus.Published, SortOrder = 0, CreatedAt = now, UpdatedAt = now },
            new MaterialFile { Id = "file-nursing", FolderId = "f-nursing", MediaAssetId = "asset-nursing", SubtestCode = "speaking", Kind = "pdf", Title = "Nursing Guide", Status = ContentStatus.Published, SortOrder = 0, CreatedAt = now, UpdatedAt = now });

        // Videos (Medicine-tagged, Shared untagged, Nursing-tagged; all carry batch:shared
        // per migration — the course-family gate allows shared on every plan)
        db.LibraryVideos.AddRange(
            new LibraryVideo
            {
                Id = "vid-med",
                Title = "Medicine Case Study",
                AccessTier = "premium",
                TagsCsv = CourseFamilyPolicy.SharedTag,
                Status = ContentStatus.Published,
                ProfessionIdsJson = """["medicine"]""",
                DurationSeconds = 600,
                CreatedAt = now,
                PublishedAt = now,
                UpdatedAt = now,
            },
            new LibraryVideo
            {
                Id = "vid-shared",
                Title = "Reading Strategy",
                AccessTier = "premium",
                TagsCsv = CourseFamilyPolicy.SharedTag,
                Status = ContentStatus.Published,
                ProfessionIdsJson = "[]",
                DurationSeconds = 600,
                CreatedAt = now,
                PublishedAt = now,
                UpdatedAt = now,
            },
            new LibraryVideo
            {
                Id = "vid-nursing",
                Title = "Nursing Handover",
                AccessTier = "premium",
                TagsCsv = CourseFamilyPolicy.SharedTag,
                Status = ContentStatus.Published,
                ProfessionIdsJson = """["nursing"]""",
                DurationSeconds = 600,
                CreatedAt = now,
                PublishedAt = now,
                UpdatedAt = now,
            });

        // Vocabulary terms (2 recall sets)
        db.VocabularyTerms.AddRange(
            new VocabularyTerm
            {
                Id = "vt-med-1",
                Term = "Auscultation",
                Definition = "Listening to internal body sounds",
                ExamTypeCode = "oet",
                Category = "medical",
                Status = "active",
                RecallSetCodesJson = """["set-2026-a"]""",
            },
            new VocabularyTerm
            {
                Id = "vt-med-2",
                Term = "Bradycardia",
                Definition = "Abnormally slow heart rate",
                ExamTypeCode = "oet",
                Category = "medical",
                Status = "active",
                RecallSetCodesJson = """["set-2026-b"]""",
            });

        await db.SaveChangesAsync();
    }

    private static async Task<HashSet<string>> VisibleFolderNamesAsync(
        MaterialAccessService service, ClaimsPrincipal principal)
    {
        var tree = await service.GetVisibleTreeAsync(principal, default);
        var json = JsonSerializer.Serialize(tree);
        var root = JsonDocument.Parse(json).RootElement;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(root, names);
        return names;

        static void Collect(JsonElement element, HashSet<string> acc)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray()) Collect(item, acc);
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("Name", out var name) && name.ValueKind == JsonValueKind.String)
                    acc.Add(name.GetString()!);
                if (element.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                    acc.Add(n.GetString()!);
                if (element.TryGetProperty("folders", out var folders)) Collect(folders, acc);
            }
        }
    }

    [Fact]
    public async Task ModuleOnlyTicks_EmptyNestedScopes_LearnerReceivesAllAndOnlyPackageContent()
    {
        await using var db = CreateDb();
        const string userId = "learner-med-pkg";
        var now = DateTimeOffset.UtcNow;
        await SeedEnvironmentAsync(db, userId, now);

        var allocationService = CreateAllocationService(db);

        // 1. Assign "Full Course + TutorBook" package to Medicine candidate
        await allocationService.GrantPackageAsync(
            "admin-1", "Admin", userId,
            new AdminUserAccessPackageRequest(
                PlanCode: MedicinePlanCode,
                StartsAt: null,
                ExpiresAt: null,
                MakePrimary: true,
                GrantIncludedCredits: false,
                OverrideProfessionMismatch: false),
            default);

        // 2 & 3. Tick ONLY module-level toggles (Recalls, MaterialsLibrary, VideoLibrary; Mocks=false);
        // empty nested scopes (MaterialFolderIds: [], RecallSetCodes: [], VideoIds: [])
        var access = await allocationService.PutScopeAsync(
            "admin-1", "Admin", userId,
            new AdminUserAccessScopeRequest(
                Modules: new List<AdminModuleOverrideDto>
                {
                    new("Recalls", true),
                    new("MaterialsLibrary", true),
                    new("VideoLibrary", true),
                    new("Mocks", false),
                },
                MaterialFolderIds: [],
                RecallSetCodes: [],
                AccessExpiresAt: null,
                ClearAccessExpiry: false,
                VideoIds: []),
            default);

        // Assert response DTO and DB restriction tables have zero restriction rows
        Assert.Empty(access.MaterialFolderIds);
        Assert.Empty(access.RecallSetCodes);
        Assert.Empty(access.VideoIds);
        Assert.Empty(await db.UserMaterialFolderAccesses.Where(x => x.UserId == userId).ToListAsync());
        Assert.Empty(await db.UserRecallSetAccesses.Where(x => x.UserId == userId).ToListAsync());
        Assert.Empty(await db.UserVideoAccesses.Where(x => x.UserId == userId).ToListAsync());

        // 4. Assert candidate receives ALL and ONLY package-granted content automatically:
        var principal = Principal(userId);
        var materialService = new MaterialAccessService(db, new EffectiveEntitlementResolver(db));
        var visibleFolders = await VisibleFolderNamesAsync(materialService, principal);

        // Materials: Medicine + shared visible, Nursing excluded
        Assert.Contains("Medicine", visibleFolders);
        Assert.Contains("Reading", visibleFolders);
        Assert.DoesNotContain("Nursing", visibleFolders);

        Assert.True(await materialService.CanCandidateAccessMaterialFileAsync(userId, "asset-med", default));
        Assert.True(await materialService.CanCandidateAccessMaterialFileAsync(userId, "asset-shared", default));
        Assert.False(await materialService.CanCandidateAccessMaterialFileAsync(userId, "asset-nursing", default));

        // Videos: Medicine + shared allowed, Nursing denied
        var videoGate = new VideoEntitlementService(db, new EffectiveEntitlementResolver(db));
        var medVideo = await db.LibraryVideos.FirstAsync(v => v.Id == "vid-med");
        var sharedVideo = await db.LibraryVideos.FirstAsync(v => v.Id == "vid-shared");
        var nursingVideo = await db.LibraryVideos.FirstAsync(v => v.Id == "vid-nursing");

        var medResult = await videoGate.AllowAccessAsync(userId, medVideo, default);
        var sharedResult = await videoGate.AllowAccessAsync(userId, sharedVideo, default);
        var nursingResult = await videoGate.AllowAccessAsync(userId, nursingVideo, default);

        Assert.True(medResult.Allowed);
        Assert.Equal("plan_grants_video_library", medResult.Reason);
        Assert.True(sharedResult.Allowed);
        Assert.Equal("plan_grants_video_library", sharedResult.Reason);
        Assert.False(nursingResult.Allowed);
        Assert.Equal("profession_mismatch", nursingResult.Reason);

        var videoLearnerService = new VideoLibraryLearnerService(db, videoGate, settingsProvider: null!);
        Assert.NotNull(await videoLearnerService.FindVisibleVideoAsync(userId, "vid-med", now, default));
        Assert.NotNull(await videoLearnerService.FindVisibleVideoAsync(userId, "vid-shared", now, default));
        Assert.Null(await videoLearnerService.FindVisibleVideoAsync(userId, "vid-nursing", now, default));

        // Vocabulary / Recalls: all recall sets / terms visible
        var vocabService = new VocabularyService(db, new Sm2Scheduler());
        var vocabPage = await vocabService.GetTermsAsync(
            examTypeCode: "oet",
            category: null,
            profession: null,
            search: null,
            page: 1,
            pageSize: 50,
            ct: default,
            userId: userId);

        Assert.Equal(2, vocabPage.Total);
        Assert.Contains(vocabPage.Terms, t => t.Id == "vt-med-1");
        Assert.Contains(vocabPage.Terms, t => t.Id == "vt-med-2");
    }

    [Fact]
    public async Task ManualRestriction_ThenEmptyResave_RestoresFullPackageAccess()
    {
        await using var db = CreateDb();
        const string userId = "learner-med-restrict-restore";
        var now = DateTimeOffset.UtcNow;
        await SeedEnvironmentAsync(db, userId, now);

        var allocationService = CreateAllocationService(db);

        // Assign package
        await allocationService.GrantPackageAsync(
            "admin-1", "Admin", userId,
            new AdminUserAccessPackageRequest(
                PlanCode: MedicinePlanCode,
                StartsAt: null,
                ExpiresAt: null,
                MakePrimary: true,
                GrantIncludedCredits: false,
                OverrideProfessionMismatch: false),
            default);

        // Save manual restrictions: one folder ("f-med"), one video ("vid-med"), one recall set ("set-2026-a")
        await allocationService.PutScopeAsync(
            "admin-1", "Admin", userId,
            new AdminUserAccessScopeRequest(
                Modules: new List<AdminModuleOverrideDto>
                {
                    new("Recalls", true),
                    new("MaterialsLibrary", true),
                    new("VideoLibrary", true),
                    new("Mocks", false),
                },
                MaterialFolderIds: new List<string> { "f-med" },
                RecallSetCodes: new List<string> { "set-2026-a" },
                AccessExpiresAt: null,
                ClearAccessExpiry: false,
                VideoIds: new List<string> { "vid-med" }),
            default);

        // Verify restriction is active
        Assert.Single(await db.UserMaterialFolderAccesses.Where(x => x.UserId == userId).ToListAsync());
        Assert.Single(await db.UserVideoAccesses.Where(x => x.UserId == userId).ToListAsync());
        Assert.Single(await db.UserRecallSetAccesses.Where(x => x.UserId == userId).ToListAsync());

        var principal = Principal(userId);
        var materialService = new MaterialAccessService(db, new EffectiveEntitlementResolver(db));
        var restrictedFolders = await VisibleFolderNamesAsync(materialService, principal);
        Assert.Contains("Medicine", restrictedFolders);
        Assert.DoesNotContain("Reading", restrictedFolders);
        Assert.DoesNotContain("Nursing", restrictedFolders);

        Assert.True(await materialService.CanCandidateAccessMaterialFileAsync(userId, "asset-med", default));
        Assert.False(await materialService.CanCandidateAccessMaterialFileAsync(userId, "asset-shared", default));
        Assert.False(await materialService.CanCandidateAccessMaterialFileAsync(userId, "asset-nursing", default));

        var videoGate = new VideoEntitlementService(db, new EffectiveEntitlementResolver(db));
        var medVideo = await db.LibraryVideos.FirstAsync(v => v.Id == "vid-med");
        var sharedVideo = await db.LibraryVideos.FirstAsync(v => v.Id == "vid-shared");
        var nursingVideo = await db.LibraryVideos.FirstAsync(v => v.Id == "vid-nursing");

        var restrictedMed = await videoGate.AllowAccessAsync(userId, medVideo, default);
        var restrictedShared = await videoGate.AllowAccessAsync(userId, sharedVideo, default);
        var restrictedNursing = await videoGate.AllowAccessAsync(userId, nursingVideo, default);

        Assert.True(restrictedMed.Allowed);
        Assert.False(restrictedShared.Allowed);
        Assert.Equal("not_in_user_allocation", restrictedShared.Reason);
        Assert.False(restrictedNursing.Allowed);

        var videoLearnerService = new VideoLibraryLearnerService(db, videoGate, settingsProvider: null!);
        Assert.NotNull(await videoLearnerService.FindVisibleVideoAsync(userId, "vid-med", now, default));
        Assert.Null(await videoLearnerService.FindVisibleVideoAsync(userId, "vid-shared", now, default));
        Assert.Null(await videoLearnerService.FindVisibleVideoAsync(userId, "vid-nursing", now, default));

        var vocabService = new VocabularyService(db, new Sm2Scheduler());
        var restrictedVocab = await vocabService.GetTermsAsync("oet", null, null, null, 1, 50, default, userId: userId);
        Assert.Equal(1, restrictedVocab.Total);
        Assert.Contains(restrictedVocab.Terms, t => t.Id == "vt-med-1");
        Assert.DoesNotContain(restrictedVocab.Terms, t => t.Id == "vt-med-2");

        // Re-save with empty lists (remove manual restrictions, restore full package access)
        var restoredAccess = await allocationService.PutScopeAsync(
            "admin-1", "Admin", userId,
            new AdminUserAccessScopeRequest(
                Modules: new List<AdminModuleOverrideDto>
                {
                    new("Recalls", true),
                    new("MaterialsLibrary", true),
                    new("VideoLibrary", true),
                    new("Mocks", false),
                },
                MaterialFolderIds: [],
                RecallSetCodes: [],
                AccessExpiresAt: null,
                ClearAccessExpiry: false,
                VideoIds: []),
            default);

        Assert.Empty(restoredAccess.MaterialFolderIds);
        Assert.Empty(restoredAccess.RecallSetCodes);
        Assert.Empty(restoredAccess.VideoIds);
        Assert.Empty(await db.UserMaterialFolderAccesses.Where(x => x.UserId == userId).ToListAsync());
        Assert.Empty(await db.UserVideoAccesses.Where(x => x.UserId == userId).ToListAsync());
        Assert.Empty(await db.UserRecallSetAccesses.Where(x => x.UserId == userId).ToListAsync());

        // Assert full package access is restored
        var restoredMaterialService = new MaterialAccessService(db, new EffectiveEntitlementResolver(db));
        var restoredFolders = await VisibleFolderNamesAsync(restoredMaterialService, principal);
        Assert.Contains("Medicine", restoredFolders);
        Assert.Contains("Reading", restoredFolders);
        Assert.DoesNotContain("Nursing", restoredFolders);

        Assert.True(await restoredMaterialService.CanCandidateAccessMaterialFileAsync(userId, "asset-med", default));
        Assert.True(await restoredMaterialService.CanCandidateAccessMaterialFileAsync(userId, "asset-shared", default));
        Assert.False(await restoredMaterialService.CanCandidateAccessMaterialFileAsync(userId, "asset-nursing", default));

        var restoredVideoGate = new VideoEntitlementService(db, new EffectiveEntitlementResolver(db));
        var restoredMed = await restoredVideoGate.AllowAccessAsync(userId, medVideo, default);
        var restoredShared = await restoredVideoGate.AllowAccessAsync(userId, sharedVideo, default);
        var restoredNursing = await restoredVideoGate.AllowAccessAsync(userId, nursingVideo, default);

        Assert.True(restoredMed.Allowed);
        Assert.Equal("plan_grants_video_library", restoredMed.Reason);
        Assert.True(restoredShared.Allowed);
        Assert.Equal("plan_grants_video_library", restoredShared.Reason);
        Assert.False(restoredNursing.Allowed);
        Assert.Equal("profession_mismatch", restoredNursing.Reason);

        var restoredVideoLearnerService = new VideoLibraryLearnerService(db, restoredVideoGate, settingsProvider: null!);
        Assert.NotNull(await restoredVideoLearnerService.FindVisibleVideoAsync(userId, "vid-med", now, default));
        Assert.NotNull(await restoredVideoLearnerService.FindVisibleVideoAsync(userId, "vid-shared", now, default));
        Assert.Null(await restoredVideoLearnerService.FindVisibleVideoAsync(userId, "vid-nursing", now, default));

        var restoredVocab = await vocabService.GetTermsAsync("oet", null, null, null, 1, 50, default, userId: userId);
        Assert.Equal(2, restoredVocab.Total);
        Assert.Contains(restoredVocab.Terms, t => t.Id == "vt-med-1");
        Assert.Contains(restoredVocab.Terms, t => t.Id == "vt-med-2");
    }
}
