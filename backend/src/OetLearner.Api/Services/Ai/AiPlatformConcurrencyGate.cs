namespace OetLearner.Api.Services.Ai;

/// <summary>
/// W3 — global in-process cap on simultaneous platform-key provider calls.
/// Five in flight is enough for mixed scoring + interactive load without
/// letting a retry storm open unbounded outbound HTTP to Anthropic/OpenAI.
/// BYOK callers never enter this gate (they spend the learner's own key).
/// </summary>
public sealed class AiPlatformConcurrencyGate
{
    public const int MaxInFlight = 5;

    private readonly SemaphoreSlim _semaphore = new(MaxInFlight, MaxInFlight);

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        await WaitAsync(ct);
        try
        {
            return await work(ct);
        }
        finally
        {
            Release();
        }
    }

    public Task WaitAsync(CancellationToken ct) => _semaphore.WaitAsync(ct);

    public void Release() => _semaphore.Release();
}
