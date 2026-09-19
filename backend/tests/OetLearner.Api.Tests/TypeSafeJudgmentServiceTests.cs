using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Ai.TypeSafe;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

/// <summary>
/// Contract tests for the TypeSafe SystemOne (Jev) judgment surface:
/// wire-payload shaping, response parsing, transient-only retries, and the
/// governed service's fail-soft contract (disabled → no-op; any provider or
/// lease failure → Unavailable, never a throw to the caller).
/// </summary>
public sealed class TypeSafeJudgmentServiceTests
{
    private static TypeSafeOptions EnabledOptions() => new()
    {
        Enabled = true,
        ApiKey = "apikey_test",
        BaseUrl = "https://api.typesafe.test",
        TimeoutSeconds = 5,
        MaxRetries = 2,
    };

    private static TypeSafeOptions DisabledOptions() => new()
    {
        Enabled = false,
        ApiKey = "apikey_test",
    };

    private static JevJudgmentRequest SampleRequest() => new()
    {
        StateText = "sample letter text",
        Questions =
        [
            new JevQuestion
            {
                Id = "letter_type",
                Kind = JevQuestionKind.Choice,
                Instructions = "Which letter type is this?",
                ChoiceCriteria = new Dictionary<string, string?>
                {
                    ["urgent_referral"] = "Time-critical",
                    ["routine_referral"] = "Not time-critical",
                },
            },
            new JevQuestion
            {
                Id = "is_urgent",
                Kind = JevQuestionKind.Noul,
                Instructions = "Is this urgent?",
                NoulCriteria = new Dictionary<string, string?>
                {
                    ["true"] = "Same-day action",
                    ["false"] = "No urgency",
                },
            },
        ],
    };

    // ── TypeSafeRequestBuilder ──────────────────────────────────────────────

    [Fact]
    public void BuildPayload_EmitsWireShape()
    {
        var payload = TypeSafeRequestBuilder.BuildPayload(SampleRequest(), "jev-1.13.0");
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        Assert.Equal("sample letter text", root.GetProperty("state").GetString());
        Assert.Equal("jev-1.13.0", root.GetProperty("model").GetString());

        var questions = root.GetProperty("questions");
        Assert.Equal("choice", questions.GetProperty("letter_type").GetProperty("type").GetString());
        Assert.True(questions.GetProperty("letter_type").GetProperty("criteria").TryGetProperty("urgent_referral", out _));
        Assert.Equal("noul", questions.GetProperty("is_urgent").GetProperty("type").GetString());
        Assert.True(questions.GetProperty("is_urgent").GetProperty("criteria").TryGetProperty("true", out _));
    }

    [Fact]
    public void BuildPayload_AcceptsStructuredState()
    {
        var state = JsonDocument.Parse("{\"letter\":\"text\",\"task\":\"grading\"}").RootElement.Clone();
        var payload = TypeSafeRequestBuilder.BuildPayload(new JevJudgmentRequest
        {
            StateJson = state,
            Questions = SampleRequest().Questions,
        }, "jev-1.13.0");

        using var doc = JsonDocument.Parse(payload);
        Assert.Equal("text", doc.RootElement.GetProperty("state").GetProperty("letter").GetString());
    }

    [Theory]
    [InlineData("both_states")]
    [InlineData("no_state")]
    public void BuildPayload_RejectsAmbiguousState(string variant)
    {
        var withBoth = new JevJudgmentRequest
        {
            StateText = "x",
            StateJson = JsonDocument.Parse("\"y\"").RootElement.Clone(),
            Questions = SampleRequest().Questions,
        };
        var withNone = new JevJudgmentRequest { Questions = SampleRequest().Questions };

        Assert.Throws<InvalidOperationException>(() => TypeSafeRequestBuilder.BuildPayload(
            variant == "both_states" ? withBoth : withNone, "jev-1.13.0"));
    }

