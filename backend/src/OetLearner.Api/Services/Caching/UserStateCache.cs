using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;

namespace OetLearner.Api.Services.Caching;

/// <summary>
/// Settings for <see cref="UserStateCache"/> (section <c>Performance:UserStateCache</c>).
/// Owner-approved 2026-10-05: ON by default, 15 s TTL. The runtime kill switch is the
/// <c>user_state_cache</c> feature flag (see <see cref="UserStateCache.FeatureFlagKey"/>).
/// </summary>
public sealed class UserStateCacheOptions
{
    public const string SectionName = "Performance:UserStateCache";
    public const int MinTtlSeconds = 1;
    public const int MaxTtlSeconds = 30;
    public const int DefaultTtlSeconds = 15;
    public const int DefaultMaxEntries = 50_000;

    /// <summary>Hard off switch (needs a restart). The runtime switch is the feature flag.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Entry lifetime, clamped to 1..30 s. This is also the worst-case staleness for a
    /// change made by ANOTHER process (idle blue/green slot, ai-worker) or by a bulk SQL write.</summary>
    public int TtlSeconds { get; set; } = DefaultTtlSeconds;

    /// <summary>Memory bound: past this the cache is swept, then cleared.</summary>
    public int MaxEntries { get; set; } = DefaultMaxEntries;

    internal TimeSpan Ttl => TimeSpan.FromSeconds(Math.Clamp(TtlSeconds, MinTtlSeconds, MaxTtlSeconds));
}

/// <summary>The kinds of per-user state cached (also the metric tag).</summary>
public static class UserStateCacheKinds
{
    /// <summary>JWT account-state liveness row (Program.cs OnTokenValidated).</summary>
    public const string JwtAccount = "jwt_account";

    /// <summary>EffectiveEntitlementSnapshot (EffectiveEntitlementResolver).</summary>
    public const string Entitlement = "entitlement";

    /// <summary>Learner freeze-status DTO shown on /me, bootstrap and the dashboard.</summary>
    public const string FreezeStatus = "freeze_status";

    /// <summary>Current freeze record used by the learner write gate.</summary>
    public const string FreezeGate = "freeze_gate";
}

/// <summary>
/// Invalidation subjects. Two id spaces exist and must never be confused: the auth account id
/// (<c>ApplicationUserAccount.Id</c>, the JWT <c>auth_account_id</c> claim) and the learner user
/// id (<c>LearnerUser.Id</c>, the JWT <c>sub</c>). The prefix keeps them apart.
/// </summary>
public static class UserStateCacheSubjects
{
    public static string AuthAccount(string authAccountId) => "acct:" + authAccountId;

    public static string Learner(string learnerUserId) => "user:" + learnerUserId;
}

/// <summary>
/// Taken BEFORE the database read that fills an entry. <see cref="UserStateCache.Set"/> refuses
/// to store the value if the subject (or anything) was invalidated since, so a value read
/// before a concurrent write can never be cached after that write's invalidation.
/// </summary>
public readonly record struct UserStateReadToken(int Stripe, long StripeVersion, long GlobalVersion);

public sealed record UserStateCacheKindStats(string Kind, long Hits, long Misses, long Sets, long RejectedSets);

public sealed record UserStateCacheSnapshot(
    bool ConfigEnabled,
    bool RuntimeEnabled,
    int TtlSeconds,
    int Entries,
    long Invalidations,
    long GlobalInvalidations,
    string FeatureFlagKey,
    IReadOnlyList<UserStateCacheKindStats> Kinds);

/// <summary>
/// Per-process, short-lived (default 15 s) cache of per-user state that is read on every
/// request: the JWT account-liveness row, the entitlement snapshot and the freeze state.
///
/// <para><b>Safety model.</b> A cache hit is only ever as stale as the TTL. In-process writes
/// shorten that to "immediately": <see cref="UserStateInvalidationInterceptor"/> invalidates the
/// affected user after every committed EF save of the entities these values derive from
/// (freeze, revoke / logout-all, password change, role change, suspension, subscription change,
/// plan edit). What the interceptor cannot see are writes made by ANOTHER process (the idle
/// blue/green slot and the ai-worker run the background sweeps) and bulk SQL writes; for those
/// the TTL is the bound. Cached values that carry an end time (entitlement expiry, learner access
/// expiry) are also capped at that instant, so nothing is served past its own end.</para>
///
/// <para>Implementation: a version stripe per subject hash (no per-user bookkeeping to clean up;
/// a stripe collision only costs an extra miss) plus a global version, compared on every read.</para>
/// </summary>
public sealed class UserStateCache
{
    /// <summary>Feature-flag key of the runtime kill switch. No row = ON (the approved default);
    /// a row with <c>Enabled = false</c> turns the cache off within ~30 s, no deploy
    /// (<see cref="UserStateCacheSwitchWorker"/> polls it).</summary>
    public const string FeatureFlagKey = "user_state_cache";

    private const int Stripes = 4096;
    private const int SweepEverySets = 1024;

