using System.Collections.Concurrent;

namespace OetLearner.Api.Services.AiAssistant.Safety;

/// <summary>
/// In-memory circuit breaker implementation.
/// - Opens after 5 consecutive failures.
/// - Half-open after 60 seconds (allows 1 probe request).
/// - Closes after 3 successful probes.
/// Thread-safe via locking per circuit state.
/// </summary>
public sealed class CircuitBreaker : ICircuitBreaker
{
    private const int FailureThreshold = 5;
    private const int SuccessProbesRequired = 3;
    private static readonly TimeSpan HalfOpenDelay = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, CircuitState> _states = new();

    public Task<bool> IsOpenAsync(string toolCode, CancellationToken ct)
    {
        var state = _states.GetOrAdd(toolCode, _ => new CircuitState());

        lock (state.Lock)
        {
            switch (state.Status)
            {
                case CircuitStatus.Closed:
                    return Task.FromResult(false);

                case CircuitStatus.Open:
                    // Check if enough time has passed to transition to half-open
                    if (DateTimeOffset.UtcNow - state.LastFailureAt >= HalfOpenDelay)
                    {
                        state.Status = CircuitStatus.HalfOpen;
                        state.HalfOpenProbesAllowed = 1;
                        return Task.FromResult(false);
                    }
                    return Task.FromResult(true);

                case CircuitStatus.HalfOpen:
                    // Allow one probe at a time
                    if (state.HalfOpenProbesAllowed > 0)
                    {
                        state.HalfOpenProbesAllowed--;
                        return Task.FromResult(false);
                    }
                    return Task.FromResult(true);

                default:
                    return Task.FromResult(false);
            }
        }
    }

    public Task RecordSuccessAsync(string toolCode, CancellationToken ct)
    {
        var state = _states.GetOrAdd(toolCode, _ => new CircuitState());

        lock (state.Lock)
        {
            state.ConsecutiveFailures = 0;

            if (state.Status == CircuitStatus.HalfOpen)
            {
                state.SuccessfulProbes++;
                if (state.SuccessfulProbes >= SuccessProbesRequired)
                {
                    // Circuit fully closes
                    state.Status = CircuitStatus.Closed;
                    state.SuccessfulProbes = 0;
                }
                else
                {
                    // Allow more probes
                    state.HalfOpenProbesAllowed = 1;
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task RecordFailureAsync(string toolCode, CancellationToken ct)
    {
        var state = _states.GetOrAdd(toolCode, _ => new CircuitState());

        lock (state.Lock)
        {
            state.ConsecutiveFailures++;
            state.LastFailureAt = DateTimeOffset.UtcNow;
            state.SuccessfulProbes = 0;

            if (state.Status == CircuitStatus.HalfOpen)
            {
                // Failed probe — back to open
                state.Status = CircuitStatus.Open;
            }
            else if (state.ConsecutiveFailures >= FailureThreshold)
            {
                state.Status = CircuitStatus.Open;
            }
        }

        return Task.CompletedTask;
    }

    private enum CircuitStatus { Closed, Open, HalfOpen }

    private sealed class CircuitState
    {
        public readonly object Lock = new();
        public CircuitStatus Status = CircuitStatus.Closed;
        public int ConsecutiveFailures;
        public int SuccessfulProbes;
        public int HalfOpenProbesAllowed;
        public DateTimeOffset LastFailureAt = DateTimeOffset.MinValue;
    }
}