    [Fact]
    public void BuildPayload_RejectsScoreWithFewerThanTwoLevels()
    {
        var request = new JevJudgmentRequest
        {
            StateText = "x",
            Questions = [new JevQuestion
            {
                Id = "one_level",
                Kind = JevQuestionKind.Score,
                Instructions = "Rate it.",
                ScoreLevels = ["only one"],
            }],
        };

        Assert.Throws<InvalidOperationException>(() => TypeSafeRequestBuilder.BuildPayload(request, "jev-1.13.0"));
    }

    [Fact]
    public void BuildPayload_RejectsNoulWithoutTrueFalseCriteria()
    {
        var request = new JevJudgmentRequest
        {
            StateText = "x",
            Questions = [new JevQuestion
            {
                Id = "bad_noul",
                Kind = JevQuestionKind.Noul,
                Instructions = "Yes or no?",
                NoulCriteria = new Dictionary<string, string?> { ["yes"] = "y", ["no"] = "n" },
            }],
        };

        Assert.Throws<InvalidOperationException>(() => TypeSafeRequestBuilder.BuildPayload(request, "jev-1.13.0"));
    }

    // ── Parsing ─────────────────────────────────────────────────────────────

    [Fact]
    public void ParseResponse_BindsAllThreeAnswerKinds()
    {
        var body = """
            {"model":"jev-1.13.0","answers":{
              "is_urgent":{"type":"noul","noul":0.92},
              "department":{"type":"choice","choice":"technical","probabilities":{"billing":0.08,"technical":0.85,"sales":0.07},"confidence":0.82},
              "frustration":{"type":"score","score":1.6,"probabilities":{"0":0.05,"1":0.3,"2":0.65},"confidence":0.78}},
             "usage":{"input_tokens":312,"output_tokens":48}}
            """;

        var response = TypeSafeJudgmentClient.ParseResponse(body);

        Assert.Equal("jev-1.13.0", response.Model);
        Assert.Equal(312, response.InputTokens);
        Assert.Equal(48, response.OutputTokens);
        Assert.Equal(0.92, response.Answers["is_urgent"].Noul!.Probability);
        Assert.Equal("technical", response.Answers["department"].Choice!.Choice);
        Assert.Equal(0.82, response.Answers["department"].Choice!.Confidence);
        Assert.Equal(0.85, response.Answers["department"].Choice!.Probabilities["technical"]);
        Assert.Equal(1.6, response.Answers["frustration"].Score!.Score);
        Assert.Equal(0.78, response.Answers["frustration"].Score!.Confidence);
    }

    [Fact]
    public void ParseResponse_UnknownAnswerTypeThrows()
    {
        var body = """{"model":"m","answers":{"q":{"type":"essay","text":"hi"}},"usage":{"input_tokens":1,"output_tokens":1}}""";

        Assert.Throws<TypeSafeHttpException>(() => TypeSafeJudgmentClient.ParseResponse(body));
    }

    // ── Transport: transient-only retries ───────────────────────────────────

    [Fact]
    public async Task Client_Retries429AndSucceeds()
    {
        var calls = 0;
        var handler = new StubHandler((_, _) =>
        {
            calls++;
            if (calls == 1)
            {
                var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(1));
                return Task.FromResult(throttled);
            }

            return Task.FromResult(JsonResponse(SuccessBody()));
        });
        var client = new TypeSafeJudgmentClient(
            new SingleClientFactory(new HttpClient(handler)), Options.Create(EnabledOptions()));

        var response = await client.SendAsync(TypeSafeRequestBuilder.BuildPayload(SampleRequest(), "jev-1.13.0"), CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Equal("jev-1.13.0", response.Model);
    }

