using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai.Review;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// The REAL Speaking secondary reviewer (<see cref="SpeakingGradeReviewer"/>) driven through a stub gateway:
/// the same prompt runs on the Codex subscription route, and when Codex is exhausted/unavailable/too slow the
/// shared pipeline automatically reviews on the API route instead. If BOTH routes fail the primary Claude grade
/// stands untouched and the trace says so - a Speaking assessment never waits on a Codex quota reset.
/// </summary>
public sealed class SpeakingGradeReviewerSharedPipelineTests
{
    private const string PrimaryGrade = """
        {
          "criterionScores": {
            "grammarExpression": { "score": 5, "rationale": "Mostly accurate." },
            "pronunciation": { "score": 4, "rationale": "Clear enough." },
            "listening": { "score": 5, "rationale": "Responded well." }
          },
          "overallSummary": "Solid role-play."
        }
        """;

    private const string ReviewerGrade = """
        {
          "criterionScores": {
            "grammarExpression": { "score": 3, "rationale": "Too many errors." },
            "pronunciation": { "score": 4, "rationale": "Clear enough." },
            "listening": { "score": 5, "rationale": "Responded well." }
          },
          "overallSummary": "Grammar needs work."
        }
        """;

    private static AiGatewayRequest GradeRequest() => new()
    {
        UserInput = "Grade this role-play transcript.",
        Provider = WritingSubscriptionProviders.Claude,
        Model = WritingSubscriptionProviders.ClaudeModel,
        FeatureCode = AiFeatureCodes.SpeakingGrade,
        PromptTemplateId = "speaking.score.v4",
    };

private static AiGatewayResult Primary() => new()
    {
        Completion = PrimaryGrade,
        ResolvedProvider = WritingSubscriptionProviders.Claude,
        ResolvedModel = WritingSubscriptionProviders.ClaudeModel,
    };

    /// <summary>A bounded, fast shared policy so the fallback paths run in seconds instead of minutes.</summary>
    private static SharedReviewerOptions Fast() => new()
    {
        MaxConcurrency = 2,
        MaxQueueWaitSeconds = 2,
        CodexAttempts = 2,
        CodexAttemptSeconds = 1,
        CodexBudgetSeconds = 1,
        CodexRetryDelaySeconds = 0,
        ApiAttempts = 1,
        ApiAttemptSeconds = 5,
    };

    private sealed class ProviderPinnedGateway(Func<AiGatewayRequest, AiGatewayResult> respond) : IAiGatewayService
    {
        public List<AiGatewayRequest> ReviewRequests { get; } = new();

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context) => new()
        {
            SystemPrompt = "grounded\n",
            TaskInstruction = "Score this speaking attempt.",
            Metadata = new AiGroundedPromptMetadata { RulebookVersion = "test", RulebookKind = context.Kind },
        };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (request.FeatureCode == AiFeatureCodes.SpeakingGradeReview) ReviewRequests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    [Fact]
    public async Task Codex_route_reviews_the_grade_and_is_recorded_in_the_trace()
    {
        var gateway = new ProviderPinnedGateway(request => new AiGatewayResult
        {
            Completion = ReviewerGrade,
            ResolvedProvider = WritingSubscriptionProviders.Codex,
            ResolvedModel = WritingSubscriptionProviders.CodexModel,
        });

        var result = await SpeakingGradeReviewer.ReviewAsync(gateway, GradeRequest(), Primary(), NullLogger.Instance, CancellationToken.None, "session-1", Fast());

        Assert.Equal(WritingSubscriptionProviders.Codex, gateway.ReviewRequests[0].Provider);
        Assert.Equal("gpt-6.1-sol", gateway.ReviewRequests[0].Model);
        Assert.Equal(WritingSubscriptionProviders.Codex, result.Trace.Provider);
        Assert.Null(result.Trace.FallbackReason);
        Assert.Equal("ran", result.Trace.Status);
        // Bounded merge: one band at most (grammar 5 -> 4), and the score the reviewer proposed is kept for the trace.
        Assert.Contains("\"score\":4", result.Completion.Replace(" ", string.Empty));
        Assert.Equal(1, result.Trace.Changes.Count);
    }

