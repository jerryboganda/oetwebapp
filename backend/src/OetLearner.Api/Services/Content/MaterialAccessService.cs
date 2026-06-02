using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Services.Content;

/// <summary>
/// Resolves which MaterialFolders and MaterialFiles a candidate can see,
/// and whether a candidate can download a specific media asset via the
/// materials library.
/// </summary>
public sealed class MaterialAccessService(
    LearnerDbContext db,
    IEffectiveEntitlementResolver entitlementResolver)
{
    private const int MaxFolderDepth = 5;

    public bool IsAdmin(ClaimsPrincipal principal)
    {
        var role = principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        return string.Equals(role, ApplicationUserRoles.Admin, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the pruned visible tree for the authenticated candidate.
    /// Folders with no published file in their subtree are excluded.
    /// </summary>
    public async Task<IReadOnlyList<object>> GetVisibleTreeAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var allFolders = await db.MaterialFolders
            .AsNoTracking()
            .Include(f => f.Audiences)
            .Where(f => f.Status == ContentStatus.Published)
            .OrderBy(f => f.SortOrder)
            .ToListAsync(ct);

        var allFiles = await db.MaterialFiles
            .AsNoTracking()
            .Include(f => f.MediaAsset)
            .Where(f => f.Status == ContentStatus.Published)
            .OrderBy(f => f.SortOrder)
            .ToListAsync(ct);

        var (planId, planCode, cohortIds, sponsorIds) = await ResolveMembershipsAsync(userId, ct);

        var folderDict = allFolders.ToDictionary(f => f.Id);
        var visibleFolderIds = new HashSet<string>();

        foreach (var folder in allFolders)
        {
            if (IsFolderVisible(folder, folderDict, planId, planCode, cohortIds, sponsorIds))
                visibleFolderIds.Add(folder.Id);
        }

        // Root-level files (FolderId == null) are always visible
        var visibleFiles = allFiles
            .Where(f => f.FolderId == null || visibleFolderIds.Contains(f.FolderId))
            .ToList();

        // Prune: remove folders that have no published file anywhere in their subtree
        var foldersWithContent = BuildFoldersWithContent(visibleFolderIds, visibleFiles, folderDict);

        var rootFolders = allFolders
            .Where(f => f.ParentFolderId == null && foldersWithContent.Contains(f.Id))
            .Select(f => BuildFolderNode(f, allFolders, visibleFiles, foldersWithContent))
            .Cast<object>()
            .ToList();

        return rootFolders;
    }

    /// <summary>
    /// Returns true if the candidate is entitled to download any MaterialFile
    /// that references the given mediaAssetId.
    /// </summary>
    public async Task<bool> CanCandidateAccessMaterialFileAsync(string userId, string mediaAssetId, CancellationToken ct)
    {
        var files = await db.MaterialFiles
            .AsNoTracking()
            .Where(f => f.MediaAssetId == mediaAssetId && f.Status == ContentStatus.Published)
            .ToListAsync(ct);

        if (files.Count == 0) return false;

        // Root-level files are accessible to all authenticated candidates
        if (files.Any(f => f.FolderId == null)) return true;

        var folderIds = files.Select(f => f.FolderId!).Distinct().ToList();
        var folders = await db.MaterialFolders
            .AsNoTracking()
            .Include(f => f.Audiences)
            .Where(f => folderIds.Contains(f.Id) && f.Status == ContentStatus.Published)
            .ToListAsync(ct);

        if (folders.Count == 0) return false;

        // Also load all ancestor folders for the published-ancestors check
        var allFolders = await db.MaterialFolders
            .AsNoTracking()
            .Include(f => f.Audiences)
            .Where(f => f.Status == ContentStatus.Published)
            .ToListAsync(ct);

        var folderDict = allFolders.ToDictionary(f => f.Id);
        var (planId, planCode, cohortIds, sponsorIds) = await ResolveMembershipsAsync(userId, ct);

        return folders.Any(folder =>
            IsFolderVisible(folder, folderDict, planId, planCode, cohortIds, sponsorIds));
    }

    private bool IsFolderVisible(
        MaterialFolder folder,
        Dictionary<string, MaterialFolder> allFolders,
        string? planId,
        string? planCode,
        HashSet<string> cohortIds,
        HashSet<string> sponsorIds)
    {
        // Verify every ancestor is also published
        var current = folder;
        while (current.ParentFolderId != null)
        {
            if (!allFolders.TryGetValue(current.ParentFolderId, out var parent))
                return false;
            if (parent.Status != ContentStatus.Published)
                return false;
            current = parent;
        }

        var (effectiveMode, effectiveAudiences) = ResolveEffectiveAudience(folder, allFolders);

        return effectiveMode switch
        {
            MaterialAudienceMode.Everyone => true,
            MaterialAudienceMode.Restricted => MatchesAudience(effectiveAudiences, planId, planCode, cohortIds, sponsorIds),
            _ => false, // Inherit with no explicit ancestor → hidden (safe default)
        };
    }

    private static (MaterialAudienceMode mode, IEnumerable<MaterialFolderAudience> audiences) ResolveEffectiveAudience(
        MaterialFolder folder,
        Dictionary<string, MaterialFolder> allFolders)
    {
        var current = folder;
        while (true)
        {
            if (current.AudienceMode != MaterialAudienceMode.Inherit)
                return (current.AudienceMode, current.Audiences);

            if (current.ParentFolderId == null || !allFolders.TryGetValue(current.ParentFolderId, out var parent))
                return (MaterialAudienceMode.Inherit, []); // no explicit ancestor → hidden

            current = parent;
        }
    }

    private static bool MatchesAudience(
        IEnumerable<MaterialFolderAudience> audiences,
        string? planId,
        string? planCode,
        HashSet<string> cohortIds,
        HashSet<string> sponsorIds)
    {
        foreach (var row in audiences)
        {
            switch (row.TargetType)
            {
                case "plan":
                    if ((!string.IsNullOrWhiteSpace(planId) && row.TargetId == planId)
                        || (!string.IsNullOrWhiteSpace(planCode) && row.TargetId == planCode))
                        return true;
                    break;
                case "cohort":
                    if (cohortIds.Contains(row.TargetId))
                        return true;
                    break;
                case "institution":
                    if (sponsorIds.Contains(row.TargetId))
                        return true;
                    break;
            }
        }
        return false;
    }

    private async Task<(string? planId, string? planCode, HashSet<string> cohortIds, HashSet<string> sponsorIds)>
        ResolveMembershipsAsync(string userId, CancellationToken ct)
    {
        var entitlement = await entitlementResolver.ResolveAsync(userId, ct);

        var cohortIds = await db.CohortMembers
            .AsNoTracking()
            .Where(m => m.LearnerId == userId && m.Status == "active")
            .Select(m => m.CohortId)
            .ToListAsync(ct);

        var sponsorIds = await db.SponsorLearnerLinks
            .AsNoTracking()
            .Where(l => l.LearnerId == userId)
            .Select(l => l.SponsorId)
            .ToListAsync(ct);

        return (
            entitlement.PlanId,
            entitlement.PlanCode,
            new HashSet<string>(cohortIds),
            new HashSet<string>(sponsorIds)
        );
    }

    /// <summary>
    /// Returns the set of folder IDs that have at least one visible file
    /// anywhere in their subtree (direct or nested).
    /// </summary>
    private static HashSet<string> BuildFoldersWithContent(
        HashSet<string> visibleFolderIds,
        IEnumerable<MaterialFile> visibleFiles,
        Dictionary<string, MaterialFolder> folderDict)
    {
        // Seed: folders with at least one direct file
        var result = visibleFiles
            .Where(f => f.FolderId != null && visibleFolderIds.Contains(f.FolderId))
            .Select(f => f.FolderId!)
            .ToHashSet();

        // Propagate up: if a child has content, its parent does too
        bool changed;
        do
        {
            changed = false;
            foreach (var folderId in visibleFolderIds)
            {
                if (result.Contains(folderId)) continue;
                if (!folderDict.TryGetValue(folderId, out var folder)) continue;

                var childHasContent = visibleFolderIds
                    .Where(id => folderDict.TryGetValue(id, out var child) && child.ParentFolderId == folderId)
                    .Any(id => result.Contains(id));

                if (!childHasContent) continue;
                result.Add(folderId);
                changed = true;
            }
        } while (changed);

        return result;
    }

    private static object BuildFolderNode(
        MaterialFolder folder,
        List<MaterialFolder> allFolders,
        List<MaterialFile> visibleFiles,
        HashSet<string> foldersWithContent)
    {
        var childFolders = allFolders
            .Where(f => f.ParentFolderId == folder.Id && foldersWithContent.Contains(f.Id))
            .Select(f => BuildFolderNode(f, allFolders, visibleFiles, foldersWithContent))
            .ToList();

        var files = visibleFiles
            .Where(f => f.FolderId == folder.Id)
            .Select(f => (object)new
            {
                f.Id,
                f.Title,
                f.Description,
                f.SubtestCode,
                f.Kind,
                f.SortOrder,
                mediaAssetId = f.MediaAssetId,
                downloadUrl = $"/v1/media/{f.MediaAssetId}/content",
                sizeBytes = f.MediaAsset?.SizeBytes,
                originalFilename = f.MediaAsset?.OriginalFilename,
            })
            .ToList();

        return new
        {
            folder.Id,
            folder.Name,
            folder.Description,
            folder.SubtestCode,
            folder.SortOrder,
            folders = childFolders,
            files,
        };
    }

    /// <summary>Validates folder nesting depth. Returns the depth (0 = root).</summary>
    public async Task<int> GetFolderDepthAsync(string? parentFolderId, CancellationToken ct)
    {
        if (parentFolderId == null) return 0;
        var depth = 1;
        var currentId = parentFolderId;
        while (currentId != null && depth <= MaxFolderDepth)
        {
            var parent = await db.MaterialFolders.AsNoTracking()
                .Where(f => f.Id == currentId)
                .Select(f => new { f.ParentFolderId })
                .FirstOrDefaultAsync(ct);
            if (parent == null) break;
            currentId = parent.ParentFolderId;
            depth++;
        }
        return depth;
    }
}