    private static readonly Meter CacheMeter = new("OetLearner.UserStateCache", "1.0");
    private static readonly Counter<long> HitMetric = CacheMeter.CreateCounter<long>(
        "oet.user_state_cache.hits", description: "Per-user state cache hits.");
    private static readonly Counter<long> MissMetric = CacheMeter.CreateCounter<long>(
        "oet.user_state_cache.misses", description: "Per-user state cache misses (absent, expired or invalidated).");
    private static readonly Counter<long> SetMetric = CacheMeter.CreateCounter<long>(
        "oet.user_state_cache.sets", description: "Per-user state cache fills.");
    private static readonly Counter<long> RejectedSetMetric = CacheMeter.CreateCounter<long>(
        "oet.user_state_cache.rejected_sets", description: "Fills dropped because the subject was invalidated during the read.");
    private static readonly Counter<long> InvalidationMetric = CacheMeter.CreateCounter<long>(
        "oet.user_state_cache.invalidations", description: "Subject / global invalidations.");

    private sealed class Entry(object value, DateTimeOffset storedAt, DateTimeOffset expiresAt, int stripe, long stripeVersion, long globalVersion)
    {
        public readonly object Value = value;
        public readonly DateTimeOffset StoredAt = storedAt;
        public readonly DateTimeOffset ExpiresAt = expiresAt;
        public readonly int Stripe = stripe;
        public readonly long StripeVersion = stripeVersion;
        public readonly long GlobalVersion = globalVersion;
    }

    private sealed class KindCounters
    {
        public long Hits;
        public long Misses;
        public long Sets;
        public long RejectedSets;
    }

    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, KindCounters> counters = new(StringComparer.Ordinal);
    private readonly long[] stripeVersions = new long[Stripes];
    private readonly IOptions<UserStateCacheOptions> options;
    private readonly TimeProvider time;

    private long globalVersion;
    private long invalidations;
    private long globalInvalidations;
    private int setsSinceSweep;
    private int sweeping;
    private volatile bool runtimeEnabled = true;

    public UserStateCache(IOptions<UserStateCacheOptions> options, TimeProvider? time = null)
    {
        this.options = options;
        this.time = time ?? TimeProvider.System;
    }

    /// <summary>True when neither the config nor the runtime kill switch has turned the cache off.
    /// Synchronous and I/O free: safe on every request.</summary>
    public bool IsEnabled => options.Value.Enabled && runtimeEnabled;

    /// <summary>
    /// Sets the runtime kill switch (fed from the <c>user_state_cache</c> feature flag by
    /// <see cref="UserStateCacheSwitchWorker"/>). Turning it OFF drops every entry, so turning it
    /// back ON can never serve an entry from before the switch.
    /// </summary>
    public void ApplyRuntimeSwitch(bool enabled)
    {
        if (runtimeEnabled && !enabled)
        {
            InvalidateAll();
        }

        runtimeEnabled = enabled;
    }

    public UserStateReadToken BeginRead(string subject)
    {
        var stripe = StripeOf(subject);
        return new UserStateReadToken(
            stripe,
            Volatile.Read(ref stripeVersions[stripe]),
            Volatile.Read(ref globalVersion));
    }

    public bool TryGet<T>(string kind, string subject, string variant, out T? value) where T : class
    {
        value = null;
        if (!IsEnabled)
        {
            return false;
        }

        var key = KeyOf(kind, subject, variant);
        if (!entries.TryGetValue(key, out var entry))
        {
            Count(kind, c => Interlocked.Increment(ref c.Misses), MissMetric);
            return false;
        }

        var now = time.GetUtcNow();
        // now < StoredAt: the wall clock moved backwards; never trust such an entry.
        if (now < entry.StoredAt
            || now >= entry.ExpiresAt
            || entry.GlobalVersion != Volatile.Read(ref globalVersion)
            || entry.StripeVersion != Volatile.Read(ref stripeVersions[entry.Stripe]))
        {
            entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
            Count(kind, c => Interlocked.Increment(ref c.Misses), MissMetric);
            return false;
        }

        if (entry.Value is not T typed)
        {
            Count(kind, c => Interlocked.Increment(ref c.Misses), MissMetric);
            return false;
        }

        value = typed;
        Count(kind, c => Interlocked.Increment(ref c.Hits), HitMetric);
        return true;
    }

