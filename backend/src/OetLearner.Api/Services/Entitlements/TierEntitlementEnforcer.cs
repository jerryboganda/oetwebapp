using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Entitlements;

/// <summary>
/// Server-side entitlement enforcement for OET tier packaging.
/// Per docs/product-strategy/07_subscription_pricing_and_entitlements_strategy.md:
/// "All entitlement enforcement MUST be server-side. Client-side gating is for UX
/// convenience only and MUST NOT be trusted for security."
/// </summary>
public interface ITierEntitlementEnforcer
{
    Task<bool> HasEntitlementAsync(string userId, EntitlementCategory category, CancellationToken ct);
    Task<int?> GetLimitAsync(string userId, EntitlementCategory category, CancellationToken ct);
    Task<Dictionary<EntitlementCategory, bool>> GetEffectiveEntitlementsAsync(string userId, CancellationToken ct);
    Task<Dictionary<EntitlementCategory, int?>> GetEffectiveLimitsAsync(string userId, CancellationToken ct);
}

public enum EntitlementCategory
{
    Diagnostic,
    PracticeQuestions,
    AiEvaluation,
    MockExams,
    ExpertReview,
    SpeakingCoaching,
    PersonalStudyCoach,
    PrioritySupport,
    AnalyticsReadiness,
    CompareAttempts,
    ContentMarketplaceSubmit,
    ScoreGuarantee
}

