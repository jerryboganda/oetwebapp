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
                && IsDisciplineVisible(folder, folderDict, disciplineUniverse, learnerDisciplines))
                visibleFolderIds.Add(folder.Id);
        }

        // Root-level files (FolderId == null) are always visible
        var visibleFiles = allFiles
            .Where(f => f.FolderId == null || visibleFolderIds.Contains(f.FolderId))
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
        var (planId, planCode, planDatabaseId, cohortIds, sponsorIds) = await ResolveMembershipsAsync(entitlement, userId, ct);
        // Mirror the tree's discipline gate so a learner cannot download another discipline's
        // file by guessing its mediaAssetId (defence in depth alongside GetVisibleTreeAsync).
        var (disciplineUniverse, learnerDisciplines) = await ResolveDisciplineFilterAsync(userId, ct);

        return folders.Any(folder =>
            IsFolderVisible(folder, folderDict, planId, planCode, planDatabaseId, cohortIds, sponsorIds)
            && IsDisciplineVisible(folder, folderDict, disciplineUniverse, learnerDisciplines));
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
