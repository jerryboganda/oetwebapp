using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Services.VideoLibrary;

// ═════════════════════════════════════════════════════════════════════════════
// Video Library entitlement gate — mirrors ContentEntitlementService.
//
// Source of truth
// ───────────────
//   Video-level tier comes from LibraryVideo.AccessTier ("free" | "premium").
//   • "free"    → any authenticated learner may watch.
//   • "premium" → requires either
//       (a) plan EntitlementsJson node  "video_library": { "tier": "premium" }, or
//       (b) an active add-on whose GrantEntitlementsJson has "videoLibrary": true
//           (snake_case "video_library": true also accepted).
//
//   Deliberately NO legacy-plan default: a plan without the video_library node
//   grants NOTHING here (unlike ContentEntitlementService's legacy premium
//   fallback) — the Video Library launches as an explicit entitlement.
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
    IReadOnlySet<string>? GrantedSubtests = null);

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
                CurrentTier: frozen ? "frozen" : expired ? "expired" : "free");
        }

        var planJson = await ResolvePlanEntitlementsJsonAsync(entitlement, ct);
        var bundle = ParseVideoLibraryBundle(planJson);
        // Admin-togglable module gate. When disabled the plan no longer grants the Video Library
        // (nor free-tier viewing) — but a live add-on the learner paid for still does.
        var moduleEnabled = entitlement.IsModuleEnabled(ModuleKeys.VideoLibrary);
        var planGrantsPremium = bundle.GrantsPremium && moduleEnabled;

        // Resolve add-on grants even when the plan already grants premium, so their subtest scope
        // is UNIONed in (a Writing-only plan + a Listening add-on ⇒ both modules unlock).
        var addOn = await ResolveAddOnVideoGrantAsync(userId, ct);
        var addOnGrants = addOn.Grants;

        // Union the per-subtest scope across the plan and every active add-on. If ANY grant is
        // unrestricted, the learner gets all modules; otherwise only the union of listed subtests.
        var allSubtestsGranted = (planGrantsPremium && !bundle.RestrictsSubtests)
            || (addOnGrants && addOn.AllSubtests);
        var grantedSubtests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (planGrantsPremium && bundle.RestrictsSubtests) grantedSubtests.UnionWith(bundle.Subtests);
        if (addOnGrants && !addOn.AllSubtests) grantedSubtests.UnionWith(addOn.Subtests);

        return new VideoAccessContext(
            IsAdmin: false, Authenticated: true,
            HasEligibleSubscription: true, Frozen: false, Expired: false,
            PlanGrantsPremium: planGrantsPremium,
            AddOnGrantsPremium: addOnGrants,
            CurrentTier: entitlement.IsTrial ? "trial" : planGrantsPremium || addOnGrants ? "premium" : "free",
            ModuleEnabled: moduleEnabled,
            AllSubtestsGranted: allSubtestsGranted,
            GrantedSubtests: grantedSubtests);
    }

    public VideoEntitlementResult Evaluate(VideoAccessContext context, LibraryVideo video)
    {
        if (context.IsAdmin)
        {
            return new VideoEntitlementResult(true, "admin", "admin");
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
            if (!context.AllSubtestsGranted
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
