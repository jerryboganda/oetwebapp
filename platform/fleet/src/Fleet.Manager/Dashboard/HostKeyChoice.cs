using Fleet.Core.Ssh;
using Fleet.Manager.Operations;

namespace Fleet.Manager.Dashboard;

/// <summary>
/// A host usually offers several keys (ed25519, ecdsa, rsa) but the manager pins exactly ONE of them (<see cref="HostKeys.Preferred"/>) and the
/// typed characters are checked against that one only. The console says which, so the owner compares the right fingerprint instead of failing
/// the check (three wrong entries fail the enrollment) by reading out a key type that is never pinned.
/// </summary>
public static class HostKeyChoice
{
    /// <summary>The algorithm of the key that will be pinned, or null when the host offered none.</summary>
    public static string? PinnedAlgorithm(IEnumerable<HostKeyCandidateView> candidates) =>
        HostKeys.Preferred(candidates.Select(c => new ScannedHostKey(c.Algorithm, string.Empty, c.Fingerprint)))?.Algorithm;
}
