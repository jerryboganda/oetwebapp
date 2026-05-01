using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Mocks V2 Wave 7 — Diagnostic-mock entitlement gate.
/// Source of truth for whether a learner can start a diagnostic mock based on
/// their active <see cref="BillingPlan.DiagnosticMockEntitlement"/> setting.
///
/// Values supported (admin-configurable):
/// <list type="bullet">
///   <item><c>unlimited</c> — always allowed.</item>
///   <item><c>one_per_lifetime</c> — allowed only if the user has never
///   completed a diagnostic.</item>
///   <item><c>one_per_renewal_period</c> — allowed only if no diagnostic was
///   completed since the active subscription's <c>StartedAt</c>.</item>
///   <item><c>paid_per_use</c> — must purchase a credit per attempt (gated at
///   billing layer; this service treats it as allowed-with-receipt-check).</item>
///   <item><c>disabled</c> — never allowed.</item>
/// </list>
/// </summary>
public sealed class MockDiagnosticEntitlementService
{
    public const string Unlimited = "unlimited";
    public const string OnePerLifetime = "one_per_lifetime";
    public const string OnePerRenewalPeriod = "one_per_renewal_period";
    public const string PaidPerUse = "paid_per_use";
    public const string Disabled = "disabled";

    public static readonly IReadOnlySet<string> AllValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Unlimited, OnePerLifetime, OnePerRenewalPeriod, PaidPerUse, Disabled,
    };

    private readonly LearnerDbContext _db;

    public MockDiagnosticEntitlementService(LearnerDbContext db) { _db = db; }

    public async Task<DiagnosticEntitlementDecision> CanStartDiagnosticAsync(string userId, CancellationToken ct)
    {
        var subscription = await _db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.ChangedAt)
            .FirstOrDefaultAsync(ct);
        var plan = subscription is null
            ? null
            : await _db.BillingPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == subscription.PlanId, ct);
        var entitlement = plan?.DiagnosticMockEntitlement ?? OnePerLifetime;

        if (string.Equals(entitlement, Disabled, StringComparison.OrdinalIgnoreCase))
        {
            return new DiagnosticEntitlementDecision(false, entitlement, "diagnostic_disabled_for_plan",
                "Your current plan does not include diagnostic mocks.");
        }
        if (string.Equals(entitlement, Unlimited, StringComparison.OrdinalIgnoreCase))
        {
            return new DiagnosticEntitlementDecision(true, entitlement, null, null);
        }
        if (string.Equals(entitlement, PaidPerUse, StringComparison.OrdinalIgnoreCase))
        {
            // Per-use billing is enforced at the billing layer when starting the
            // attempt; this service simply allows it through.
            return new DiagnosticEntitlementDecision(true, entitlement, null, null);
        }

        // Lifetime / renewal-period gate: count completed diagnostic attempts.
        var sinceUtc = string.Equals(entitlement, OnePerRenewalPeriod, StringComparison.OrdinalIgnoreCase)
            ? subscription?.StartedAt
            : (DateTimeOffset?)null;

        var query = _db.MockAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.MockType == MockTypes.Diagnostic && a.CompletedAt != null);
        if (sinceUtc.HasValue)
        {
            query = query.Where(a => a.CompletedAt >= sinceUtc.Value);
        }
        var alreadyTaken = await query.AnyAsync(ct);
        if (alreadyTaken)
        {
            var msg = string.Equals(entitlement, OnePerRenewalPeriod, StringComparison.OrdinalIgnoreCase)
                ? "You have already used your diagnostic mock for this billing period."
                : "You have already used your one-time diagnostic mock.";
            return new DiagnosticEntitlementDecision(false, entitlement, "diagnostic_already_used", msg);
        }
        return new DiagnosticEntitlementDecision(true, entitlement, null, null);
    }
}

public sealed record DiagnosticEntitlementDecision(bool Allowed, string Entitlement, string? Reason, string? Message);
