using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Entitlements;

namespace OetWithDrHesham.Api.Services.VideoLibrary;

// ═════════════════════════════════════════════════════════════════════════════
// Video Library entitlement gate — mirrors ContentEntitlementService.
//
// Source of truth
// ───────────────
//   Video-level tier comes from LibraryVideo.AccessTier ("free" | "premium").
//   • "free"    → any authenticated learner may watch.
//   • "premium" → requires either
//       (a) the plan EXPLICITLY enabling the admin "VideoLibrary" module
//           (DashboardModulesJson contains "VideoLibrary" → the "Videos"
//           Enable/Disable toggle in the admin Edit-plan dialog), matching how
//           Recalls / MaterialsLibrary / Mocks gate — this is the primary path
//           for every real plan; or
//       (b) a legacy plan EntitlementsJson node "video_library": { "tier":
//           "premium" } (still honoured, and still able to scope by subtest); or
//       (c) an active add-on whose GrantEntitlementsJson has "videoLibrary": true
//           (snake_case "video_library": true also accepted).
//
//   Owner directive 2026-07-13: the "Videos" module toggle IS the grant. Before
//   this, premium videos required node (b) which NO admin UI ever wrote and NO
//   seeded plan carried, so enabling "Videos" in admin never actually unlocked
//   playback. A plan that neither enables the module nor carries the node (nor
//   an add-on) still grants NOTHING — a bare eligible subscription is not enough.
//
// Denial signal
// ─────────────
//   AllowAccessAsync returns a result record; RequireAccessAsync throws
//   ApiException.PaymentRequired("content_locked", …) — or Forbidden for
//   frozen/expired — so call sites stay one-liners.
// ═════════════════════════════════════════════════════════════════════════════

public interface IVideoEntitlementService
{
    Task<VideoEntitlementResult> AllowAccessAsync(string? userId, LibraryVideo video, CancellationToken ct);

    /// <summary>Throws <see cref="ApiException"/> when access is denied.</summary>
    Task RequireAccessAsync(string? userId, LibraryVideo video, CancellationToken ct);

    /// <summary>
    /// Resolve the caller's grant context once, then evaluate many videos
    /// cheaply via <see cref="Evaluate"/> (catalog listings).
    /// </summary>
    Task<VideoAccessContext> ResolveContextAsync(string? userId, bool isAdmin, CancellationToken ct);

    VideoEntitlementResult Evaluate(VideoAccessContext context, LibraryVideo video);
}

public sealed record VideoEntitlementResult(
    bool Allowed,
    string Reason,        // "admin" | "free_tier" | "plan_grants_video_library" | "addon_grants_video_library"
                          // | "no_active_subscription" | "subscription_frozen" | "subscription_expired" | "plan_does_not_grant"
                          // | "plan_does_not_grant_subtest" | "profession_mismatch" | "plan_excludes_video"
    string? CurrentTier); // null | "free" | "premium" | "trial" | "frozen" | "expired" | "admin"

/// <summary>Resolved-once grant context for evaluating many videos.</summary>
public sealed record VideoAccessContext(
    bool IsAdmin,
    bool Authenticated,
    bool HasEligibleSubscription,
    bool Frozen,
    bool Expired,
    bool PlanGrantsPremium,
    bool AddOnGrantsPremium,
    string? CurrentTier,
    // Whether the plan's admin-togglable "VideoLibrary" module is enabled (fail-open when the
    // plan carries no module list). Disabling it withholds the plan/free-tier grants but never
    // confiscates a separately-purchased Video Library add-on (AddOnGrantsPremium still wins).
    bool ModuleEnabled = true,
    // Per-subtest (OET module: listening|reading|writing|speaking) scoping of the premium grant.
    // AllSubtestsGranted = true  → the grant covers every module (the default / backward-compatible
    // behaviour when no plan or add-on lists specific subtests). When false, only videos whose
    // SubtestCode ∈ GrantedSubtests unlock; a video with no SubtestCode is not subtest-restricted
    // (fails open under any premium grant). Admin and free-tier paths ignore this entirely.
    bool AllSubtestsGranted = true,
    IReadOnlySet<string>? GrantedSubtests = null,
    // Profession axis of the "subtest × profession" content model (access & payment spec §3):
    // the learner's registered profession, lower-cased, or null when they have none. Checked
    // against LibraryVideo.ProfessionIdsJson in Evaluate as defence in depth — the learner
    // listing/lookup paths filter by profession too, but a caller holding a video by id must
    // not be able to skip the check.
    string? ProfessionId = null,
    // Per-plan content overrides (BillingPlan.ContentOverridesJson), keyed by LibraryVideo.Id.
    // An explicit include beats the subtest/profession scope AND an exclude; neither ever
    // bypasses the module / subscription gate.
    IReadOnlySet<string>? VideoIncludes = null,
    IReadOnlySet<string>? VideoExcludes = null);

