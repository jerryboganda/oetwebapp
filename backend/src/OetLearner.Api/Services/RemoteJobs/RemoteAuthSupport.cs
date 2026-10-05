using System.Collections.Concurrent;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>Scheme, policy and claim names for the two credential planes (OET-RWP/1 section 2.2).</summary>
public static class RemoteWorkerAuth
{
    /// <summary>Authentication scheme for per-node tokens (job plane).</summary>
    public const string NodeScheme = "RemoteWorker";

    /// <summary>Authentication scheme for the fleet-service credential (service plane).</summary>
    public const string FleetScheme = "FleetService";

    public const string NodePolicy = "RemoteWorkerOnly";
    public const string FleetPolicy = "FleetServiceOnly";

    /// <summary>Claim carrying <c>node</c> or <c>fleet</c>; the policies require it, so a token of the other plane cannot pass.</summary>
    public const string KindClaim = "oet_remote_kind";
    public const string NodeIdClaim = "oet_remote_node";
    public const string TokenIdClaim = "oet_remote_token";

    /// <summary>HttpContext.Items keys filled by the authentication handlers.</summary>
    public const string NodeItem = "oet.remote.node";
    public const string CredentialItem = "oet.remote.credential";
    public const string ThrottledItem = "oet.remote.throttled";

    public static RemoteWorker? NodeOf(HttpContext context)
        => context.Items.TryGetValue(NodeItem, out var node) ? node as RemoteWorker : null;

    public static RemoteCredential? CredentialOf(HttpContext context)
        => context.Items.TryGetValue(CredentialItem, out var credential) ? credential as RemoteCredential : null;
}

/// <summary>
/// Endpoint metadata marking the only two routes that may be authenticated from a cached credential and
/// node row (<c>workers/heartbeat</c> and <c>jobs/{id}/heartbeat</c>). Everything else reads the credential
/// and the node uncached so revocation bites immediately (OET-RWP/1 section 2.2, RW-007).
/// </summary>
public sealed class RemoteAuthCacheableMetadata
{
    public static readonly RemoteAuthCacheableMetadata Instance = new();
}

/// <summary>Endpoint metadata naming the per-node rate-limit bucket of a route (section 2.6).</summary>
public sealed record RemoteRateBucketMetadata(string Bucket, int PerNodePerMinute, int? PerJobPerMinute = null);

/// <summary>A credential and its node as loaded by the authentication handler.</summary>
public sealed record RemoteAuthSnapshot(RemoteCredential Credential, RemoteWorker? Node, DateTimeOffset LoadedAt);

/// <summary>
/// Per-process cache of verified credential and node rows, at most five seconds old and only consulted
/// for the two heartbeat routes. The presented secret is ALWAYS re-verified against the cached hash.
/// </summary>
public sealed class RemoteAuthCache(TimeProvider timeProvider)
{
    internal static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, RemoteAuthSnapshot> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _lastUsedWrite = new(StringComparer.Ordinal);
    private int _operations;

    public bool TryGet(string tokenId, out RemoteAuthSnapshot? snapshot)
    {
        if (_entries.TryGetValue(tokenId, out var entry) && timeProvider.GetUtcNow() - entry.LoadedAt < Ttl)
        {
            snapshot = entry;
            return true;
        }

        snapshot = null;
        return false;
    }

    public void Set(string tokenId, RemoteAuthSnapshot snapshot)
    {
        _entries[tokenId] = snapshot;
        if (Interlocked.Increment(ref _operations) % 512 == 0) Prune();
    }

    public void Remove(string tokenId) => _entries.TryRemove(tokenId, out _);

    public void Clear() => _entries.Clear();

    /// <summary>True at most once a minute per credential: the caller then writes <c>LastUsedAt</c>.</summary>
    public bool ShouldRecordUse(string tokenId)
    {
        var now = timeProvider.GetUtcNow().UtcTicks;
        var last = _lastUsedWrite.GetOrAdd(tokenId, 0);
        if (now - last < TimeSpan.TicksPerMinute) return false;
        _lastUsedWrite[tokenId] = now;
        return true;
    }

    private void Prune()
    {
        var cutoff = timeProvider.GetUtcNow() - Ttl - Ttl;
        foreach (var (key, entry) in _entries)
        {
            if (entry.LoadedAt < cutoff) _entries.TryRemove(key, out _);
        }
    }
}
