using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiTools;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Seeding;

namespace OetLearner.Api.Tests.Services;

public sealed class RegistryBackedProviderTests
{
    [Fact]
    public async Task CompleteAsync_ExplicitMissingOpenAiProvider_ThrowsInsteadOfFallingBack()
    {
        var provider = await NewProviderAsync(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(new AiProviderRequest
        {
            ProviderCode = "missing-provider",
            Model = "glm-5",
            SystemPrompt = "system",
            UserPrompt = "user",
        }, CancellationToken.None));

        Assert.Contains("missing-provider", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not active", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteAsync_OpenAiCompatibleSuccessWithMissingMessage_ThrowsStableInvalidResponse()
    {
        var provider = await NewProviderAsync(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[{}]}", Encoding.UTF8, "application/json"),
        })));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(new AiProviderRequest
        {
            ProviderCode = "digitalocean-serverless",
            Model = "glm-5",
            SystemPrompt = "system",
            UserPrompt = "user",
        }, CancellationToken.None));

        Assert.Contains("invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("choices[0].message", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteAsync_InvalidToolSchema_ThrowsBeforeSendingRequest()
    {
        var handlerWasCalled = false;
        var provider = await NewProviderAsync(new StubHandler(_ =>
        {
            handlerWasCalled = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(new AiProviderRequest
        {
            ProviderCode = "digitalocean-serverless",
            Model = "glm-5",
            SystemPrompt = "system",
            UserPrompt = "user",
            Tools = new[]
            {
                new AiToolDefinition(
                    "lookup_case",
                    "Lookup case",
                    "Lookup a case record.",
                    AiToolCategory.Read,
                    "[]"),
            },
        }, CancellationToken.None));

        Assert.Contains("tool schema", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(handlerWasCalled);
    }

    [Fact]
    public async Task CompleteAsync_ResponseFormatJsonObject_SendsResponseFormat()
    {
        string? capturedBody = null;
        var provider = await NewProviderAsync(new StubHandler(async req =>
        {
            capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"{\\\"summary\\\":\\\"ok\\\"}\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":5,\"completion_tokens\":3}}",
                    Encoding.UTF8,
                    "application/json"),
            };
        }));

        var completion = await provider.CompleteAsync(new AiProviderRequest
        {
            ProviderCode = "digitalocean-serverless",
            Model = "glm-5",
            SystemPrompt = "system",
            UserPrompt = "summarise",
            ResponseFormatJson = "json_object",
        }, CancellationToken.None);

        Assert.Equal("{\"summary\":\"ok\"}", completion.Text);
        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody!);
        var format = doc.RootElement.GetProperty("response_format");
        Assert.Equal("json_object", format.GetProperty("type").GetString());
    }

    [Fact]
    public async Task CompleteAsync_ForcedToolWithoutProviderToolCalls_CoercesArgsJson()
    {
        var provider = await NewProviderAsync(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Here is the JSON: {\\\"verdicts\\\":[]} done.\"},\"finish_reason\":\"stop\"}]}",
                Encoding.UTF8,
                "application/json"),
        })));

        var completion = await provider.CompleteAsync(new AiProviderRequest
        {
            ProviderCode = "digitalocean-serverless",
            Model = "glm-5",
            SystemPrompt = "system",
            UserPrompt = "judge",
            ResponseFormatJson = "json_object",
            Tools = new[]
            {
                new AiToolDefinition(
                    "emit_part_a_verdicts",
                    "Emit verdicts",
                    "Emit verdicts.",
                    AiToolCategory.Read,
                    "{\"type\":\"object\",\"properties\":{\"verdicts\":{\"type\":\"array\"}},\"required\":[\"verdicts\"]}"),
            },
            ToolChoice = "emit_part_a_verdicts",
        }, CancellationToken.None);

        var call = Assert.Single(completion.ToolCalls!);
        Assert.Equal("emit_part_a_verdicts", call.ToolCode);
        Assert.Equal("{\"verdicts\":[]}", call.ArgsJson);
    }

    [Fact]
    public async Task CompleteAsync_UbagFailure_SurfacesFacadeErrorDetail()
    {
        Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", "oet-agent-gateway,ubag-vps-gateway-1");
        try
        {
            var options = new DbContextOptionsBuilder<LearnerDbContext>()
                .UseInMemoryDatabase($"registry-provider-ubag-{Guid.NewGuid():N}")
                .Options;
            var db = new LearnerDbContext(options);
            var dpProvider = new EphemeralDataProtectionProvider();
            var protector = dpProvider.CreateProtector("AiProvider.PlatformKey.v1");
            db.AiProviders.Add(new AiProvider
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = "ubag",
                Name = "UBAG (browser AI providers)",
                Dialect = AiProviderDialect.OpenAiCompatible,
                Category = AiProviderCategory.TextChat,
                BaseUrl = "http://ubag-vps-gateway-1:8080/v1/openai",
                EncryptedApiKey = protector.Protect("ubag-test-pat-1234567890"),
                ApiKeyHint = "ubag-pat",
                DefaultModel = "mock",
                IsActive = true,
                FailoverPriority = 70,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            var provider = new RegistryBackedProvider(
                new StubHttpClientFactory(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent(
                        "{\"error\":{\"message\":\"Selector drift detected; all fallbacks failed.\",\"type\":\"provider_error\",\"code\":\"provider_transient\"}}",
                        Encoding.UTF8,
                        "application/json"),
                }))),
                new AiProviderRegistry(db, dpProvider),
                Options.Create(new AiProviderOptions()));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(new AiProviderRequest
            {
                ProviderCode = "ubag",
                Model = "chatgpt_web",
                SystemPrompt = "system",
                UserPrompt = "ping",
            }, CancellationToken.None));

            Assert.Contains("UBAG provider", ex.Message);
            Assert.Contains("Selector drift", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", null);
        }
    }

    [Fact]
    public async Task CompleteAsync_UbagToolsRequest_FailsFastWithGuidance()
    {
        Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", "oet-agent-gateway,ubag-vps-gateway-1");
        try
        {
            var options = new DbContextOptionsBuilder<LearnerDbContext>()
                .UseInMemoryDatabase($"registry-provider-ubag-tools-{Guid.NewGuid():N}")
                .Options;
            var db = new LearnerDbContext(options);
            var dpProvider = new EphemeralDataProtectionProvider();
            var protector = dpProvider.CreateProtector("AiProvider.PlatformKey.v1");
            db.AiProviders.Add(new AiProvider
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = "ubag",
                Name = "UBAG (browser AI providers)",
                Dialect = AiProviderDialect.OpenAiCompatible,
                Category = AiProviderCategory.TextChat,
                BaseUrl = "http://ubag-vps-gateway-1:8080/v1/openai",
                EncryptedApiKey = protector.Protect("ubag-test-pat-1234567890"),
                ApiKeyHint = "ubag-pat",
                DefaultModel = "mock",
                IsActive = true,
                FailoverPriority = 70,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            var called = false;
            var provider = new RegistryBackedProvider(
                new StubHttpClientFactory(new StubHandler(_ =>
                {
                    called = true;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
                })),
                new AiProviderRegistry(db, dpProvider),
                Options.Create(new AiProviderOptions()));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(new AiProviderRequest
            {
                ProviderCode = "ubag",
                Model = "chatgpt_web",
                SystemPrompt = "system",
                UserPrompt = "ping",
                Tools = new[]
                {
                    new AiToolDefinition(
                        "lookup_case",
                        "Lookup case",
                        "Lookup a case record.",
                        AiToolCategory.Read,
                        "{}"),
                },
                ToolChoice = "auto",
            }, CancellationToken.None));

            Assert.Contains("does not support native function calling", ex.Message);
            Assert.False(called);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", null);
        }
    }

    [Fact]
    public async Task CompleteAsync_UbagEmptyCompletion_ThrowsWithRetryGuidance()
    {
        Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", "oet-agent-gateway,ubag-vps-gateway-1");
        try
        {
            var options = new DbContextOptionsBuilder<LearnerDbContext>()
                .UseInMemoryDatabase($"registry-provider-ubag-empty-{Guid.NewGuid():N}")
                .Options;
            var db = new LearnerDbContext(options);
            var dpProvider = new EphemeralDataProtectionProvider();
            var protector = dpProvider.CreateProtector("AiProvider.PlatformKey.v1");
            db.AiProviders.Add(new AiProvider
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = "ubag",
                Name = "UBAG (browser AI providers)",
                Dialect = AiProviderDialect.OpenAiCompatible,
                Category = AiProviderCategory.TextChat,
                BaseUrl = "http://ubag-vps-gateway-1:8080/v1/openai",
                EncryptedApiKey = protector.Protect("ubag-test-pat-1234567890"),
                ApiKeyHint = "ubag-pat",
                DefaultModel = "mock",
                IsActive = true,
                FailoverPriority = 70,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            var provider = new RegistryBackedProvider(
                new StubHttpClientFactory(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\"},\"finish_reason\":\"stop\"}],\"model\":\"chatgpt_web\"}",
                        Encoding.UTF8,
                        "application/json"),
                }))),
                new AiProviderRegistry(db, dpProvider),
                Options.Create(new AiProviderOptions()));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(new AiProviderRequest
            {
                ProviderCode = "ubag",
                Model = "chatgpt_web",
                SystemPrompt = "system",
                UserPrompt = "ping",
            }, CancellationToken.None));

            Assert.Contains("returned no text", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", null);
        }
    }

    [Fact]
    public async Task ExtractUbagErrorDetail_ReturnsFacadeMessage()
    {
        Assert.Equal(
            "Selector drift detected.",
            RegistryBackedProvider.ExtractUbagErrorDetail(
                "{\"error\":{\"message\":\"Selector drift detected.\",\"type\":\"provider_error\",\"code\":\"provider_transient\"}}"));
        Assert.Null(RegistryBackedProvider.ExtractUbagErrorDetail(null));
    }

    [Fact]
    public async Task AnthropicProvider_SendsPromptCachingHeaderAndSystemCacheBlock()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var provider = await NewAnthropicProviderAsync(new StubHandler(async req =>
        {
            capturedRequest = req;
            capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}],\"usage\":{\"input_tokens\":12,\"output_tokens\":3},\"stop_reason\":\"end_turn\"}",
                    Encoding.UTF8,
                    "application/json"),
            };
        }));

        var completion = await provider.CompleteAsync(new AiProviderRequest
        {
            ProviderCode = "anthropic",
            Model = "claude-sonnet-5",
            SystemPrompt = "rulebook and scoring criteria",
            UserPrompt = "grade this",
        }, CancellationToken.None);

        Assert.Equal("ok", completion.Text);
        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest!.Headers.TryGetValues("anthropic-beta", out var betaHeaders));
        Assert.Contains("prompt-caching-2024-07-31", string.Join(",", betaHeaders));

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody!);
        var system = doc.RootElement.GetProperty("system");
        Assert.Equal(JsonValueKind.Array, system.ValueKind);
        var block = system[0];
        Assert.Equal("text", block.GetProperty("type").GetString());
        Assert.Equal("rulebook and scoring criteria", block.GetProperty("text").GetString());
        Assert.Equal("ephemeral", block.GetProperty("cache_control").GetProperty("type").GetString());
    }

    private const string CreditBalanceBody =
        """{"type":"error","error":{"type":"invalid_request_error","message":"Your credit balance is too low to access the Anthropic API. Please go to Plans & Billing to upgrade or purchase credits."},"request_id":"req_body123"}""";

    private static AiProviderRequest AnthropicRequest(string providerCode = "anthropic", string model = "claude-sonnet-5")
        => new()
        {
            ProviderCode = providerCode,
            Model = model,
            SystemPrompt = "system",
            UserPrompt = "user",
        };

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task AnthropicProvider_Http400CreditBalance_CapturesQuotaClassWithoutLeakingTheTextIntoMessage()
    {
        var provider = await NewAnthropicProviderAsync(new StubHandler(_ =>
        {
            var response = JsonResponse(HttpStatusCode.BadRequest, CreditBalanceBody);
            response.Headers.TryAddWithoutValidation("request-id", "req_hdr456");
            return Task.FromResult(response);
        }));

        var ex = await Assert.ThrowsAsync<AiProviderHttpException>(() => provider.CompleteAsync(AnthropicRequest(), CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(AiProviderErrorClass.QuotaExhausted, ex.ErrorClass);
        // The exception Message is parsed by the retry policy and the gateway classifier: unchanged, no provider text.
        Assert.Equal("Anthropic call failed: HTTP 400 Bad Request.", ex.Message);
        Assert.DoesNotContain("credit balance", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(ex.ProviderError);
        Assert.Equal("invalid_request_error", ex.ProviderError!.Type);
        Assert.Equal("req_hdr456", ex.ProviderError.RequestId);
        // The test row's host is not a first-party Anthropic host, so only the head of the text is kept.
        Assert.StartsWith("Your credit balance is too low", ex.ProviderError.Message);
    }

    [Fact]
    public async Task AnthropicProvider_Http529WithRetryAfter_IsOverloadedAndKeepsRetryAfter()
    {
        var provider = await NewAnthropicProviderAsync(new StubHandler(_ =>
        {
            var response = JsonResponse(
                (HttpStatusCode)529,
                """{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return Task.FromResult(response);
        }));

        var ex = await Assert.ThrowsAsync<AiProviderHttpException>(() => provider.CompleteAsync(AnthropicRequest(), CancellationToken.None));

        Assert.Equal(529, ex.StatusCode);
        Assert.Equal(AiProviderErrorClass.Overloaded, ex.ErrorClass);
        Assert.Equal(TimeSpan.FromSeconds(7), ex.RetryAfter);
        Assert.Equal(TimeSpan.FromSeconds(7), ex.ProviderError!.RetryAfter);
    }

    [Fact]
    public async Task AnthropicProvider_ErrorBodyEchoingTheApiKey_IsRedacted()
    {
        var provider = await NewAnthropicProviderAsync(new StubHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.Unauthorized,
            """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key anthropic-key-1234567890"}}"""))));

        var ex = await Assert.ThrowsAsync<AiProviderHttpException>(() => provider.CompleteAsync(AnthropicRequest(), CancellationToken.None));

        Assert.Equal(AiProviderErrorClass.Auth, ex.ErrorClass);
        Assert.DoesNotContain("anthropic-key-1234567890", ex.ProviderError!.Message);
        Assert.Contains("***REDACTED***", ex.ProviderError.Message);
    }

    [Fact]
    public async Task AnthropicProvider_TemperatureDeprecatedRetryThatFailsAgain_CapturesTheSecondBody()
    {
        var calls = 0;
        var provider = await NewAnthropicProviderAsync(new StubHandler(_ =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? JsonResponse(
                    HttpStatusCode.BadRequest,
                    """{"type":"error","error":{"type":"invalid_request_error","message":"`temperature` is deprecated for this model."}}""")
                : JsonResponse(HttpStatusCode.BadRequest, CreditBalanceBody));
        }));

        // A model that still accepts temperature, so the first request carries it.
        var ex = await Assert.ThrowsAsync<AiProviderHttpException>(() =>
            provider.CompleteAsync(AnthropicRequest(model: "claude-3-haiku"), CancellationToken.None));

        Assert.Equal(2, calls);
        Assert.Equal(AiProviderErrorClass.QuotaExhausted, ex.ErrorClass);
    }

    [Theory]
    [InlineData("https://api.anthropic.com", true)]
    [InlineData("https://api.anthropic.com/v1", true)]
    [InlineData("https://anthropic.com", true)]
    [InlineData("https://anthropic.example.test/v1", false)]
    [InlineData("https://evilanthropic.com", false)]
    [InlineData("http://oet-writing-claude:8080", false)]
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void RetainsVendorText_OnlyForFirstPartyAnthropicHosts(string? baseUrl, bool expected)
    {
        Assert.Equal(expected, AnthropicProvider.RetainsVendorText(baseUrl));
    }

    [Fact]
    public async Task AnthropicProvider_SubscriptionSidecarRowWithMarkerKey_CallsTheSidecarWithoutDecryptingTheKey()
    {
        Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", "oet-writing-claude,oet-writing-codex");
        try
        {
            HttpRequestMessage? captured = null;
            var provider = await NewSidecarProviderAsync(new StubHandler(req =>
            {
                captured = req;
                return Task.FromResult(JsonResponse(
                    HttpStatusCode.OK,
                    """{"content":[{"type":"text","text":"graded"}],"usage":{"input_tokens":12,"output_tokens":3},"stop_reason":"end_turn"}"""));
            }));

            var completion = await provider.CompleteAsync(
                AnthropicRequest(WritingSubscriptionProviderDefaults.ClaudeCode, WritingSubscriptionProviderDefaults.ClaudeModel),
                CancellationToken.None);

            Assert.Equal("graded", completion.Text);
            Assert.NotNull(captured);
            Assert.Equal("http://oet-writing-claude:8080/v1/messages", captured!.RequestUri!.ToString());
            Assert.True(captured.Headers.TryGetValues("x-api-key", out var keys));
            Assert.Equal(WritingSubscriptionProviderDefaults.MarkerKey, Assert.Single(keys!));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", null);
        }
    }

    [Fact]
    public async Task AnthropicProvider_SubscriptionSidecarRowOutsideTheInternalHostList_HasNoKey_AndIsNeverCalled()
    {
        // The marker only counts as a key while the row's host is on OET_INTERNAL_AI_HOSTS.
        Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", "ubag-vps-gateway-1");
        try
        {
            var called = false;
            var provider = await NewSidecarProviderAsync(new StubHandler(_ =>
            {
                called = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(
                AnthropicRequest(WritingSubscriptionProviderDefaults.ClaudeCode, WritingSubscriptionProviderDefaults.ClaudeModel),
                CancellationToken.None));

            Assert.Contains("Platform API key missing", ex.Message);
            Assert.False(called);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", null);
        }
    }

    [Fact]
    public async Task AnthropicProvider_SubscriptionSidecarUrlOutsideTheInternalHostList_IsRefusedByTheHostGuard()
    {
        // Same wrong allow-list, but the caller supplies the marker itself, so credential resolution
        // is skipped and the SSRF guard is the only thing left standing between the call and the host.
        Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", "ubag-vps-gateway-1");
        try
        {
            var called = false;
            var provider = await NewSidecarProviderAsync(new StubHandler(_ =>
            {
                called = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(
                new AiProviderRequest
                {
                    ProviderCode = WritingSubscriptionProviderDefaults.ClaudeCode,
                    Model = WritingSubscriptionProviderDefaults.ClaudeModel,
                    SystemPrompt = "system",
                    UserPrompt = "user",
                    BaseUrlOverride = WritingSubscriptionProviderDefaults.ClaudeBaseUrl,
                    ApiKeyOverride = WritingSubscriptionProviderDefaults.MarkerKey,
                },
                CancellationToken.None));

            Assert.Contains("https", ex.Message);
            Assert.False(called);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", null);
        }
    }

    [Fact]
    public async Task AnthropicProvider_SidecarQuota429_IsQuotaExhaustedAndKeepsOnlyTheHeadOfTheText()
    {
        Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", "oet-writing-claude,oet-writing-codex");
        try
        {
            var provider = await NewSidecarProviderAsync(new StubHandler(_ => Task.FromResult(JsonResponse(
                (HttpStatusCode)429,
                """{"error":{"code":"quota_exceeded","message":"Claude subscription quota/rate limit: you have hit your limit; evidence quote from the transcript","type":"rate_limit_error"}}"""))));

            var ex = await Assert.ThrowsAsync<AiProviderHttpException>(() => provider.CompleteAsync(
                AnthropicRequest(WritingSubscriptionProviderDefaults.ClaudeCode, WritingSubscriptionProviderDefaults.ClaudeModel),
                CancellationToken.None));

            Assert.Equal(AiProviderErrorClass.QuotaExhausted, ex.ErrorClass);
            Assert.Equal("quota_exceeded", ex.ProviderError!.Code);
            Assert.Equal("Claude subscription quota/rate limit", ex.ProviderError.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", null);
        }
    }

    [Fact]
    public async Task AnthropicProvider_Sidecar502EngineError_NeverKeepsTheCliOutputTail()
    {
        Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", "oet-writing-claude,oet-writing-codex");
        try
        {
            var provider = await NewSidecarProviderAsync(new StubHandler(_ => Task.FromResult(JsonResponse(
                HttpStatusCode.BadGateway,
                """{"error":{"code":"engine_error","message":"claude exited 1: the candidate said this evidence quote"}}"""))));

            var ex = await Assert.ThrowsAsync<AiProviderHttpException>(() => provider.CompleteAsync(
                AnthropicRequest(WritingSubscriptionProviderDefaults.ClaudeCode, WritingSubscriptionProviderDefaults.ClaudeModel),
                CancellationToken.None));

            Assert.Equal(AiProviderErrorClass.ServerError, ex.ErrorClass);
            Assert.Equal("claude exited 1", ex.ProviderError!.Message);
            Assert.DoesNotContain("evidence quote", ex.ProviderError.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", null);
        }
    }

    [Fact]
    public async Task Registry_ReturnsTheMarkerForKeylessSidecarRows_AndNullForUndecryptableKeys()
    {
        Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", "oet-writing-claude,oet-writing-codex");
        try
        {
            var options = new DbContextOptionsBuilder<LearnerDbContext>()
                .UseInMemoryDatabase($"registry-marker-{Guid.NewGuid():N}")
                .Options;
            var db = new LearnerDbContext(options);
            foreach (var (code, storedKey) in new[]
                     {
                         (WritingSubscriptionProviderDefaults.ClaudeCode, WritingSubscriptionProviderDefaults.MarkerKey),
                         ("broken-key", "definitely-not-ciphertext"),
                     })
            {
                db.AiProviders.Add(new AiProvider
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Code = code,
                    Name = code,
                    Dialect = AiProviderDialect.Anthropic,
                    Category = AiProviderCategory.TextChat,
                    BaseUrl = "http://oet-writing-claude:8080",
                    EncryptedApiKey = storedKey,
                    ApiKeyHint = "hint",
                    DefaultModel = "claude-opus-5-5",
                    IsActive = true,
                    FailoverPriority = 1,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
            }

            await db.SaveChangesAsync();
            var registry = new AiProviderRegistry(db, new EphemeralDataProtectionProvider());

            Assert.Equal(
                WritingSubscriptionProviderDefaults.MarkerKey,
                await registry.GetPlatformKeyAsync(WritingSubscriptionProviderDefaults.ClaudeCode, CancellationToken.None));
            Assert.Null(await registry.GetPlatformKeyAsync("broken-key", CancellationToken.None));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", null);
        }
    }

    [Fact]
    public async Task Registry_ResolvesTheMarkerOnlyWhileTheRowPointsAtAnAllowListedInternalHost()
    {
        Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", "oet-writing-claude,oet-writing-codex");
        try
        {
            var options = new DbContextOptionsBuilder<LearnerDbContext>()
                .UseInMemoryDatabase($"registry-marker-host-{Guid.NewGuid():N}")
                .Options;
            var db = new LearnerDbContext(options);
            foreach (var (code, baseUrl) in new[]
                     {
                         ("marker-internal", "http://oet-writing-claude:8080"),
                         // A vendor row an admin re-pointed at a public URL keeps its seeded marker key.
                         ("marker-vendor", "https://api.anthropic.com"),
                         // Plain HTTP to a host that is not on the allow-list.
                         ("marker-unlisted", "http://some-other-host:8080"),
                         ("marker-blank-url", ""),
                     })
            {
                db.AiProviders.Add(new AiProvider
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Code = code,
                    Name = code,
                    Dialect = AiProviderDialect.Anthropic,
                    Category = AiProviderCategory.TextChat,
                    BaseUrl = baseUrl,
                    EncryptedApiKey = WritingSubscriptionProviderDefaults.MarkerKey,
                    ApiKeyHint = "hint",
                    DefaultModel = "claude-opus-5-5",
                    IsActive = true,
                    FailoverPriority = 1,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
            }

            await db.SaveChangesAsync();
            var registry = new AiProviderRegistry(db, new EphemeralDataProtectionProvider());

            Assert.Equal(
                WritingSubscriptionProviderDefaults.MarkerKey,
                await registry.GetPlatformKeyAsync("marker-internal", CancellationToken.None));
            Assert.Null(await registry.GetPlatformKeyAsync("marker-vendor", CancellationToken.None));
            Assert.Null(await registry.GetPlatformKeyAsync("marker-unlisted", CancellationToken.None));
            Assert.Null(await registry.GetPlatformKeyAsync("marker-blank-url", CancellationToken.None));

            // With the allow-list gone the very same seeded row stops looking credentialed.
            Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", null);
            Assert.Null(await registry.GetPlatformKeyAsync("marker-internal", CancellationToken.None));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", null);
        }
    }

    [Fact]
    public async Task AnthropicProvider_SubscriptionSidecarRow_DoesNotTakeAPlatformGatePermit()
    {
        Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", "oet-writing-claude,oet-writing-codex");
        try
        {
            var gate = await SaturatedGateAsync();
            var provider = await NewSidecarProviderAsync(new StubHandler(_ => Task.FromResult(JsonResponse(HttpStatusCode.OK, SidecarMessagesBody))), gate);

            // Every permit is held by other platform-key calls: the sidecar call must not queue behind them,
            // because its own serial CLI lane is the limiter.
            var completion = await provider
                .CompleteAsync(AnthropicRequest(WritingSubscriptionProviderDefaults.ClaudeCode, WritingSubscriptionProviderDefaults.ClaudeModel), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal("graded", completion.Text);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", null);
        }
    }

    [Fact]
    public async Task AnthropicProvider_KeyedRow_StillQueuesForAPlatformGatePermit()
    {
        Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", "oet-writing-claude,oet-writing-codex");
        try
        {
            var gate = await SaturatedGateAsync();
            var handlerCalled = false;
            var provider = await NewSidecarProviderAsync(
                new StubHandler(_ =>
                {
                    handlerCalled = true;
                    return Task.FromResult(JsonResponse(HttpStatusCode.OK, SidecarMessagesBody));
                }),
                gate,
                markerKey: false);

            var pending = provider.CompleteAsync(
                AnthropicRequest(WritingSubscriptionProviderDefaults.ClaudeCode, WritingSubscriptionProviderDefaults.ClaudeModel),
                CancellationToken.None);
            await Task.Delay(300);

            Assert.False(pending.IsCompleted);
            Assert.False(handlerCalled);

            gate.Release();
            var completion = await pending.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("graded", completion.Text);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", null);
        }
    }

    [Fact]
    public async Task RegistryBackedProvider_CodexSubscriptionSidecarRow_DoesNotTakeAPlatformGatePermit()
    {
        Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", "oet-writing-claude,oet-writing-codex");
        try
        {
            var gate = await SaturatedGateAsync();
            var provider = await NewCodexSidecarProviderAsync(new StubHandler(_ => Task.FromResult(JsonResponse(HttpStatusCode.OK, SidecarChatCompletionBody))), gate);

            var completion = await provider
                .CompleteAsync(
                    new AiProviderRequest
                    {
                        ProviderCode = WritingSubscriptionProviderDefaults.CodexCode,
                        Model = WritingSubscriptionProviderDefaults.CodexModel,
                        SystemPrompt = "system",
                        UserPrompt = "user",
                    },
                    CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal("graded", completion.Text);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", null);
        }
    }

    [Fact]
    public async Task RegistryBackedProvider_KeyedRow_StillQueuesForAPlatformGatePermit()
    {
        Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", "oet-writing-claude,oet-writing-codex");
        try
        {
            var gate = await SaturatedGateAsync();
            var handlerCalled = false;
            var provider = await NewCodexSidecarProviderAsync(
                new StubHandler(_ =>
                {
                    handlerCalled = true;
                    return Task.FromResult(JsonResponse(HttpStatusCode.OK, SidecarChatCompletionBody));
                }),
                gate,
                markerKey: false);

            var pending = provider.CompleteAsync(
                new AiProviderRequest
                {
                    ProviderCode = WritingSubscriptionProviderDefaults.CodexCode,
                    Model = WritingSubscriptionProviderDefaults.CodexModel,
                    SystemPrompt = "system",
                    UserPrompt = "user",
                },
                CancellationToken.None);
            await Task.Delay(300);

            Assert.False(pending.IsCompleted);
            Assert.False(handlerCalled);

            gate.Release();
            var completion = await pending.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("graded", completion.Text);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OET_INTERNAL_AI_HOSTS", null);
        }
    }

    private const string SidecarMessagesBody =
        """{"content":[{"type":"text","text":"graded"}],"usage":{"input_tokens":12,"output_tokens":3},"stop_reason":"end_turn"}""";

    private const string SidecarChatCompletionBody =
        """{"choices":[{"message":{"role":"assistant","content":"graded"},"finish_reason":"stop"}],"usage":{"prompt_tokens":12,"completion_tokens":3}}""";

    private static async Task<OetLearner.Api.Services.Ai.AiPlatformConcurrencyGate> SaturatedGateAsync()
    {
        var gate = new OetLearner.Api.Services.Ai.AiPlatformConcurrencyGate();
        for (var i = 0; i < OetLearner.Api.Services.Ai.AiPlatformConcurrencyGate.MaxInFlight; i++)
            await gate.WaitAsync(CancellationToken.None);
        return gate;
    }

    /// <param name="markerKey">True stores exactly what the seeder stores (the literal marker); false stores a
    /// real encrypted key, so the row is an ordinary keyed row that merely lives at the sidecar URL.</param>
    private static async Task<AnthropicProvider> NewSidecarProviderAsync(
        HttpMessageHandler handler,
        OetLearner.Api.Services.Ai.AiPlatformConcurrencyGate? gate = null,
        bool markerKey = true)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"anthropic-sidecar-{Guid.NewGuid():N}")
            .Options;
        var db = new LearnerDbContext(options);
        var dpProvider = new EphemeralDataProtectionProvider();
        db.AiProviders.Add(new AiProvider
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = WritingSubscriptionProviderDefaults.ClaudeCode,
            Name = WritingSubscriptionProviderDefaults.ClaudeName,
            Dialect = AiProviderDialect.Anthropic,
            Category = AiProviderCategory.TextChat,
            BaseUrl = WritingSubscriptionProviderDefaults.ClaudeBaseUrl,
            // Exactly what WritingSubscriptionProviderSeeder stores: a literal marker, not ciphertext.
            EncryptedApiKey = markerKey
                ? WritingSubscriptionProviderDefaults.MarkerKey
                : dpProvider.CreateProtector("AiProvider.PlatformKey.v1").Protect("sidecar-real-key-1234567890"),
            ApiKeyHint = "claude-max-5x",
            DefaultModel = WritingSubscriptionProviderDefaults.ClaudeModel,
            IsActive = true,
            FailoverPriority = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return new AnthropicProvider(
            new StubHttpClientFactory(handler),
            new AiProviderRegistry(db, dpProvider),
            gate);
    }

    private static async Task<RegistryBackedProvider> NewCodexSidecarProviderAsync(
        HttpMessageHandler handler,
        OetLearner.Api.Services.Ai.AiPlatformConcurrencyGate? gate = null,
        bool markerKey = true)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"codex-sidecar-{Guid.NewGuid():N}")
            .Options;
        var db = new LearnerDbContext(options);
        var dpProvider = new EphemeralDataProtectionProvider();
        db.AiProviders.Add(new AiProvider
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = WritingSubscriptionProviderDefaults.CodexCode,
            Name = WritingSubscriptionProviderDefaults.CodexName,
            Dialect = AiProviderDialect.OpenAiCompatible,
            Category = AiProviderCategory.TextChat,
            BaseUrl = WritingSubscriptionProviderDefaults.CodexBaseUrl,
            EncryptedApiKey = markerKey
                ? WritingSubscriptionProviderDefaults.MarkerKey
                : dpProvider.CreateProtector("AiProvider.PlatformKey.v1").Protect("sidecar-real-key-1234567890"),
            ApiKeyHint = "codex-chatgpt",
            DefaultModel = WritingSubscriptionProviderDefaults.CodexModel,
            IsActive = true,
            FailoverPriority = 2,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return new RegistryBackedProvider(
            new StubHttpClientFactory(handler),
            new AiProviderRegistry(db, dpProvider),
            Options.Create(new AiProviderOptions()),
            gate);
    }

    private static async Task<RegistryBackedProvider> NewProviderAsync(HttpMessageHandler handler)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"registry-provider-{Guid.NewGuid():N}")
            .Options;
        var db = new LearnerDbContext(options);

        var dpProvider = new EphemeralDataProtectionProvider();
        var protector = dpProvider.CreateProtector("AiProvider.PlatformKey.v1");
        db.AiProviders.Add(new AiProvider
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = "digitalocean-serverless",
            Name = "DigitalOcean Serverless",
            Dialect = AiProviderDialect.OpenAiCompatible,
            Category = AiProviderCategory.TextChat,
            BaseUrl = "https://example.test/v1",
            EncryptedApiKey = protector.Protect("sk-test-1234567890"),
            ApiKeyHint = "...7890",
            DefaultModel = "glm-5",
            IsActive = true,
            FailoverPriority = 10,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return new RegistryBackedProvider(
            new StubHttpClientFactory(handler),
            new AiProviderRegistry(db, dpProvider),
            Options.Create(new AiProviderOptions()));
    }

    private static async Task<AnthropicProvider> NewAnthropicProviderAsync(HttpMessageHandler handler)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"anthropic-provider-{Guid.NewGuid():N}")
            .Options;
        var db = new LearnerDbContext(options);

        var dpProvider = new EphemeralDataProtectionProvider();
        var protector = dpProvider.CreateProtector("AiProvider.PlatformKey.v1");
        db.AiProviders.Add(new AiProvider
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = "anthropic",
            Name = "Anthropic",
            Dialect = AiProviderDialect.Anthropic,
            Category = AiProviderCategory.TextChat,
            BaseUrl = "https://anthropic.example.test/v1",
            EncryptedApiKey = protector.Protect("anthropic-key-1234567890"),
            ApiKeyHint = "...7890",
            DefaultModel = "claude-sonnet-5",
            IsActive = true,
            FailoverPriority = 10,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return new AnthropicProvider(
            new StubHttpClientFactory(handler),
            new AiProviderRegistry(db, dpProvider));
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request);
    }
}
