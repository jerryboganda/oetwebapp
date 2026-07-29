using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;

namespace OetLearner.Api.Security;

public enum SignInRiskLevel
{
    None,
    Medium,
    High,
}

public sealed record SignInRiskAssessment(SignInRiskLevel Level, IReadOnlyList<string> Reasons);

public interface ISignInRiskService
{
    /// <summary>Rule evaluation over the account's own sign-in history plus
    /// an optional <see cref="IIpIntelligenceService"/> lookup. The external
    /// provider is fail-open when it is disabled, unavailable, or not
    /// configured. Never throws for provider failures; callers decide what to
    /// do with the result.</summary>
    Task<SignInRiskAssessment> EvaluateAsync(string authAccountId, string? currentCountryCode, string? ipAddress, CancellationToken ct);
}

/// <summary>
/// Rule-based sign-in risk scoring (Course Platform Security Requirements
/// §3.3). It combines account history captured on
/// <c>RefreshTokenRecord</c> (country-of-record via CF-IPCountry plus
/// device/family churn) with optional IPinfo privacy/network intelligence for
/// public client IPs.
/// </summary>
public sealed class SignInRiskService(
    LearnerDbContext db,
    IIpIntelligenceService ipIntelligence,
    TimeProvider timeProvider) : ISignInRiskService
{
    private static readonly TimeSpan ImpossibleTravelWindow = TimeSpan.FromHours(2);
    private const int DeviceChurnWindowDays = 7;
    private const int DeviceChurnThreshold = 5;

    public async Task<SignInRiskAssessment> EvaluateAsync(
        string authAccountId, string? currentCountryCode, string? ipAddress, CancellationToken ct)
    {
        var reasons = new List<string>();
        var level = SignInRiskLevel.None;
        var now = timeProvider.GetUtcNow();

        if (!string.IsNullOrWhiteSpace(currentCountryCode))
        {
            var lastKnown = await db.RefreshTokenRecords
                .Where(t => t.ApplicationUserAccountId == authAccountId && t.CountryCode != null)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new { t.CountryCode, t.CreatedAt })
                .FirstOrDefaultAsync(ct);

            if (lastKnown is not null
                && !string.Equals(lastKnown.CountryCode, currentCountryCode, StringComparison.OrdinalIgnoreCase))
            {
                if (now - lastKnown.CreatedAt < ImpossibleTravelWindow)
                {
                    level = SignInRiskLevel.High;
                    reasons.Add("impossible_travel");
                }
                else
                {
                    level = SignInRiskLevel.Medium;
                    reasons.Add("country_changed");
                }
            }
        }

        if (level != SignInRiskLevel.High)
        {
            // Counts DISTINCT DEVICES, not sign-in/family count — a single
            // device re-authenticating often (short-lived sessions, active
            // daily use) is not device churn and must never trip this. Only
            // genuinely DIFFERENT devices signing in within the window are a
            // credential-sharing/compromise signal. (Previously counted
            // FamilyId, which is unique per fresh sign-in even from the exact
            // same device — that miscounted routine reuse as churn and could
            // pin an active account at Medium risk indefinitely, forcing a
            // step-up on every sign-in regardless of device trust.)
            var recentDevices = await db.RefreshTokenRecords
                .Where(t => t.ApplicationUserAccountId == authAccountId
                    && t.CreatedAt > now.AddDays(-DeviceChurnWindowDays)
                    && t.DeviceId != null)
                .Select(t => t.DeviceId)
                .Distinct()
                .CountAsync(ct);
            if (recentDevices >= DeviceChurnThreshold)
            {
                level = SignInRiskLevel.Medium;
                reasons.Add("frequent_sign_ins");
            }
        }

        // External IP intelligence returns null while disabled or unavailable.
        // A known anonymizer/datacenter address bumps the score to at least Medium.
        var intel = await ipIntelligence.LookupAsync(ipAddress, ct);
        if (intel?.IsHighRiskNetwork == true)
        {
            reasons.Add("high_risk_network");
            if (level == SignInRiskLevel.None)
            {
                level = SignInRiskLevel.Medium;
            }
        }

        return new SignInRiskAssessment(level, reasons);
    }
}
