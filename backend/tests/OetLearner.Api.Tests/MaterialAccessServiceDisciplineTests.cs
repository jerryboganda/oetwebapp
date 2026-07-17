using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Tests;

/// <summary>
/// Covers the name-based discipline (profession) filter added to the Materials library
/// (owner directive 2026-07-12): a learner sees only their own discipline's folders
/// (Speaking/Medicine, Writing/Nursing …), while non-discipline folders (Reading, Listening …)
/// stay visible to all. Admins are unfiltered; a learner with no profession fails CLOSED on
/// discipline-tagged folders (spec §7.5, profession-scoped access).
/// </summary>
public class MaterialAccessServiceDisciplineTests
{
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
    /// Seeds: the profession reference rows, a learner with the given ActiveProfessionId + an
    /// active subscription (so the Materials module gate passes, fail-open on modules), and a tree:
    ///   Speaking (Everyone) → Medicine, Nursing, Radiography
    ///   Reading  (Everyone) → Benchmark
    /// with one file under Medicine and one under Nursing.
    /// </summary>
    private static async Task SeedAsync(LearnerDbContext db, string userId, string? activeProfessionId)
    {
        var now = DateTimeOffset.UtcNow;

        db.Professions.AddRange(
            new ProfessionReference { Id = "medicine", Code = "medicine", Label = "Medicine", Status = "active", SortOrder = 1 },
            new ProfessionReference { Id = "nursing", Code = "nursing", Label = "Nursing", Status = "active", SortOrder = 2 },
            new ProfessionReference { Id = "radiography", Code = "radiography", Label = "Radiography", Status = "active", SortOrder = 3 });

        db.Users.Add(new LearnerUser
        {
            Id = userId,
            Email = $"{userId}@test.dev",
            DisplayName = "Test Learner",
            Role = ApplicationUserRoles.Learner,
            ActiveProfessionId = activeProfessionId,
            CreatedAt = now,
            LastActiveAt = now,
        });

        // Active subscription → HasEligibleSubscription = true; empty DashboardModulesJson means
        // IsModuleEnabled("MaterialsLibrary") reads fail-open true.
        var planCode = "plan-materials-test";
        db.BillingPlans.Add(new BillingPlan { Id = planCode, Code = planCode, Name = "Test plan", EntitlementsJson = "{}" });
        db.Subscriptions.Add(new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = userId,
            PlanId = planCode,
            Status = SubscriptionStatus.Active,
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
            Folder("f-speaking", "Speaking", null, MaterialAudienceMode.Everyone),
            Folder("f-medicine", "Medicine", "f-speaking", MaterialAudienceMode.Inherit),
            Folder("f-nursing", "Nursing", "f-speaking", MaterialAudienceMode.Inherit),
            Folder("f-radiography", "Radiography", "f-speaking", MaterialAudienceMode.Inherit),
            Folder("f-reading", "Reading", null, MaterialAudienceMode.Everyone),
            Folder("f-benchmark", "Benchmark", "f-reading", MaterialAudienceMode.Inherit));

        MaterialFile File(string id, string folderId, string assetId) => new()
        {
            Id = id,
            FolderId = folderId,
            MediaAssetId = assetId,
            SubtestCode = "speaking",
            Kind = "pdf",
            Title = id,
            Status = ContentStatus.Published,
            SortOrder = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.MaterialFiles.AddRange(
            File("file-med", "f-medicine", "asset-med"),
            File("file-nurse", "f-nursing", "asset-nurse"));

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
    public async Task MedicineLearner_SeesOnlyMedicine_UnderSpeaking()
    {
        await using var db = CreateDb();
        await SeedAsync(db, "learner-med", "medicine");
        var names = await VisibleFolderNamesAsync(CreateService(db), Principal("learner-med"));

        Assert.Contains("Speaking", names);
        Assert.Contains("Medicine", names);
        Assert.DoesNotContain("Nursing", names);
        Assert.DoesNotContain("Radiography", names);
        // Non-discipline folders stay visible to every learner.
        Assert.Contains("Reading", names);
        Assert.Contains("Benchmark", names);
    }

    [Fact]
    public async Task RadiographyLearner_SeesOnlyRadiography_UnderSpeaking()
    {
        await using var db = CreateDb();
        await SeedAsync(db, "learner-rad", "radiography");
        var names = await VisibleFolderNamesAsync(CreateService(db), Principal("learner-rad"));

        Assert.Contains("Radiography", names);
        Assert.DoesNotContain("Medicine", names);
        Assert.DoesNotContain("Nursing", names);
        Assert.Contains("Reading", names);
    }

    [Fact]
    public async Task LearnerWithNoProfession_FailsClosedOnDisciplineFolders()
    {
        await using var db = CreateDb();
        await SeedAsync(db, "learner-none", activeProfessionId: null);
        var names = await VisibleFolderNamesAsync(CreateService(db), Principal("learner-none"));

        // Spec §7.5 (profession-scoped access): no/unknown profession fails CLOSED on every
        // discipline-tagged folder; non-discipline folders stay visible.
        Assert.DoesNotContain("Medicine", names);
        Assert.DoesNotContain("Nursing", names);
        Assert.DoesNotContain("Radiography", names);
        Assert.Contains("Speaking", names);
        Assert.Contains("Reading", names);
        Assert.Contains("Benchmark", names);
    }

    [Fact]
    public async Task Admin_SeesEveryDiscipline()
    {
        await using var db = CreateDb();
        await SeedAsync(db, "admin-1", "medicine");
        var names = await VisibleFolderNamesAsync(
            CreateService(db), Principal("admin-1", ApplicationUserRoles.Admin));

        Assert.Contains("Medicine", names);
        Assert.Contains("Nursing", names);
        Assert.Contains("Radiography", names);
    }

    [Fact]
    public async Task DownloadAuth_DeniesCrossDisciplineFile_ButAllowsOwn()
    {
        await using var db = CreateDb();
        await SeedAsync(db, "learner-med", "medicine");
        var service = CreateService(db);

        Assert.True(await service.CanCandidateAccessMaterialFileAsync("learner-med", "asset-med", default));
        Assert.False(await service.CanCandidateAccessMaterialFileAsync("learner-med", "asset-nurse", default));
    }
}
