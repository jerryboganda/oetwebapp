using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Resolves whether a buyer is allowed to purchase a given add-on, per the
/// OET 2026 portfolio's "three independent eligibility flags" rule:
///
/// <list type="bullet">
///   <item>Writing letter assessment add-ons require a parent enrolment with <c>WritingAddonsEnabled=true</c>.</item>
///   <item>Speaking session add-ons require a parent with <c>SpeakingAddonsEnabled=true</c>.</item>
///   <item>The £32 Tutor Book add-on requires a parent with <c>TutorBookDiscountEnabled=true</c> AND no
///   pre-existing Tutor Book entitlement (block double-charge).</item>
/// </list>
///
/// <para>Returns the candidate parent enrolment(s) so the checkout UI can
/// either auto-apply (single candidate) or surface a selector
/// (multiple candidates). When no candidate exists, returns the cheapest
/// eligible plan code so the UI can render a one-click upsell CTA.</para>
/// </summary>
public interface IAddonEligibilityService
{
    Task<AddonEligibilityResult> ResolveAsync(string userId, string addOnCode, CancellationToken ct);
}

public sealed record EligibleParentEnrolment(
    string SubscriptionId,
    string PlanCode,
    string PlanName,
    DateTimeOffset? ExpiresAt);

public sealed record AddonEligibilityResult(
    bool Eligible,
    string AddOnCode,
    string AddOnName,
    string? AddonKind,
    string? RequiredFlag,
    IReadOnlyList<EligibleParentEnrolment> EligibleParents,
    string? Reason,
    string? RedirectSku);

public sealed class AddonEligibilityService(LearnerDbContext db) : IAddonEligibilityService
{
    public async Task<AddonEligibilityResult> ResolveAsync(string userId, string addOnCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Fail(addOnCode, name: "?", kind: null, flag: null, reason: "user_missing", redirect: null);
        }

        var addOn = await db.BillingAddOns.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Code == addOnCode || a.Id == addOnCode, ct);
        if (addOn is null)
        {
            return Fail(addOnCode, name: "?", kind: null, flag: null, reason: "addon_not_found", redirect: null);
        }

        // Add-ons that do not require an eligible parent skip the check.
        if (!addOn.RequiresEligibleParent)
        {
            return new AddonEligibilityResult(
                Eligible: true,
                AddOnCode: addOn.Code,
                AddOnName: addOn.Name,
                AddonKind: addOn.AddonKind,
                RequiredFlag: null,
                EligibleParents: Array.Empty<EligibleParentEnrolment>(),
                Reason: null,
                RedirectSku: null);
        }

        // Tutor Book add-on has an additional "no double-charge" guard.
        if (string.Equals(addOn.AddonKind, "tutor_book", StringComparison.OrdinalIgnoreCase))
        {
            var alreadyOwns = await db.Subscriptions.AsNoTracking()
                .AnyAsync(s => s.UserId == userId
                    && s.TutorBookUnlocked
                    && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial), ct);
            if (alreadyOwns)
            {
                return Fail(
                    addOn.Code, addOn.Name, addOn.AddonKind, addOn.EligibilityFlag,
                    reason: "addon_already_owned",
                    redirect: null);
            }
        }

        var flag = addOn.EligibilityFlag?.Trim().ToLowerInvariant() ?? string.Empty;

        // Query the user's active subscriptions joined to their plan to read the three flag columns.
        var now = DateTimeOffset.UtcNow;
        var candidateRows = await db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == userId
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial)
                && (s.ExpiresAt == null || s.ExpiresAt > now))
            .Join(db.BillingPlans.AsNoTracking(),
                s => s.PlanId,
                p => p.Code,
                (s, p) => new
                {
                    s.Id,
                    s.ExpiresAt,
                    PlanCode = p.Code,
                    PlanName = p.Name,
                    p.WritingAddonsEnabled,
                    p.SpeakingAddonsEnabled,
                    p.TutorBookDiscountEnabled
                })
            .ToListAsync(ct);

        var matches = candidateRows.Where(row => FlagMatches(row.WritingAddonsEnabled, row.SpeakingAddonsEnabled, row.TutorBookDiscountEnabled, flag)).ToList();

        if (matches.Count == 0)
        {
            // Find the cheapest plan that would unlock this add-on.
            var redirectSku = await ResolveCheapestEligiblePlanCodeAsync(flag, ct);
            return Fail(addOn.Code, addOn.Name, addOn.AddonKind, addOn.EligibilityFlag, reason: "no_eligible_parent", redirect: redirectSku);
        }

        var parents = matches
            .Select(row => new EligibleParentEnrolment(row.Id, row.PlanCode, row.PlanName, row.ExpiresAt))
            .ToList();

        return new AddonEligibilityResult(
            Eligible: true,
            AddOnCode: addOn.Code,
            AddOnName: addOn.Name,
            AddonKind: addOn.AddonKind,
            RequiredFlag: flag,
            EligibleParents: parents,
            Reason: null,
            RedirectSku: null);
    }

    private static bool FlagMatches(bool writingAddons, bool speakingAddons, bool tutorBookDiscount, string flag) => flag switch
    {
        "writing_addons" => writingAddons,
        "speaking_addons" => speakingAddons,
        "tutor_book_discount" => tutorBookDiscount,
        _ => false
    };

    private async Task<string?> ResolveCheapestEligiblePlanCodeAsync(string flag, CancellationToken ct)
    {
        var query = db.BillingPlans.AsNoTracking()
            .Where(p => p.Status == BillingPlanStatus.Active && p.IsVisible && !p.IsDraft);

        query = flag switch
        {
            "writing_addons" => query.Where(p => p.WritingAddonsEnabled),
            "speaking_addons" => query.Where(p => p.SpeakingAddonsEnabled),
            "tutor_book_discount" => query.Where(p => p.TutorBookDiscountEnabled),
            _ => query.Where(p => false)
        };

        var cheapest = await query
            .OrderBy(p => p.Price)
            .ThenBy(p => p.DisplayOrder)
            .Select(p => p.Code)
            .FirstOrDefaultAsync(ct);

        return cheapest;
    }

    private static AddonEligibilityResult Fail(string code, string name, string? kind, string? flag, string reason, string? redirect)
        => new(
            Eligible: false,
            AddOnCode: code,
            AddOnName: name,
            AddonKind: kind,
            RequiredFlag: string.IsNullOrWhiteSpace(flag) ? null : flag,
            EligibleParents: Array.Empty<EligibleParentEnrolment>(),
            Reason: reason,
            RedirectSku: redirect);
}
