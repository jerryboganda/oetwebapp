using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// WAI-03 — the Writing grade chain (owner rule MAX-ALWAYS-ON): Max ×2 → Anthropic API ×1 →
/// Codex ×2, each attempt on its own AI-operation slot and wall-clock budget, failing over on
/// every provider-side failure and never on a control-plane refusal or the caller's cancellation.
/// </summary>
public sealed class WritingGradeChainTests
{
    private const string Max = WritingSubscriptionProviders.Claude;
    private const string Api = WritingSubscriptionProviders.ClaudeApi;
    private const string Codex = WritingSubscriptionProviders.Codex;

    private static readonly WritingSubscriptionDecision MaxDecision =
        new(Max, WritingSubscriptionProviders.ClaudeModel, "max_always_first", null, false);

    private static AiGatewayRequest Template() => new()
    {
        Prompt = new AiGroundedPrompt { SystemPrompt = "system", TaskInstruction = "score" },
        UserInput = "letter",
        Temperature = 0.2,
        MaxTokens = 16000,
        FeatureCode = AiFeatureCodes.WritingGrade,
        PromptTemplateId = "writing.score.v1",
        UserId = "chain-learner",
        CreditReservationId = "res-1",
        FreeSampleGrant = true,
        ResourceId = "sub-1",
        ResourceType = "writing_submission",
        AssessmentContext = AiAssessmentContext.Practice,
    };

    private static AiGatewayResult Ok(AiGatewayRequest request) => new()
    {
        Completion = "graded",
        ResolvedProvider = request.Provider,
        ResolvedModel = request.Model,
    };

    private static string Parse(AiGatewayResult result)
        => result.Completion == "unreadable"
            ? throw new WritingRubricUnreadableException("unreadable")
            : result.Completion;

    private static Task<string> RunAsync(
        IAiGatewayService gateway,
        WritingGradeChainOptions? options = null,
        TimeProvider? clock = null,
        Func<WritingGradeHop, bool>? fault = null,
        WritingSubscriptionDecision? decision = null,
        int epoch = 1,
        CancellationToken ct = default)
        => WritingGradeChain.RunAsync(
            gateway, Template(), decision ?? MaxDecision, epoch, Parse,
            options ?? new WritingGradeChainOptions(), clock ?? TimeProvider.System,
            NullLogger.Instance, fault, ct);

    private static AiProviderHttpException Typed(AiProviderErrorClass errorClass, int status) => new(
        "Anthropic", status, "Error", null,
        new AiProviderError(errorClass, status, "invalid_request_error", null, "Your credit balance is too low.", "req_1", null));

    [Fact]
    public async Task EveryRouteFails_InPlanOrder_MaxTwice_ApiOnce_CodexTwice_ThenOneGenericFailure()
    {
        var gateway = new RecordingGateway(_ => throw new HttpRequestException("connection refused"));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(gateway));