/// <summary>Strongly-typed projection of the plan EntitlementsJson video_library node.</summary>
public sealed record VideoLibraryBundle(bool HasNode, string Tier, IReadOnlyList<string> Subtests)
{
    public bool GrantsPremium => HasNode && string.Equals(Tier, "premium", StringComparison.OrdinalIgnoreCase);

    /// <summary>Non-empty = the grant is limited to these OET modules; empty = all modules.</summary>
    public bool RestrictsSubtests => Subtests.Count > 0;
}

/// <summary>Aggregated Video Library grant from a learner's active add-ons.</summary>
public sealed record AddOnVideoGrant(bool Grants, bool AllSubtests, IReadOnlyCollection<string> Subtests)
{
    public static readonly AddOnVideoGrant None = new(false, false, Array.Empty<string>());
}

public sealed class VideoEntitlementService(
    LearnerDbContext db,
    IEffectiveEntitlementResolver entitlementResolver) : IVideoEntitlementService
{
    public async Task<VideoEntitlementResult> AllowAccessAsync(string? userId, LibraryVideo video, CancellationToken ct)
    {
        var context = await ResolveContextAsync(userId, isAdmin: false, ct);
        return Evaluate(context, video);
    }

    public async Task RequireAccessAsync(string? userId, LibraryVideo video, CancellationToken ct)
    {
        var result = await AllowAccessAsync(userId, video, ct);
        if (result.Allowed) return;

        switch (result.Reason)
        {
            case "subscription_frozen":
                throw ApiException.Forbidden("subscription_frozen",
                    "Your subscription is frozen. Resume access before watching this video.");
            case "subscription_expired":
                throw ApiException.Forbidden("subscription_expired",
                    "Your subscription has expired. Renew access before watching this video.");
            case "profession_mismatch":
                throw ApiException.PaymentRequired("content_locked",
                    "This video is for a different profession. It is not part of your package.");
            default:
                throw ApiException.PaymentRequired("content_locked",
                    "Your current plan does not include the Video Library. Upgrade to a plan or add-on that includes it.");
        }
    }

    public async Task<VideoAccessContext> ResolveContextAsync(string? userId, bool isAdmin, CancellationToken ct)
    {
        if (isAdmin)
        {
            return new VideoAccessContext(
                IsAdmin: true, Authenticated: true,
                HasEligibleSubscription: true, Frozen: false, Expired: false,
                PlanGrantsPremium: true, AddOnGrantsPremium: false, CurrentTier: "admin");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return new VideoAccessContext(
                IsAdmin: false, Authenticated: false,
                HasEligibleSubscription: false, Frozen: false, Expired: false,
                PlanGrantsPremium: false, AddOnGrantsPremium: false, CurrentTier: null);
        }

        var entitlement = await entitlementResolver.ResolveAsync(userId, ct);
        if (!entitlement.HasEligibleSubscription)
        {
            var frozen = entitlement.SubscriptionStatus == SubscriptionStatus.Frozen;
            var expired = !frozen
                && entitlement.ExpiresAt is { } expires
                && expires <= DateTimeOffset.UtcNow;
            return new VideoAccessContext(
                IsAdmin: false, Authenticated: true,
                HasEligibleSubscription: false, Frozen: frozen, Expired: expired,
                PlanGrantsPremium: false, AddOnGrantsPremium: false,
                CurrentTier: frozen ? "frozen" : expired ? "expired" : "free",
                ProfessionId: entitlement.ProfessionId);
        }

        var planJson = await ResolvePlanEntitlementsJsonAsync(entitlement, ct);
        var bundle = ParseVideoLibraryBundle(planJson);
        // Admin-togglable module gate. When disabled the plan no longer grants the Video Library
        // (nor free-tier viewing) — but a live add-on the learner paid for still does.
        var moduleEnabled = entitlement.IsModuleEnabled(ModuleKeys.VideoLibrary);
        // Is the VideoLibrary module EXPLICITLY enabled on the plan (i.e. the admin "Videos" toggle
        // is on / the key is present in DashboardModulesJson)? This — not a separate entitlement
        // node — is the primary grant for every real plan, mirroring Recalls/Materials/Mocks. We
        // require the module to be EXPLICITLY listed rather than relying on IsModuleEnabled's
        // fail-open (empty-list ⇒ enabled) contract, so a plan that carries no module list at all
        // still grants nothing here (preserving "a bare eligible subscription is not enough").
        var moduleExplicitlyEnabled = entitlement.EnabledModules
            .Any(m => string.Equals(m, ModuleKeys.VideoLibrary, StringComparison.OrdinalIgnoreCase));
        // Grant premium when the module is enabled (not per-user/per-plan disabled) AND either the
        // plan explicitly turns the module on OR carries a legacy video_library:{tier:premium} node.
        // The moduleEnabled prefix keeps a per-user/per-plan DISABLE authoritative over both paths.
        var planGrantsPremium = moduleEnabled && (bundle.GrantsPremium || moduleExplicitlyEnabled);

        // Resolve add-on grants even when the plan already grants premium, so their subtest scope
        // is UNIONed in (a Writing-only plan + a Listening add-on ⇒ both modules unlock).
        var addOn = await ResolveAddOnVideoGrantAsync(userId, ct);
        var addOnGrants = addOn.Grants;

        // The plan's subtest scope is the INTERSECTION of two independent restrictions: the legacy
        // video_library.subtests node and the plan's IncludedSubtestsJson (the subtest axis of the
        // subtest × profession model, spec §3). Either side being unrestricted narrows nothing, so
        // the plan is unrestricted only when BOTH are.
        var (planAllSubtests, planSubtests) = IntersectSubtestScopes(
            bundle.RestrictsSubtests ? bundle.Subtests : null,
            entitlement.AllSubtestsIncluded ? null : entitlement.IncludedSubtests);

        // Union the per-subtest scope across the plan and every active add-on. If ANY grant is
        // unrestricted, the learner gets all modules; otherwise only the union of listed subtests.
        var allSubtestsGranted = (planGrantsPremium && planAllSubtests)
            || (addOnGrants && addOn.AllSubtests);
        var grantedSubtests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (planGrantsPremium && !planAllSubtests) grantedSubtests.UnionWith(planSubtests);
        if (addOnGrants && !addOn.AllSubtests) grantedSubtests.UnionWith(addOn.Subtests);

        return new VideoAccessContext(
            IsAdmin: false, Authenticated: true,
            HasEligibleSubscription: true, Frozen: false, Expired: false,
            PlanGrantsPremium: planGrantsPremium,
            AddOnGrantsPremium: addOnGrants,
            CurrentTier: entitlement.IsTrial ? "trial" : planGrantsPremium || addOnGrants ? "premium" : "free",
            ModuleEnabled: moduleEnabled,
            AllSubtestsGranted: allSubtestsGranted,
            GrantedSubtests: grantedSubtests,
            ProfessionId: entitlement.ProfessionId,
            VideoIncludes: entitlement.ContentOverrides.VideoIncludes,
            VideoExcludes: entitlement.ContentOverrides.VideoExcludes);
    }

    public VideoEntitlementResult Evaluate(VideoAccessContext context, LibraryVideo video)
    {
        if (context.IsAdmin)
        {
            return new VideoEntitlementResult(true, "admin", "admin");
        }

        // Content scope (spec §3): an explicit per-plan include wins over the exclude list and over
        // the subtest/profession scope — but never over the module/subscription gates below, which
        // every path still has to clear.
        var explicitlyIncluded = context.VideoIncludes is { Count: > 0 }
            && context.VideoIncludes.Contains(video.Id);
        if (!explicitlyIncluded)
        {
            if (context.VideoExcludes is { Count: > 0 } && context.VideoExcludes.Contains(video.Id))
            {
                return new VideoEntitlementResult(false, "plan_excludes_video", context.CurrentTier);
            }
            if (!VideoLibraryLearnerService.IsProfessionVisible(video.ProfessionIdsJson, context.ProfessionId))
            {
                return new VideoEntitlementResult(false, "profession_mismatch", context.CurrentTier);
            }
        }

        if (string.Equals(video.AccessTier, "free", StringComparison.OrdinalIgnoreCase))
        {
            if (!context.Authenticated)
            {
                return new VideoEntitlementResult(false, "no_active_subscription", null);
            }
            // The plan explicitly disabled the Video Library module and no add-on re-grants it.
            if (!context.ModuleEnabled && !context.AddOnGrantsPremium)
            {
                return new VideoEntitlementResult(false, "plan_does_not_grant", context.CurrentTier);
            }
            return new VideoEntitlementResult(true, "free_tier", context.CurrentTier ?? "free");
        }

        if (!context.Authenticated)
        {
            return new VideoEntitlementResult(false, "no_active_subscription", null);
        }

        if (!context.HasEligibleSubscription)
        {
            if (context.Frozen) return new VideoEntitlementResult(false, "subscription_frozen", "frozen");
            if (context.Expired) return new VideoEntitlementResult(false, "subscription_expired", "expired");
            return new VideoEntitlementResult(false, "no_active_subscription", "free");
        }

        if (context.PlanGrantsPremium || context.AddOnGrantsPremium)
        {
            // Per-subtest scoping: when the grant is limited to specific OET modules, a video whose
            // SubtestCode falls outside that set stays locked. A video with no SubtestCode is not
            // subtest-restricted and unlocks under any premium grant (fail-open).
            if (!explicitlyIncluded
                && !context.AllSubtestsGranted
                && !string.IsNullOrWhiteSpace(video.SubtestCode)
                && (context.GrantedSubtests is null
                    || !context.GrantedSubtests.Contains(video.SubtestCode.Trim())))
            {
                return new VideoEntitlementResult(false, "plan_does_not_grant_subtest", context.CurrentTier);
            }

            return new VideoEntitlementResult(
                true,
                context.PlanGrantsPremium ? "plan_grants_video_library" : "addon_grants_video_library",
                context.CurrentTier);
        }

        return new VideoEntitlementResult(false, "plan_does_not_grant", context.CurrentTier);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Intersects two optional subtest restrictions. A null side imposes no restriction, so the
    /// result is unrestricted only when both are null; otherwise it is the intersection (or the
    /// single non-null side). An empty result with allSubtests=false means the two restrictions
    /// are disjoint — the grant covers no subtest at all.
    /// </summary>
    private static (bool allSubtests, IReadOnlySet<string> subtests) IntersectSubtestScopes(
        IReadOnlyCollection<string>? left,
        IReadOnlySet<string>? right)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (left is null && right is null) return (true, result);
        if (left is null) { result.UnionWith(right!); return (false, result); }
        if (right is null) { result.UnionWith(left); return (false, result); }

        foreach (var code in left)
        {
            if (right.Contains(code)) result.Add(code);
        }
        return (false, result);
    }

    /// <summary>
    /// Parse the plan EntitlementsJson video_library node. Fail-low: malformed
    /// JSON, a non-object root, or an absent node all yield an ungranted
    /// bundle — there is deliberately NO legacy-plan premium default here.
    /// Accepts both "video_library" (canonical) and "videoLibrary" spellings.
    /// </summary>
    public static VideoLibraryBundle ParseVideoLibraryBundle(string? entitlementsJson)
    {
        var empty = new VideoLibraryBundle(false, "none", Array.Empty<string>());
        if (string.IsNullOrWhiteSpace(entitlementsJson))
        {
            return empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(entitlementsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return empty;
            }

            if (!doc.RootElement.TryGetProperty("video_library", out var node)
                && !doc.RootElement.TryGetProperty("videoLibrary", out node))
            {
                return empty;
            }

            if (node.ValueKind != JsonValueKind.Object)
            {
                return empty;
            }

            var tier = "free";
            if (node.TryGetProperty("tier", out var t) && t.ValueKind == JsonValueKind.String)
            {
                var raw = t.GetString();
                if (!string.IsNullOrWhiteSpace(raw)) tier = raw.Trim().ToLowerInvariant();
            }
            return new VideoLibraryBundle(true, tier, ReadSubtestList(node));
        }
        catch (JsonException)
        {
            return empty;
        }
    }

    /// <summary>
    /// Read an optional per-module scope array from an entitlement node. Accepts either
    /// "subtests" (canonical) or "modules" (alias), each a JSON array of OET module codes
    /// (listening|reading|writing|speaking). Absent/empty/malformed ⇒ empty list ⇒ "all modules".
    /// </summary>
    private static IReadOnlyList<string> ReadSubtestList(JsonElement node)
    {
        if (!node.TryGetProperty("subtests", out var arr) && !node.TryGetProperty("modules", out arr))
        {
            return Array.Empty<string>();
        }
        if (arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }
        var list = new List<string>();
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim().ToLowerInvariant());
            }
        }
        return list;
    }

    private async Task<string?> ResolvePlanEntitlementsJsonAsync(
        EffectiveEntitlementSnapshot entitlement, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(entitlement.PlanVersionId))
        {
            var version = await db.BillingPlanVersions.AsNoTracking()
                .Where(v => v.Id == entitlement.PlanVersionId)
                .Select(v => v.EntitlementsJson)
                .FirstOrDefaultAsync(ct);
            if (version is not null) return version;
        }

        var lookup = entitlement.PlanId ?? entitlement.PlanCode;
        if (string.IsNullOrWhiteSpace(lookup)) return null;

        var normalized = lookup.Trim().ToLowerInvariant();
        return await db.BillingPlans.AsNoTracking()
            .Where(p => p.Id.ToLower() == normalized || p.Code.ToLower() == normalized)
            .Select(p => p.EntitlementsJson)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Aggregate the Video Library grant across every active add-on on the learner's live
    /// subscriptions. Mirrors the active-item join used by MockEntitlementService. Returns whether
    /// any add-on grants the library, and the union of their per-subtest scope (any unrestricted
    /// add-on ⇒ all modules).
    /// </summary>
    private async Task<AddOnVideoGrant> ResolveAddOnVideoGrantAsync(string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var subscriptionIds = await db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == userId
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial))
            .Select(s => s.Id)
            .ToListAsync(ct);
        if (subscriptionIds.Count == 0) return AddOnVideoGrant.None;

        var grantJsons = await (
            from item in db.SubscriptionItems.AsNoTracking()
            join addOn in db.BillingAddOns.AsNoTracking() on item.ItemCode equals addOn.Code
            where subscriptionIds.Contains(item.SubscriptionId)
                && item.Status == SubscriptionItemStatus.Active
                && item.StartsAt <= now
                && (item.EndsAt == null || item.EndsAt > now)
            select addOn.GrantEntitlementsJson).ToListAsync(ct);

        var grants = false;
        var allSubtests = false;
        var subtests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var json in grantJsons)
        {
            var g = ParseAddOnVideoGrant(json);
            if (!g.Grants) continue;
            grants = true;
            if (g.AllSubtests) allSubtests = true;
            else subtests.UnionWith(g.Subtests);
        }
        return new AddOnVideoGrant(grants, allSubtests, subtests);
    }

    /// <summary>Accepts "videoLibrary": true (canonical add-on key) or "video_library": true.</summary>
    public static bool GrantsVideoLibrary(string? grantEntitlementsJson)
        => ParseAddOnVideoGrant(grantEntitlementsJson).Grants;

    /// <summary>
    /// Parse a single add-on's grant JSON. Grants the library when "videoLibrary"/"video_library"
    /// is truthy; an optional "videoLibrarySubtests"/"video_library_subtests" array narrows the
    /// grant to those OET modules (absent/empty ⇒ all modules).
    /// </summary>
    public static AddOnVideoGrant ParseAddOnVideoGrant(string? grantEntitlementsJson)
    {
        if (string.IsNullOrWhiteSpace(grantEntitlementsJson)) return AddOnVideoGrant.None;
        try
        {
            using var doc = JsonDocument.Parse(grantEntitlementsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return AddOnVideoGrant.None;
            var root = doc.RootElement;
            var grants = (root.TryGetProperty("videoLibrary", out var camel) && IsTruthy(camel))
                || (root.TryGetProperty("video_library", out var snake) && IsTruthy(snake));
            if (!grants) return AddOnVideoGrant.None;

            JsonElement arr = default;
            var hasArr = (root.TryGetProperty("videoLibrarySubtests", out arr) && arr.ValueKind == JsonValueKind.Array)
                || (root.TryGetProperty("video_library_subtests", out arr) && arr.ValueKind == JsonValueKind.Array);
            var subtests = new List<string>();
            if (hasArr)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        var s = el.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) subtests.Add(s.Trim().ToLowerInvariant());
                    }
                }
            }
            return new AddOnVideoGrant(true, subtests.Count == 0, subtests);
        }
        catch (JsonException)
        {
            return AddOnVideoGrant.None;
        }
    }

    private static bool IsTruthy(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.String => string.Equals(el.GetString(), "true", StringComparison.OrdinalIgnoreCase),
        JsonValueKind.Number => el.TryGetInt32(out var i) && i > 0,
        _ => false,
    };
}
