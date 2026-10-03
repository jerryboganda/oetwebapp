using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai.TypeSafe;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// Owner-console triage is fail-open: only a real Jev judgment may say
/// <c>review_required</c>; no key, an outage, a timeout, a bad config or an
/// oversized message all report <c>unavailable</c> so the caller carries on.
/// The effort tier is a third, advisory answer in the same batched call: it never
/// decides <c>review_required</c> and a missing or doubtful tier is simply null.
/// </summary>
public sealed class JevDevelopmentTriageTests
{
    private sealed class FakeJudgments(Func<CancellationToken, Task<JevJudgmentResult>> respond) : ITypeSafeJudgmentService
    {
        public int Calls { get; private set; }
        public JevJudgmentRequest? LastRequest { get; private set; }
        public JevCallMetadata? LastMetadata { get; private set; }

        public Task<JevJudgmentResult> AskAsync(JevJudgmentRequest request, JevCallMetadata call, CancellationToken ct)
        {
            Calls++;
            LastRequest = request;
            LastMetadata = call;
            return respond(ct);
        }
    }

    private static readonly string[] TaskKeys = ["implement", "debug", "review", "verify", "plan", "content", "other", "unclear"];
    private static readonly string[] RiskKeys = ["low", "elevated", "high", "unclear"];
    private static readonly string[] EffortKeys = ["lookup", "bounded_edit", "cross_module", "unclear"];

    private static JevAnswer Choice(string[] keys, string winner, double top)
    {
        var rest = (1 - top) / (keys.Length - 1);
        return new JevAnswer(JevQuestionKind.Choice, null,
            new JevChoiceAnswer(winner, keys.ToDictionary(key => key, key => key == winner ? top : rest), top), null);
    }

    /// <summary>
    /// A well-formed Ok triage (confident "debug" / "low") whose effort_tier answer is
    /// <paramref name="effortTier"/> at <paramref name="effortConfidence"/>; null omits the answer.
    /// </summary>
    private static JevJudgmentResult Triage(string? effortTier, double effortConfidence = 0.95, string task = "debug", double taskConfidence = 0.95)
    {
        var answers = new Dictionary<string, JevAnswer>
        {
            ["task_kind"] = Choice(TaskKeys, task, taskConfidence),
            ["risk_level"] = Choice(RiskKeys, "low", 0.95),
        };
        if (effortTier is not null)
            answers["effort_tier"] = Choice(EffortKeys, effortTier, effortConfidence);
        return new JevJudgmentResult(JevCallStatus.Ok, new TypeSafeOptions().Model, answers, 200, 10, null);
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
        Assert.Null(advisory.EffortTier);
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
    public async Task Triage_AsksTaskRiskAndEffortTier_InOneBatchedCall()
    {
        var fake = Returning(Triage("bounded_edit"));

        await JevWorkflowAdvisor.TriageDevelopmentAsync(fake, Options(), "Fix the bug", CancellationToken.None);

        Assert.Equal(1, fake.Calls);
        Assert.Equal(AiFeatureCodes.JevDevelopmentTriage, fake.LastMetadata!.FeatureCode);
        Assert.Equal(new[] { "task_kind", "risk_level", "effort_tier" }, fake.LastRequest!.Questions.Select(q => q.Id).ToArray());
        var effort = fake.LastRequest.Questions.Single(q => q.Id == "effort_tier");
        Assert.Equal(JevQuestionKind.Choice, effort.Kind);
        Assert.Equal(EffortKeys.Order(StringComparer.Ordinal), effort.ChoiceCriteria!.Keys.Order(StringComparer.Ordinal));
        Assert.Contains("untrusted", effort.Instructions, StringComparison.Ordinal);
        Assert.Contains("select an engine, model or reasoning effort", effort.Instructions, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("lookup")]
    [InlineData("bounded_edit")]
    [InlineData("cross_module")]
    public async Task AConfidentEffortTier_IsReturnedAsAdvice(string tier)
    {
        var advisory = await JevWorkflowAdvisor.TriageDevelopmentAsync(
            Returning(Triage(tier)), Options(), "Fix the bug", CancellationToken.None);

        Assert.Equal("ok", advisory!.Status);
        Assert.False(advisory.RequiresHumanReview);
        Assert.Equal(tier, advisory.EffortTier);
        Assert.Equal("debug", advisory.TaskKind);
        Assert.Equal("low", advisory.RiskLevel);
    }

    [Theory]
    [InlineData("unclear", 0.95)]
    [InlineData("cross_module", 0.50)]
    public async Task AnUnclearOrDoubtfulEffortTier_IsNull_AndNeverBlocksTheTriage(string tier, double confidence)
    {
        var advisory = await JevWorkflowAdvisor.TriageDevelopmentAsync(
            Returning(Triage(tier, confidence)), Options(), "Fix the bug", CancellationToken.None);

        Assert.Equal("ok", advisory!.Status);
        Assert.False(advisory.RequiresHumanReview);
        Assert.Null(advisory.EffortTier);
    }

    [Fact]
    public async Task AMissingEffortAnswer_DropsTheTier_NotTheTriage()
    {
        var advisory = await JevWorkflowAdvisor.TriageDevelopmentAsync(
            Returning(Triage(effortTier: null)), Options(), "Fix the bug", CancellationToken.None);

        Assert.Equal("ok", advisory!.Status);
        Assert.Equal("debug", advisory.TaskKind);
        Assert.Null(advisory.EffortTier);
    }

    [Fact]
    public async Task AMalformedEffortAnswer_DropsTheTier_NotTheTriage()
    {
        var result = Triage("lookup");
        var answers = result.Answers!.ToDictionary(pair => pair.Key, pair => pair.Value);
        answers["effort_tier"] = new JevAnswer(JevQuestionKind.Choice, null,
            new JevChoiceAnswer("epic", new Dictionary<string, double> { ["epic"] = 1 }, 1), null);

        var advisory = await JevWorkflowAdvisor.TriageDevelopmentAsync(
            Returning(result with { Answers = answers }), Options(), "Fix the bug", CancellationToken.None);

        Assert.Equal("ok", advisory!.Status);
        Assert.Null(advisory.EffortTier);
    }

    [Fact]
    public async Task ADoubtfulTask_StillRequiresReview_WhateverTheEffortTierSays()
    {
        var advisory = await JevWorkflowAdvisor.TriageDevelopmentAsync(
            Returning(Triage("lookup", effortConfidence: 0.99, task: "implement", taskConfidence: 0.55)),
            Options(), "Fix the bug", CancellationToken.None);

        Assert.Equal("review_required", advisory!.Status);
        Assert.True(advisory.RequiresHumanReview);
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
