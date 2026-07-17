using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Content;
using OetWithDrHesham.Api.Services.Entitlements;
using OetWithDrHesham.Api.Services.VideoLibrary;

namespace OetWithDrHesham.Api.Services.Billing;

// ═════════════════════════════════════════════════════════════════════════════
// "Does this plan actually reach any content for this profession?" (access &
// payment spec §3). Checkout calls this to block plan × profession combos that
// would resolve to an empty library — Physiotherapy has zero videos today, so a
// Physiotherapy learner buying a Videos plan would pay for nothing.
//
// This answers the SAME question the learner-side gates answer, using their code
// (MaterialAccessService.IsFolderVisible / IsDisciplineVisible /
// ResolveEffectiveSubtest, VideoLibraryLearnerService.IsProfessionVisible,
// EffectiveEntitlementResolver.UnionIncludedSubtests / MergeContentOverrides) —
// asked of a PLAN + profession instead of a signed-in learner. Any drift between
// the two would either block a sellable package or sell an empty one.
// ═════════════════════════════════════════════════════════════════════════════

public interface IPlanContentAvailabilityService
{
    /// <summary>
    /// Counts the content each of the plan's enabled content modules would actually reach for a
    /// buyer registered in <paramref name="professionId"/> (null = a buyer with no profession).
    /// Cached for ~5 minutes per (plan, plan.UpdatedAt, profession).
    /// </summary>
    Task<PlanContentAvailability> ResolveAsync(BillingPlan plan, string? professionId, CancellationToken ct);
}

/// <summary>
/// Per-module reachable-item counts for one (plan, profession) pair. A module that the plan does
/// NOT enable is reported as disabled with a count of 0 and never appears in
/// <see cref="EmptyEnabledModules"/> — an unsold module cannot make a package unsellable.
/// </summary>
public sealed record PlanContentAvailability(
    bool VideoLibraryEnabled,
    int VideoCount,
    bool MaterialsLibraryEnabled,
    int MaterialFileCount,
    IReadOnlyList<string> EmptyEnabledModules)
{
    /// <summary>True when the plan reaches at least one item of any kind.</summary>
    public bool HasAnyContent => VideoCount > 0 || MaterialFileCount > 0;

    /// <summary>True when NO enabled content module resolves to zero items — i.e. every content
    /// module the buyer is paying for has something in it. This is the checkout gate
    /// (<c>content_unavailable_for_profession</c>); a plan that enables no content module at all
    /// passes, because there is nothing to be empty.</summary>
    public bool IsSellable => EmptyEnabledModules.Count == 0;

    public static readonly PlanContentAvailability None =
        new(false, 0, false, 0, Array.Empty<string>());
}

