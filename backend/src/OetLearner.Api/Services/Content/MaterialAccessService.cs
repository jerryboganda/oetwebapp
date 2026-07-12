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
    private const int MaxFolderDepth = 8;

    public bool IsAdmin(ClaimsPrincipal principal)
    {
        var role = principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        return string.Equals(role, ApplicationUserRoles.Admin, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the visible tree for the authenticated candidate.
    /// Every published, accessible folder is included — even empty ones.
    /// </summary>
    public async Task<IReadOnlyList<object>> GetVisibleTreeAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // Admin-togglable module gate (owner directive 2026-07-11). Materials is gated like the
        // other subscription modules: admins always see the tree; every other learner needs an
        // eligible subscription whose plan has the "MaterialsLibrary" module enabled. Fail-open
        // module semantics (see EffectiveEntitlementSnapshot.IsModuleEnabled) mean legacy plans
        // with no module list keep working until an admin explicitly disables Materials.
        var entitlement = await entitlementResolver.ResolveAsync(userId, ct);
        if (!IsAdmin(principal)
            && (!entitlement.HasEligibleSubscription || !entitlement.IsModuleEnabled(ModuleKeys.MaterialsLibrary)))
        {
            return Array.Empty<object>();
        }

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

        var (planId, planCode, planDatabaseId, cohortIds, sponsorIds) = await ResolveMembershipsAsync(entitlement, userId, ct);

        var folderDict = allFolders.ToDictionary(f => f.Id);
        var visibleFolderIds = new HashSet<string>();

        // Per-user Materials folder allow-list (Phase E — restriction-within-plan,
        // owner directive). A learner with ANY UserMaterialFolderAccess rows is
        // restricted to those folders — plus ANCESTORS (so the tree stays
        // navigable down to a granted folder) and DESCENDANTS (so content under a
        // granted folder is included). No rows == unchanged. Admins bypass,
        // mirroring every other gate in this service.
        HashSet<string>? navigableFolderScope = null;
        HashSet<string>? contentFolderScope = null;
        if (!IsAdmin(principal))
        {
            var allowedFolderIds = await db.UserMaterialFolderAccesses
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .Select(x => x.FolderId)
                .ToListAsync(ct);
            if (allowedFolderIds.Count > 0)
            {
                (navigableFolderScope, contentFolderScope) = ComputeUserFolderScope(allowedFolderIds, folderDict);
            }
        }

        // Discipline (profession) filtering — owner directive 2026-07-12. The Materials tree
        // encodes discipline as folder names (Speaking/Medicine, Writing/Nursing, …) with no
        // structured column, so we match those names against the learner's ActiveProfessionId:
        // a learner sees only their own discipline's folders, while Reading/Listening and every
        // non-discipline folder stay visible to all. Admins and learners with no profession are
        // unfiltered (fail-open).
        HashSet<string> disciplineUniverse;
        HashSet<string> learnerDisciplines;
        if (IsAdmin(principal))
        {
            // Admins see every discipline — leave both sets empty so IsDisciplineVisible is a no-op.
            disciplineUniverse = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            learnerDisciplines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            (disciplineUniverse, learnerDisciplines) = await ResolveDisciplineFilterAsync(userId, ct);
        }

        foreach (var folder in allFolders)
        {
            if (IsFolderVisible(folder, folderDict, planId, planCode, planDatabaseId, cohortIds, sponsorIds)
                && IsDisciplineVisible(folder, folderDict, disciplineUniverse, learnerDisciplines)
                && (navigableFolderScope is null || navigableFolderScope.Contains(folder.Id)))
                visibleFolderIds.Add(folder.Id);
        }

        // Root-level files (FolderId == null) are always visible — UNLESS a per-user
        // folder allow-list restriction is active, in which case they aren't under
        // any granted folder and are hidden.
        var visibleFiles = allFiles
            .Where(f => contentFolderScope is null
                ? (f.FolderId == null || visibleFolderIds.Contains(f.FolderId))
                : (f.FolderId != null && visibleFolderIds.Contains(f.FolderId) && contentFolderScope.Contains(f.FolderId)))
            .ToList();

        var rootFolders = allFolders
            .Where(f => f.ParentFolderId == null && visibleFolderIds.Contains(f.Id))
            .Select(f => BuildFolderNode(f, allFolders, visibleFiles, visibleFolderIds))
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
        var entitlement = await entitlementResolver.ResolveAsync(userId, ct);

        // Admin-togglable module gate (fail-open): a plan that explicitly disables the
        // "MaterialsLibrary" module withholds all materials downloads. No new subscription
        // requirement is imposed here (unlike the visible tree) so admin/preview media paths and
        // legacy audience-based access are preserved; the empty-tree gate already hides materials
        // from ineligible learners.
        if (!entitlement.IsModuleEnabled(ModuleKeys.MaterialsLibrary)) return false;

        var files = await db.MaterialFiles
            .AsNoTracking()
            .Where(f => f.MediaAssetId == mediaAssetId && f.Status == ContentStatus.Published)
            .ToListAsync(ct);

        if (files.Count == 0) return false;

        // Per-user Materials folder allow-list (Phase E — restriction-within-plan).
        // A learner with ANY UserMaterialFolderAccess rows is restricted to those
        // folders and their descendants; admins bypass (mirrors the IsAdmin
        // short-circuit used by GetVisibleTreeAsync — this overload only receives
        // a userId, so the role is looked up directly).
        var allowedFolderIds = await db.UserMaterialFolderAccesses
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.FolderId)
            .ToListAsync(ct);

        var hasFolderRestriction = false;
        if (allowedFolderIds.Count > 0)
        {
            var role = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.Role)
                .FirstOrDefaultAsync(ct);
            hasFolderRestriction = !string.Equals(role, ApplicationUserRoles.Admin, StringComparison.OrdinalIgnoreCase);
        }

        if (hasFolderRestriction)
        {
            // Root-level files aren't under any granted folder — hide them outright.
            files = files.Where(f => f.FolderId != null).ToList();
            if (files.Count == 0) return false;
        }
        else if (files.Any(f => f.FolderId == null))
        {
            // Root-level files are accessible to all authenticated candidates.
            return true;
        }

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
        var (planId, planCode, planDatabaseId, cohortIds, sponsorIds) = await ResolveMembershipsAsync(entitlement, userId, ct);
        // Mirror the tree's discipline gate so a learner cannot download another discipline's
        // file by guessing its mediaAssetId (defence in depth alongside GetVisibleTreeAsync).
        var (disciplineUniverse, learnerDisciplines) = await ResolveDisciplineFilterAsync(userId, ct);

        HashSet<string>? contentFolderScope = null;
        if (hasFolderRestriction)
        {
            (_, contentFolderScope) = ComputeUserFolderScope(allowedFolderIds, folderDict);
        }

        return folders.Any(folder =>
            IsFolderVisible(folder, folderDict, planId, planCode, planDatabaseId, cohortIds, sponsorIds)
            && IsDisciplineVisible(folder, folderDict, disciplineUniverse, learnerDisciplines)
            && (contentFolderScope is null || contentFolderScope.Contains(folder.Id)));
    }

    private bool IsFolderVisible(
        MaterialFolder folder,
        Dictionary<string, MaterialFolder> allFolders,
        string? planId,
        string? planCode,
        string? planDatabaseId,
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
            MaterialAudienceMode.Restricted => MatchesAudience(effectiveAudiences, planId, planCode, planDatabaseId, cohortIds, sponsorIds),
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
        string? planDatabaseId,
        HashSet<string> cohortIds,
        HashSet<string> sponsorIds)
    {
        foreach (var row in audiences)
        {
            switch (row.TargetType)
            {
                case "plan":
                    // Compare against subscription.PlanId (usually the plan Code),
                    // the normalised plan Code, AND the BillingPlan.Id (UUID/slug).
                    // The admin audience picker stores BillingPlan.Id; the entitlement
                    // snapshot carries subscription.PlanId (= BillingPlan.Code in most
                    // deployments). Accept any of the three to tolerate either format.
                    if ((!string.IsNullOrWhiteSpace(planId) && row.TargetId == planId)
                        || (!string.IsNullOrWhiteSpace(planCode) && row.TargetId == planCode)
                        || (!string.IsNullOrWhiteSpace(planDatabaseId) && row.TargetId == planDatabaseId))
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

    private async Task<(string? planId, string? planCode, string? planDatabaseId, HashSet<string> cohortIds, HashSet<string> sponsorIds)>
        ResolveMembershipsAsync(EffectiveEntitlementSnapshot entitlement, string userId, CancellationToken ct)
    {
        // Resolve the BillingPlan.Id for the user's plan.
        // The entitlement snapshot carries subscription.PlanId which is typically
        // the plan's Code (e.g. "premium-yearly"), but the admin audience picker
        // stores BillingPlan.Id (e.g. "plan-premium-yearly"). We look up both so
        // MatchesAudience can compare against whichever value was stored.
        string? planDatabaseId = null;
        if (!string.IsNullOrWhiteSpace(entitlement.PlanId))
        {
            var normalised = entitlement.PlanId.Trim().ToLowerInvariant();
            planDatabaseId = await db.BillingPlans
                .AsNoTracking()
                .Where(p => p.Id.ToLower() == normalised || p.Code.ToLower() == normalised)
                .Select(p => p.Id)
                .FirstOrDefaultAsync(ct);
        }

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
            planDatabaseId,
            new HashSet<string>(cohortIds),
            new HashSet<string>(sponsorIds)
        );
    }

    /// <summary>
    /// Resolves the name-based discipline filter for a learner. Returns:
    ///   <c>universe</c> — every profession's Id and Label (from the <c>Professions</c> reference
    ///     table); the set of folder names that count as a "discipline folder";
    ///   <c>learner</c> — the Id and Label of the learner's own <c>ActiveProfessionId</c>, or an
    ///     empty set when the learner has no/unknown profession (caller treats empty as unfiltered).
    /// The Materials tree encodes discipline as folder names (Speaking/Medicine, Writing/Nursing …)
    /// with no structured column, so matching is by name (owner directive 2026-07-12).
    /// </summary>
    private async Task<(HashSet<string> universe, HashSet<string> learner)> ResolveDisciplineFilterAsync(
        string userId, CancellationToken ct)
    {
        var professions = await db.Professions
            .AsNoTracking()
            .Select(p => new { p.Id, p.Label })
            .ToListAsync(ct);

        var universe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in professions)
        {
            if (!string.IsNullOrWhiteSpace(p.Id)) universe.Add(p.Id.Trim());
            if (!string.IsNullOrWhiteSpace(p.Label)) universe.Add(p.Label.Trim());
        }

        var active = (await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.ActiveProfessionId)
            .FirstOrDefaultAsync(ct))?.Trim();

        var learner = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(active))
        {
            var row = professions.FirstOrDefault(p =>
                string.Equals(p.Id, active, StringComparison.OrdinalIgnoreCase));
            if (row is not null)
            {
                if (!string.IsNullOrWhiteSpace(row.Id)) learner.Add(row.Id.Trim());
                if (!string.IsNullOrWhiteSpace(row.Label)) learner.Add(row.Label.Trim());
            }
            else
            {
                // Profession set but absent from the reference table — still match by its raw id.
                learner.Add(active);
            }
        }

        return (universe, learner);
    }

    /// <summary>
    /// Name-based discipline gate. A folder (or any of its ancestors) whose name is a known
    /// discipline that is NOT the learner's own is hidden; non-discipline folders (Reading,
    /// Listening, subtest parents, source folders …) are always visible. An empty
    /// <paramref name="learnerDisciplines"/> (admin, or learner with no profession) disables the
    /// filter (fail-open). Walking ancestors ensures a non-discipline child under an excluded
    /// discipline parent is also dropped.
    /// </summary>
    private static bool IsDisciplineVisible(
        MaterialFolder folder,
        Dictionary<string, MaterialFolder> allFolders,
        HashSet<string> disciplineUniverse,
        HashSet<string> learnerDisciplines)
    {
        if (learnerDisciplines.Count == 0) return true;

        var current = folder;
        var guard = 0;
        while (current is not null && guard++ < 64)
        {
            var name = current.Name?.Trim() ?? string.Empty;
            if (disciplineUniverse.Contains(name) && !learnerDisciplines.Contains(name))
                return false;

            if (current.ParentFolderId is null) break;
            allFolders.TryGetValue(current.ParentFolderId, out current);
        }

        return true;
    }

    /// <summary>
    /// Computes the per-user Materials folder allow-list scope from a learner's
    /// granted <see cref="UserMaterialFolderAccess"/> folder ids (Phase E —
    /// restriction-within-plan). Walks the adjacency list
    /// (<see cref="MaterialFolder.ParentFolderId"/>) to return two sets:
    ///   <c>navigable</c> — granted folders + their ANCESTORS (so the tree stays
    ///     navigable down to a granted folder) + their DESCENDANTS. Gates folder
    ///     visibility (<see cref="GetVisibleTreeAsync"/>).
    ///   <c>content</c> — granted folders + their DESCENDANTS only (NOT
    ///     ancestors — an ancestor is navigation-only and must not expose its own
    ///     files unless independently granted). Gates file visibility/download
    ///     in both <see cref="GetVisibleTreeAsync"/> and
    ///     <see cref="CanCandidateAccessMaterialFileAsync"/>.
    /// </summary>
    private static (HashSet<string> navigable, HashSet<string> content) ComputeUserFolderScope(
        IReadOnlyCollection<string> allowedFolderIds,
        Dictionary<string, MaterialFolder> allFolders)
    {
        var navigable = new HashSet<string>(allowedFolderIds);
        var content = new HashSet<string>(allowedFolderIds);

        // Ancestors — navigation only.
        foreach (var id in allowedFolderIds)
        {
            if (!allFolders.TryGetValue(id, out var folder)) continue;
            var current = folder;
            var guard = 0;
            while (current.ParentFolderId is not null && guard++ < 64)
            {
                if (!allFolders.TryGetValue(current.ParentFolderId, out var parent)) break;
                navigable.Add(parent.Id);
                current = parent;
            }
        }

        // Descendants — navigation + content. Build a children-by-parent lookup
        // once, then breadth-first walk down from every granted folder.
        var childrenByParent = new Dictionary<string, List<MaterialFolder>>();
        foreach (var folder in allFolders.Values)
        {
            if (folder.ParentFolderId is null) continue;
            if (!childrenByParent.TryGetValue(folder.ParentFolderId, out var siblings))
            {
                siblings = new List<MaterialFolder>();
                childrenByParent[folder.ParentFolderId] = siblings;
            }
            siblings.Add(folder);
        }

        var queue = new Queue<string>(allowedFolderIds);
        var visited = new HashSet<string>(allowedFolderIds);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!childrenByParent.TryGetValue(id, out var children)) continue;
            foreach (var child in children)
            {
                if (!visited.Add(child.Id)) continue;
                navigable.Add(child.Id);
                content.Add(child.Id);
                queue.Enqueue(child.Id);
            }
        }

        return (navigable, content);
    }

    private static object BuildFolderNode(
        MaterialFolder folder,
        List<MaterialFolder> allFolders,
        List<MaterialFile> visibleFiles,
        HashSet<string> visibleFolderIds)
    {
        var childFolders = allFolders
            .Where(f => f.ParentFolderId == folder.Id && visibleFolderIds.Contains(f.Id))
            .Select(f => BuildFolderNode(f, allFolders, visibleFiles, visibleFolderIds))
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
