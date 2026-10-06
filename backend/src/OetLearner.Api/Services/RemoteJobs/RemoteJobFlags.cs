using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>An immutable view of the remote-job feature flags at one instant (absent row = OFF).</summary>
public sealed class RemoteFlagSnapshot(IReadOnlySet<string> enabledKeys)
{
    public static readonly RemoteFlagSnapshot AllOff = new(new HashSet<string>(StringComparer.Ordinal));

    public bool IsOn(string key) => enabledKeys.Contains(key);

    /// <summary>Master switch (producers and <c>claim</c>).</summary>
    public bool Master => IsOn(RemoteJobFlagKeys.Master);

    /// <summary>Emergency freeze: <c>complete</c> refuses to apply results.</summary>
    public bool FreezeApplies => IsOn(RemoteJobFlagKeys.FreezeApplies);

    /// <summary>The fleet service plane (<c>/v1/internal/fleet/*</c>) answers only when this is on.</summary>
    public bool FleetService => IsOn(RemoteJobFlagKeys.FleetService);

    /// <summary>True when the master switch AND the kind's flag for <paramref name="purpose"/> are on (canary: master only).</summary>
    public bool KindEnabled(string kind, string purpose)
    {
        if (!Master) return false;
        var key = RemoteJobKinds.FlagKeyFor(kind, purpose);
        return key is null || IsOn(key);
    }
}

public interface IRemoteJobFlags
{
    /// <summary>Flags as of at most five seconds ago; fails closed (everything off) when they cannot be read.</summary>
    Task<RemoteFlagSnapshot> GetAsync(CancellationToken ct);

    /// <summary>Drops the cache so the next read hits the database (tests, admin flips).</summary>
    void Invalidate();
}

/// <summary>
/// Reads the remote-job flags from <c>FeatureFlags</c> with a per-process cache of at most five
/// seconds (OET-RWP/1 section 9.2) so claim polling does not turn into a query per request. Every
/// flag defaults OFF: a missing row, an unreadable database or an ambiguous value is "off".
/// </summary>
public sealed class RemoteJobFlags(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<RemoteJobFlags> logger) : IRemoteJobFlags
{
    internal static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(5);

    /// <summary>A snapshot older than this is not served when the database is unreachable (fail closed).</summary>
    private static readonly TimeSpan MaxStale = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile RemoteFlagSnapshot _snapshot = RemoteFlagSnapshot.AllOff;

    // UTC ticks of the last load attempt; 0 = never. Read and written atomically (64-bit).
    private long _loadedTicks;

    // UTC ticks of the last SUCCESSFUL load; 0 = never. The stale-snapshot limit is measured from here, not from the last
    // attempt, so a database that stays down turns every flag off after MaxStale instead of serving old values forever.
    private long _successTicks;

    public async Task<RemoteFlagSnapshot> GetAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        if (IsFresh(now)) return _snapshot;

        await _gate.WaitAsync(ct);
        try
        {
            now = timeProvider.GetUtcNow();
            if (IsFresh(now)) return _snapshot;

            try
            {
                _snapshot = await LoadAsync(ct);
                Volatile.Write(ref _successTicks, now.UtcTicks);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Remote job flags could not be read; failing closed.");
                var successTicks = Volatile.Read(ref _successTicks);
                var age = successTicks == 0 ? MaxStale : now - new DateTimeOffset(successTicks, TimeSpan.Zero);
                if (age >= MaxStale) _snapshot = RemoteFlagSnapshot.AllOff;
            }

            // Back off for the TTL either way so an unreachable database is not hammered.
            Volatile.Write(ref _loadedTicks, now.UtcTicks);
            return _snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate() => Volatile.Write(ref _loadedTicks, 0);

    private bool IsFresh(DateTimeOffset now)
    {
        var ticks = Volatile.Read(ref _loadedTicks);
        return ticks != 0 && now - new DateTimeOffset(ticks, TimeSpan.Zero) < CacheTtl;
    }

    private async Task<RemoteFlagSnapshot> LoadAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var keys = RemoteJobFlagKeys.All.ToArray();

        var rows = await db.FeatureFlags
            .AsNoTracking()
            .Where(flag => keys.Contains(flag.Key))
            .Select(flag => new { flag.Key, flag.Enabled, flag.UpdatedAt })
            .ToListAsync(ct);

        // Newest row wins if a key was ever duplicated (mirrors CompanionFeatureFlags).
        var enabled = rows
            .GroupBy(row => row.Key, StringComparer.Ordinal)
            .Where(group => group.OrderByDescending(row => row.UpdatedAt).First().Enabled)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        return new RemoteFlagSnapshot(enabled);
    }
}