    /// <summary>
    /// Stores <paramref name="value"/> unless the subject was invalidated since
    /// <paramref name="token"/> was taken. The entry lives for the TTL, or until
    /// <paramref name="notAfter"/> when that comes first (an entitlement / access end time).
    /// </summary>
    public void Set<T>(string kind, string subject, string variant, T value, UserStateReadToken token, DateTimeOffset? notAfter)
        where T : class
    {
        if (!IsEnabled)
        {
            return;
        }

        if (token.StripeVersion != Volatile.Read(ref stripeVersions[token.Stripe])
            || token.GlobalVersion != Volatile.Read(ref globalVersion))
        {
            Count(kind, c => Interlocked.Increment(ref c.RejectedSets), RejectedSetMetric);
            return;
        }

        var now = time.GetUtcNow();
        var expiresAt = now + options.Value.Ttl;
        if (notAfter is { } cap && cap < expiresAt)
        {
            expiresAt = cap;
        }

        if (expiresAt <= now)
        {
            return;
        }

        entries[KeyOf(kind, subject, variant)] = new Entry(value, now, expiresAt, token.Stripe, token.StripeVersion, token.GlobalVersion);
        Count(kind, c => Interlocked.Increment(ref c.Sets), SetMetric);

        if (Interlocked.Increment(ref setsSinceSweep) >= SweepEverySets)
        {
            Sweep(now);
        }
    }

    /// <summary>Drops every cached value of one subject (see <see cref="UserStateCacheSubjects"/>).</summary>
    public void Invalidate(string subject)
    {
        Interlocked.Increment(ref stripeVersions[StripeOf(subject)]);
        Interlocked.Increment(ref invalidations);
        InvalidationMetric.Add(1, new KeyValuePair<string, object?>("scope", "subject"));
    }

    public void InvalidateAuthAccount(string? authAccountId)
    {
        if (!string.IsNullOrEmpty(authAccountId))
        {
            Invalidate(UserStateCacheSubjects.AuthAccount(authAccountId));
        }
    }

    public void InvalidateLearner(string? learnerUserId)
    {
        if (!string.IsNullOrEmpty(learnerUserId))
        {
            Invalidate(UserStateCacheSubjects.Learner(learnerUserId));
        }
    }

    /// <summary>Drops everything (plan catalog or freeze policy edits, add-on changes, kill switch).</summary>
    public void InvalidateAll()
    {
        Interlocked.Increment(ref globalVersion);
        Interlocked.Increment(ref globalInvalidations);
        InvalidationMetric.Add(1, new KeyValuePair<string, object?>("scope", "all"));
        entries.Clear();
    }

    public UserStateCacheSnapshot Snapshot() => new(
        ConfigEnabled: options.Value.Enabled,
        RuntimeEnabled: runtimeEnabled,
        TtlSeconds: (int)options.Value.Ttl.TotalSeconds,
        Entries: entries.Count,
        Invalidations: Interlocked.Read(ref invalidations),
        GlobalInvalidations: Interlocked.Read(ref globalInvalidations),
        FeatureFlagKey: FeatureFlagKey,
        Kinds: counters
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new UserStateCacheKindStats(
                pair.Key,
                Interlocked.Read(ref pair.Value.Hits),
                Interlocked.Read(ref pair.Value.Misses),
                Interlocked.Read(ref pair.Value.Sets),
                Interlocked.Read(ref pair.Value.RejectedSets)))
            .ToList());

    private static string KeyOf(string kind, string subject, string variant)
        => string.Concat(kind, "|", subject, "|", variant);

    private static int StripeOf(string subject)
        => (StringComparer.Ordinal.GetHashCode(subject) & int.MaxValue) % Stripes;

    private void Count(string kind, Action<KindCounters> bump, Counter<long> metric)
    {
        bump(counters.GetOrAdd(kind, static _ => new KindCounters()));
        metric.Add(1, new KeyValuePair<string, object?>("kind", kind));
    }

    private void Sweep(DateTimeOffset now)
    {
        if (Interlocked.CompareExchange(ref sweeping, 1, 0) != 0)
        {
            return;
        }

        try
        {
            Interlocked.Exchange(ref setsSinceSweep, 0);
            var globalNow = Volatile.Read(ref globalVersion);
            foreach (var pair in entries)
            {
                var entry = pair.Value;
                if (now >= entry.ExpiresAt
                    || now < entry.StoredAt
                    || entry.GlobalVersion != globalNow
                    || entry.StripeVersion != Volatile.Read(ref stripeVersions[entry.Stripe]))
                {
                    entries.TryRemove(pair);
                }
            }

            // ponytail: a hard bound beats an LRU here. With a 15 s TTL a full clear only costs
            // one round of misses; upgrade to LRU if MaxEntries is ever reached in practice.
            if (entries.Count > Math.Max(1, options.Value.MaxEntries))
            {
                entries.Clear();
            }
        }
        finally
        {
            Volatile.Write(ref sweeping, 0);
        }
    }
}

/// <summary>
/// The JWT account-liveness row (Program.cs OnTokenValidated), projected from the account /
/// learner / expert / refresh-token tables. Held by <see cref="UserStateCache"/> only after the
/// token was ACCEPTED; a rejected state is always re-read, so a stale denial can never lock a
/// reinstated user out.
/// </summary>
public sealed record JwtAccountState(
    DateTimeOffset? DeletedAt,
    string Role,
    bool LearnerIsActive,
    DateTimeOffset? LearnerAccessExpiresAt,
    bool ExpertIsActive,
    bool SessionFamilyAlive);