        Assert.Equal(new[] { Max, Max, Api, Codex, Codex }, gateway.Requests.Select(r => r.Provider));
        Assert.Equal(
            new[] { WritingSubscriptionProviders.ClaudeModel, WritingSubscriptionProviders.ClaudeModel, WritingSubscriptionProviders.ClaudeModel, "gpt-6.1-sol", "gpt-6.1-sol" },
            gateway.Requests.Select(r => r.Model));
        Assert.IsType<HttpRequestException>(thrown.InnerException);
    }

    [Fact]
    public async Task EachAttempt_HasItsOwnSlot_SpacedClearOfTheReplayWalk()
    {
        var gateway = new RecordingGateway(_ => throw new HttpRequestException("down"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(gateway, epoch: 3));

        var versions = gateway.Requests.Select(r => r.ResourceVersion!.Value).ToList();
        Assert.Equal(
            new[]
            {
                WritingGradeChain.ResourceVersion(3, WritingGradeHop.ClaudeMax, 0),
                WritingGradeChain.ResourceVersion(3, WritingGradeHop.ClaudeMax, 1),
                WritingGradeChain.ResourceVersion(3, WritingGradeHop.ClaudeApi, 0),
                WritingGradeChain.ResourceVersion(3, WritingGradeHop.Codex, 0),
                WritingGradeChain.ResourceVersion(3, WritingGradeHop.Codex, 1),
            },
            versions);

        // Across runs too: every slot is unique, at least 16 apart (the coordinator's replay walk
        // bumps a version at most 10 times) and clear of the legacy versions (null, 2-10, 1001-1010, 2001-2010).
        var all = (from e in Enumerable.Range(0, 40)
                   from hop in Enum.GetValues<WritingGradeHop>()
                   from a in new[] { 0, 1 }
                   select WritingGradeChain.ResourceVersion(e, hop, a)).OrderBy(v => v).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.All(all.Zip(all.Skip(1)), pair => Assert.True(pair.Second - pair.First >= 16));
        Assert.All(all, v => Assert.True(v > 2010 + AiOperationReplayPolicy.MaxReplayRoundsCeiling));
    }

    [Fact]
    public async Task MaxFailsTwice_TheApiServes()
    {
        var gateway = new RecordingGateway(r => r.Provider == Max ? throw new AiProviderHttpException("Claude", 502, "Bad Gateway") : Ok(r));

        Assert.Equal("graded", await RunAsync(gateway));
        Assert.Equal(new[] { Max, Max, Api }, gateway.Requests.Select(r => r.Provider));
    }

    [Fact]
    public async Task TheApiRequest_IsTheMaxRequest_ExceptProviderModelAndSlot()
    {
        var gateway = new RecordingGateway(r => r.Provider == Max ? throw new HttpRequestException("down") : Ok(r));
        await RunAsync(gateway);

        var max = gateway.Requests[0];
        var api = gateway.Requests[2];
        Assert.Equal(Api, api.Provider);
        Assert.Equal(WritingSubscriptionProviders.ClaudeModel, api.Model);
        Assert.Equal(0.2, api.Temperature);
        Assert.Equal(16000, api.MaxTokens);
        Assert.Equal(AiFeatureCodes.WritingGrade, api.FeatureCode);
        Assert.Equal("writing.score.v1", api.PromptTemplateId);
        Assert.Equal(max with { Provider = api.Provider, Model = api.Model, ResourceVersion = api.ResourceVersion }, api);
        Assert.Null(api.ThinkingEffort); // the API hop runs at the API's default effort
    }

    [Theory]
    [InlineData(AiProviderErrorClass.QuotaExhausted, 429)]
    [InlineData(AiProviderErrorClass.Auth, 401)]
    [InlineData(AiProviderErrorClass.InvalidRequest, 400)]
    public async Task TypedQuotaAuthOrInvalid_SkipsOnlyTheSameRoutesSecondAttempt(AiProviderErrorClass errorClass, int status)
    {
        var gateway = new RecordingGateway(r => r.Provider == Max ? throw Typed(errorClass, status) : Ok(r));

        Assert.Equal("graded", await RunAsync(gateway));
        Assert.Equal(new[] { Max, Api }, gateway.Requests.Select(r => r.Provider));
    }

    [Fact]
    public async Task ApiCreditBalanceTooLow_GoesStraightToCodex()
    {
        // Live 1 Oct 2026: Anthropic answers "credit balance too low" as HTTP 400 invalid_request_error.
        var gateway = new RecordingGateway(r => r.Provider switch
        {
            Max => throw new HttpRequestException("down"),
            Api => throw Typed(AiProviderErrorClass.QuotaExhausted, 400),
            _ => Ok(r),
        });

        Assert.Equal("graded", await RunAsync(gateway));
        Assert.Equal(new[] { Max, Max, Api, Codex }, gateway.Requests.Select(r => r.Provider));
    }

    [Fact]
    public async Task ControlPlaneRefusals_AreNeverFailedOver()
    {
        var refusals = new Exception[]
        {
            new AiQuotaDeniedException("feature_disabled", "disabled"),
            new AiBudgetExhaustedException("global_budget_exhausted"),
            new AiFeaturePolicyRefusedException(AiFeatureCodes.WritingGrade, "disabled"),
            new PromptNotGroundedException("SystemPrompt is empty."),
            new MockAssessmentForbiddenException(AiFeatureCodes.WritingGrade),
        };

        foreach (var refusal in refusals)
        {
            var gateway = new RecordingGateway(_ => throw refusal);
            var thrown = await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(gateway));
            Assert.Same(refusal, thrown);
            Assert.Single(gateway.Requests);
        }
    }

    [Fact]
    public async Task DuplicateConflictAndInFlightSlots_AreFailedOver()
    {
        var slotFailures = new Exception[]
        {
            new AiOperationDuplicateResultUnavailableException("op-1", AiOperationState.Indeterminate, null),
            new AiOperationDuplicateResultUnavailableException("op-2", AiOperationState.Completed, "usage-1"),
            new AiOperationDuplicateResultUnavailableException("op-3", AiOperationState.Leased, null),
            new AiOperationConflictException("writing.grade:sub-1"),
            new AiOperationInFlightException("writing.grade:sub-1"),
        };

        foreach (var failure in slotFailures)
        {
            var gateway = new RecordingGateway(r => FirstSlotFails(r, failure));
            Assert.Equal("graded", await RunAsync(gateway));
            Assert.Equal(2, gateway.Requests.Count);
            Assert.Equal(new[] { Max, Max }, gateway.Requests.Select(r => r.Provider));
        }

        static AiGatewayResult FirstSlotFails(AiGatewayRequest request, Exception failure)
            => request.ResourceVersion == WritingGradeChain.ResourceVersion(1, WritingGradeHop.ClaudeMax, 0) ? throw failure : Ok(request);
    }

    [Fact]
    public async Task AnUnreadableAnswer_IsParsedInsideTheAttempt_AndFailsOver()
    {
        var calls = 0;
        var gateway = new RecordingGateway(r => ++calls == 1 ? new AiGatewayResult { Completion = "unreadable" } : Ok(r));

        Assert.Equal("graded", await RunAsync(gateway));
        Assert.Equal(new[] { Max, Max }, gateway.Requests.Select(r => r.Provider));
    }

    [Fact]
    public async Task AnAttemptThatOutlivesItsBudget_IsCancelled_AndTheNextAttemptRuns()
    {
        var gateway = new HangingMaxGateway();
        var options = new WritingGradeChainOptions { L1AttemptSeconds = 1 };

        Assert.Equal("graded", await RunAsync(gateway, options));
        Assert.Equal(new[] { Max, Max, Api }, gateway.Requests.Select(r => r.Provider));
        Assert.True(gateway.MaxCallsWereCancelled);
    }

    [Fact]
    public async Task TheRunDeadline_StopsBeforeAnAttemptWithUnderTwentySecondsLeft()
    {
        // Every failed attempt costs 7 minutes of a 20-minute run: Max, Max, API, then Codex has -1 min.
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var gateway = new RecordingGateway(_ =>
        {
            clock.Advance(TimeSpan.FromMinutes(7));
            throw new HttpRequestException("slow failure");
        });

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(gateway, clock: clock));

        Assert.Equal(new[] { Max, Max, Api }, gateway.Requests.Select(r => r.Provider));
        Assert.Contains("deadline", thrown.Message);
    }

    [Fact]
    public async Task CallerCancellation_IsNeverFailedOver()
    {
        var gateway = new HangingMaxGateway();
        using var cts = new CancellationTokenSource();

        var pending = RunAsync(gateway, ct: cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Single(gateway.Requests);
    }

    [Fact]
    public async Task AnInjectedFault_FailsTheHopBeforeAnyProviderCall()
    {
        var gateway = new RecordingGateway(Ok);

        var result = await RunAsync(gateway, fault: hop => hop != WritingGradeHop.Codex);

        Assert.Equal("graded", result);
        Assert.Equal(new[] { Codex }, gateway.Requests.Select(r => r.Provider));
    }

    [Fact]
    public async Task ATestHostRoute_HasNoApiHop()
    {
        var gateway = new RecordingGateway(_ => throw new HttpRequestException("down"));
        var stub = new WritingSubscriptionDecision("", "", "test", null, false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(gateway, decision: stub));

        Assert.Equal(new[] { "", "", Codex, Codex }, gateway.Requests.Select(r => r.Provider));
    }

    private sealed class RecordingGateway(Func<AiGatewayRequest, AiGatewayResult> respond) : IAiGatewayService
    {
        public List<AiGatewayRequest> Requests { get; } = new();

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context) => throw new NotSupportedException();

        public async Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            await Task.Yield();
            return respond(request);
        }
    }

    /// <summary>Max never answers (it waits on its token); every other route answers at once.</summary>
    private sealed class HangingMaxGateway : IAiGatewayService
    {
        public List<AiGatewayRequest> Requests { get; } = new();
        public bool MaxCallsWereCancelled { get; private set; } = true;

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context) => throw new NotSupportedException();

        public async Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            if (request.Provider == Max)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    MaxCallsWereCancelled &= ct.IsCancellationRequested;
                    throw;
                }
            }

            return Ok(request);
        }
    }
}
