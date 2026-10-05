using Fleet.Manager.Infrastructure;

namespace Fleet.Manager.Tests.Infrastructure;

/// <summary>A clock the test moves by hand. Every timeout, TTL and wait in the manager is measured against it.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset? start = null)
    {
        _now = start ?? new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public void Advance(TimeSpan by)
    {
        lock (_gate)
        {
            _now += by;
        }
    }
}

/// <summary>A delay that returns immediately and moves the manual clock forward, so a 180-second verification window costs no wall time.</summary>
public sealed class AdvancingDelay : IDelay
{
    private readonly ManualTimeProvider _time;

    public AdvancingDelay(ManualTimeProvider time)
    {
        _time = time;
    }

    public int Delays { get; private set; }

    public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Delays++;
        _time.Advance(duration);
        return Task.CompletedTask;
    }
}
