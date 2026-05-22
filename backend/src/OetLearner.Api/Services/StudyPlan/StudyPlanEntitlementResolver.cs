using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Planner;

/// <summary>
/// Resolves a learner's current subscription tier code so the generator can
/// filter the template catalog. Defaults to "free" when no active paid
/// subscription is found — guarantees every learner gets a plan.
/// </summary>
public interface IStudyPlanEntitlementResolver
{
    Task<string> ResolveTierAsync(string userId, CancellationToken cancellationToken);
}

public class StudyPlanEntitlementResolver(LearnerDbContext db) : IStudyPlanEntitlementResolver
{
    public const string FreeTier = "free";
    public const string PremiumTier = "premium";
    public const string EliteTier = "elite";

    public async Task<string> ResolveTierAsync(string userId, CancellationToken cancellationToken)
    {
        var activeSub = await db.Subscriptions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .Where(s => s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeSub is null)
        {
            return FreeTier;
        }

        var planId = (activeSub.PlanId ?? string.Empty).ToLowerInvariant();
        if (planId.Contains("elite") || planId.Contains("ultimate"))
        {
            return EliteTier;
        }

        if (planId.Contains("premium") || planId.Contains("pro") || planId.Contains("plus"))
        {
            return PremiumTier;
        }

        return FreeTier;
    }
}
