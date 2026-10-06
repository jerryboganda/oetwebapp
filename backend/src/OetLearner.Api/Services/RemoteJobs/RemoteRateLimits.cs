using System.Collections.Concurrent;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>
/// Per-key counters over a fixed one-minute window, used for the remote-worker limits of OET-RWP/1
/// section 2.6 and for the pre-authentication failed-verification throttle of section 2.2.
///
/// <para>
/// Post-authentication limits are keyed by the VERIFIED node id (or credential id), never by IP:
/// helpers sit behind provider NAT, and forwarded-header trust is sized for the web path. The
/// built-in ASP.NET rate limiter runs BEFORE authorization and could not see the verified node, so
/// the limits live here and are applied by an endpoint filter after authentication.
/// </para>
///
/// <para>Windows are aligned to wall-clock minutes and time comes from <see cref="TimeProvider"/>, so tests can drive them.</para>
/// </summary>
public sealed class RemoteRateLimits(TimeProvider timeProvider)
{
    private static readonly long WindowTicks = TimeSpan.TicksPerMinute;

    private sealed class Window
    {
        public long Start;
        public int Count;
    }

    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _streams = new(StringComparer.Ordinal);
    private int _operations;

    /// <summary>
    /// Counts one hit for <paramref name="bucket"/>/<paramref name="key"/>. Returns false (without
    /// counting) when <paramref name="limit"/> hits were already taken in the current window.
    /// </summary>
    public bool TryConsume(string bucket, string key, int limit, out int retryAfterSeconds)
    {
        var now = timeProvider.GetUtcNow().UtcTicks;
        var windowStart = now - (now % WindowTicks);
        var window = _windows.GetOrAdd($"{bucket}|{key}", _ => new Window { Start = windowStart });
        retryAfterSeconds = SecondsUntil(windowStart + WindowTicks, now);

        bool allowed;
        lock (window)
        {
            if (window.Start != windowStart)
            {
                window.Start = windowStart;
                window.Count = 0;
            }

            allowed = window.Count < limit;
            if (allowed) window.Count++;
        }

        PruneOccasionally(windowStart);
        return allowed;
    }

    /// <summary>True when <paramref name="limit"/> or more hits were recorded in the current window (does not count).</summary>
    public bool IsExceeded(string bucket, string key, int limit, out int retryAfterSeconds)
    {
        var now = timeProvider.GetUtcNow().UtcTicks;
        var windowStart = now - (now % WindowTicks);
        retryAfterSeconds = SecondsUntil(windowStart + WindowTicks, now);
        if (!_windows.TryGetValue($"{bucket}|{key}", out var window)) return false;

        lock (window)
        {
            return window.Start == windowStart && window.Count >= limit;
        }
    }

    /// <summary>Records one hit unconditionally (a failed authentication).</summary>
    public void Hit(string bucket, string key)
    {
        var now = timeProvider.GetUtcNow().UtcTicks;
        var windowStart = now - (now % WindowTicks);
        var window = _windows.GetOrAdd($"{bucket}|{key}", _ => new Window { Start = windowStart });
        lock (window)
        {
            if (window.Start != windowStart)
            {
                window.Start = windowStart;
                window.Count = 0;
            }

            if (window.Count < int.MaxValue) window.Count++;
        }

        PruneOccasionally(windowStart);
    }

    /// <summary>
    /// Takes one concurrent-stream slot for <paramref name="nodeId"/>. Returns null when
    /// <paramref name="limit"/> streams are already open (queue length 0); dispose the result to release.
    /// </summary>
    public IDisposable? TryAcquireStream(string nodeId, int limit)
    {
        var count = _streams.AddOrUpdate(nodeId, 1, (_, current) => current + 1);
        if (count > limit)
        {
            Release(nodeId);
            return null;
        }

        return new StreamLease(this, nodeId);
    }

    /// <summary>Open stream slots (diagnostics and tests).</summary>
    public int OpenStreams(string nodeId) => _streams.TryGetValue(nodeId, out var count) ? count : 0;

    private void Release(string nodeId)
    {
        var remaining = _streams.AddOrUpdate(nodeId, 0, (_, current) => Math.Max(0, current - 1));
        if (remaining == 0) _streams.TryRemove(new KeyValuePair<string, int>(nodeId, 0));
    }

    private static int SecondsUntil(long endTicks, long nowTicks)
        => (int)Math.Max(1, (endTicks - nowTicks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond);

    private void PruneOccasionally(long currentWindowStart)
    {
        if (Interlocked.Increment(ref _operations) % 2048 != 0) return;

        foreach (var (key, window) in _windows)
        {
            bool stale;
            lock (window)
            {
                stale = window.Start < currentWindowStart - WindowTicks;
            }

            if (stale) _windows.TryRemove(key, out _);
        }
    }

    private sealed class StreamLease(RemoteRateLimits owner, string nodeId) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) owner.Release(nodeId);
        }
    }
}
