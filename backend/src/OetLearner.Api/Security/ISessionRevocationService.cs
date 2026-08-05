namespace OetLearner.Api.Security;

/// <summary>
/// Central choke point for every session-revocation path (self-serve
/// revoke, sign-in-elsewhere, admin revoke, suspend, password reset,
/// delete). Revoking through here — rather than directly flipping
/// <c>RefreshTokenRecord.RevokedAt</c> — guarantees the three things Course
/// Platform Security Requirements §3.1 requires happen TOGETHER: the
/// account's video playback sessions die too, a live push tells the other
/// device immediately, and the event lands in the security audit trail.
/// </summary>
public interface ISessionRevocationService
{
    /// <summary>Revokes every active refresh-token family for the account
    /// except <paramref name="exceptFamilyId"/> (pass null to revoke all).
    /// Returns the number of families revoked.</summary>
    Task<int> RevokeAllFamiliesAsync(
        string authAccountId, Guid? exceptFamilyId, string reason, CancellationToken ct);

    /// <summary>Revokes one specific family. Returns false if it was already
    /// revoked or didn't exist.</summary>
    Task<bool> RevokeFamilyAsync(
        string authAccountId, Guid familyId, string reason, CancellationToken ct);

    /// <summary>Revokes every active session family issued to one client
    /// identity. Used when a one-device replacement or a reduced per-learner
    /// limit removes that identity's approval.</summary>
    Task<int> RevokeDeviceFamiliesAsync(
        string authAccountId, string deviceId, string reason, CancellationToken ct);
}
