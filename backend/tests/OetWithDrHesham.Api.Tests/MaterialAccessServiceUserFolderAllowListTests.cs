using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Content;
using OetWithDrHesham.Api.Services.Entitlements;

namespace OetWithDrHesham.Api.Tests;

/// <summary>
/// Covers the per-user Materials folder allow-list added in Phase E (owner directive):
/// a learner with ANY <see cref="UserMaterialFolderAccess"/> rows is restricted to those
/// folders plus their ANCESTORS (navigation) and DESCENDANTS (content); a learner with
/// NO rows is unchanged. Admins bypass. Enforced in <see cref="MaterialAccessService"/>.
/// </summary>
public class MaterialAccessServiceUserFolderAllowListTests
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
    /// Seeds a learner (no profession, so the discipline gate is a no-op) with an
    /// active subscription (module gate passes, fail-open) and a tree:
    ///   Root (Everyone)
    ///     -> ChildA (Inherit)
    ///          -> GrandchildA (Inherit)   [file-a]
    ///     -> ChildB (Inherit)             [file-b]
    /// plus one root-level file (file-root, FolderId == null).
    /// </summary>
    private static async Task SeedAsync(LearnerDbContext db, string userId, string role = ApplicationUserRoles.Learner)
    {
        var now = DateTimeOffset.UtcNow;

        db.Users.Add(new LearnerUser
        {
            Id = userId,
            Email = $"{userId}@test.dev",
            DisplayName = "Test Learner",
            Role = role,
            ActiveProfessionId = null,
            CreatedAt = now,
            LastActiveAt = now,
        });

        var planCode = "plan-folder-allowlist-test";
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
            Folder("f-root", "Root", null, MaterialAudienceMode.Everyone),
            Folder("f-child-a", "ChildA", "f-root", MaterialAudienceMode.Inherit),
            Folder("f-grandchild-a", "GrandchildA", "f-child-a", MaterialAudienceMode.Inherit),
            Folder("f-child-b", "ChildB", "f-root", MaterialAudienceMode.Inherit));

        MaterialFile File(string id, string? folderId, string assetId) => new()
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

        // MaterialFile → MediaAsset is a REQUIRED relationship; GetVisibleTreeAsync
        // does an AsNoTracking Include of it, which drops files whose principal is
        // missing. Seed the assets so the files actually load (prod always has them).
        MediaAsset Asset(string id) => new()
        {
            Id = id,
            OriginalFilename = $"{id}.pdf",
            MimeType = "application/pdf",
            Format = "pdf",
            StoragePath = $"/store/{id}.pdf",
            SizeBytes = 1024,
            Status = MediaAssetStatus.Ready,
        };
        db.MediaAssets.AddRange(Asset("asset-a"), Asset("asset-b"), Asset("asset-root"));

        db.MaterialFiles.AddRange(
            File("file-a", "f-grandchild-a", "asset-a"),
            File("file-b", "f-child-b", "asset-b"),
            File("file-root", null, "asset-root"));

        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> TreeJsonAsync(MaterialAccessService service, ClaimsPrincipal principal)
    {
        var tree = await service.GetVisibleTreeAsync(principal, default);
        var json = JsonSerializer.Serialize(tree);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static async Task<HashSet<string>> VisibleFolderNamesAsync(
        MaterialAccessService service, ClaimsPrincipal principal)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(await TreeJsonAsync(service, principal), names);
        return names;

        static void Collect(JsonElement element, HashSet<string> acc)
        {
            if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray()) Collect(item, acc);
            else if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("Name", out var name) && name.ValueKind == JsonValueKind.String)
                    acc.Add(name.GetString()!);
                if (element.TryGetProperty("folders", out var folders)) Collect(folders, acc);
            }
        }
    }

    private static async Task<HashSet<string>> VisibleFileIdsAsync(
        MaterialAccessService service, ClaimsPrincipal principal)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(await TreeJsonAsync(service, principal), ids);
        return ids;

        static void Collect(JsonElement element, HashSet<string> acc)
        {
            if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray()) Collect(item, acc);
            else if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
                    foreach (var file in files.EnumerateArray())
                        if (file.TryGetProperty("Id", out var id) && id.ValueKind == JsonValueKind.String)
                            acc.Add(id.GetString()!);
                if (element.TryGetProperty("folders", out var folders)) Collect(folders, acc);
            }
        }
    }

    [Fact]
    public async Task LearnerWithAllowList_SeesGrantedFolder_PlusAncestorsAndDescendants_Only()
    {
        await using var db = CreateDb();
        await SeedAsync(db, "learner-restricted");
        db.UserMaterialFolderAccesses.Add(new UserMaterialFolderAccess
        {
            Id = "ufa-1",
            UserId = "learner-restricted",
            FolderId = "f-child-a",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var principal = Principal("learner-restricted");

        var folderNames = await VisibleFolderNamesAsync(service, principal);
        Assert.Contains("Root", folderNames);          // ancestor — navigation only
        Assert.Contains("ChildA", folderNames);         // granted
        Assert.Contains("GrandchildA", folderNames);     // descendant
        Assert.DoesNotContain("ChildB", folderNames);    // unrelated sibling — hidden

        var fileIds = await VisibleFileIdsAsync(service, principal);
        Assert.Contains("file-a", fileIds);   // under a granted-descendant folder
        Assert.DoesNotContain("file-b", fileIds);    // under a non-granted folder
        Assert.DoesNotContain("file-root", fileIds); // root-level, hidden once restricted
    }

    [Fact]
    public async Task LearnerWithNoAllowList_IsUnchanged()
    {
        await using var db = CreateDb();
        await SeedAsync(db, "learner-unrestricted");
        // No UserMaterialFolderAccess rows added.

        var service = CreateService(db);
        var principal = Principal("learner-unrestricted");

        var folderNames = await VisibleFolderNamesAsync(service, principal);
        Assert.Contains("Root", folderNames);
        Assert.Contains("ChildA", folderNames);
        Assert.Contains("GrandchildA", folderNames);
        Assert.Contains("ChildB", folderNames);

        var fileIds = await VisibleFileIdsAsync(service, principal);
        Assert.Contains("file-a", fileIds);
        Assert.Contains("file-b", fileIds);
        // NOTE: root-level files (FolderId == null) are never rendered inside the folder
        // tree by BuildFolderNode (pre-existing behaviour — they surface via other paths),
        // so they are absent here regardless of the per-user allow-list.
        Assert.DoesNotContain("file-root", fileIds);
    }

    [Fact]
    public async Task DownloadAuth_DeniesNonGrantedFolder_AllowsGrantedDescendant_DeniesRootLevel()
    {
        await using var db = CreateDb();
        await SeedAsync(db, "learner-restricted-dl");
        db.UserMaterialFolderAccesses.Add(new UserMaterialFolderAccess
        {
            Id = "ufa-2",
            UserId = "learner-restricted-dl",
            FolderId = "f-child-a",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        Assert.True(await service.CanCandidateAccessMaterialFileAsync("learner-restricted-dl", "asset-a", default));
        Assert.False(await service.CanCandidateAccessMaterialFileAsync("learner-restricted-dl", "asset-b", default));
        Assert.False(await service.CanCandidateAccessMaterialFileAsync("learner-restricted-dl", "asset-root", default));
    }

    [Fact]
    public async Task Admin_BypassesFolderAllowList()
    {
        await using var db = CreateDb();
        await SeedAsync(db, "admin-1", role: ApplicationUserRoles.Admin);
        // Even if an allow-list row somehow exists for the admin, they still see everything.
        db.UserMaterialFolderAccesses.Add(new UserMaterialFolderAccess
        {
            Id = "ufa-3",
            UserId = "admin-1",
            FolderId = "f-child-a",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var principal = Principal("admin-1", ApplicationUserRoles.Admin);

        var folderNames = await VisibleFolderNamesAsync(service, principal);
        Assert.Contains("ChildB", folderNames);

        Assert.True(await service.CanCandidateAccessMaterialFileAsync("admin-1", "asset-b", default));
    }
}