public sealed class TierEntitlementEnforcer(
    LearnerDbContext db,
    IEffectiveEntitlementResolver entitlementResolver,
    ILogger<TierEntitlementEnforcer> logger) : ITierEntitlementEnforcer
{
    // Maps BillingPlan.Code (lowercase) -> OET tier code
    private static readonly Dictionary<string, string> PlanCodeToTier = new(StringComparer.OrdinalIgnoreCase)
    {
        ["free"] = "free",
        ["core"] = "core",
        ["plus"] = "plus",
        ["review"] = "review",
        ["basic"] = "core",
        ["premium"] = "plus",
        ["pro"] = "review",
    };

    // Base tier entitlements (matches lib/entitlement-categories.ts TIER_ENTITLEMENTS)
    private static readonly Dictionary<string, Dictionary<EntitlementCategory, object>> TierEntitlements = new()
    {
        ["free"] = new()
        {
            [EntitlementCategory.Diagnostic] = true,
            [EntitlementCategory.PracticeQuestions] = false,
            [EntitlementCategory.AiEvaluation] = 1,
            [EntitlementCategory.MockExams] = false,
            [EntitlementCategory.ExpertReview] = false,
            [EntitlementCategory.SpeakingCoaching] = false,
            [EntitlementCategory.PersonalStudyCoach] = false,
            [EntitlementCategory.PrioritySupport] = false,
            [EntitlementCategory.AnalyticsReadiness] = false,
            [EntitlementCategory.CompareAttempts] = false,
            [EntitlementCategory.ContentMarketplaceSubmit] = true,
            [EntitlementCategory.ScoreGuarantee] = false,
        },
        ["core"] = new()
        {
            [EntitlementCategory.Diagnostic] = true,
            [EntitlementCategory.PracticeQuestions] = true,
            [EntitlementCategory.AiEvaluation] = true,
            [EntitlementCategory.MockExams] = 2,
            [EntitlementCategory.ExpertReview] = false,
            [EntitlementCategory.SpeakingCoaching] = false,
            [EntitlementCategory.PersonalStudyCoach] = false,
            [EntitlementCategory.PrioritySupport] = false,
            [EntitlementCategory.AnalyticsReadiness] = true,
            [EntitlementCategory.CompareAttempts] = false,
            [EntitlementCategory.ContentMarketplaceSubmit] = true,
            [EntitlementCategory.ScoreGuarantee] = false,
        },
        ["plus"] = new()
        {
            [EntitlementCategory.Diagnostic] = true,
            [EntitlementCategory.PracticeQuestions] = true,
            [EntitlementCategory.AiEvaluation] = true,
            [EntitlementCategory.MockExams] = true,
            [EntitlementCategory.ExpertReview] = 4,
            [EntitlementCategory.SpeakingCoaching] = false,
            [EntitlementCategory.PersonalStudyCoach] = false,
            [EntitlementCategory.PrioritySupport] = true,
            [EntitlementCategory.AnalyticsReadiness] = true,
            [EntitlementCategory.CompareAttempts] = true,
            [EntitlementCategory.ContentMarketplaceSubmit] = true,
            [EntitlementCategory.ScoreGuarantee] = true,
        },
        ["review"] = new()
        {
            [EntitlementCategory.Diagnostic] = true,
            [EntitlementCategory.PracticeQuestions] = true,
            [EntitlementCategory.AiEvaluation] = true,
            [EntitlementCategory.MockExams] = true,
            [EntitlementCategory.ExpertReview] = true,
            [EntitlementCategory.SpeakingCoaching] = 2,
            [EntitlementCategory.PersonalStudyCoach] = true,
            [EntitlementCategory.PrioritySupport] = true,
            [EntitlementCategory.AnalyticsReadiness] = true,
            [EntitlementCategory.CompareAttempts] = true,
            [EntitlementCategory.ContentMarketplaceSubmit] = true,
            [EntitlementCategory.ScoreGuarantee] = true,
        },
    };

    // Exam-family overrides (matches lib/entitlement-categories.ts EXAM_FAMILY_ENTITLEMENT_OVERRIDES)
    private static readonly Dictionary<string, Dictionary<EntitlementCategory, object>> ExamFamilyOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ielts"] = new()
        {
            [EntitlementCategory.MockExams] = 2,
            [EntitlementCategory.SpeakingCoaching] = false,
        },
        ["pte"] = new()
        {
            [EntitlementCategory.AiEvaluation] = false,
            [EntitlementCategory.MockExams] = false,
            [EntitlementCategory.ExpertReview] = false,
            [EntitlementCategory.SpeakingCoaching] = false,
            [EntitlementCategory.AnalyticsReadiness] = false,
            [EntitlementCategory.CompareAttempts] = false,
            [EntitlementCategory.ScoreGuarantee] = false,
        },
    };

    public async Task<bool> HasEntitlementAsync(string userId, EntitlementCategory category, CancellationToken ct)
    {
        var effective = await ResolveEffectiveAsync(userId, ct);
        if (!effective.TryGetValue(category, out var value))
            return false;
        if (value is bool b) return b;
        if (value is int i) return i > 0;
        return false;
    }

    public async Task<int?> GetLimitAsync(string userId, EntitlementCategory category, CancellationToken ct)
    {
        var effective = await ResolveEffectiveAsync(userId, ct);
        if (!effective.TryGetValue(category, out var value))
            return 0;
        if (value is true) return null; // unlimited
        if (value is int i) return i;
        return 0;
    }

    public async Task<Dictionary<EntitlementCategory, bool>> GetEffectiveEntitlementsAsync(string userId, CancellationToken ct)
    {
        var raw = await ResolveEffectiveAsync(userId, ct);
        var result = new Dictionary<EntitlementCategory, bool>();
        foreach (var kvp in raw)
        {
            result[kvp.Key] = kvp.Value is bool b ? b : (kvp.Value is int i && i > 0);
        }
        return result;
    }

    public async Task<Dictionary<EntitlementCategory, int?>> GetEffectiveLimitsAsync(string userId, CancellationToken ct)
    {
        var raw = await ResolveEffectiveAsync(userId, ct);
        var result = new Dictionary<EntitlementCategory, int?>();
        foreach (var kvp in raw)
        {
            result[kvp.Key] = kvp.Value is bool b
                ? (b ? (int?)null : 0)
                : (kvp.Value is int i ? (int?)i : 0);
        }
        return result;
    }

    private async Task<Dictionary<EntitlementCategory, object>> ResolveEffectiveAsync(string userId, CancellationToken ct)
    {
        var snapshot = await entitlementResolver.ResolveAsync(userId, ct);
        var tierCode = ResolveTierCode(snapshot);
        var examFamily = await ResolveExamFamilyAsync(userId, ct);

        if (!TierEntitlements.TryGetValue(tierCode, out var baseEntitlements))
        {
            logger.LogWarning("Unknown tier code {TierCode} for user {UserId}, falling back to free", tierCode, userId);
            baseEntitlements = TierEntitlements["free"];
        }

        var effective = new Dictionary<EntitlementCategory, object>(baseEntitlements);

        // Apply exam-family overrides
        if (examFamily is not null && ExamFamilyOverrides.TryGetValue(examFamily, out var overrides))
        {
            foreach (var (category, value) in overrides)
            {
                effective[category] = value;
            }
        }

        // Apply freeze override: frozen accounts lose ALL paid entitlements
        if (snapshot.IsFrozen)
        {
            foreach (var category in effective.Keys.ToList())
            {
                effective[category] = false;
            }
            // Retain only free-tier entitlements
            effective[EntitlementCategory.Diagnostic] = true;
            effective[EntitlementCategory.ContentMarketplaceSubmit] = true;
        }

        return effective;
    }

    private static string ResolveTierCode(EffectiveEntitlementSnapshot snapshot)
    {
        if (snapshot.PlanCode is not null && PlanCodeToTier.TryGetValue(snapshot.PlanCode, out var tier))
            return tier;
        return snapshot.Tier?.ToLowerInvariant() switch
        {
            "paid" => "core", // conservative default for unknown paid plans
            "trial" => "core",
            "free" => "free",
            _ => "free"
        };
    }

    private async Task<string?> ResolveExamFamilyAsync(string userId, CancellationToken ct)
    {
        var goal = await db.Goals.AsNoTracking()
            .FirstOrDefaultAsync(g => g.UserId == userId, ct);
        return goal?.ExamFamilyCode?.ToLowerInvariant();
    }
}
