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
    /// an optional <see cref="IIpIntelligenceService"/> lookup (noop until a
    /// paid provider is configured — Course Platform Security Requirements
    /// §3.3 treats that as a later, optional upgrade; see class doc). Never
    /// throws; callers decide what to do with the result.</summary>
    Task<SignInRiskAssessment> EvaluateAsync(string authAccountId, string? currentCountryCode, string? ipAddress, CancellationToken ct);
}

/// <summary>
/// Rule-based sign-in risk scoring (Course Platform Security Requirements
/// §3.3). v1 deliberately covers only what's derivable from data already
/// captured on <c>RefreshTokenRecord</c> — country-of-record via CF-IPCountry
/// and device/family churn — NOT datacenter/VPN/Tor detection, which needs a
/// paid IP-intelligence feed (ipinfo/MaxMind) and static lists are not
/// reliable enough to act on. That integration is a documented upgrade path,
/// not implemented here.
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
            var recentFamilies = await db.RefreshTokenRecords
                .Where(t => t.ApplicationUserAccountId == authAccountId && t.CreatedAt > now.AddDays(-DeviceChurnWindowDays))
                .Select(t => t.FamilyId)
                .Distinct()
                .CountAsync(ct);
            if (recentFamilies >= DeviceChurnThreshold)
            {
                level = SignInRiskLevel.Medium;
                reasons.Add("frequent_sign_ins");
            }
        }

        // External IP intelligence (noop by default — returns null). A known
        // anonymizer/datacenter address bumps the score to at least Medium.
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
