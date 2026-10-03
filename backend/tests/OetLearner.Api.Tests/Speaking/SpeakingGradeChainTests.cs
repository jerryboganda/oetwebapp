using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// Owner directive 2026-09-30: Speaking grading tries the pinned Claude subscription
/// sidecar first and falls back to the ORIGINAL unpinned request (today's default route).
/// The pin can never be blank (RULE MAX-ALWAYS-ON); only a null options object (a test
/// construction of the assessors without DI) is one unpinned call.
/// </summary>
public sealed class SpeakingGradeChainTests
{
    private const string PinnedProvider = "writing-claude-sub";
    private const string PinnedModel = "claude-opus-5-5";

    private static SpeakingGradingOptions Pinned()
        => new() { PinnedProviderCode = PinnedProvider, PinnedModel = PinnedModel };

    private static AiGatewayRequest Template() => new()
    {
        Prompt = new AiGroundedPrompt
        {
            SystemPrompt = "OET AI — Rulebook-Grounded System Prompt\n(test fixture)\n",
            TaskInstruction = "Score this speaking attempt.",
        },
        UserInput = "transcript",
        Model = string.Empty,
        Temperature = 0.1,
        MaxTokens = 4096,
        FeatureCode = AiFeatureCodes.SpeakingGrade,
        UserId = "chain-learner",
        PromptTemplateId = "speaking.score.v2",
        FreeSampleGrant = true,
        AssessmentContext = AiAssessmentContext.Practice,
    };

    private static AiGatewayResult Ok(AiGatewayRequest request) => new()
    {
        Completion = "{}",
        ResolvedProvider = request.Provider,
        ResolvedModel = request.Model,
    };

    private static AiProviderHttpException QuotaExhausted() => new(
        "Anthropic",
        400,
        "Bad Request",
        null,
        new AiProviderError(
            AiProviderErrorClass.QuotaExhausted, 400, "invalid_request_error", null,
            "Your credit balance is too low to access the Anthropic API.", "req_chain", null));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankPinnedProvider_StillStartsOnMax(string pinnedProvider)
    {
        // RULE MAX-ALWAYS-ON: a blank pin is NOT "off" any more; it resolves to the Max route.
        var gateway = new RecordingGateway(Ok);
        var options = new SpeakingGradingOptions { PinnedProviderCode = pinnedProvider, PinnedModel = PinnedModel };

        var result = await SpeakingGradeChain.CompleteAsync(gateway, Template(), options, NullLogger.Instance, default);

        Assert.Equal(PinnedProvider, Assert.Single(gateway.Requests).Provider);
        Assert.Equal(PinnedProvider, result.ResolvedProvider);
    }

    [Fact]
    public async Task NullOptions_MakesOneUnpinnedCall()
    {
        var gateway = new RecordingGateway(Ok);
        var template = Template();

        await SpeakingGradeChain.CompleteAsync(gateway, template, null, NullLogger.Instance, default);

        Assert.Same(template, Assert.Single(gateway.Requests));
    }

    [Fact]
    public async Task PinnedProviderSucceeds_MakesExactlyOnePinnedCall()
    {
        var gateway = new RecordingGateway(Ok);
        var template = Template();

        var result = await SpeakingGradeChain.CompleteAsync(gateway, template, Pinned(), NullLogger.Instance, default);

        var call = Assert.Single(gateway.Requests);
        Assert.Equal(PinnedProvider, call.Provider);
        Assert.Equal(PinnedModel, call.Model);
        Assert.Equal(PinnedProvider, result.ResolvedProvider);
        // Everything else is the caller's request, untouched.
        Assert.Same(template.Prompt, call.Prompt);
        Assert.Equal(template.UserInput, call.UserInput);
        Assert.Equal(AiFeatureCodes.SpeakingGrade, call.FeatureCode);
        Assert.Equal(template.UserId, call.UserId);
        Assert.Equal(template.PromptTemplateId, call.PromptTemplateId);
        Assert.Equal(template.MaxTokens, call.MaxTokens);
        Assert.Equal(template.Temperature, call.Temperature);
        Assert.True(call.FreeSampleGrant);
        Assert.Equal(AiAssessmentContext.Practice, call.AssessmentContext);
        // The template itself is never mutated (it is the level 2 request).
        Assert.Equal(string.Empty, template.Provider);
        Assert.Equal(string.Empty, template.Model);
    }

    [Fact]
    public async Task PinnedProviderAndModel_AreTrimmed_AndAnEmptyModelMeansTheRowDefault()
    {
        var gateway = new RecordingGateway(Ok);
        var options = new SpeakingGradingOptions { PinnedProviderCode = "  writing-claude-sub  ", PinnedModel = "   " };

        await SpeakingGradeChain.CompleteAsync(gateway, Template(), options, NullLogger.Instance, default);

        var call = Assert.Single(gateway.Requests);
        Assert.Equal(PinnedProvider, call.Provider);
        Assert.Equal(string.Empty, call.Model);
    }

