using OetLearner.Api.Services.Ai;
using Xunit;

namespace OetLearner.Api.Tests.Services;

public sealed class AiPlatformConcurrencyGateTests
{
    [Fact]
    public async Task RunAsync_CapsSimultaneousPlatformCallsAtFive()
    {
        var gate = new AiPlatformConcurrencyGate();
        var inFlight = 0;
        var peak = 0;
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, 12).Select(_ => gate.RunAsync(async _ =>
        {
            var now = Interlocked.Increment(ref inFlight);
            Interlocked.Exchange(ref peak, Math.Max(peak, now));
            if (now >= AiPlatformConcurrencyGate.MaxInFlight)
                started.TrySetResult();
            await release.Task;
            Interlocked.Decrement(ref inFlight);
            return 1;
        }, CancellationToken.None)).ToArray();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(AiPlatformConcurrencyGate.MaxInFlight, peak);
        release.SetResult();
        await Task.WhenAll(tasks);
        Assert.Equal(AiPlatformConcurrencyGate.MaxInFlight, peak);
    }
}
