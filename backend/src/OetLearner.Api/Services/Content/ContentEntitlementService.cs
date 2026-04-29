using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Services.Content;

// ═════════════════════════════════════════════════════════════════════════════
// Content entitlement service — cross-platform paper-access gate.
//
// First implementation of subscription-based access control for ContentPaper
// rows. Until this service shipped, Reading / Listening / Writing / Speaking
// papers were guarded only by JWT auth + ContentStatus.Published — there was
// no plan check anywhere in the request path.
//
// Source of truth
// ───────────────
//
//   Paper-level access tier comes from ContentPaper.TagsCsv:
//     • Tag "access:free"     → free preview, anyone authenticated may attempt
//     • Tag "access:premium"  → requires premium entitlement (default if no tag)
//
//   Plan-level entitlement comes from BillingPlan.EntitlementsJson under a new
//   "content" key (additive — existing keys like productiveSkillReviewsEnabled
//   continue to work):
//     {
//       "productiveSkillReviewsEnabled": true,
//       "content": {
//         "tier": "premium",                                 // "free" | "premium"
//         "subtests": ["listening","reading","writing","speaking"],
//         "papers":   ["paper-id-1","paper-id-2"]            // explicit grants
//       }
//     }
//
//   Subscription eligibility comes from IEffectiveEntitlementResolver so this
//   gate follows the same latest-subscription semantics as compact AI and
//   practice gates. Subscription add-ons are not yet inspected here.
//
//   Admins always pass.
//
// Denial signal
// ─────────────
//
//   AllowAccessAsync returns a result record (matching the Conversation /
//   Pronunciation / Grammar pattern). RequireAccessAsync throws
//   ApiException.PaymentRequired("content_locked", …) so call sites can
//   stay one-liners.
// ═════════════════════════════════════════════════════════════════════════════

public interface IContentEntitlementService
{
    Task<ContentEntitlementResult> AllowAccessAsync(
        string? userId,
        ContentPaper paper,
        CancellationToken ct);

    /// <summary>Convenience wrapper that throws <see cref="ApiException.PaymentRequired"/>
    /// when access is denied, so call sites can stay one line.</summary>
    Task RequireAccessAsync(string? userId, ContentPaper paper, CancellationToken ct);

    /// <summary>True if the caller has an admin role claim. Useful when an
    /// endpoint needs to skip the gate explicitly (e.g. preview-as-admin).</summary>
    bool IsAdmin(System.Security.Claims.ClaimsPrincipal? principal);
}

public sealed record ContentEntitlementResult(
    bool Allowed,
    string Reason,            // "free_paper" | "admin" | "plan_grants_tier" | "plan_grants_subtest" | "plan_grants_paper" | "no_active_subscription" | "plan_does_not_grant"
    string? CurrentTier,      // null if anonymous-equivalent; "free"|"premium"|"trial"
    string? RequiredScope);   // e.g. "subtest:listening" | "tier:premium" | "paper:<id>"

/// <summary>Strongly-typed projection of the BillingPlan EntitlementsJson.content node.</summary>
public sealed record ContentEntitlementBundle(
    string Tier,                                  // "free" | "premium"
    IReadOnlySet<string> GrantedSubtests,         // lowercased, e.g. {"listening","reading"}
    IReadOnlySet<string> GrantedPaperIds);

public sealed class ContentEntitlementService(LearnerDbContext db, IEffectiveEntitlementResolver entitlementResolver) : IContentEntitlementService
{
    private const string AccessFreeTag = "access:free";
    private const string AccessPremiumTag = "access:premium";

    public bool IsAdmin(System.Security.Claims.ClaimsPrincipal? principal)
    {
        if (principal is null) return false;
        return principal.IsInRole("admin") || principal.IsInRole("Admin");
    }

    public async Task<ContentEntitlementResult> AllowAccessAsync(
        string? userId, ContentPaper paper, CancellationToken ct)
    {
        if (paper is null) throw new ArgumentNullException(nameof(paper));

        // 1. Paper-level free preview.
        if (HasTag(paper.TagsCsv, AccessFreeTag))
        {
            return new ContentEntitlementResult(
                Allowed: true, Reason: "free_paper",
                CurrentTier: "free", RequiredScope: null);
        }

        // 2. Anonymous / no userId — only free papers visible.
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new ContentEntitlementResult(
                Allowed: false, Reason: "no_active_subscription",
                CurrentTier: null,
                RequiredScope: $"subtest:{paper.SubtestCode}");
        }

        var entitlement = await entitlementResolver.ResolveAsync(userId, ct);
        if (!entitlement.HasEligibleSubscription)
        {
            return new ContentEntitlementResult(
                Allowed: false, Reason: "no_active_subscription",
                CurrentTier: "free",
                RequiredScope: $"subtest:{paper.SubtestCode}");
        }

        var planShape = await ResolveContentPlanShapeAsync(entitlement, ct);
        if (planShape.AnchorBroken)
        {
            return new ContentEntitlementResult(
                Allowed: false,
                Reason: "plan_does_not_grant",
                CurrentTier: entitlement.Tier,
                RequiredScope: $"subtest:{paper.SubtestCode}");
        }

        var bundle = ParseBundle(planShape.EntitlementsJson, planShape.IncludedSubtestsJson);
        var currentTier = entitlement.IsTrial ? "trial" : bundle.Tier;

        if (string.Equals(bundle.Tier, "premium", StringComparison.OrdinalIgnoreCase))
        {
            return new ContentEntitlementResult(
                Allowed: true, Reason: "plan_grants_tier",
                CurrentTier: currentTier, RequiredScope: null);
        }

