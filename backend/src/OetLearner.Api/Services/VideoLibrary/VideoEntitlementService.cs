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
    string? CurrentTier);

/// <summary>Strongly-typed projection of the plan EntitlementsJson video_library node.</summary>
public sealed record VideoLibraryBundle(bool HasNode, string Tier)
{
    public bool GrantsPremium => HasNode && string.Equals(Tier, "premium", StringComparison.OrdinalIgnoreCase);
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
        var addOnGrants = !bundle.GrantsPremium && await AnyActiveAddOnGrantsVideoLibraryAsync(userId, ct);

        return new VideoAccessContext(
            IsAdmin: false, Authenticated: true,
            HasEligibleSubscription: true, Frozen: false, Expired: false,
            PlanGrantsPremium: bundle.GrantsPremium,
            AddOnGrantsPremium: addOnGrants,
            CurrentTier: entitlement.IsTrial ? "trial" : bundle.GrantsPremium || addOnGrants ? "premium" : "free");
    }

    public VideoEntitlementResult Evaluate(VideoAccessContext context, LibraryVideo video)
    {
        if (context.IsAdmin)
        {
            return new VideoEntitlementResult(true, "admin", "admin");
        }

        if (string.Equals(video.AccessTier, "free", StringComparison.OrdinalIgnoreCase))
        {
            return context.Authenticated
                ? new VideoEntitlementResult(true, "free_tier", context.CurrentTier ?? "free")
                : new VideoEntitlementResult(false, "no_active_subscription", null);
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

        if (context.PlanGrantsPremium)
        {
            return new VideoEntitlementResult(true, "plan_grants_video_library", context.CurrentTier);
        }

        if (context.AddOnGrantsPremium)
        {
            return new VideoEntitlementResult(true, "addon_grants_video_library", context.CurrentTier);
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
        if (string.IsNullOrWhiteSpace(entitlementsJson))
        {
            return new VideoLibraryBundle(false, "none");
        }

        try
        {
            using var doc = JsonDocument.Parse(entitlementsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new VideoLibraryBundle(false, "none");
            }

            if (!doc.RootElement.TryGetProperty("video_library", out var node)
                && !doc.RootElement.TryGetProperty("videoLibrary", out node))
            {
                return new VideoLibraryBundle(false, "none");
            }

            if (node.ValueKind != JsonValueKind.Object)
            {
                return new VideoLibraryBundle(false, "none");
            }

            var tier = "free";
            if (node.TryGetProperty("tier", out var t) && t.ValueKind == JsonValueKind.String)
            {
                var raw = t.GetString();
                if (!string.IsNullOrWhiteSpace(raw)) tier = raw.Trim().ToLowerInvariant();
            }
            return new VideoLibraryBundle(true, tier);
        }
        catch (JsonException)
        {
            return new VideoLibraryBundle(false, "none");
        }
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
    /// True when any active add-on on a live subscription grants the Video
    /// Library ("videoLibrary": true or "video_library": true). Mirrors the
    /// active-item join used by MockEntitlementService.
    /// </summary>
    private async Task<bool> AnyActiveAddOnGrantsVideoLibraryAsync(string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var subscriptionIds = await db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == userId
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial))
            .Select(s => s.Id)
            .ToListAsync(ct);
        if (subscriptionIds.Count == 0) return false;

        var grantJsons = await (
            from item in db.SubscriptionItems.AsNoTracking()
            join addOn in db.BillingAddOns.AsNoTracking() on item.ItemCode equals addOn.Code
            where subscriptionIds.Contains(item.SubscriptionId)
                && item.Status == SubscriptionItemStatus.Active
                && item.StartsAt <= now
                && (item.EndsAt == null || item.EndsAt > now)
            select addOn.GrantEntitlementsJson).ToListAsync(ct);

        foreach (var json in grantJsons)
        {
            if (GrantsVideoLibrary(json)) return true;
        }
        return false;
    }

    /// <summary>Accepts "videoLibrary": true (canonical add-on key) or "video_library": true.</summary>
    public static bool GrantsVideoLibrary(string? grantEntitlementsJson)
    {
        if (string.IsNullOrWhiteSpace(grantEntitlementsJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(grantEntitlementsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (doc.RootElement.TryGetProperty("videoLibrary", out var camel) && IsTruthy(camel)) return true;
            if (doc.RootElement.TryGetProperty("video_library", out var snake) && IsTruthy(snake)) return true;
            return false;
        }
        catch (JsonException)
        {
            return false;
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
