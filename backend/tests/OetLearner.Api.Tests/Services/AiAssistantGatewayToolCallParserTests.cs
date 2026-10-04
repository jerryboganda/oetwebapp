using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.AiAssistant;
using OetLearner.Api.Services.AiTools;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Seeding;

namespace OetLearner.Api.Tests.Services;

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

    [Fact]
    public async Task StreamCompleteWithToolsAsync_DefaultRow_SkipsKeylessSubscriptionSidecarRows()
    {
        // The route names no provider, so the gateway picks "the first active row with a key". The seeded
        // subscription sidecar row carries the marker key, is listed first and must never win that pick.
        var sidecar = new NamedCapturingProvider("anthropic");
        var registry = new NamedCapturingProvider("registry");
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver(providerCode: "", model: null),
            new MapProviderRegistry(
                new AiProvider { Code = "writing-claude-sub", Dialect = AiProviderDialect.Anthropic, DefaultModel = "claude-opus-5-5", IsActive = true, EncryptedApiKey = OetLearner.Api.Services.Seeding.WritingSubscriptionProviderDefaults.MarkerKey },
                new AiProvider { Code = "ubag", Dialect = AiProviderDialect.OpenAiCompatible, DefaultModel = "mock", IsActive = true, EncryptedApiKey = "k" }),
            new IAiModelProvider[] { sidecar, registry },
            NullLogger<AiAssistantGateway>.Instance);

        var chunks = new List<LlmStreamChunk>();
        await foreach (var chunk in gateway.StreamCompleteWithToolsAsync(
                   AiFeatureCodes.AiAssistantLearner,
                   "learner-1",
                   [new LlmMessage("system", "You are helpful."), new LlmMessage("user", "hi")],
                   Array.Empty<AiToolDefinition>(),
                   modelOverride: null,
                   CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        Assert.Null(sidecar.SeenRequest);
        Assert.NotNull(registry.SeenRequest);
        Assert.Equal("ubag", Assert.IsType<LlmServedModel>(chunks[0]).ProviderCode);
    }

    [Fact]
    public async Task StreamCompleteWithToolsAsync_DefaultRow_RefusesRatherThanUseAKeylessSidecarRow_WhenItIsTheOnlyActiveRow()
    {
        var sidecar = new NamedCapturingProvider("anthropic");
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver(providerCode: "", model: null),
            new MapProviderRegistry(
                new AiProvider { Code = "writing-claude-sub", Dialect = AiProviderDialect.Anthropic, DefaultModel = "claude-opus-5-5", IsActive = true, EncryptedApiKey = OetLearner.Api.Services.Seeding.WritingSubscriptionProviderDefaults.MarkerKey }),
            new IAiModelProvider[] { sidecar },
            NullLogger<AiAssistantGateway>.Instance);

        var chunks = new List<LlmStreamChunk>();
        await foreach (var chunk in gateway.StreamCompleteWithToolsAsync(
                   AiFeatureCodes.AiAssistantLearner,
                   "learner-1",
                   [new LlmMessage("system", "You are helpful."), new LlmMessage("user", "hi")],
                   Array.Empty<AiToolDefinition>(),
                   modelOverride: null,
                   CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        Assert.Null(sidecar.SeenRequest);
        var refusal = Assert.IsType<LlmTextChunk>(Assert.Single(chunks));
        Assert.Contains("No AI provider is configured", refusal.Text);
    }

    // ── OpenCode: explicit-only, learner-only, busy-message-on-any-failure ─────────────────────────

    [Fact]
    public async Task StreamCompleteWithToolsAsync_DefaultRow_RefusesRatherThanUseAKeyedOpenCodeRow_WhenItIsTheOnlyActiveRow()
    {
        // A real-key OpenCode row has no marker key, so only the explicit-only guard keeps it out of
        // "the first active row with a key".
        var registry = new NamedCapturingProvider("registry");
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver(providerCode: "", model: null),
            new MapProviderRegistry(OpenCodeRow()),
            new IAiModelProvider[] { registry },
            NullLogger<AiAssistantGateway>.Instance);

        var chunks = await CollectAsync(gateway, AiFeatureCodes.AiAssistantLearner, modelOverride: null);

        Assert.Null(registry.SeenRequest);
        var refusal = Assert.IsType<LlmTextChunk>(Assert.Single(chunks));
        Assert.Contains("No AI provider is configured", refusal.Text);
    }

    [Fact]
    public async Task StreamCompleteWithToolsAsync_DefaultRow_SkipsAKeyedOpenCodeRow_AndUsesTheNextEligibleRow()
    {
        var registry = new NamedCapturingProvider("registry");
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver(providerCode: "", model: null),
            new MapProviderRegistry(
                OpenCodeRow(),
                new AiProvider { Code = "ubag", Dialect = AiProviderDialect.OpenAiCompatible, DefaultModel = "mock", IsActive = true, EncryptedApiKey = "k" }),
            new IAiModelProvider[] { registry },
            NullLogger<AiAssistantGateway>.Instance);

        var chunks = await CollectAsync(gateway, AiFeatureCodes.AiAssistantLearner, modelOverride: null);

        Assert.NotNull(registry.SeenRequest);
        Assert.Equal("ubag", registry.SeenRequest!.ProviderCode);
        Assert.Equal("ubag", Assert.IsType<LlmServedModel>(chunks[0]).ProviderCode);
    }

    [Fact]
    public async Task StreamCompleteWithToolsAsync_OpenAiRoute_NeverResolvesToAKeyedOpenCodeRow()
    {
        // The "openai" shim picks the first credentialed OpenAI-compatible row; OpenCode is one.
        var registry = new NamedCapturingProvider("registry");
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver(providerCode: "openai", model: "gpt-4o"),
            new MapProviderRegistry(OpenCodeRow()),
            new IAiModelProvider[] { registry },
            NullLogger<AiAssistantGateway>.Instance);

        var chunks = await CollectAsync(gateway, AiFeatureCodes.AiAssistantLearner, modelOverride: null);

        Assert.Null(registry.SeenRequest);
        var refusal = Assert.IsType<LlmTextChunk>(Assert.Single(chunks));
        Assert.Contains("No AI provider is configured", refusal.Text);
    }

    [Fact]
    public async Task StreamCompleteWithToolsAsync_OpenCodeOverride_ServesFromTheOpenCodeRow_AndForwardsTheRawConversationKey()
    {
        var openCode = new TextProvider("registry", "hello");
        var anthropic = new NamedCapturingProvider("anthropic");
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver("anthropic", "claude-sonnet-5"),
            new MapProviderRegistry(AnthropicRow(), OpenCodeRow()),
            new IAiModelProvider[] { anthropic, openCode },
            NullLogger<AiAssistantGateway>.Instance);

        var chunks = await CollectAsync(
            gateway, AiFeatureCodes.AiAssistantLearner, modelOverride: "glm-5.3", conversationKey: "thread-123");

        Assert.Null(anthropic.SeenRequest);
        Assert.NotNull(openCode.SeenRequest);
        Assert.Equal("opencode", openCode.SeenRequest!.ProviderCode);
        Assert.Equal("glm-5.3", openCode.SeenRequest.Model);
        // Raw id: the provider adapter pseudonymises it, the gateway must not.
        Assert.Equal("thread-123", openCode.SeenRequest.SessionKey);
        Assert.Equal("opencode", Assert.IsType<LlmServedModel>(chunks[0]).ProviderCode);
        Assert.Equal("hello", Assert.Single(chunks.OfType<LlmTextChunk>()).Text);
    }

    [Theory]
    [InlineData(429, "provider_429")]
    [InlineData(401, "provider_auth")]
    [InlineData(402, "provider_quota_exhausted")]
    [InlineData(503, "provider_overloaded")]
    [InlineData(500, "provider_5xx")]
    [InlineData(400, "provider_invalid_request")]
    public async Task StreamCompleteWithToolsAsync_OpenCodeProviderFailure_YieldsExactlyTheBusyMessage_AndRecordsTheClassifiedCode(
        int status, string expectedCode)
    {
        var (gateway, openCode, anthropic, recorder) = BuildOpenCodeGateway(
            new AiProviderHttpException("OpenCode", status, "Failure"));

        var chunks = await CollectAsync(gateway, AiFeatureCodes.AiAssistantLearner, modelOverride: "glm-5.3-flash");

        Assert.Equal(OpenCodeProviderDefaults.LearnerBusyMessage, Assert.Single(chunks.OfType<LlmTextChunk>()).Text);
        // One attempt, no fallback provider call (the Claude row is right there and active).
        Assert.Equal(1, openCode.Calls);
        Assert.Null(anthropic.SeenRequest);
        Assert.Equal(AiCallOutcome.ProviderError, recorder.FailureOutcome);
        Assert.Equal(expectedCode, recorder.FailureErrorCode);
        Assert.Equal(1, recorder.FailureCount);
    }

    [Fact]
    public async Task StreamCompleteWithToolsAsync_OpenCodeProviderFailure_RecordsTheSanitisedMessage_NotTheProviderText()
    {
        var (gateway, _, _, recorder) = BuildOpenCodeGateway(
            new AiProviderHttpException("OpenCode", 429, "Too Many Requests"));

        await CollectAsync(gateway, AiFeatureCodes.AiAssistantLearner, modelOverride: "glm-5.3-flash");

        Assert.Equal("Provider request failed with HTTP 429 (rate_limited).", recorder.FailureMessage);
    }

    [Fact]
    public async Task StreamCompleteWithToolsAsync_OpenCodeUntypedFailure_YieldsTheBusyMessage_WithTheGenericCode()
    {
        var (gateway, _, _, recorder) = BuildOpenCodeGateway(new InvalidOperationException("boom"));

        var chunks = await CollectAsync(gateway, AiFeatureCodes.AiAssistantLearner, modelOverride: "glm-5.3-flash");

        Assert.Equal(OpenCodeProviderDefaults.LearnerBusyMessage, Assert.Single(chunks.OfType<LlmTextChunk>()).Text);
        Assert.Equal("provider_error", recorder.FailureErrorCode);
    }

    [Fact]
    public async Task StreamCompleteWithToolsAsync_NonOpenCodeProviderFailure_KeepsTheGenericText_ButRecordsTheClassifiedCode()
    {
        var provider = new ThrowingProvider("anthropic", new AiProviderHttpException("Claude", 529, "Overloaded"));
        var recorder = new FakeUsageRecorder();
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver("anthropic", "claude-sonnet-5"),
            new MapProviderRegistry(AnthropicRow()),
            new IAiModelProvider[] { provider },
            NullLogger<AiAssistantGateway>.Instance,
            usageRecorder: recorder);

        var chunks = await CollectAsync(gateway, AiFeatureCodes.AiAssistantLearner, modelOverride: null);

        Assert.Equal(
            "I encountered an error communicating with the AI service. Please try again.",
            Assert.Single(chunks.OfType<LlmTextChunk>()).Text);
        Assert.Equal("provider_overloaded", recorder.FailureErrorCode);
    }

    [Fact]
    public async Task StreamCompleteWithToolsAsync_OpenCodeEmptyCompletion_YieldsTheBusyMessage_AndRecordsAFailure()
    {
        var openCode = new TextProvider("registry", "   ");
        var recorder = new FakeUsageRecorder();
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver("anthropic", "claude-sonnet-5"),
            new MapProviderRegistry(AnthropicRow(), OpenCodeRow()),
            new IAiModelProvider[] { openCode },
            NullLogger<AiAssistantGateway>.Instance,
            usageRecorder: recorder);

        var chunks = await CollectAsync(gateway, AiFeatureCodes.AiAssistantLearner, modelOverride: "glm-5.3-flash");

        Assert.Equal(OpenCodeProviderDefaults.LearnerBusyMessage, Assert.Single(chunks.OfType<LlmTextChunk>()).Text);
        Assert.Equal("provider_empty_completion", recorder.FailureErrorCode);
        Assert.Equal(AiCallOutcome.ProviderError, recorder.FailureOutcome);
        Assert.Null(recorder.RequestedUsageId); // no success record for a call that produced nothing
    }

    [Fact]
    public async Task StreamCompleteWithToolsAsync_OpenCodeToolCallWithoutText_IsNotAnEmptyCompletion()
    {
        var openCode = new TextProvider("registry", "", new[] { new AiToolCall { Id = "call-1", ToolCode = "lookup", ArgsJson = "{}" } });
        var recorder = new FakeUsageRecorder();
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver("anthropic", "claude-sonnet-5"),
            new MapProviderRegistry(AnthropicRow(), OpenCodeRow()),
            new IAiModelProvider[] { openCode },
            NullLogger<AiAssistantGateway>.Instance,
            usageRecorder: recorder);

        var chunks = await CollectAsync(gateway, AiFeatureCodes.AiAssistantLearner, modelOverride: "glm-5.3-flash");

        var call = Assert.Single(chunks.OfType<LlmToolCallChunk>());
        Assert.Equal("lookup", call.Name);
        Assert.Empty(chunks.OfType<LlmTextChunk>());
        Assert.Null(recorder.FailureErrorCode);
    }

    [Fact]
    public async Task StreamCompleteWithToolsAsync_NonOpenCodeEmptyCompletion_IsNotTreatedAsAFailure()
    {
        var provider = new TextProvider("anthropic", "");
        var recorder = new FakeUsageRecorder();
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver("anthropic", "claude-sonnet-5"),
            new MapProviderRegistry(AnthropicRow()),
            new IAiModelProvider[] { provider },
            NullLogger<AiAssistantGateway>.Instance,
            usageRecorder: recorder);

        var chunks = await CollectAsync(gateway, AiFeatureCodes.AiAssistantLearner, modelOverride: null);

        Assert.Empty(chunks.OfType<LlmTextChunk>());
        Assert.Null(recorder.FailureErrorCode);
    }

    [Theory]
    [InlineData(AiFeatureCodes.AiAssistantAdmin)]
    [InlineData(AiFeatureCodes.AiAssistantExpert)]
    public async Task StreamCompleteWithToolsAsync_OpenCodeOverride_IsRefusedOutsideTheLearnerAssistant(string featureCode)
    {
        var (gateway, openCode, anthropic, recorder) = BuildOpenCodeGateway(new InvalidOperationException("unused"));

        var chunks = await CollectAsync(gateway, featureCode, modelOverride: "glm-5.3");

        var refusal = Assert.IsType<LlmTextChunk>(Assert.Single(chunks));
        Assert.Contains("only available in the learner assistant", refusal.Text);
        Assert.DoesNotContain("busy", refusal.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, openCode.Calls);
        Assert.Null(anthropic.SeenRequest);
        Assert.Equal(AiCallOutcome.GatewayRefused, recorder.FailureOutcome);
        Assert.Equal("opencode_learner_only", recorder.FailureErrorCode);
    }

    [Theory]
    [InlineData(true)]  // row exists but an admin switched it off
    [InlineData(false)] // row is gone
    public async Task StreamCompleteWithToolsAsync_OpenCodePinnedButRowUnavailable_ShowsTheBusyMessage_AndNeverFallsThrough(bool rowPresent)
    {
        // Every other provider name the old fall-through could reach is registered and must stay untouched.
        var mock = new NamedCapturingProvider("mock");
        var registry = new NamedCapturingProvider("registry");
        var direct = new NamedCapturingProvider("opencode");
        var anthropic = new NamedCapturingProvider("anthropic");
        var recorder = new FakeUsageRecorder();
        var rows = rowPresent
            ? new[] { AnthropicRow(), OpenCodeRow(isActive: false) }
            : new[] { AnthropicRow() };
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver("anthropic", "claude-sonnet-5"),
            new MapProviderRegistry(rows),
            new IAiModelProvider[] { mock, registry, direct, anthropic },
            NullLogger<AiAssistantGateway>.Instance,
            usageRecorder: recorder);

        var chunks = await CollectAsync(gateway, AiFeatureCodes.AiAssistantLearner, modelOverride: "glm-5.3-flash");

        Assert.Equal(OpenCodeProviderDefaults.LearnerBusyMessage, Assert.IsType<LlmTextChunk>(Assert.Single(chunks)).Text);
        Assert.Null(mock.SeenRequest);
        Assert.Null(registry.SeenRequest);
        Assert.Null(direct.SeenRequest);
        Assert.Null(anthropic.SeenRequest);
        Assert.Equal("no_provider", recorder.FailureErrorCode);
    }

    // ── quota: own messages, and the continuation-only daily-cap waiver ────────────────────────────

    [Fact]
    public async Task StreamCompleteWithToolsAsync_QuotaDenial_KeepsItsOwnMessage_EvenForOpenCode()
    {
        var quota = new FakeQuotaService(Deny("quota_exhausted", "Daily AI credits exhausted on plan free.", "plan.free.daily.deny"));
        var (gateway, openCode, _, recorder) = BuildOpenCodeGateway(new InvalidOperationException("unused"), quota);

        var chunks = await CollectAsync(gateway, AiFeatureCodes.AiAssistantLearner, modelOverride: "glm-5.3-flash");

        Assert.Equal("Daily AI credits exhausted on plan free.", Assert.Single(chunks.OfType<LlmTextChunk>()).Text);
        Assert.Equal(0, openCode.Calls);
        Assert.Equal(AiCallOutcome.GatewayRefused, recorder.FailureOutcome);
        Assert.Equal("quota_exhausted", recorder.FailureErrorCode);
    }

    [Fact]
    public async Task StreamCompleteWithToolsAsync_FirstIteration_RefusesADailyCapDenial()
    {
        var provider = new FakeAssistantProvider();
        var quota = new FakeQuotaService(Deny("quota_exhausted", "Daily AI credits exhausted on plan free.", "plan.free.daily.deny"));
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver(),
            new EmptyProviderRegistry(),
            new[] { (IAiModelProvider)provider },
            NullLogger<AiAssistantGateway>.Instance,
            quotaService: quota);

        var chunks = await CollectAsync(gateway, AiFeatureCodes.AiAssistantLearner, modelOverride: null, isContinuation: false);

        Assert.False(provider.WasCalled);
        Assert.Equal("Daily AI credits exhausted on plan free.", Assert.Single(chunks.OfType<LlmTextChunk>()).Text);
    }

    [Fact]
    public async Task StreamCompleteWithToolsAsync_Continuation_ToleratesTheDailyCapDenial_ButStillCommitsQuota()
    {
        var provider = new FakeAssistantProvider();
        var recorder = new FakeUsageRecorder();
        var quota = new FakeQuotaService(Deny("quota_exhausted", "Daily AI credits exhausted on plan free.", "plan.free.daily.deny"));
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver(),
            new EmptyProviderRegistry(),
            new[] { (IAiModelProvider)provider },
            NullLogger<AiAssistantGateway>.Instance,
            usageRecorder: recorder,
            quotaService: quota);

        var chunks = await CollectAsync(gateway, AiFeatureCodes.AiAssistantLearner, modelOverride: null, isContinuation: true);

        Assert.True(provider.WasCalled);
        Assert.Equal("assistant response", Assert.Single(chunks.OfType<LlmTextChunk>()).Text);
        Assert.Null(recorder.FailureErrorCode);
        Assert.Equal("plan.free.daily.deny.continuation_allowed", recorder.SuccessPolicyTrace);
        Assert.Equal(1, quota.CommitCalls);
    }

    [Theory]
    [InlineData("quota_exhausted", "Monthly AI credits exhausted on plan free.", "plan.free.monthly.deny")]
    [InlineData("kill_switch", "AI is temporarily disabled by an administrator.", "kill_switch.AllCalls")]
    [InlineData("global_budget_exhausted", "Platform AI budget has been reached.", "global_budget.hard_kill")]
    [InlineData("user_disabled", "AI access has been disabled for this account.", "override.user_disabled")]
    [InlineData("feature_disabled", "This AI feature is temporarily disabled by an administrator.", "feature_disabled.ai_assistant.learner")]
    [InlineData("overage_consent_required", "Quota exceeded. Upgrade or top up to continue.", "plan.free.daily.allow_with_charge.not_yet_consented")]
    public async Task StreamCompleteWithToolsAsync_Continuation_StillRefusesEveryOtherDenial(string errorCode, string message, string trace)
    {
        var provider = new FakeAssistantProvider();
        var recorder = new FakeUsageRecorder();
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver(),
            new EmptyProviderRegistry(),
            new[] { (IAiModelProvider)provider },
            NullLogger<AiAssistantGateway>.Instance,
            usageRecorder: recorder,
            quotaService: new FakeQuotaService(Deny(errorCode, message, trace)));

        var chunks = await CollectAsync(gateway, AiFeatureCodes.AiAssistantLearner, modelOverride: null, isContinuation: true);

        Assert.False(provider.WasCalled);
        Assert.Equal(message, Assert.Single(chunks.OfType<LlmTextChunk>()).Text);
        Assert.Equal(errorCode, recorder.FailureErrorCode);
    }

    private static async Task<List<LlmStreamChunk>> CollectAsync(
        AiAssistantGateway gateway,
        string featureCode,
        string? modelOverride,
        bool isContinuation = false,
        string? conversationKey = null)
    {
        var chunks = new List<LlmStreamChunk>();
        await foreach (var chunk in gateway.StreamCompleteWithToolsAsync(
                   featureCode,
                   "learner-1",
                   [new LlmMessage("system", "You are helpful."), new LlmMessage("user", "hi")],
                   Array.Empty<AiToolDefinition>(),
                   modelOverride,
                   CancellationToken.None,
                   conversationKey: conversationKey,
                   isContinuation: isContinuation))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private static AiProvider AnthropicRow()
        => new() { Code = "anthropic", Dialect = AiProviderDialect.Anthropic, DefaultModel = "claude-sonnet-5", IsActive = true, EncryptedApiKey = "k" };

    private static AiProvider OpenCodeRow(bool isActive = true)
        => new()
        {
            Code = OpenCodeProviderDefaults.ProviderCode,
            Dialect = AiProviderDialect.OpenAiCompatible,
            DefaultModel = OpenCodeProviderDefaults.DefaultModel,
            IsActive = isActive,
            EncryptedApiKey = "k",
        };

    private static AiQuotaDecision Deny(string errorCode, string message, string trace)
        => new(false, errorCode, message, trace, new AiGlobalPolicy(), null, null, 0, 0);

    // Claude row (default route) + active keyed OpenCode row; the OpenCode adapter ("registry") fails
    // with `failure`. The "anthropic" provider is the would-be fallback and must never be called.
    private static (AiAssistantGateway Gateway, ThrowingProvider OpenCode, NamedCapturingProvider Anthropic, FakeUsageRecorder Recorder)
        BuildOpenCodeGateway(Exception failure, IAiQuotaService? quota = null)
    {
        var openCode = new ThrowingProvider("registry", failure);
        var anthropic = new NamedCapturingProvider("anthropic");
        var recorder = new FakeUsageRecorder();
        var gateway = new AiAssistantGateway(
            new FakeRouteResolver("anthropic", "claude-sonnet-5"),
            new MapProviderRegistry(AnthropicRow(), OpenCodeRow()),
            new IAiModelProvider[] { anthropic, openCode },
            NullLogger<AiAssistantGateway>.Instance,
            usageRecorder: recorder,
            quotaService: quota);
        return (gateway, openCode, anthropic, recorder);
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

    private sealed class ThrowingProvider(string name, Exception failure) : IAiModelProvider
    {
        public string Name => name;
        public int Calls { get; private set; }

        public Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
        {
            Calls++;
            return Task.FromException<AiProviderCompletion>(failure);
        }
    }

    private sealed class TextProvider(string name, string text, IReadOnlyList<AiToolCall>? toolCalls = null) : IAiModelProvider
    {
        public string Name => name;
        public AiProviderRequest? SeenRequest { get; private set; }

        public Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
        {
            SeenRequest = request;
            return Task.FromResult(new AiProviderCompletion
            {
                Text = text,
                ToolCalls = toolCalls,
                Usage = new AiUsage { PromptTokens = 1, CompletionTokens = 1 },
            });
        }
    }

    private sealed class FakeQuotaService(AiQuotaDecision decision) : IAiQuotaService
    {
        public int CommitCalls { get; private set; }

        public Task<AiQuotaDecision> TryReserveAsync(string? userId, string featureCode, AiKeySource prospectiveKeySource, CancellationToken ct)
            => Task.FromResult(decision);

        public Task CommitAsync(string? userId, string featureCode, int promptTokens, int completionTokens, decimal costEstimateUsd, CancellationToken ct)
        {
            CommitCalls++;
            return Task.CompletedTask;
        }

        public Task<AiUserPolicySnapshot> GetUserPolicyAsync(string userId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<AiGlobalPolicy> GetGlobalPolicyAsync(CancellationToken ct)
            => throw new NotSupportedException();
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
        public string? FailureMessage { get; private set; }
        public AiCallOutcome? FailureOutcome { get; private set; }
        public int FailureCount { get; private set; }
        public string? SuccessPolicyTrace { get; private set; }

        public Task<string?> RecordSuccessAsync(AiUsageContext context, string providerId, string model, AiKeySource keySource, AiUsage? usage, int latencyMs, int retryCount, string? policyTrace, CancellationToken ct, string? accountId = null, string? failoverTrace = null, decimal costEstimateUsd = 0, string? usageRecordId = null, string? operationId = null, int? attemptNumber = null, AiCacheTokenBreakdown? cacheTokens = null, bool? providerInvoked = null)
        {
            SuccessPolicyTrace = policyTrace;
            RequestedUsageId = usageRecordId;
            RecordedUsageId = returnNull ? null : persistedUsageId ?? usageRecordId ?? "assistant-usage-1";
            return Task.FromResult<string?>(RecordedUsageId);
        }

        public Task<string?> RecordFailureAsync(AiUsageContext context, string? providerId, string? model, AiKeySource keySource, AiCallOutcome outcome, string errorCode, string? errorMessage, int latencyMs, int retryCount, string? policyTrace, CancellationToken ct, string? accountId = null, string? failoverTrace = null, AiUsage? usage = null, decimal costEstimateUsd = 0, string? usageRecordId = null, string? operationId = null, int? attemptNumber = null, bool? providerInvoked = null)
        {
            FailureErrorCode = errorCode;
            FailureMessage = errorMessage;
            FailureOutcome = outcome;
            FailureCount++;
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