    [Fact]
    public async Task PinnedProviderFails_FallsBackToTheOriginalUnpinnedRequest()
    {
        var gateway = new RecordingGateway(request =>
        {
            if (!string.IsNullOrEmpty(request.Provider)) throw QuotaExhausted();
            return Ok(request);
        });
        var template = Template();
        var logger = new CapturingLogger();

        var result = await SpeakingGradeChain.CompleteAsync(gateway, template, Pinned(), logger, default);

        Assert.Equal(2, gateway.Requests.Count);
        Assert.Equal(PinnedProvider, gateway.Requests[0].Provider);
        Assert.Same(template, gateway.Requests[1]);
        Assert.Equal(string.Empty, result.ResolvedProvider);

        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains(PinnedProvider, warning.Message);
        Assert.Contains("quota_exhausted", warning.Message);
        // No provider text in this line: the gateway logs the redacted text itself.
        Assert.DoesNotContain("credit balance", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("req_chain", warning.Message);
    }

    [Fact]
    public async Task ProviderSideFailures_AreFailedOver()
    {
        var failures = new Exception[]
        {
            new AiProviderHttpException("Anthropic", 502, "Bad Gateway"),
            new HttpRequestException("connection refused"),
            new InvalidOperationException("HTTP 503 Provider circuit is open."),
            new InvalidOperationException("AI provider 'writing-claude-sub' is not configured."),
            new TaskCanceledException("timed out", new TimeoutException()),
        };

        foreach (var failure in failures)
        {
            var gateway = new RecordingGateway(request =>
            {
                if (!string.IsNullOrEmpty(request.Provider)) throw failure;
                return Ok(request);
            });

            await SpeakingGradeChain.CompleteAsync(gateway, Template(), Pinned(), NullLogger.Instance, default);

            Assert.Equal(2, gateway.Requests.Count);
            Assert.Equal(string.Empty, gateway.Requests[1].Provider);
        }
    }

    [Fact]
    public async Task ControlPlaneRefusals_AreNotFailedOver()
    {
        var refusals = new Exception[]
        {
            new AiQuotaDeniedException("quota_denied", "AI quota exceeded."),
            new AiBudgetExhaustedException("global_budget_exhausted"),
            new AiOperationDuplicateResultUnavailableException("op-1", AiOperationState.Leased, null),
            // A recent Completed twin means the work already happened: failing over would double-spend.
            new AiOperationDuplicateResultUnavailableException("op-2", AiOperationState.Completed, "usage-1"),
            new AiOperationConflictException("speaking.assess:s-1"),
            new AiOperationInFlightException("speaking.assess:s-1"),
            new PromptNotGroundedException("SystemPrompt is empty."),
            new AiFeaturePolicyRefusedException(AiFeatureCodes.SpeakingGrade, "disabled"),
            new MockAssessmentForbiddenException(AiFeatureCodes.SpeakingGrade),
        };

        foreach (var refusal in refusals)
        {
            var gateway = new RecordingGateway(_ => throw refusal);

            var thrown = await Assert.ThrowsAnyAsync<Exception>(() =>
                SpeakingGradeChain.CompleteAsync(gateway, Template(), Pinned(), NullLogger.Instance, default));

            Assert.Same(refusal, thrown);
            Assert.Single(gateway.Requests);
        }
    }

    [Fact]
    public async Task IndeterminatePinnedDuplicate_FallsBackToTheOriginalUnpinnedRequest()
    {
        // A dropped sidecar connection leaves the pinned operation Indeterminate for good and the
        // coordinator then refuses every identical pinned request as a duplicate. Level 2 is a
        // different provider and a different operation, so it must still run.
        var stuck = new AiOperationDuplicateResultUnavailableException("op-1", AiOperationState.Indeterminate, null);
        var gateway = new RecordingGateway(request =>
        {
            if (!string.IsNullOrEmpty(request.Provider)) throw stuck;
            return Ok(request);
        });
        var template = Template();
        var logger = new CapturingLogger();

        var result = await SpeakingGradeChain.CompleteAsync(gateway, template, Pinned(), logger, default);

        Assert.Equal(2, gateway.Requests.Count);
        Assert.Equal(PinnedProvider, gateway.Requests[0].Provider);
        Assert.Same(template, gateway.Requests[1]);
        Assert.Equal(string.Empty, result.ResolvedProvider);
        Assert.Contains("operation_indeterminate", Assert.Single(logger.Entries).Message);
    }

    [Fact]
    public async Task DuplicateRefusalAtTheDefaultLevel_IsRethrown()
    {
        // The default level is the last leg: an Indeterminate twin there has nowhere left to fail over to.
        var stuck = new AiOperationDuplicateResultUnavailableException("op-3", AiOperationState.Indeterminate, null);
        var gateway = new RecordingGateway(request =>
        {
            if (!string.IsNullOrEmpty(request.Provider)) throw new HttpRequestException("connection refused");
            throw stuck;
        });

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() =>
            SpeakingGradeChain.CompleteAsync(gateway, Template(), Pinned(), NullLogger.Instance, default));

        Assert.Same(stuck, thrown);
        Assert.Equal(2, gateway.Requests.Count);
    }

