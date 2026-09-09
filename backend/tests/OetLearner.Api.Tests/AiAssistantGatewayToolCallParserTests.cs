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

        var text = Assert.IsType<LlmTextChunk>(chunks.OfType<LlmTextChunk>().Single());
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

        Assert.IsType<LlmTextChunk>(chunks.OfType<LlmTextChunk>().Single());
        Assert.StartsWith("aiu_", recorder.RequestedUsageId);
        Assert.Equal("persisted-usage-1", credits.DebitRequest?.UsageRecordId);
    }

    [Fact]
    public async Task StreamCompleteWithToolsAsync_EmitsServedModelChunk_BeforeText()
    {
        var provider = new FakeAssistantProvider();
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver(),
            new EmptyProviderRegistry(),
            new[] { (IAiModelProvider)provider },
            NullLogger<AiAssistantGateway>.Instance);

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

        var served = Assert.IsType<LlmServedModel>(chunks[0]);
        Assert.Equal("assistant-model", served.Model);
        Assert.IsType<LlmTextChunk>(chunks[1]);
    }

    [Fact]
    public async Task StreamCompleteWithToolsAsync_ForwardsImageAndDocumentAttachments_ToProvider()
    {
        var provider = new CapturingAssistantProvider();
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver(),
            new EmptyProviderRegistry(),
            new[] { (IAiModelProvider)provider },
            NullLogger<AiAssistantGateway>.Instance);

        var images = new[]
        {
            new AiProviderImageAttachment { MimeType = "image/png", Data = new byte[] { 1, 2, 3 } },
        };
        var document = new AiProviderDocumentAttachment
        {
            FileName = "notes.pdf",
            MimeType = "application/pdf",
            Text = "case notes excerpt",
        };

        await foreach (var _ in gateway.StreamCompleteWithToolsAsync(
                   AiFeatureCodes.AiAssistantLearner,
                   "user-1",
                   [new LlmMessage("system", "sys"), new LlmMessage("user", "hi")],
                   Array.Empty<AiToolDefinition>(),
                   modelOverride: null,
                   CancellationToken.None,
                   images,
                   document))
        {
        }

        Assert.NotNull(provider.SeenRequest);
        Assert.Same(images, provider.SeenRequest!.ImageAttachments);
        Assert.Same(document, provider.SeenRequest.DocumentAttachment);
        Assert.Contains("notes.pdf", provider.SeenRequest.UserPrompt);
        Assert.Contains("case notes excerpt", provider.SeenRequest.UserPrompt);
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

        Assert.IsType<LlmTextChunk>(chunks.OfType<LlmTextChunk>().Single());
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

        var text = Assert.IsType<LlmTextChunk>(chunks.OfType<LlmTextChunk>().Single());
        Assert.Contains("AI credit accounting is not configured", text.Text);
        Assert.False(provider.WasCalled);
        Assert.Equal("ai_credit_accounting_unavailable", recorder.FailureErrorCode);
    }

    [Fact]
    public async Task StreamCompleteWithToolsAsync_RoutesDuckAiOverride_ToUbag_NotAnthropic()
    {
        var (gateway, anthropic, ubag) = BuildSplitCatalogGateway();

        var chunks = new List<LlmStreamChunk>();
        await foreach (var chunk in gateway.StreamCompleteWithToolsAsync(
                   AiFeatureCodes.AiAssistantAdmin,
                   "admin-1",
                   [new LlmMessage("system", "You are helpful."), new LlmMessage("user", "hi")],
                   Array.Empty<AiToolDefinition>(),
                   modelOverride: "duckai_web|GPT-5.6 Luna",
                   CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        Assert.Null(anthropic.SeenRequest);
        Assert.NotNull(ubag.SeenRequest);
        Assert.Equal("duckai_web|GPT-5.6 Luna", ubag.SeenRequest!.Model);
        Assert.Equal("ubag", ubag.SeenRequest.ProviderCode);
        Assert.Equal("ubag", Assert.IsType<LlmServedModel>(chunks[0]).ProviderCode);
    }

    [Fact]
    public async Task StreamCompleteWithToolsAsync_RoutesClaudeOverride_ToAnthropic()
    {
        var (gateway, anthropic, ubag) = BuildSplitCatalogGateway();

        await foreach (var _ in gateway.StreamCompleteWithToolsAsync(
                   AiFeatureCodes.AiAssistantAdmin,
                   "admin-1",
                   [new LlmMessage("system", "You are helpful."), new LlmMessage("user", "hi")],
                   Array.Empty<AiToolDefinition>(),
                   modelOverride: "claude-haiku-5",
                   CancellationToken.None))
        { }

        Assert.Null(ubag.SeenRequest);
        Assert.NotNull(anthropic.SeenRequest);
        Assert.Equal("claude-haiku-5", anthropic.SeenRequest!.Model);
        Assert.Equal("anthropic", anthropic.SeenRequest.ProviderCode);
    }

    [Fact]
    public async Task StreamCompleteWithToolsAsync_RoutesClaudeWeb_ToUbag_NotAnthropic()
    {
        var (gateway, anthropic, ubag) = BuildSplitCatalogGateway();

        await foreach (var _ in gateway.StreamCompleteWithToolsAsync(
                   AiFeatureCodes.AiAssistantAdmin,
                   "admin-1",
                   [new LlmMessage("system", "You are helpful."), new LlmMessage("user", "hi")],
                   Array.Empty<AiToolDefinition>(),
                   modelOverride: "claude_web",
                   CancellationToken.None))
        { }

        Assert.Null(anthropic.SeenRequest);
        Assert.NotNull(ubag.SeenRequest);
        Assert.Equal("claude_web", ubag.SeenRequest!.Model);
        Assert.Equal("ubag", ubag.SeenRequest.ProviderCode);
    }

    private static (AiAssistantGateway Gateway, NamedCapturingProvider Anthropic, NamedCapturingProvider Ubag) BuildSplitCatalogGateway()
    {
        var anthropic = new NamedCapturingProvider("anthropic");
        var ubag = new NamedCapturingProvider("registry");
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver("anthropic", "claude-sonnet-5"),
            new MapProviderRegistry(
                new AiProvider { Code = "anthropic", Dialect = AiProviderDialect.Anthropic, DefaultModel = "claude-sonnet-5", IsActive = true, EncryptedApiKey = "k" },
                new AiProvider { Code = "ubag", Dialect = AiProviderDialect.OpenAiCompatible, DefaultModel = "mock", IsActive = true, EncryptedApiKey = "k" }),
            new IAiModelProvider[] { anthropic, ubag },
            NullLogger<AiAssistantGateway>.Instance);
        return (gateway, anthropic, ubag);
    }

    private sealed class FakeRouteResolver(string providerCode = "fake-assistant", string? model = "assistant-model") : IAiFeatureRouteResolver
    {
        public Task<AiFeatureRouteResolution?> ResolveAsync(string featureCode, CancellationToken ct)
            => Task.FromResult<AiFeatureRouteResolution?>(new(providerCode, model));

        public bool IsKnownFeatureCode(string featureCode) => true;
    }

    private sealed class MapProviderRegistry(params AiProvider[] rows) : IAiProviderRegistry
    {
        public Task<AiProvider?> FindByCodeAsync(string code, CancellationToken ct)
            => Task.FromResult(rows.FirstOrDefault(r => r.Code.Equals(code, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<AiProvider>> ListActiveAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AiProvider>>(rows);

        public Task<IReadOnlyList<AiProvider>> ListByCategoryAsync(AiProviderCategory category, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AiProvider>>(rows);

        public Task<string?> GetPlatformKeyAsync(string providerCode, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    private sealed class NamedCapturingProvider(string name) : IAiModelProvider
    {
        public string Name => name;
        public AiProviderRequest? SeenRequest { get; private set; }

        public Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
        {
            SeenRequest = request;
            return Task.FromResult(new AiProviderCompletion
            {
                Text = "ok",
                Usage = new AiUsage { PromptTokens = 1, CompletionTokens = 1 },
            });
        }
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

    private sealed class CapturingAssistantProvider : IAiModelProvider
    {
        public string Name => "fake-assistant";
        public AiProviderRequest? SeenRequest { get; private set; }

        public Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
        {
            SeenRequest = request;
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

        public Task<string?> RecordSuccessAsync(AiUsageContext context, string providerId, string model, AiKeySource keySource, AiUsage? usage, int latencyMs, int retryCount, string? policyTrace, CancellationToken ct, string? accountId = null, string? failoverTrace = null, decimal costEstimateUsd = 0, string? usageRecordId = null, string? operationId = null, int? attemptNumber = null, AiCacheTokenBreakdown? cacheTokens = null, bool? providerInvoked = null)
        {
            RequestedUsageId = usageRecordId;
            RecordedUsageId = returnNull ? null : persistedUsageId ?? usageRecordId ?? "assistant-usage-1";
            return Task.FromResult<string?>(RecordedUsageId);
        }

        public Task<string?> RecordFailureAsync(AiUsageContext context, string? providerId, string? model, AiKeySource keySource, AiCallOutcome outcome, string errorCode, string? errorMessage, int latencyMs, int retryCount, string? policyTrace, CancellationToken ct, string? accountId = null, string? failoverTrace = null, AiUsage? usage = null, decimal costEstimateUsd = 0, string? usageRecordId = null, string? operationId = null, int? attemptNumber = null, bool? providerInvoked = null)
        {
            FailureErrorCode = errorCode;
            return Task.FromResult<string?>(RecordedUsageId);
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