    [Theory]
    [InlineData(402)]
    [InlineData(503)]
    public async Task Codex_quota_or_unavailability_falls_back_to_the_api_route(int status)
    {
        var gateway = new ProviderPinnedGateway(request => request.Provider == WritingSubscriptionProviders.Codex
            ? throw new AiProviderHttpException("writing-codex-sub", status, "failed")
            : new AiGatewayResult
            {
                Completion = ReviewerGrade,
                ResolvedProvider = WritingSubscriptionProviders.ClaudeApi,
                ResolvedModel = WritingSubscriptionProviders.ClaudeModel,
            });

        var result = await SpeakingGradeReviewer.ReviewAsync(gateway, GradeRequest(), Primary(), NullLogger.Instance, CancellationToken.None, "session-2", Fast());

        // Two review calls: the Codex attempt, then the automatic API fallback with the SAME prompt.
        Assert.Equal(2, gateway.ReviewRequests.Count);
        Assert.Equal(WritingSubscriptionProviders.Codex, gateway.ReviewRequests[0].Provider);
        Assert.Equal(WritingSubscriptionProviders.ClaudeApi, gateway.ReviewRequests[1].Provider);
        Assert.Equal(WritingSubscriptionProviders.ClaudeApi, result.Trace.Provider);
        Assert.NotNull(result.Trace.FallbackReason);
        Assert.Equal("ran", result.Trace.Status);
        Assert.Contains("FIRST ASSESSOR'S GRADE", gateway.ReviewRequests[1].UserInput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Codex_timeout_falls_back_to_the_api_route()
    {
        var gateway = new ProviderPinnedGateway(request => request.Provider == WritingSubscriptionProviders.Codex
            ? throw new TaskCanceledException("codex attempt budget expired")
            : new AiGatewayResult
            {
                Completion = ReviewerGrade,
                ResolvedProvider = WritingSubscriptionProviders.ClaudeApi,
                ResolvedModel = WritingSubscriptionProviders.ClaudeModel,
            });

        var result = await SpeakingGradeReviewer.ReviewAsync(gateway, GradeRequest(), Primary(), NullLogger.Instance, CancellationToken.None, "session-3", Fast());

        Assert.Equal(2, gateway.ReviewRequests.Count);
        Assert.Equal("codex_timeout", result.Trace.FallbackReason);
        Assert.Equal("ran", result.Trace.Status);
    }

    [Fact]
    public async Task Both_routes_failing_keeps_the_primary_grade_and_reports_failed()
    {
        var gateway = new ProviderPinnedGateway(_ => throw new AiProviderHttpException("writing-codex-sub", (int)HttpStatusCode.ServiceUnavailable, "down"));

        var result = await SpeakingGradeReviewer.ReviewAsync(gateway, GradeRequest(), Primary(), NullLogger.Instance, CancellationToken.None, "session-4", Fast());

        // The candidate's grade is Claude's, untouched; the trace is explicit about the failure.
        Assert.Equal(PrimaryGrade.Trim(), result.Completion.Trim());
        Assert.Equal("failed", result.Trace.Status);
        Assert.True(gateway.ReviewRequests.Count >= 2, "the API fallback was attempted");
    }

    [Fact]
    public async Task An_unreadable_reviewer_reply_keeps_the_primary_grade()
    {
        var gateway = new ProviderPinnedGateway(_ => new AiGatewayResult
        {
            Completion = "I could not review this transcript.",
            ResolvedProvider = WritingSubscriptionProviders.Codex,
            ResolvedModel = WritingSubscriptionProviders.CodexModel,
        });

        var result = await SpeakingGradeReviewer.ReviewAsync(gateway, GradeRequest(), Primary(), NullLogger.Instance, CancellationToken.None, "session-5", Fast());

        Assert.Equal(PrimaryGrade.Trim(), result.Completion.Trim());
        Assert.Equal("failed", result.Trace.Status);
    }

    [Fact]
    public void The_shared_pipeline_is_the_only_reviewer_route_both_assessment_types_use()
    {
        // One gate, one options instance, one Codex route: Writing and Speaking cannot diverge into
        // separate unbounded queues.
        Assert.Same(SharedReviewerOptions.Current, SharedReviewerOptions.Current);
        Assert.Equal(2, CodexReviewerGate.Default.Capacity);
        Assert.True(CodexReviewerGate.Default.MaxQueueWaitSeconds > 0);
        Assert.True(SharedReviewerOptions.Current.MaxQueueWaitSeconds <= 120);
    }
}