    [Fact]
    public async Task Client_DoesNotRetryUnauthorized()
    {
        var calls = 0;
        var handler = new StubHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        });
        var client = new TypeSafeJudgmentClient(
            new SingleClientFactory(new HttpClient(handler)), Options.Create(EnabledOptions()));

        await Assert.ThrowsAsync<TypeSafeHttpException>(() => client.SendAsync(
            TypeSafeRequestBuilder.BuildPayload(SampleRequest(), "jev-1.13.0"), CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Client_Retries529UntilExhaustedThenThrows()
    {
        var calls = 0;
        var handler = new StubHandler((_, _) =>
        {
            calls++;
            var throttled = new HttpResponseMessage((HttpStatusCode)529);
            throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(1));
            return Task.FromResult(throttled);
        });
        var client = new TypeSafeJudgmentClient(
            new SingleClientFactory(new HttpClient(handler)), Options.Create(EnabledOptions()));

        await Assert.ThrowsAsync<TypeSafeHttpException>(() => client.SendAsync(
            TypeSafeRequestBuilder.BuildPayload(SampleRequest(), "jev-1.13.0"), CancellationToken.None));
        Assert.Equal(3, calls); // initial + MaxRetries(2)
    }

    // ── Governed service ────────────────────────────────────────────────────

    [Fact]
    public async Task Service_DisabledWhenSwitchOff_NoLeaseNoTransport()
    {
        var recorder = new FakeRecorder();
        var service = new TypeSafeJudgmentService(
            new FailingClient(),
            recorder,
            Options.Create(DisabledOptions()),
            TimeProvider.System,
            NullLogger<TypeSafeJudgmentService>.Instance);

        var result = await service.AskAsync(SampleRequest(), Call(), CancellationToken.None);

        Assert.Equal(JevCallStatus.Disabled, result.Status);
        Assert.Equal(0, recorder.BeginCalls);
        Assert.Equal(0, recorder.SuccessCalls);
    }

    [Fact]
    public async Task Service_DisabledWhenKeyMissing_NoLeaseNoTransport()
    {
        var recorder = new FakeRecorder();
        var service = new TypeSafeJudgmentService(
            new FailingClient(),
            recorder,
            Options.Create(new TypeSafeOptions { Enabled = true, ApiKey = "" }),
            TimeProvider.System,
            NullLogger<TypeSafeJudgmentService>.Instance);

        var result = await service.AskAsync(SampleRequest(), Call(), CancellationToken.None);

        Assert.Equal(JevCallStatus.Disabled, result.Status);
        Assert.Contains("key", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, recorder.BeginCalls);
    }

    [Fact]
    public async Task Service_UnavailableWhenPolicyRefused_NoTransport()
    {
        var recorder = new FakeRecorder(lease: DirectAiOperationLease.Blocked(
            DirectAiOperationDisposition.PolicyRefused, "policy_disabled"));
        var service = new TypeSafeJudgmentService(
            new FailingClient(),
            recorder,
            Options.Create(EnabledOptions()),
            TimeProvider.System,
            NullLogger<TypeSafeJudgmentService>.Instance);

        var result = await service.AskAsync(SampleRequest(), Call(), CancellationToken.None);

        Assert.Equal(JevCallStatus.Unavailable, result.Status);
        Assert.Contains("jev_lease_", result.Reason);
        Assert.Equal(0, recorder.SuccessCalls);
        Assert.Equal(0, recorder.FailureCalls);
    }

    [Fact]
    public async Task Service_FailSoftWhenProviderFails_RecordsFailure()
    {
        var recorder = new FakeRecorder();
        var service = new TypeSafeJudgmentService(
            new TypeSafeJudgmentClient(
                new SingleClientFactory(new HttpClient(new StubHandler((_, _) =>
                    Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))))),
                Options.Create(EnabledOptions())),
            recorder,
            Options.Create(EnabledOptions()),
            TimeProvider.System,
            NullLogger<TypeSafeJudgmentService>.Instance);

        var result = await service.AskAsync(SampleRequest(), Call(), CancellationToken.None);

        Assert.Equal(JevCallStatus.Unavailable, result.Status);
        Assert.Equal(1, recorder.BeginCalls);
        Assert.Equal(1, recorder.FailureCalls);
        Assert.Equal(0, recorder.SuccessCalls);
    }

    [Fact]
    public async Task Service_OkPath_RecordsSuccessAndCompletesOperation()
    {
        var recorder = new FakeRecorder();
        var handler = new StubHandler((_, _) => Task.FromResult(JsonResponse(SuccessBody())));
        var service = new TypeSafeJudgmentService(
            new TypeSafeJudgmentClient(
                new SingleClientFactory(new HttpClient(handler)),
                Options.Create(EnabledOptions())),
            recorder,
            Options.Create(EnabledOptions()),
            TimeProvider.System,
            NullLogger<TypeSafeJudgmentService>.Instance);

        var result = await service.AskAsync(SampleRequest(), Call(), CancellationToken.None);

        Assert.Equal(JevCallStatus.Ok, result.Status);
        Assert.Equal("urgent_referral", result["letter_type"].Choice!.Choice);
        Assert.Equal(500, result.InputTokens);
        Assert.Equal(1, recorder.SuccessCalls);
        Assert.Equal(1, recorder.CompleteCalls);
        Assert.Equal(AiOperationState.Completed, recorder.LastCompletedState);
        Assert.True(recorder.LastCostEstimateUsd > 0m);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static JevCallMetadata Call() => new()
    {
        FeatureCode = AiFeatureCodes.JevWritingGuard,
        UserId = "user-1",
    };

    private static string SuccessBody() => """
        {"model":"jev-1.13.0","answers":{
          "letter_type":{"type":"choice","choice":"urgent_referral","probabilities":{"urgent_referral":1,"routine_referral":0},"confidence":1},
          "is_urgent":{"type":"noul","noul":0.99}},
         "usage":{"input_tokens":500,"output_tokens":85}}
        """;

    private static HttpResponseMessage JsonResponse(string body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        return response;
    }

    private sealed class FailingClient : ITypeSafeJudgmentClient
    {
        public Task<TypeSafeRawResponse> SendAsync(string payloadJson, CancellationToken ct)
            => throw new InvalidOperationException("This test must not reach the transport.");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request, cancellationToken);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FakeRecorder(DirectAiOperationLease? lease = null) : IDirectAiCallRecorder
    {
        public int BeginCalls { get; private set; }
        public int SuccessCalls { get; private set; }
        public int FailureCalls { get; private set; }
        public int CompleteCalls { get; private set; }
        public AiOperationState? LastCompletedState { get; private set; }
        public decimal LastCostEstimateUsd { get; private set; }

        public Task<DirectAiOperationLease> BeginOperationAsync(DirectAiOperationRequest request, CancellationToken ct)
        {
            BeginCalls++;
            return Task.FromResult(lease ?? DirectAiOperationLease.Granted("op-1", 1));
        }

        public Task<string?> RecordSuccessAsync(
            AiUsageContext context, string providerId, string model, AiUsage? usage,
            int latencyMs, string? policyTrace, decimal costEstimateUsd, CancellationToken ct,
            AiCacheTokenBreakdown? cacheTokens = null, string? operationId = null, int? attemptNumber = null)
        {
            SuccessCalls++;
            LastCostEstimateUsd = costEstimateUsd;
            return Task.FromResult<string?>("usage-1");
        }

        public Task<string?> RecordFailureAsync(
            AiUsageContext context, string? providerId, string? model, AiCallOutcome outcome,
            string errorCode, string? errorMessage, int latencyMs, string? policyTrace, CancellationToken ct,
            string? operationId = null, int? attemptNumber = null)
        {
            FailureCalls++;
            return Task.FromResult<string?>("usage-fail");
        }

        public Task CompleteOperationAsync(
            string operationId, AiOperationState state, string? resultRef, string? providerId,
            string? model, CancellationToken ct, AiBudgetReservation? budgetReservation = null)
        {
            CompleteCalls++;
            LastCompletedState = state;
            return Task.CompletedTask;
        }
    }
}
