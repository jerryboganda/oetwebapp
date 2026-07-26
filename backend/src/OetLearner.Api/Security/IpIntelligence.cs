namespace OetLearner.Api.Security;

/// <summary>
/// What an external IP-intelligence feed knows about a sign-in address
/// (Course Platform Security Requirements §3.3 — VPN/datacenter/Tor
/// detection). <see cref="IsHighRiskNetwork"/> feeds the sign-in risk score.
/// </summary>
public sealed record IpIntelligence(bool IsAnonymizer, bool IsDatacenter, string? Provider)
{
    public bool IsHighRiskNetwork => IsAnonymizer || IsDatacenter;
}

/// <summary>
/// Upgrade seam for a paid IP-intelligence feed (ipinfo/MaxMind). The
/// registered default is <see cref="NoopIpIntelligenceService"/> — static IP
/// range lists are not reliable enough to act on, so until a real provider is
/// configured every lookup returns null and callers MUST fail open (see the
/// content-protection matrix §7 for the risk assessment).
/// </summary>
public interface IIpIntelligenceService
{
    /// <summary>Null when nothing is known (no provider configured, lookup
    /// failed, or private address). Never throws.</summary>
    Task<IpIntelligence?> LookupAsync(string? ipAddress, CancellationToken ct);
}

public sealed class NoopIpIntelligenceService : IIpIntelligenceService
{
    public Task<IpIntelligence?> LookupAsync(string? ipAddress, CancellationToken ct)
        => Task.FromResult<IpIntelligence?>(null);
}
