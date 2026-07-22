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
        // non-discipline folder stay visible to all. Admins are unfiltered; a learner with no
        // profession now fails CLOSED on discipline-tagged folders (spec §7.5).
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

        // Subtest × profession content scope (spec §3) — the plan's IncludedSubtests axis plus
        // its per-plan folder include/exclude overrides. Admins bypass; for everyone else an
        // unscoped plan resolves to "all subtests, no overrides", so this is a no-op until a
        // plan actually declares a scope.
        var scopeEntitlement = IsAdmin(principal) ? null : entitlement;

        // General English separation (course-materials diagram, owner directive 2026-07-18): the
        // Basic English Course tree is its own product's materials — visible only with a Basic
        // English grant, and a standalone Basic English holder sees ONLY that tree.
        var basicEnglishScope = IsAdmin(principal)
            ? BasicEnglishScope.Unfiltered
            : ResolveBasicEnglishScope(entitlement);

        foreach (var folder in allFolders)
        {
            if (IsFolderVisible(folder, folderDict, planId, planCode, planDatabaseId, cohortIds, sponsorIds)
                && IsContentScopeVisible(folder, folderDict, scopeEntitlement, disciplineUniverse, learnerDisciplines, basicEnglishScope)
                && (navigableFolderScope is null || navigableFolderScope.Contains(folder.Id)))
                visibleFolderIds.Add(folder.Id);
        }

        // Root-level files (FolderId == null) carry no folder, hence no audience and no
        // discipline/subtest tag of their own — like an untagged video they are intentionally
        // neutral, and the subscription + module gate at the top of this method has already run.
        // They are hidden when a per-user folder allow-list restriction is active, since they sit
        // under no granted folder. A file's own SubtestCode still has to be in the plan's scope.
        var visibleFiles = allFiles
            .Where(f => contentFolderScope is null
                ? (f.FolderId == null || visibleFolderIds.Contains(f.FolderId))
                : (f.FolderId != null && visibleFolderIds.Contains(f.FolderId) && contentFolderScope.Contains(f.FolderId)))
            .Where(f => IsSubtestInScope(f.SubtestCode, scopeEntitlement))
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
        // This overload only receives a userId, so the role is resolved directly (the tree path
        // short-circuits on the ClaimsPrincipal instead). Admins bypass every learner gate below,
        // mirroring GetVisibleTreeAsync.
        var role = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Role)
            .FirstOrDefaultAsync(ct);
        var isAdmin = string.Equals(role, ApplicationUserRoles.Admin, StringComparison.OrdinalIgnoreCase);

        var entitlement = await entitlementResolver.ResolveAsync(userId, ct);

        // Subscription + module gate — MIRRORS the tree gate (spec §7.1). Downloads previously
        // required only IsModuleEnabled, which is FAIL-OPEN on an empty module list; an expired
        // subscription normalises to exactly that empty list, so every material stayed
        // downloadable after expiry. HasEligibleSubscription is what closes it.
        if (!isAdmin
            && (!entitlement.HasEligibleSubscription
                || !entitlement.IsModuleEnabled(ModuleKeys.MaterialsLibrary)))
        {
            return false;
        }

        var files = await db.MaterialFiles
            .AsNoTracking()
            .Where(f => f.MediaAssetId == mediaAssetId && f.Status == ContentStatus.Published)
            .ToListAsync(ct);

        if (files.Count == 0) return false;

        // Per-user Materials folder allow-list (Phase E — restriction-within-plan).
        // A learner with ANY UserMaterialFolderAccess rows is restricted to those
        // folders and their descendants; admins bypass.
        var allowedFolderIds = await db.UserMaterialFolderAccesses
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.FolderId)
            .ToListAsync(ct);

        var hasFolderRestriction = allowedFolderIds.Count > 0 && !isAdmin;

        // Subtest × profession content scope (spec §3). Admins bypass; an unscoped plan resolves
        // to "all subtests, no overrides".
        var scopeEntitlement = isAdmin ? null : entitlement;

        // Mirror the tree's General English gate (defence in depth alongside GetVisibleTreeAsync).
        // Resolved BEFORE the root-level-file branch: root files can never belong to the
        // folder-rooted Basic English tree, so an exclusively-Basic-English learner must not
        // reach them either.
        var basicEnglishScope = isAdmin
            ? BasicEnglishScope.Unfiltered
            : ResolveBasicEnglishScope(entitlement);

        if (!hasFolderRestriction
            && !basicEnglishScope.ExclusivelyBasicEnglish
            && files.Any(f => f.FolderId == null && IsSubtestInScope(f.SubtestCode, scopeEntitlement)))
        {
            // Root-level files carry no folder, so no audience or discipline tag applies to them —
            // they are neutral content, NOT a bypass: the subscription + module gate above has
            // already run (spec §7.2 — this used to return true before any of those checks). Under
            // a per-user folder allow-list they sit beneath no granted folder, so they stay hidden.
            return true;
        }

        // Every remaining candidate is folder-bound; a root-level file that got here is out of
        // scope (or restricted away) and must not leak through the folder checks below.
        files = files.Where(f => f.FolderId != null).ToList();
        if (files.Count == 0) return false;

        // Every published folder (not just the candidates') — the ancestor walks in
        // IsFolderVisible / IsDisciplineVisible / IsContentScopeVisible need the whole chain.
        var allFolders = await db.MaterialFolders
            .AsNoTracking()
            .Include(f => f.Audiences)
            .Where(f => f.Status == ContentStatus.Published)
            .ToListAsync(ct);

        var folderDict = allFolders.ToDictionary(f => f.Id);
        var (planId, planCode, planDatabaseId, cohortIds, sponsorIds) = await ResolveMembershipsAsync(entitlement, userId, ct);
        // Mirror the tree's discipline gate so a learner cannot download another discipline's
        // file by guessing its mediaAssetId (defence in depth alongside GetVisibleTreeAsync).
        var (disciplineUniverse, learnerDisciplines) = isAdmin
            ? (new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase))
            : await ResolveDisciplineFilterAsync(userId, ct);

        HashSet<string>? contentFolderScope = null;
        if (hasFolderRestriction)
        {
            (_, contentFolderScope) = ComputeUserFolderScope(allowedFolderIds, folderDict);
        }

        // Evaluated per FILE (not per folder) so the file's own SubtestCode is checked against the
        // same folder that grants it.
        return files.Any(file =>
            folderDict.TryGetValue(file.FolderId!, out var folder)
            && IsFolderVisible(folder, folderDict, planId, planCode, planDatabaseId, cohortIds, sponsorIds)
            && IsContentScopeVisible(folder, folderDict, scopeEntitlement, disciplineUniverse, learnerDisciplines, basicEnglishScope)
            && IsSubtestInScope(file.SubtestCode, scopeEntitlement)
            && (contentFolderScope is null || contentFolderScope.Contains(folder.Id)));
    }

    /// <summary>Audience gate for one folder. Static + internal so
    /// <see cref="Billing.PlanContentAvailabilityService"/> can ask the same question of a plan
    /// (rather than of a signed-in learner) without a second implementation drifting from this one.</summary>
    internal static bool IsFolderVisible(
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
    /// <paramref name="learnerDisciplines"/> means the learner has no/unknown profession and now
    /// fails CLOSED on every discipline-tagged folder (spec §7.5 — it used to disable the filter
    /// entirely, so a profession-less learner saw every discipline; this matches how video
    /// targeting already behaves). Admins are unfiltered by being passed an EMPTY
    /// <paramref name="disciplineUniverse"/>, which no folder name can match. Walking ancestors
    /// ensures a non-discipline child under an excluded discipline parent is also dropped.
    /// </summary>
    internal static bool IsDisciplineVisible(
        MaterialFolder folder,
        Dictionary<string, MaterialFolder> allFolders,
        HashSet<string> disciplineUniverse,
        HashSet<string> learnerDisciplines)
    {
        if (disciplineUniverse.Count == 0) return true;

        var current = folder;
        var guard = 0;
        while (current is not null && guard++ < 64)
        {
            if (current.ScopeKind == MaterialScopeKinds.Profession)
                return !string.IsNullOrWhiteSpace(current.ProfessionId)
                    && learnerDisciplines.Contains(current.ProfessionId);
            if (current.ScopeKind is MaterialScopeKinds.Shared or MaterialScopeKinds.GeneralEnglish)
                return true;

            var name = current.Name?.Trim() ?? string.Empty;
            if (disciplineUniverse.Contains(name) && !learnerDisciplines.Contains(name))
                return false;

            if (current.ParentFolderId is null) break;
            allFolders.TryGetValue(current.ParentFolderId, out current);
        }

        return true;
    }

    /// <summary>
    /// Folder names that mark the General English (Basic English Course) tree — matched by name
    /// (self or ancestor), exactly like the discipline filter, because the tree has no structured
    /// column. Course-materials diagram (owner directive 2026-07-18): the six OET professions
    /// share Listening/Reading and split Writing/Speaking, while General English "has its own
    /// separate materials" — so this tree is NOT part of the shared pool.
    /// </summary>
    internal static readonly HashSet<string> BasicEnglishFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Basic English Course",
        "Basic English",
        "General English",
        "Academic / General English",
    };

    /// <summary>
    /// The full module cluster the standalone basic-english plan grants — its own course modules
    /// plus the four universally-backfilled admin toggles (migration 20260725090000 appended those
    /// to every plan). Exclusivity means the learner's ENTIRE module set stays inside this
    /// cluster: any other key (Listening, Writing, SpeakingSession, TutorBook, ModelLetters, …) is
    /// evidence of another content package, so gaining Basic English on top of another product
    /// never REMOVES access the other product granted. Keep in sync with the basic-english entry
    /// in <c>Data/Seeds/oet-2026-catalog.json</c>.
    /// </summary>
    private static readonly HashSet<string> BasicEnglishCourseModules = new(StringComparer.OrdinalIgnoreCase)
    {
        ModuleKeys.BasicEnglish, "Vocabulary", "Grammar", "ListeningFoundations", "StudyPlan", "Booklet",
        ModuleKeys.Recalls, ModuleKeys.MaterialsLibrary, ModuleKeys.VideoLibrary, ModuleKeys.Mocks,
    };

    /// <summary>True when the folder or any ancestor is the General English tree root.</summary>
    internal static bool IsBasicEnglishFolder(MaterialFolder folder, Dictionary<string, MaterialFolder> allFolders)
    {
        var current = folder;
        var guard = 0;
        while (current is not null && guard++ < 64)
        {
            if (current.ScopeKind == MaterialScopeKinds.GeneralEnglish) return true;
            if (current.ScopeKind is MaterialScopeKinds.Shared or MaterialScopeKinds.Profession) return false;
            if (BasicEnglishFolderNames.Contains(current.Name?.Trim() ?? string.Empty)) return true;
            if (current.ParentFolderId is null) break;
            allFolders.TryGetValue(current.ParentFolderId, out current);
        }

        return false;
    }

    /// <summary>
    /// General English visibility scope. <c>Entitled</c> — may see the Basic English tree;
    /// <c>ExclusivelyBasicEnglish</c> — holds Basic English WITHOUT any OET subtest module
    /// (the standalone basic-english plan), so every non-Basic-English folder is hidden.
    /// <see cref="Unfiltered"/> (admins) sees both sides.
    /// </summary>
    internal readonly record struct BasicEnglishScope(bool Entitled, bool ExclusivelyBasicEnglish)
    {
        public static readonly BasicEnglishScope Unfiltered = new(true, false);

        public bool IsFolderVisible(bool isBasicEnglishFolder) =>
            isBasicEnglishFolder ? Entitled : !ExclusivelyBasicEnglish;
    }

    /// <summary>
    /// Resolves the learner-side <see cref="BasicEnglishScope"/> from the entitlement snapshot.
    /// The raw grant comes from the "BasicEnglish" plan module (fail-open on legacy empty module
    /// lists), or the purchase-derived
    /// <see cref="EffectiveEntitlementSnapshot.BasicEnglishUnlocked"/> bundle flag. A per-user
    /// admin DISABLE of "BasicEnglish" strips <c>Entitled</c> but deliberately NOT
    /// <c>ExclusivelyBasicEnglish</c>: revoking a standalone Basic English learner's only product
    /// must fail CLOSED (they see nothing), not fall open into the whole OET pool.
    /// Exclusivity requires every enabled module to sit inside
    /// <see cref="BasicEnglishCourseModules"/> — a learner who ALSO holds any other package
    /// (subtest modules, SpeakingSession, TutorBook, …) keeps the shared OET tree. A legacy
    /// empty-module plan is never exclusive (fail-open, unchanged behaviour).
    /// </summary>
    internal static BasicEnglishScope ResolveBasicEnglishScope(EffectiveEntitlementSnapshot entitlement)
    {
        var rawGrant = entitlement.EnabledModules.Count == 0
            || entitlement.EnabledModules.Any(m => string.Equals(m, ModuleKeys.BasicEnglish, StringComparison.OrdinalIgnoreCase))
            || entitlement.BasicEnglishUnlocked;

        var explicitlyDisabled = entitlement.DisabledModules
            .Any(m => string.Equals(m, ModuleKeys.BasicEnglish, StringComparison.OrdinalIgnoreCase));

        var exclusively = rawGrant
            && entitlement.EnabledModules.Count > 0
            && entitlement.EnabledModules.All(BasicEnglishCourseModules.Contains);

        return new BasicEnglishScope(rawGrant && !explicitlyDisabled, exclusively);
    }

    /// <summary>
    /// Plan-side flavour for <see cref="Billing.PlanContentAvailabilityService"/>: modules are the
    /// plan's EXPLICIT list (that service already treats a legacy no-module plan as granting no
    /// content module, so fail-open never reaches here). Same exclusivity rule as the learner
    /// overload: any module outside <see cref="BasicEnglishCourseModules"/> keeps the OET tree
    /// countable, so e.g. a session plan with BundledBasicEnglish ticked never under-counts.
    /// </summary>
    internal static BasicEnglishScope ResolveBasicEnglishScope(
        IReadOnlyList<string> explicitModules, bool bundledBasicEnglish)
    {
        var entitled = bundledBasicEnglish
            || explicitModules.Any(m => string.Equals(m, ModuleKeys.BasicEnglish, StringComparison.OrdinalIgnoreCase));
        var exclusively = entitled
            && explicitModules.Count > 0
            && explicitModules.All(BasicEnglishCourseModules.Contains);
        return new BasicEnglishScope(entitled, exclusively);
    }

    /// <summary>
    /// Subtest × profession content scope for a folder (access &amp; payment spec §3): the
    /// discipline gate, the General English gate, the plan's IncludedSubtests axis, and its
    /// per-plan folder include/exclude overrides. The audience, module and subscription gates are
    /// the caller's job — an override never bypasses those. An explicit include DOES win over the
    /// discipline, General English and subtest scope; an exclude drops the folder. A null
    /// <paramref name="entitlement"/> (admin) skips the plan-derived scope, an admin's empty
    /// <paramref name="disciplineUniverse"/> already neutralises the discipline gate, and admins
    /// pass <see cref="BasicEnglishScope.Unfiltered"/>.
    /// </summary>
    private static bool IsContentScopeVisible(
        MaterialFolder folder,
        Dictionary<string, MaterialFolder> allFolders,
        EffectiveEntitlementSnapshot? entitlement,
        HashSet<string> disciplineUniverse,
        HashSet<string> learnerDisciplines,
        BasicEnglishScope basicEnglishScope)
    {
        if (entitlement is not null
            && ResolveFolderOverride(folder, allFolders, entitlement.ContentOverrides) is bool decided)
        {
            return decided;
        }

        return basicEnglishScope.IsFolderVisible(IsBasicEnglishFolder(folder, allFolders))
            && IsDisciplineVisible(folder, allFolders, disciplineUniverse, learnerDisciplines)
            && IsSubtestInScope(ResolveEffectiveSubtest(folder, allFolders), entitlement);
    }

    /// <summary>
    /// Nearest per-plan content override on the folder or any ANCESTOR (so excluding a parent
    /// excludes its whole subtree): true = explicitly included, false = explicitly excluded,
    /// null = no override applies and the normal scope decides. Internal so
    /// <see cref="Billing.PlanContentAvailabilityService"/> reads overrides exactly as the
    /// learner gate does.
    /// </summary>
    internal static bool? ResolveFolderOverride(
        MaterialFolder folder,
        Dictionary<string, MaterialFolder> allFolders,
        ContentOverrideSets overrides)
    {
        if (overrides.IsEmpty) return null;

        var current = folder;
        var guard = 0;
        while (current is not null && guard++ < 64)
        {
            if (overrides.MaterialFolderIncludes.Contains(current.Id)) return true;
            if (overrides.MaterialFolderExcludes.Contains(current.Id)) return false;
            if (current.ParentFolderId is null) break;
            allFolders.TryGetValue(current.ParentFolderId, out current);
        }

        return null;
    }

    /// <summary>
    /// The subtest axis of the content model. An untagged item (no SubtestCode of its own or of
    /// any ancestor) is not subtest-restricted and stays in scope, mirroring how an untagged video
    /// unlocks under any premium grant. A null <paramref name="entitlement"/> (admin) is a no-op.
    /// </summary>
    private static bool IsSubtestInScope(string? subtestCode, EffectiveEntitlementSnapshot? entitlement)
    {
        if (entitlement is null || entitlement.AllSubtestsIncluded) return true;
        if (string.IsNullOrWhiteSpace(subtestCode)) return true;
        return entitlement.IncludedSubtests.Contains(subtestCode.Trim());
    }

    /// <summary>Nearest SubtestCode walking up from the folder (folders inherit their parent's
    /// subtest — the tree is Writing/Medicine, Speaking/Nursing, …).</summary>
    internal static string? ResolveEffectiveSubtest(
        MaterialFolder folder,
        Dictionary<string, MaterialFolder> allFolders)
    {
        var current = folder;
        var guard = 0;
        while (current is not null && guard++ < 64)
        {
            if (!string.IsNullOrWhiteSpace(current.SubtestCode)) return current.SubtestCode;
            if (current.ParentFolderId is null) return null;
            allFolders.TryGetValue(current.ParentFolderId, out current);
        }
        return null;
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