public sealed class PlanContentAvailabilityService(
    LearnerDbContext db,
    IMemoryCache cache) : IPlanContentAvailabilityService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public async Task<PlanContentAvailability> ResolveAsync(
        BillingPlan plan, string? professionId, CancellationToken ct)
    {
        var normalizedProfession = string.IsNullOrWhiteSpace(professionId)
            ? null
            : professionId.Trim().ToLowerInvariant();

        // UpdatedAt is part of the key so an admin edit to the plan's modules/scope is reflected
        // immediately instead of after the TTL.
        var cacheKey = $"plan-content-availability:{plan.Id}:{plan.UpdatedAt.UtcTicks}:{normalizedProfession ?? "-"}";
        if (cache.TryGetValue<PlanContentAvailability>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var result = await ResolveCoreAsync(plan, normalizedProfession, ct);
        cache.Set(cacheKey, result, CacheTtl);
        return result;
    }

    private async Task<PlanContentAvailability> ResolveCoreAsync(
        BillingPlan plan, string? normalizedProfession, CancellationToken ct)
    {
        // A module counts as enabled only when it is EXPLICITLY listed, mirroring
        // VideoEntitlementService: a legacy plan with no module list grants no content module, so
        // it has nothing that can be empty and is never blocked at checkout.
        var modules = EffectiveEntitlementResolver.ParseDashboardModules(plan.DashboardModulesJson);
        var videoEnabled = modules.Any(m => string.Equals(m, ModuleKeys.VideoLibrary, StringComparison.OrdinalIgnoreCase));
        var materialsEnabled = modules.Any(m => string.Equals(m, ModuleKeys.MaterialsLibrary, StringComparison.OrdinalIgnoreCase));
        if (!videoEnabled && !materialsEnabled)
        {
            return PlanContentAvailability.None;
        }

        var plans = new[] { plan };
        var (allSubtests, includedSubtests) = EffectiveEntitlementResolver.UnionIncludedSubtests(plans);
        var overrides = EffectiveEntitlementResolver.MergeContentOverrides(plans);

        var videoCount = videoEnabled
            ? await CountVideosAsync(normalizedProfession, allSubtests, includedSubtests, overrides, ct)
            : 0;
        var materialCount = materialsEnabled
            ? await CountMaterialFilesAsync(plan, normalizedProfession, allSubtests, includedSubtests, overrides, ct)
            : 0;

        var empty = new List<string>();
        if (videoEnabled && videoCount == 0) empty.Add(ModuleKeys.VideoLibrary);
        if (materialsEnabled && materialCount == 0) empty.Add(ModuleKeys.MaterialsLibrary);

        return new PlanContentAvailability(
            videoEnabled, videoCount, materialsEnabled, materialCount, empty);
    }

    private async Task<int> CountVideosAsync(
        string? normalizedProfession,
        bool allSubtests,
        IReadOnlySet<string> includedSubtests,
        ContentOverrideSets overrides,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        // ProfessionIdsJson is a JSON column — never LINQ into it; filter client-side.
        var videos = await db.LibraryVideos.AsNoTracking()
            .Where(v => v.Status == ContentStatus.Published && (v.PublishAt == null || v.PublishAt <= now))
            .Select(v => new { v.Id, v.SubtestCode, v.ProfessionIdsJson })
            .ToListAsync(ct);

        return videos.Count(video =>
        {
            if (overrides.VideoIncludes.Contains(video.Id)) return true;
            if (overrides.VideoExcludes.Contains(video.Id)) return false;
            return VideoLibraryLearnerService.IsProfessionVisible(video.ProfessionIdsJson, normalizedProfession)
                && IsSubtestInScope(video.SubtestCode, allSubtests, includedSubtests);
        });
    }

    private async Task<int> CountMaterialFilesAsync(
        BillingPlan plan,
        string? normalizedProfession,
        bool allSubtests,
        IReadOnlySet<string> includedSubtests,
        ContentOverrideSets overrides,
        CancellationToken ct)
    {
        var folders = await db.MaterialFolders.AsNoTracking()
            .Include(f => f.Audiences)
            .Where(f => f.Status == ContentStatus.Published)
            .ToListAsync(ct);
        var folderDict = folders.ToDictionary(f => f.Id);

        var files = await db.MaterialFiles.AsNoTracking()
            .Where(f => f.Status == ContentStatus.Published)
            .Select(f => new { f.Id, f.FolderId, f.SubtestCode })
            .ToListAsync(ct);
        if (files.Count == 0) return 0;

        var (disciplineUniverse, planDisciplines) = await ResolveDisciplineSetsAsync(normalizedProfession, ct);

        // Cohort/institution-restricted folders are deliberately NOT counted: they are not
        // reachable by buying this plan.
        var noMemberships = new HashSet<string>(StringComparer.Ordinal);

        return files.Count(file =>
        {
            if (file.FolderId is null)
            {
                // Root-level files sit under no folder: no audience, no discipline tag.
                return IsSubtestInScope(file.SubtestCode, allSubtests, includedSubtests);
            }
            if (!folderDict.TryGetValue(file.FolderId, out var folder)) return false;
            if (!MaterialAccessService.IsFolderVisible(
                    folder, folderDict, plan.Code, plan.Code, plan.Id, noMemberships, noMemberships))
            {
                return false;
            }
            if (MaterialAccessService.ResolveFolderOverride(folder, folderDict, overrides) is bool decided)
            {
                return decided;
            }

            return MaterialAccessService.IsDisciplineVisible(folder, folderDict, disciplineUniverse, planDisciplines)
                && IsSubtestInScope(
                    MaterialAccessService.ResolveEffectiveSubtest(folder, folderDict), allSubtests, includedSubtests)
                && IsSubtestInScope(file.SubtestCode, allSubtests, includedSubtests);
        });
    }

    /// <summary>
    /// Mirrors MaterialAccessService's name-based discipline filter for a PROFESSION rather than a
    /// learner: <c>universe</c> = every profession's Id and Label (the folder names that count as a
    /// discipline), <c>target</c> = the buyer's own Id and Label. An empty target means a buyer
    /// with no profession, who reaches only untagged folders (fail-closed, spec §7.5).
    /// </summary>
    private async Task<(HashSet<string> universe, HashSet<string> target)> ResolveDisciplineSetsAsync(
        string? normalizedProfession, CancellationToken ct)
    {
        var professions = await db.Professions.AsNoTracking()
            .Select(p => new { p.Id, p.Label })
            .ToListAsync(ct);

        var universe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in professions)
        {
            if (!string.IsNullOrWhiteSpace(p.Id)) universe.Add(p.Id.Trim());
            if (!string.IsNullOrWhiteSpace(p.Label)) universe.Add(p.Label.Trim());
        }

        var target = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(normalizedProfession))
        {
            var row = professions.FirstOrDefault(p =>
                string.Equals(p.Id, normalizedProfession, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                // Registerable but absent from the reference table — still match by its raw id.
                target.Add(normalizedProfession);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(row.Id)) target.Add(row.Id.Trim());
                if (!string.IsNullOrWhiteSpace(row.Label)) target.Add(row.Label.Trim());
            }
        }

        return (universe, target);
    }

    /// <summary>Untagged items are not subtest-restricted — mirrors the learner-side gates.</summary>
    private static bool IsSubtestInScope(
        string? subtestCode, bool allSubtests, IReadOnlySet<string> includedSubtests)
    {
        if (allSubtests) return true;
        if (string.IsNullOrWhiteSpace(subtestCode)) return true;
        return includedSubtests.Contains(subtestCode.Trim());
    }
}
