namespace Fleet.Manager.Infrastructure;

/// <summary>
/// The only place the manager waits. Polling loops (waiting for a heartbeat, a canary, a drain) call
/// this instead of <c>Task.Delay</c>, so tests can run a 180-second verification window instantly
/// while advancing the same clock the timeouts are measured against.
/// </summary>
public interface IDelay
{
    Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken);
}

public sealed class SystemDelay : IDelay
{
    private readonly TimeProvider _time;

    public SystemDelay(TimeProvider time)
    {
        _time = time;
    }

    public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken) =>
        Task.Delay(duration, _time, cancellationToken);
}
