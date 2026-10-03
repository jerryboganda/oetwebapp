using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Ai.TypeSafe;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// Owner-console triage is fail-open: only a real Jev judgment may say
/// <c>review_required</c>; no key, an outage, a timeout, a bad config or an
/// oversized message all report <c>unavailable</c> so the caller carries on.
/// </summary>
public sealed class JevDevelopmentTriageTests
{
    private sealed class FakeJudgments(Func<CancellationToken, Task<JevJudgmentResult>> respond) : ITypeSafeJudgmentService
    {
        public int Calls { get; private set; }

        public Task<JevJudgmentResult> AskAsync(JevJudgmentRequest request, JevCallMetadata call, CancellationToken ct)
        {
            Calls++;
            return respond(ct);
        }
    }

    private static TypeSafeOptions Options(Action<TypeSafeOptions>? configure = null)
    {
        var options = new TypeSafeOptions { Enabled = true, ApiKey = "apikey_test", DevelopmentTriageEnabled = true };
        configure?.Invoke(options);
        return options;
    }

    private static FakeJudgments Returning(JevJudgmentResult result) => new(_ => Task.FromResult(result));

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task FlagsOff_ReturnsNull_WithoutCallingJev(bool enabled, bool triageEnabled)
    {
        var fake = Returning(JevJudgmentResult.Unavailable("x"));

        var advisory = await JevWorkflowAdvisor.TriageDevelopmentAsync(
            fake, Options(o => { o.Enabled = enabled; o.DevelopmentTriageEnabled = triageEnabled; }), "Fix the bug", CancellationToken.None);

        Assert.Null(advisory);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task Disabled_ReportsUnavailable_NotConfigured()
    {
        var advisory = await JevWorkflowAdvisor.TriageDevelopmentAsync(
            Returning(JevJudgmentResult.Disabled("typesafe_key_missing")), Options(), "Fix the bug", CancellationToken.None);

        Assert.Equal("unavailable", advisory!.Status);
        Assert.Equal("jev_not_configured", advisory.Reason);
    }

    [Fact]
    public async Task Unavailable_ReportsUnavailable()
    {
        var advisory = await JevWorkflowAdvisor.TriageDevelopmentAsync(
            Returning(JevJudgmentResult.Unavailable("jev_lease_blocked")), Options(), "Fix the bug", CancellationToken.None);

        Assert.Equal("unavailable", advisory!.Status);
        Assert.Equal("jev_unavailable", advisory.Reason);
    }

    [Fact]
    public async Task ACrash_ReportsUnavailable_NeverThrows()
    {
        var advisory = await JevWorkflowAdvisor.TriageDevelopmentAsync(
            new FakeJudgments(_ => throw new InvalidOperationException("boom")), Options(), "Fix the bug", CancellationToken.None);

        Assert.Equal("unavailable", advisory!.Status);
    }

    [Fact]
    public async Task AMessageTooLargeToTriage_ReportsUnavailable_WithoutCallingJev()
    {
        var fake = Returning(JevJudgmentResult.Unavailable("x"));

        var advisory = await JevWorkflowAdvisor.TriageDevelopmentAsync(
            fake, Options(), new string('a', 20_001), CancellationToken.None);

        Assert.Equal("unavailable", advisory!.Status);
        Assert.Equal("jev_context_too_large", advisory.Reason);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task ASlowJev_IsCutOffAtTheClientTimeout_AndReportsUnavailable()
    {
        var slow = new FakeJudgments(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return JevJudgmentResult.Disabled("unreachable");
        });

        var advisory = await JevWorkflowAdvisor.TriageDevelopmentAsync(
            slow, Options(o => o.TimeoutSeconds = 1), "Fix the bug", CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("unavailable", advisory!.Status);
    }

    [Fact]
    public async Task ACallerCancellation_StillPropagates()
    {
        using var cts = new CancellationTokenSource();
        var slow = new FakeJudgments(async ct =>
        {
            cts.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return JevJudgmentResult.Disabled("unreachable");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => JevWorkflowAdvisor.TriageDevelopmentAsync(slow, Options(), "Fix the bug", cts.Token));
    }
}