        if (bundle.GrantedPaperIds.Contains(paper.Id))
        {
            return new ContentEntitlementResult(
                Allowed: true, Reason: "plan_grants_paper",
                CurrentTier: currentTier, RequiredScope: null);
        }

        if (bundle.GrantedSubtests.Contains(paper.SubtestCode.ToLowerInvariant()))
        {
            return new ContentEntitlementResult(
                Allowed: true, Reason: "plan_grants_subtest",
                CurrentTier: currentTier, RequiredScope: null);
        }

        return new ContentEntitlementResult(
            Allowed: false, Reason: "plan_does_not_grant",
            CurrentTier: currentTier,
            RequiredScope: $"subtest:{paper.SubtestCode}");
    }

    public async Task RequireAccessAsync(string? userId, ContentPaper paper, CancellationToken ct)
    {
        var result = await AllowAccessAsync(userId, paper, ct);
        if (result.Allowed) return;

        var msg = result.Reason switch
        {
            "no_active_subscription" =>
                "An active subscription is required to attempt this paper. Subscribe to a plan that includes this content.",
            _ =>
                $"Your current plan does not include this {paper.SubtestCode} paper. Upgrade or add this content pack to continue.",
        };

        throw ApiException.PaymentRequired("content_locked", msg);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static bool HasTag(string? tagsCsv, string tag)
    {
        if (string.IsNullOrWhiteSpace(tagsCsv)) return false;
        foreach (var part in tagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(part, tag, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Parses the plan's EntitlementsJson + IncludedSubtestsJson into a
    /// strongly-typed projection. Tolerates legacy plans (no "content" key) by
    /// falling back to IncludedSubtestsJson and a default tier of "premium"; any
    /// active paid subscription is assumed to grant general access until an admin
    /// chooses to scope it down via the new schema. Once a "content" object is
    /// present, it owns scoped access and legacy IncludedSubtestsJson is ignored.</summary>
    public static ContentEntitlementBundle ParseBundle(string? entitlementsJson, string? includedSubtestsJson)
    {
        string tier = "premium"; // legacy plans grant premium by default
        var subtests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var papers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        JsonElement? contentNode = null;

        if (!string.IsNullOrWhiteSpace(entitlementsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(entitlementsJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("content", out var content)
                    && content.ValueKind == JsonValueKind.Object)
                {
                    contentNode = content.Clone();
                }
            }
            catch (JsonException) { /* tolerate malformed JSON; treat as legacy */ }
        }

        if (contentNode is not null)
        {
            tier = "free";
            var content = contentNode.Value;
            if (content.TryGetProperty("tier", out var t) && t.ValueKind == JsonValueKind.String)
            {
                var raw = t.GetString();
                if (!string.IsNullOrWhiteSpace(raw)) tier = raw.Trim().ToLowerInvariant();
            }
            if (content.TryGetProperty("subtests", out var st) && st.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in st.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.String)
                        subtests.Add((el.GetString() ?? string.Empty).Trim().ToLowerInvariant());
            }
            if (content.TryGetProperty("papers", out var pp) && pp.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in pp.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.String)
                        papers.Add((el.GetString() ?? string.Empty).Trim());
            }

            return new ContentEntitlementBundle(tier, subtests, papers);
        }

        // includedSubtestsJson is the legacy ["writing","speaking"] array.
        if (!string.IsNullOrWhiteSpace(includedSubtestsJson))
        {
            try
            {
                var arr = JsonSerializer.Deserialize<List<string>>(includedSubtestsJson) ?? [];
                foreach (var s in arr) if (!string.IsNullOrWhiteSpace(s)) subtests.Add(s.Trim().ToLowerInvariant());
            }
            catch (JsonException) { /* tolerate */ }
        }

        return new ContentEntitlementBundle(tier, subtests, papers);
    }

    private async Task<ContentPlanShape> ResolveContentPlanShapeAsync(EffectiveEntitlementSnapshot entitlement, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(entitlement.PlanVersionId))
        {
            var version = await db.BillingPlanVersions.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == entitlement.PlanVersionId, ct);
            if (version is null || !MatchesSubscriptionPlan(version, entitlement.PlanId, entitlement.PlanCode))
            {
                return new ContentPlanShape(null, null, AnchorBroken: true);
            }

            return new ContentPlanShape(version.EntitlementsJson, version.IncludedSubtestsJson, AnchorBroken: false);
        }

        var lookup = entitlement.PlanId ?? entitlement.PlanCode;
        if (string.IsNullOrWhiteSpace(lookup))
        {
            return new ContentPlanShape(null, null, AnchorBroken: true);
        }

        var normalizedPlan = lookup.Trim().ToLowerInvariant();
        var plan = await db.BillingPlans.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id.ToLower() == normalizedPlan || item.Code.ToLower() == normalizedPlan, ct);

        return plan is null
            ? new ContentPlanShape(null, null, AnchorBroken: true)
            : new ContentPlanShape(plan.EntitlementsJson, plan.IncludedSubtestsJson, AnchorBroken: false);
    }

    private static bool MatchesSubscriptionPlan(BillingPlanVersion version, string? planId, string? planCode)
        => (!string.IsNullOrWhiteSpace(planId)
                && (string.Equals(version.PlanId, planId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(version.Code, planId, StringComparison.OrdinalIgnoreCase)))
            || (!string.IsNullOrWhiteSpace(planCode)
                && string.Equals(version.Code, planCode, StringComparison.OrdinalIgnoreCase));

    private sealed record ContentPlanShape(string? EntitlementsJson, string? IncludedSubtestsJson, bool AnchorBroken);
}
