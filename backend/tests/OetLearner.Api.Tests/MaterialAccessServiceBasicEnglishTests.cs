using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Tests;

/// <summary>
/// Covers the General English (Basic English Course) separation in the Materials library
/// (course-materials diagram, owner directive 2026-07-18): the Basic English tree is its own
/// product's materials — visible only with a Basic English grant ("BasicEnglish" plan module or
/// the BasicEnglishUnlocked bundle flag) — and a standalone Basic English holder (every module
/// inside the basic-english cluster) sees ONLY that tree. Holding ANY other package alongside
/// (subtest modules, SpeakingSession, …) keeps the shared OET tree; a per-user admin DISABLE of
/// BasicEnglish fails CLOSED for a standalone holder. Admins and legacy no-module plans stay
/// unfiltered.
/// </summary>
public class MaterialAccessServiceBasicEnglishTests
{
    private const string OetPlanModules =
        """["Listening","Reading","Writing","Speaking","MaterialsLibrary","Recalls","Mocks"]""";

    private const string BasicEnglishPlanModules =
        """["BasicEnglish","Vocabulary","Grammar","ListeningFoundations","Recalls","MaterialsLibrary","Mocks"]""";

    private const string PremiumWithBasicEnglishModules =
        """["Listening","Reading","Writing","Speaking","BasicEnglish","MaterialsLibrary","Recalls","Mocks"]""";

    private const string SessionPlanModules =
        """["SpeakingSession","Addons","Recalls","MaterialsLibrary","VideoLibrary","Mocks"]""";

    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static MaterialAccessService CreateService(LearnerDbContext db)
        => new(db, new EffectiveEntitlementResolver(db));

