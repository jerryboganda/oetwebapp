using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.AiAssistant;
using OetLearner.Api.Services.AiTools;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

public sealed class AiAssistantGatewayToolCallParserTests
{
    [Fact]
    public void ParseToolCalls_AcceptsOpenAiNestedFunctionShape()
    {
        const string toolCallsJson = """
            [
              {
                "id": "call-1",
                "type": "function",
                "function": {
                  "name": "lookup_case",
                  "arguments": "{\"caseId\":\"abc\"}"
                }
              }
            ]
            """;

        var method = typeof(AiAssistantGateway).GetMethod("ParseToolCalls", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var calls = Assert.IsAssignableFrom<List<AiToolCall>>(method!.Invoke(null, [toolCallsJson]));
        var call = Assert.Single(calls);
        Assert.Equal("call-1", call.Id);
        Assert.Equal("lookup_case", call.ToolCode);
        Assert.Equal("{\"caseId\":\"abc\"}", call.ArgsJson);
    }

    [Fact]
    public async Task StreamCompleteWithToolsAsync_DebitsCreditForCreditPricedFeature()
    {
        var provider = new FakeAssistantProvider();
        var recorder = new FakeUsageRecorder();
        var credits = new FakeCreditService();
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver(),
            new EmptyProviderRegistry(),
            new[] { (IAiModelProvider)provider },
            NullLogger<AiAssistantGateway>.Instance,
            usageRecorder: recorder,
            creditService: credits);

        var chunks = new List<LlmStreamChunk>();
        await foreach (var chunk in gateway.StreamCompleteWithToolsAsync(
                   AiFeatureCodes.WritingGrade,
                   "user-1",
                   [new LlmMessage("system", "You are helpful."), new LlmMessage("user", "Score this.")],
                   Array.Empty<AiToolDefinition>(),
                   modelOverride: null,
                   CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        var text = Assert.IsType<LlmTextChunk>(Assert.Single(chunks));
        Assert.Equal("assistant response", text.Text);
        Assert.True(provider.WasCalled);
        Assert.StartsWith("aiu_", recorder.RecordedUsageId);
        Assert.NotNull(credits.DebitRequest);
        Assert.Equal(recorder.RecordedUsageId, credits.DebitRequest!.UsageRecordId);
        Assert.Equal(AiFeatureCodes.WritingGrade, credits.DebitRequest.FeatureCode);
    }

    [Fact]
    public async Task StreamCompleteWithToolsAsync_DebitsPersistedUsageIdReturnedByRecorder()
    {
        var provider = new FakeAssistantProvider();
        var recorder = new FakeUsageRecorder(persistedUsageId: "persisted-usage-1");
        var credits = new FakeCreditService();
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver(),
            new EmptyProviderRegistry(),
            new[] { (IAiModelProvider)provider },
            NullLogger<AiAssistantGateway>.Instance,
            usageRecorder: recorder,
            creditService: credits);

        var chunks = new List<LlmStreamChunk>();
        await foreach (var chunk in gateway.StreamCompleteWithToolsAsync(
                   AiFeatureCodes.WritingGrade,
                   "user-1",
                   [new LlmMessage("system", "You are helpful."), new LlmMessage("user", "Score this.")],
                   Array.Empty<AiToolDefinition>(),
                   modelOverride: null,
                   CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        Assert.IsType<LlmTextChunk>(Assert.Single(chunks));
        Assert.StartsWith("aiu_", recorder.RequestedUsageId);
        Assert.Equal("persisted-usage-1", credits.DebitRequest?.UsageRecordId);
    }

    [Fact]
    public async Task StreamCompleteWithToolsAsync_DoesNotDebit_WhenUsageRecorderReturnsNull()
    {
        var provider = new FakeAssistantProvider();
        var recorder = new FakeUsageRecorder(returnNull: true);
        var credits = new FakeCreditService();
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver(),
            new EmptyProviderRegistry(),
            new[] { (IAiModelProvider)provider },
            NullLogger<AiAssistantGateway>.Instance,
            usageRecorder: recorder,
            creditService: credits);

        var chunks = new List<LlmStreamChunk>();
        await foreach (var chunk in gateway.StreamCompleteWithToolsAsync(
                   AiFeatureCodes.WritingGrade,
                   "user-1",
                   [new LlmMessage("system", "You are helpful."), new LlmMessage("user", "Score this.")],
                   Array.Empty<AiToolDefinition>(),
                   modelOverride: null,
                   CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        Assert.IsType<LlmTextChunk>(Assert.Single(chunks));
        Assert.True(provider.WasCalled);
        Assert.StartsWith("aiu_", recorder.RequestedUsageId);
        Assert.Null(credits.DebitRequest);
    }

    [Fact]
    public async Task StreamCompleteWithToolsAsync_RefusesCreditPricedFeature_WhenCreditServiceMissing()
    {
        var provider = new FakeAssistantProvider();
        var recorder = new FakeUsageRecorder();
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver(),
            new EmptyProviderRegistry(),
            new[] { (IAiModelProvider)provider },
            NullLogger<AiAssistantGateway>.Instance,
            usageRecorder: recorder);

        var chunks = new List<LlmStreamChunk>();
        await foreach (var chunk in gateway.StreamCompleteWithToolsAsync(
                   AiFeatureCodes.WritingGrade,
                   "user-1",
                   [new LlmMessage("system", "You are helpful."), new LlmMessage("user", "Score this.")],
                   Array.Empty<AiToolDefinition>(),
                   modelOverride: null,
                   CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        var text = Assert.IsType<LlmTextChunk>(Assert.Single(chunks));
        Assert.Contains("AI credit accounting is not configured", text.Text);
        Assert.False(provider.WasCalled);
        Assert.Equal("ai_credit_accounting_unavailable", recorder.FailureErrorCode);
    }

    private sealed class FakeRouteResolver : IAiFeatureRouteResolver
    {
        public Task<AiFeatureRouteResolution?> ResolveAsync(string featureCode, CancellationToken ct)
            => Task.FromResult<AiFeatureRouteResolution?>(new("fake-assistant", "assistant-model"));

        public bool IsKnownFeatureCode(string featureCode) => true;
    }

    private sealed class EmptyProviderRegistry : IAiProviderRegistry
    {
        public Task<AiProvider?> FindByCodeAsync(string code, CancellationToken ct)
            => Task.FromResult<AiProvider?>(null);

        public Task<IReadOnlyList<AiProvider>> ListActiveAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AiProvider>>(Array.Empty<AiProvider>());

        public Task<IReadOnlyList<AiProvider>> ListByCategoryAsync(AiProviderCategory category, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AiProvider>>(Array.Empty<AiProvider>());

        public Task<string?> GetPlatformKeyAsync(string providerCode, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    private sealed class FakeAssistantProvider : IAiModelProvider
    {
        public string Name => "fake-assistant";
        public bool WasCalled { get; private set; }

        public Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
        {
            WasCalled = true;
            return Task.FromResult(new AiProviderCompletion
            {
                Text = "assistant response",
                Usage = new AiUsage { PromptTokens = 10, CompletionTokens = 5 },
            });
        }
    }

    private sealed class FakeUsageRecorder(string? persistedUsageId = null, bool returnNull = false) : IAiUsageRecorder
    {
        public string? RequestedUsageId { get; private set; }
        public string? RecordedUsageId { get; private set; }
        public string? FailureErrorCode { get; private set; }

        public Task<string?> RecordSuccessAsync(AiUsageContext context, string providerId, string model, AiKeySource keySource, AiUsage? usage, int latencyMs, int retryCount, string? policyTrace, CancellationToken ct, string? accountId = null, string? failoverTrace = null, decimal costEstimateUsd = 0, string? usageRecordId = null)
        {
            RequestedUsageId = usageRecordId;
            RecordedUsageId = returnNull ? null : persistedUsageId ?? usageRecordId ?? "assistant-usage-1";
            return Task.FromResult<string?>(RecordedUsageId);
        }

        public Task RecordFailureAsync(AiUsageContext context, string? providerId, string? model, AiKeySource keySource, AiCallOutcome outcome, string errorCode, string? errorMessage, int latencyMs, int retryCount, string? policyTrace, CancellationToken ct, string? accountId = null, string? failoverTrace = null, AiUsage? usage = null, decimal costEstimateUsd = 0, string? usageRecordId = null)
        {
            FailureErrorCode = errorCode;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCreditService : IAiCreditService
    {
        public AiCreditUsageDebitRequest? DebitRequest { get; private set; }

        public Task<AiCreditBalance> GetBalanceAsync(string userId, CancellationToken ct)
            => Task.FromResult(new AiCreditBalance(1, 0m, 1, 0));

        public Task<AiCreditLedgerEntry> GrantAsync(string userId, int tokens, decimal costUsd, AiCreditSource source, string? description, string? referenceId, DateTimeOffset? expiresAt, string? adminId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> DebitUsageAsync(AiCreditUsageDebitRequest request, CancellationToken ct)
        {
            DebitRequest = request;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<AiCreditLedgerEntry>> ListAsync(string userId, int page, int pageSize, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<int> SweepExpiredAsync(DateTimeOffset asOf, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