    [Fact]
    public async Task PinnedAttemptThatOutlivesItsBudget_IsCancelled_AndFailsOverToTheDefaultRoute()
    {
        // A sidecar lane that never drains: only the chain's own budget can end the pinned call.
        var gateway = new HangingPinnedGateway();
        var options = new SpeakingGradingOptions
        {
            PinnedProviderCode = PinnedProvider,
            PinnedModel = PinnedModel,
            PinnedTimeoutSeconds = 1,
        };
        var template = Template();
        var logger = new CapturingLogger();

        var result = await SpeakingGradeChain.CompleteAsync(gateway, template, options, logger, default);

        Assert.Equal(2, gateway.Requests.Count);
        Assert.Equal(PinnedProvider, gateway.Requests[0].Provider);
        Assert.Same(template, gateway.Requests[1]);
        Assert.Equal(string.Empty, result.ResolvedProvider);
        Assert.True(gateway.PinnedCallWasCancelled);
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("timeout", warning.Message);
    }

    [Fact]
    public async Task CallerCancellationDuringThePinnedCall_IsNotFailedOver()
    {
        // The pinned call is still waiting when the caller walks away: that is a decision, not a
        // provider failure, so it must propagate instead of starting the default route.
        var gateway = new HangingPinnedGateway();
        using var cts = new CancellationTokenSource();

        var pending = SpeakingGradeChain.CompleteAsync(gateway, Template(), Pinned(), NullLogger.Instance, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Single(gateway.Requests);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-30, null)]
    [InlineData(1, 1)]
    [InlineData(900, 900)]
    [InlineData(1500, 1500)]
    [InlineData(99999, 1500)]
    public void PinnedBudget_ZeroOrLessMeansNoCap_AndTheRestIsClampedToTheCeiling(int configured, int? expectedSeconds)
    {
        var budget = SpeakingGradeChain.PinnedBudget(new SpeakingGradingOptions { PinnedTimeoutSeconds = configured });

        Assert.Equal(expectedSeconds.HasValue, budget.HasValue);
        if (expectedSeconds is { } seconds)
            Assert.Equal(TimeSpan.FromSeconds(seconds), budget!.Value);
    }

    [Fact]
    public void PinnedBudget_DefaultsToFifteenMinutes()
    {
        var budget = SpeakingGradeChain.PinnedBudget(new SpeakingGradingOptions());

        Assert.Equal(TimeSpan.FromMinutes(15), budget!.Value);
    }

    [Fact]
    public async Task CallerCancellation_IsNotFailedOver()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var gateway = new RecordingGateway(_ => throw new OperationCanceledException(cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SpeakingGradeChain.CompleteAsync(gateway, Template(), Pinned(), NullLogger.Instance, cts.Token));

        Assert.Single(gateway.Requests);
    }

    [Fact]
    public async Task BothLevelsFail_RethrowsTheLastException()
    {
        var first = new AiProviderHttpException("Anthropic", 502, "Bad Gateway");
        var last = new InvalidOperationException("HTTP 503 Provider circuit is open.");
        var gateway = new RecordingGateway(request => throw (string.IsNullOrEmpty(request.Provider) ? last : first));

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() =>
            SpeakingGradeChain.CompleteAsync(gateway, Template(), Pinned(), NullLogger.Instance, default));

        Assert.Same(last, thrown);
        Assert.Equal(2, gateway.Requests.Count);
    }

    private sealed class RecordingGateway(Func<AiGatewayRequest, AiGatewayResult> respond) : IAiGatewayService
    {
        public List<AiGatewayRequest> Requests { get; } = new();

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => throw new NotSupportedException();

        public async Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            await Task.Yield();
            return respond(request);
        }
    }

    /// <summary>The pinned call never answers (it waits on its token); the default route answers at once.</summary>
    private sealed class HangingPinnedGateway : IAiGatewayService
    {
        public List<AiGatewayRequest> Requests { get; } = new();

        /// <summary>True once the pinned call saw its token cancelled (by the chain's budget or the caller).</summary>
        public bool PinnedCallWasCancelled { get; private set; }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => throw new NotSupportedException();

        public async Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            if (!string.IsNullOrEmpty(request.Provider))
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    PinnedCallWasCancelled = ct.IsCancellationRequested;
                    throw;
                }
            }

            return new AiGatewayResult
            {
                Completion = "{}",
                ResolvedProvider = request.Provider,
                ResolvedModel = request.Model,
            };
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