    private static ClaimsPrincipal Principal(string userId, string role = ApplicationUserRoles.Learner)
        => new(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Role, role),
            },
            authenticationType: "test"));

    /// <summary>
    /// Seeds the production-shaped tree:
    ///   Basic English Course (Everyone) — file-be
    ///   Listening (Everyone) → Benchmark — file-listening
    ///   Speaking  (Everyone) → Medicine — file-med
    /// plus a medicine learner with an active subscription on a plan with the given module list
    /// and bundle flag.
    /// </summary>
    private static async Task SeedAsync(
        LearnerDbContext db,
        string userId,
        string? dashboardModulesJson,
        bool basicEnglishUnlocked)
    {
        var now = DateTimeOffset.UtcNow;

        db.Professions.Add(new ProfessionReference
        {
            Id = "medicine",
            Code = "medicine",
            Label = "Medicine",
            Status = "active",
            SortOrder = 1,
        });

        db.Users.Add(new LearnerUser
        {
            Id = userId,
            Email = $"{userId}@test.dev",
            DisplayName = "Test Learner",
            Role = ApplicationUserRoles.Learner,
            ActiveProfessionId = "medicine",
            CreatedAt = now,
            LastActiveAt = now,
        });

        var planId = $"plan-{userId}";
        db.BillingPlans.Add(new BillingPlan
        {
            Id = planId,
            Code = planId,
            Name = "Test plan",
            EntitlementsJson = "{}",
            DashboardModulesJson = dashboardModulesJson ?? "[]",
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = userId,
            PlanId = planId,
            Status = SubscriptionStatus.Active,
            BasicEnglishUnlocked = basicEnglishUnlocked,
            StartedAt = now.AddDays(-1),
            ChangedAt = now,
        });

        MaterialFolder Folder(string id, string name, string? parentId, MaterialAudienceMode mode) => new()
        {
            Id = id,
            Name = name,
            ParentFolderId = parentId,
            AudienceMode = mode,
            Status = ContentStatus.Published,
            SortOrder = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.MaterialFolders.AddRange(
            Folder("f-be", "Basic English Course", null, MaterialAudienceMode.Everyone),
            Folder("f-listening", "Listening", null, MaterialAudienceMode.Everyone),
            Folder("f-benchmark", "Benchmark", "f-listening", MaterialAudienceMode.Inherit),
            Folder("f-speaking", "Speaking", null, MaterialAudienceMode.Everyone),
            Folder("f-medicine", "Medicine", "f-speaking", MaterialAudienceMode.Inherit));

        MaterialFile File(string id, string folderId, string assetId, string subtest) => new()
        {
            Id = id,
            FolderId = folderId,
            MediaAssetId = assetId,
            SubtestCode = subtest,
            Kind = "pdf",
            Title = id,
            Status = ContentStatus.Published,
            SortOrder = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.MaterialFiles.AddRange(
            File("file-be", "f-be", "asset-be", "reading"),
            File("file-listening", "f-benchmark", "asset-listening", "listening"),
            File("file-med", "f-medicine", "asset-med", "speaking"));

        await db.SaveChangesAsync();
    }

    private static async Task<HashSet<string>> VisibleFolderNamesAsync(
        MaterialAccessService service, ClaimsPrincipal principal)
    {
        var tree = await service.GetVisibleTreeAsync(principal, default);
        var json = JsonSerializer.Serialize(tree);
        using var doc = JsonDocument.Parse(json);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(doc.RootElement, names);
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
                if (element.TryGetProperty("folders", out var folders)) Collect(folders, acc);
            }
        }
    }

    [Fact]
    public async Task OetLearner_DoesNotSeeBasicEnglishTree()
    {
        await using var db = CreateDb();
        await SeedAsync(db, "learner-oet", OetPlanModules, basicEnglishUnlocked: false);
        var names = await VisibleFolderNamesAsync(CreateService(db), Principal("learner-oet"));

        Assert.DoesNotContain("Basic English Course", names);
        Assert.Contains("Listening", names);
        Assert.Contains("Benchmark", names);
        Assert.Contains("Medicine", names);
    }

    [Fact]
    public async Task StandaloneBasicEnglishLearner_SeesOnlyBasicEnglishTree()
    {
        await using var db = CreateDb();
        // Module-only grant (flag off) so the "BasicEnglish" module path is tested in isolation.
        await SeedAsync(db, "learner-be", BasicEnglishPlanModules, basicEnglishUnlocked: false);
        var names = await VisibleFolderNamesAsync(CreateService(db), Principal("learner-be"));

        Assert.Contains("Basic English Course", names);
        Assert.DoesNotContain("Listening", names);
        Assert.DoesNotContain("Benchmark", names);
        Assert.DoesNotContain("Speaking", names);
        Assert.DoesNotContain("Medicine", names);
    }

    [Fact]
    public async Task BundleFlagOnly_WithoutModuleKey_StillGrantsOnlyBasicEnglishTree()
    {
        await using var db = CreateDb();
        // Purchase-derived flag only — the plan's module list has no "BasicEnglish" key, so this
        // isolates the BasicEnglishUnlocked branch.
        await SeedAsync(
            db, "learner-flag",
            """["Recalls","MaterialsLibrary","VideoLibrary","Mocks"]""",
            basicEnglishUnlocked: true);
        var names = await VisibleFolderNamesAsync(CreateService(db), Principal("learner-flag"));

        Assert.Contains("Basic English Course", names);
        Assert.DoesNotContain("Listening", names);
        Assert.DoesNotContain("Medicine", names);
    }

    [Fact]
    public async Task PerUserDisable_FailsClosed_ForStandaloneBasicEnglishLearner()
    {
        await using var db = CreateDb();
        await SeedAsync(db, "learner-denied", BasicEnglishPlanModules, basicEnglishUnlocked: true);
        db.UserModuleOverrides.Add(new UserModuleOverride
        {
            Id = "umo-denied",
            UserId = "learner-denied",
            ModuleKey = "BasicEnglish",
            Enabled = false,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var names = await VisibleFolderNamesAsync(service, Principal("learner-denied"));

        // Revoking a standalone Basic English learner's only product fails CLOSED — it must not
        // fall open into the shared OET pool.
        Assert.DoesNotContain("Basic English Course", names);
        Assert.DoesNotContain("Listening", names);
        Assert.DoesNotContain("Speaking", names);
        Assert.DoesNotContain("Medicine", names);
        Assert.False(await service.CanCandidateAccessMaterialFileAsync("learner-denied", "asset-be", default));
        Assert.False(await service.CanCandidateAccessMaterialFileAsync("learner-denied", "asset-listening", default));
    }

    [Fact]
    public async Task SessionPlanHolder_GainingBasicEnglish_KeepsOetTree()
    {
        await using var db = CreateDb();
        // A speaking-session holder (no subtest module keys) who ALSO gains the basic-english
        // package must keep everything the session plan already reached — buying more content
        // never removes access.
        await SeedAsync(db, "learner-combo", SessionPlanModules, basicEnglishUnlocked: false);
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "plan-combo-be",
            Code = "plan-combo-be",
            Name = "Basic English",
            EntitlementsJson = "{}",
            DashboardModulesJson = BasicEnglishPlanModules,
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = "learner-combo",
            PlanId = "plan-combo-be",
            Status = SubscriptionStatus.Active,
            BasicEnglishUnlocked = true,
            StartedAt = now.AddHours(-1),
            ChangedAt = now,
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var names = await VisibleFolderNamesAsync(service, Principal("learner-combo"));

        Assert.Contains("Basic English Course", names);
        Assert.Contains("Listening", names);
        Assert.Contains("Benchmark", names);
        Assert.Contains("Medicine", names);
        Assert.True(await service.CanCandidateAccessMaterialFileAsync("learner-combo", "asset-listening", default));
        Assert.True(await service.CanCandidateAccessMaterialFileAsync("learner-combo", "asset-be", default));
    }

    [Fact]
    public void PlanSideScope_MirrorsLearnerRules()
    {
        // Standalone basic-english plan (seed shape incl. backfilled toggles) → exclusive.
        var standalone = MaterialAccessService.ResolveBasicEnglishScope(
            new[] { "BasicEnglish", "Vocabulary", "Grammar", "ListeningFoundations", "StudyPlan", "Booklet", "Recalls", "MaterialsLibrary", "VideoLibrary", "Mocks" },
            bundledBasicEnglish: true);
        Assert.True(standalone.Entitled);
        Assert.True(standalone.ExclusivelyBasicEnglish);

        // Session plan with the bundle flag ticked → entitled but NOT exclusive (its own
        // materials stay countable at checkout).
        var session = MaterialAccessService.ResolveBasicEnglishScope(
            new[] { "SpeakingSession", "Addons", "Recalls", "MaterialsLibrary", "VideoLibrary", "Mocks" },
            bundledBasicEnglish: true);
        Assert.True(session.Entitled);
        Assert.False(session.ExclusivelyBasicEnglish);

        // Ordinary OET plan → neither.
        var oet = MaterialAccessService.ResolveBasicEnglishScope(
            new[] { "Listening", "Reading", "Writing", "Speaking", "MaterialsLibrary" },
            bundledBasicEnglish: false);
        Assert.False(oet.Entitled);
        Assert.False(oet.ExclusivelyBasicEnglish);
    }

    [Fact]
    public async Task PremiumLearnerWithBundledBasicEnglish_SeesBothTrees()
    {
        await using var db = CreateDb();
        await SeedAsync(db, "learner-premium", PremiumWithBasicEnglishModules, basicEnglishUnlocked: true);
        var names = await VisibleFolderNamesAsync(CreateService(db), Principal("learner-premium"));

        Assert.Contains("Basic English Course", names);
        Assert.Contains("Listening", names);
        Assert.Contains("Medicine", names);
    }

    [Fact]
    public async Task LegacyPlanWithoutModuleList_StaysUnfiltered()
    {
        await using var db = CreateDb();
        await SeedAsync(db, "learner-legacy", dashboardModulesJson: null, basicEnglishUnlocked: false);
        var names = await VisibleFolderNamesAsync(CreateService(db), Principal("learner-legacy"));

        // Fail-open module semantics: a legacy plan with no module list keeps today's behaviour.
        Assert.Contains("Basic English Course", names);
        Assert.Contains("Listening", names);
        Assert.Contains("Medicine", names);
    }

    [Fact]
    public async Task Admin_SeesBothTrees()
    {
        await using var db = CreateDb();
        await SeedAsync(db, "admin-1", OetPlanModules, basicEnglishUnlocked: false);
        var names = await VisibleFolderNamesAsync(
            CreateService(db), Principal("admin-1", ApplicationUserRoles.Admin));

        Assert.Contains("Basic English Course", names);
        Assert.Contains("Listening", names);
        Assert.Contains("Medicine", names);
    }

    [Fact]
    public async Task DownloadAuth_MirrorsBasicEnglishGate()
    {
        await using var db = CreateDb();
        await SeedAsync(db, "learner-oet", OetPlanModules, basicEnglishUnlocked: false);
        var service = CreateService(db);

        // OET learner: own tree downloadable, Basic English asset denied.
        Assert.True(await service.CanCandidateAccessMaterialFileAsync("learner-oet", "asset-listening", default));
        Assert.False(await service.CanCandidateAccessMaterialFileAsync("learner-oet", "asset-be", default));
    }

    [Fact]
    public async Task DownloadAuth_StandaloneBasicEnglish_ReachesOnlyOwnTree()
    {
        await using var db = CreateDb();
        await SeedAsync(db, "learner-be", BasicEnglishPlanModules, basicEnglishUnlocked: true);
        var service = CreateService(db);

        Assert.True(await service.CanCandidateAccessMaterialFileAsync("learner-be", "asset-be", default));
        Assert.False(await service.CanCandidateAccessMaterialFileAsync("learner-be", "asset-listening", default));
        Assert.False(await service.CanCandidateAccessMaterialFileAsync("learner-be", "asset-med", default));
    }
}
