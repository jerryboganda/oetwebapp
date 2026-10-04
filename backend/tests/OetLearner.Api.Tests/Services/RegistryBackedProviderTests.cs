using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
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

    // ── OpenAI-compatible hardening: tool calls, ids and empty completions (every provider) ─────────

    private static AiProviderRequest ToolRequest() => new()
    {
        ProviderCode = "digitalocean-serverless",
        Model = "glm-5",
        SystemPrompt = "system",
        UserPrompt = "user",
        Tools = new[]
        {
            new AiToolDefinition("lookup_case", "Lookup case", "Lookup a case record.", AiToolCategory.Read, "{}"),
        },
        ToolChoice = "auto",
    };

    /// <summary>An OpenAI chat-completions body whose single message carries the given tool calls. A null id
    /// omits the key; a null name serialises as JSON null.</summary>
    private static string ToolCallsBody(string? content, params (string? Id, string? Name, string Args)[] calls)
    {
        var toolCalls = calls.Select(call =>
        {
            var entry = new Dictionary<string, object?>
            {
                ["type"] = "function",
                ["function"] = new Dictionary<string, object?> { ["name"] = call.Name, ["arguments"] = call.Args },
            };
            if (call.Id is not null) entry["id"] = call.Id;
            return entry;
        }).ToList();

        return JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new { role = "assistant", content, tool_calls = toolCalls },
                    finish_reason = "tool_calls",
                },
            },
            model = "glm-5.3-flash",
            usage = new { prompt_tokens = 5, completion_tokens = 3 },
        });
    }

    [Fact]
    public async Task CompleteAsync_ContentNullWithToolCalls_ReturnsTheCallsInsteadOfAnEmptyCompletionFailure()
    {
        var provider = await NewProviderAsync(new StubHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK, ToolCallsBody(null, ("call_1", "lookup_case", "{\"q\":1}"))))));

        var completion = await provider.CompleteAsync(ToolRequest(), CancellationToken.None);

        Assert.Equal(string.Empty, completion.Text);
        var call = Assert.Single(completion.ToolCalls!);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("lookup_case", call.ToolCode);
        Assert.Equal("{\"q\":1}", call.ArgsJson);
        Assert.Equal("tool_calls", completion.FinishReason);
        Assert.Equal("glm-5.3-flash", completion.ServedModel);
    }

    [Fact]
    public async Task CompleteAsync_ToolCallWithoutAnId_GetsASynthesisedOne()
    {
        var provider = await NewProviderAsync(new StubHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK, ToolCallsBody(null, (null, "lookup_case", "{}"))))));

        var completion = await provider.CompleteAsync(ToolRequest(), CancellationToken.None);

        var call = Assert.Single(completion.ToolCalls!);
        Assert.StartsWith("call_", call.Id);
        Assert.False(string.IsNullOrWhiteSpace(call.Id));
        Assert.Equal("lookup_case", call.ToolCode);
    }

    [Fact]
    public async Task CompleteAsync_TextWithOnlyAnEmptyNamedToolCall_ReturnsTheTextAndNoToolCalls()
    {
        var provider = await NewProviderAsync(new StubHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK, ToolCallsBody("Here is the answer.", ("call_1", "", "{}"))))));

        var completion = await provider.CompleteAsync(ToolRequest(), CancellationToken.None);

        Assert.Equal("Here is the answer.", completion.Text);
        Assert.Null(completion.ToolCalls);
    }

    [Fact]
    public async Task CompleteAsync_OnlyAnEmptyNamedToolCallAndNoText_IsAnEmptyCompletion_WithoutUbagWording()
    {
        var provider = await NewProviderAsync(new StubHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK, ToolCallsBody(null, ("call_1", "", "{}"))))));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.CompleteAsync(ToolRequest(), CancellationToken.None));

        Assert.Contains("no text and no tool calls", ex.Message);
        Assert.DoesNotContain("UBAG", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("browser job", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteAsync_NonUbagInvalidResponse_NeverMentionsUbag()
    {
        var provider = await NewProviderAsync(new StubHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK, "{\"choices\":[{}]}"))));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.CompleteAsync(ToolRequest(), CancellationToken.None));

        Assert.Contains("choices[0].message", ex.Message);
        Assert.DoesNotContain("UBAG", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadOpenAiToolCalls_SynthesisesMissingOrEmptyIds_AndKeepsEveryIdUniqueWithinTheReply()
    {
        using var doc = JsonDocument.Parse(ToolCallsBody(
            null,
            (null, "a", "{}"),
            ("", "b", "{}"),
            ("dup", "c", "{}"),
            ("dup", "d", "{}"),
            ("keep", "e", "{}")));
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

        var calls = AiProviderPayloadBuilder.ReadOpenAiToolCalls(message)!;

        Assert.Equal(new[] { "a", "b", "c", "d", "e" }, calls.Select(call => call.ToolCode));
        Assert.Equal(calls.Count, calls.Select(call => call.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.StartsWith("call_", calls[0].Id);
        Assert.StartsWith("call_", calls[1].Id);
        Assert.Equal("dup", calls[2].Id);     // the first holder keeps the model's own id
        Assert.StartsWith("call_", calls[3].Id);
        Assert.NotEqual("dup", calls[3].Id);
        Assert.Equal("keep", calls[4].Id);
    }

    [Fact]
    public void ReadOpenAiToolCalls_DropsEntriesWithAnEmptyBlankOrMissingName()
    {
        using var doc = JsonDocument.Parse(ToolCallsBody(
            null,
            ("call_1", "", "{}"),
            ("call_2", "   ", "{}"),
            ("call_3", null, "{}"),
            ("call_4", "lookup_case", "{}")));
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

        var call = Assert.Single(AiProviderPayloadBuilder.ReadOpenAiToolCalls(message)!);

        Assert.Equal("call_4", call.Id);
        Assert.Equal("lookup_case", call.ToolCode);
    }

    [Fact]
    public void ReadOpenAiToolCalls_EveryEntryDropped_ReturnsNull()
    {
        using var doc = JsonDocument.Parse(ToolCallsBody(null, ("call_1", "", "{}"), ("call_2", null, "{}")));
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

        Assert.Null(AiProviderPayloadBuilder.ReadOpenAiToolCalls(message));
    }

    // ── OpenCode gateway rows ───────────────────────────────────────────────────────────────────

    private const string FakeGatewayKey = "fake-gateway-key-1234567890";

    private const string OpenAiTextBody =
        """{"choices":[{"message":{"role":"assistant","content":"hello"},"finish_reason":"stop"}],"model":"glm-5.3-flash","usage":{"prompt_tokens":5,"completion_tokens":3}}""";

    private static AiProvider OpenCodeRow(string? reasoningEffort = "low", bool active = true) => new()
    {
        Id = OpenCodeProviderDefaults.ProviderCode,
        Code = OpenCodeProviderDefaults.ProviderCode,
        Name = OpenCodeProviderDefaults.ProviderName,
        Dialect = AiProviderDialect.OpenAiCompatible,
        Category = AiProviderCategory.TextChat,
        BaseUrl = OpenCodeProviderDefaults.ZenBaseUrl,
        EncryptedApiKey = "encrypted-test-key",
        ApiKeyHint = "...test",
        DefaultModel = OpenCodeProviderDefaults.DefaultModel,
        ReasoningEffort = reasoningEffort,
        IsActive = active,
        FailoverPriority = OpenCodeProviderDefaults.FailoverPriority,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static AiProvider GenericRow(string code, int priority, string? reasoningEffort = null) => new()
    {
        Id = code,
        Code = code,
        Name = code,
        Dialect = AiProviderDialect.OpenAiCompatible,
        Category = AiProviderCategory.TextChat,
        BaseUrl = "https://example.test/v1",
        EncryptedApiKey = "encrypted-test-key",
        ApiKeyHint = "...test",
        DefaultModel = "glm-5",
        ReasoningEffort = reasoningEffort,
        IsActive = true,
        FailoverPriority = priority,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static AiProviderRequest OpenCodeRequest(string? sessionKey = null, int? maxTokens = null) => new()
    {
        ProviderCode = OpenCodeProviderDefaults.ProviderCode,
        Model = OpenCodeProviderDefaults.DefaultModel,
        SystemPrompt = "system",
        UserPrompt = "user",
        SessionKey = sessionKey,
        MaxTokens = maxTokens,
    };

    /// <summary>A provider over an in-memory, thread-safe registry (no DbContext, so concurrent calls are safe).</summary>
    private static RegistryBackedProvider NewRowsProvider(
        HttpMessageHandler handler,
        IReadOnlyList<AiProvider>? rows = null,
        AiProviderOptions? aiOptions = null,
        OetLearner.Api.Services.Ai.AiPlatformConcurrencyGate? gate = null,
        IHttpClientFactory? factory = null)
        => new(
            factory ?? new StubHttpClientFactory(handler),
            new FixedRegistry(rows ?? new[] { OpenCodeRow() }),
            Options.Create(aiOptions ?? new AiProviderOptions()),
            gate);

    [Fact]
    public async Task OpenCodeRow_SendsIdentityHeaders_ReasoningEffort_AndAtLeast6144MaxTokens()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var provider = NewRowsProvider(new StubHandler(async req =>
        {
            captured = req;
            body = await req.Content!.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.OK, OpenAiTextBody);
        }));

        var completion = await provider.CompleteAsync(OpenCodeRequest(sessionKey: "thread-123", maxTokens: 1024), CancellationToken.None);

        Assert.Equal("hello", completion.Text);
        Assert.Equal("glm-5.3-flash", completion.ServedModel);
        Assert.Equal("https://opencode.ai/zen/v1/chat/completions", captured!.RequestUri!.ToString());
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal(FakeGatewayKey, captured.Headers.Authorization.Parameter);
        Assert.StartsWith("OET-Platform/", captured.Headers.UserAgent.ToString());
        Assert.True(captured.Headers.TryGetValues("x-opencode-session", out var sessionValues));
        var session = Assert.Single(sessionValues!);
        Assert.Matches("^oet-[0-9a-f]{24}$", session);
        Assert.DoesNotContain("thread-123", session);

        using var doc = JsonDocument.Parse(body!);
        Assert.Equal("low", doc.RootElement.GetProperty("reasoning_effort").GetString());
        Assert.Equal(6144, doc.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.False(doc.RootElement.GetProperty("stream").GetBoolean());
    }

    [Theory]
    [InlineData(null, 6144)]
    [InlineData(2000, 6144)]
    [InlineData(6144, 6144)]
    [InlineData(9000, 9000)]
    public async Task OpenCodeRow_MaxTokens_IsNeverBelowTheThinkingFloor_AndNeverLowersAHigherRequest(int? requested, int expected)
    {
        string? body = null;
        var provider = NewRowsProvider(new StubHandler(async req =>
        {
            body = await req.Content!.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.OK, OpenAiTextBody);
        }));

        await provider.CompleteAsync(OpenCodeRequest(maxTokens: requested), CancellationToken.None);

        using var doc = JsonDocument.Parse(body!);
        Assert.Equal(expected, doc.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task OpenCodeRow_SessionHeader_IsAStablePseudonymOfTheThreadId()
    {
        var seen = new List<string>();
        var provider = NewRowsProvider(new StubHandler(req =>
        {
            seen.Add(req.Headers.GetValues("x-opencode-session").Single());
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, OpenAiTextBody));
        }));

        await provider.CompleteAsync(OpenCodeRequest(sessionKey: "thread-A"), CancellationToken.None);
        await provider.CompleteAsync(OpenCodeRequest(sessionKey: "thread-A"), CancellationToken.None);
        await provider.CompleteAsync(OpenCodeRequest(sessionKey: "thread-B"), CancellationToken.None);
        await provider.CompleteAsync(OpenCodeRequest(sessionKey: null), CancellationToken.None);
        await provider.CompleteAsync(OpenCodeRequest(sessionKey: "  "), CancellationToken.None);

        Assert.Equal(seen[0], seen[1]);
        Assert.NotEqual(seen[0], seen[2]);
        Assert.Equal(seen[3], seen[4]);          // no thread id: the per-process fallback, stable
        Assert.NotEqual(seen[0], seen[3]);
        Assert.All(seen, value =>
        {
            Assert.Matches("^oet-[0-9a-f]{24}$", value);
            Assert.True(value.Length <= 30, value);
            Assert.DoesNotContain("thread", value, StringComparison.OrdinalIgnoreCase);
        });

        // Independent re-derivation: oet- + first 24 lowercase hex chars of SHA-256(thread id).
        var expected = "oet-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("thread-A"))).ToLowerInvariant()[..24];
        Assert.Equal(expected, seen[0]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OpenCodeRow_WithoutARowReasoningEffort_SendsNoReasoningEffort_EvenWhenTheEnvDefaultIsSet(string? rowEffort)
    {
        string? body = null;
        var provider = NewRowsProvider(
            new StubHandler(async req =>
            {
                body = await req.Content!.ReadAsStringAsync();
                return JsonResponse(HttpStatusCode.OK, OpenAiTextBody);
            }),
            new[] { OpenCodeRow(rowEffort) },
            new AiProviderOptions { ReasoningEffort = "high" });

        await provider.CompleteAsync(OpenCodeRequest(), CancellationToken.None);

        using var doc = JsonDocument.Parse(body!);
        Assert.False(doc.RootElement.TryGetProperty("reasoning_effort", out _));
    }

    [Fact]
    public async Task NonOpenCodeRow_KeepsTheNameBasedReasoningRule_AndTheEnvDefault()
    {
        var bodies = new List<string>();
        var provider = NewRowsProvider(
            new StubHandler(async req =>
            {
                bodies.Add(await req.Content!.ReadAsStringAsync());
                return JsonResponse(HttpStatusCode.OK, OpenAiTextBody);
            }),
            new[] { GenericRow("openai-platform", 10, reasoningEffort: "low") },
            new AiProviderOptions { ReasoningEffort = "medium" });

        // glm-5 is not reasoning-capable by name: nothing is sent even though the row sets an effort.
        await provider.CompleteAsync(new AiProviderRequest { Model = "glm-5", SystemPrompt = "s", UserPrompt = "u" }, CancellationToken.None);
        // gpt-5 is: the row's own value is sent.
        await provider.CompleteAsync(new AiProviderRequest { Model = "gpt-5-mini", SystemPrompt = "s", UserPrompt = "u" }, CancellationToken.None);

        using var first = JsonDocument.Parse(bodies[0]);
        using var second = JsonDocument.Parse(bodies[1]);
        Assert.False(first.RootElement.TryGetProperty("reasoning_effort", out _));
        Assert.Equal("low", second.RootElement.GetProperty("reasoning_effort").GetString());

        // A reasoning-capable model on a row with no effort still inherits the env default.
        bodies.Clear();
        var inheriting = NewRowsProvider(
            new StubHandler(async req =>
            {
                bodies.Add(await req.Content!.ReadAsStringAsync());
                return JsonResponse(HttpStatusCode.OK, OpenAiTextBody);
            }),
            new[] { GenericRow("openai-platform", 10) },
            new AiProviderOptions { ReasoningEffort = "medium" });
        await inheriting.CompleteAsync(new AiProviderRequest { Model = "gpt-5-mini", SystemPrompt = "s", UserPrompt = "u" }, CancellationToken.None);
        using var third = JsonDocument.Parse(bodies[0]);
        Assert.Equal("medium", third.RootElement.GetProperty("reasoning_effort").GetString());
    }

    [Fact]
    public async Task NonOpenCodeRow_GetsNoOpenCodeHeaders_AndKeepsTheStandardTimeoutAndMaxTokens()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var factory = new CapturingHttpClientFactory(new StubHandler(async req =>
        {
            captured = req;
            body = await req.Content!.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.OK, OpenAiTextBody);
        }));
        var provider = NewRowsProvider(
            handler: null!,
            new[] { GenericRow("openai-platform", 10) },
            factory: factory);

        await provider.CompleteAsync(
            new AiProviderRequest { ProviderCode = "openai-platform", Model = "glm-5", SystemPrompt = "s", UserPrompt = "u", MaxTokens = 1024, SessionKey = "thread-123" },
            CancellationToken.None);

        Assert.False(captured!.Headers.Contains("x-opencode-session"));
        Assert.DoesNotContain("OET-Platform", captured.Headers.UserAgent.ToString());
        Assert.Equal(TimeSpan.FromSeconds(300), factory.LastClient!.Timeout);
        using var doc = JsonDocument.Parse(body!);
        Assert.Equal(1024, doc.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task OpenCodeRow_UsesA100SecondHttpTimeout()
    {
        var factory = new CapturingHttpClientFactory(new StubHandler(_ => Task.FromResult(JsonResponse(HttpStatusCode.OK, OpenAiTextBody))));
        var provider = NewRowsProvider(handler: null!, factory: factory);

        await provider.CompleteAsync(OpenCodeRequest(), CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(100), factory.LastClient!.Timeout);
    }

    [Fact]
    public async Task OpenCodeRow_ToolCallReplyWithNullContent_IsNotAnEmptyCompletion()
    {
        var provider = NewRowsProvider(new StubHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK, ToolCallsBody(null, ("call_9", "lookup_case", "{}"), ("", "", "{}"))))));
        var request = new AiProviderRequest
        {
            ProviderCode = OpenCodeProviderDefaults.ProviderCode,
            Model = OpenCodeProviderDefaults.DefaultModel,
            SystemPrompt = "system",
            UserPrompt = "user",
            Tools = new[] { new AiToolDefinition("lookup_case", "Lookup case", "Lookup a case record.", AiToolCategory.Read, "{}") },
            ToolChoice = "auto",
        };

        var completion = await provider.CompleteAsync(request, CancellationToken.None);

        var call = Assert.Single(completion.ToolCalls!);
        Assert.Equal("call_9", call.Id);
        Assert.Equal("lookup_case", call.ToolCode);
    }

    [Fact]
    public async Task OpenCodeRow_EmptyCompletion_Throws()
    {
        var provider = NewRowsProvider(new StubHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK, """{"choices":[{"message":{"role":"assistant","content":null},"finish_reason":"length"}]}"""))));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.CompleteAsync(OpenCodeRequest(), CancellationToken.None));

        Assert.Contains("no text and no tool calls", ex.Message);
        Assert.Contains("finish_reason=length", ex.Message);
    }

    [Theory]
    [InlineData(401, """{"type":"error","error":{"type":"CreditsError","message":"No payment method. Add one at the console."}}""", AiProviderErrorClass.QuotaExhausted, "creditserror")]
    [InlineData(401, """{"type":"error","error":{"type":"AuthError","message":"Invalid API key."}}""", AiProviderErrorClass.Auth, "autherror")]
    [InlineData(401, """{"type":"error","error":{"type":"MonthlyLimitError","message":"Monthly limit reached."}}""", AiProviderErrorClass.QuotaExhausted, "monthlylimiterror")]
    [InlineData(402, """{"error":{"message":"Insufficient account funds"}}""", AiProviderErrorClass.QuotaExhausted, null)]
    [InlineData(429, """{"type":"error","error":{"type":"RateLimitError","message":"Rate limit exceeded."}}""", AiProviderErrorClass.RateLimited, "ratelimiterror")]
    [InlineData(429, """{"type":"error","error":{"type":"GoUsageLimitError","message":"Go usage limit reached."}}""", AiProviderErrorClass.QuotaExhausted, "gousagelimiterror")]
    [InlineData(503, """{"type":"error","error":{"type":"api_error","message":"Upstream unavailable."}}""", AiProviderErrorClass.Overloaded, "api_error")]
    [InlineData(400, """{"type":"error","error":{"type":"ModelError","message":"Model rejected the request."}}""", AiProviderErrorClass.InvalidRequest, "modelerror")]
    public async Task OpenCodeRow_NonSuccess_ThrowsATypedFailure_ClassifiedFromTheGatewayErrorType(
        int status, string body, AiProviderErrorClass expectedClass, string? expectedType)
    {
        var provider = NewRowsProvider(new StubHandler(_ => Task.FromResult(JsonResponse((HttpStatusCode)status, body))));

        var ex = await Assert.ThrowsAsync<AiProviderHttpException>(
            () => provider.CompleteAsync(OpenCodeRequest(), CancellationToken.None));

        Assert.Equal(status, ex.StatusCode);
        Assert.Equal(expectedClass, ex.ErrorClass);
        Assert.Equal(expectedType, ex.ProviderError!.Type);
        // The gateway's own text never enters the exception Message; AiRetryPolicy still reads "HTTP nnn".
        var reason = new HttpResponseMessage((HttpStatusCode)status).ReasonPhrase;
        Assert.Equal($"OpenCode call failed: HTTP {status} {reason}.", ex.Message);

        // The assistant gateway records exactly these two values on the usage row.
        var errorCode = AiGatewayService.ClassifyError(ex);
        Assert.Equal(
            expectedClass switch
            {
                AiProviderErrorClass.QuotaExhausted => "provider_quota_exhausted",
                AiProviderErrorClass.RateLimited => "provider_429",
                AiProviderErrorClass.Auth => "provider_auth",
                AiProviderErrorClass.Overloaded => "provider_overloaded",
                _ => "provider_invalid_request",
            },
            errorCode);
        var classCode = expectedClass.ToCode();
        Assert.Equal(
            expectedType is null
                ? $"Provider request failed with HTTP {status} ({classCode})."
                : $"Provider request failed with HTTP {status} ({classCode}; {expectedType}).",
            AiGatewayService.SanitiseProviderErrorMessage(ex, errorCode));
    }

    [Fact]
    public async Task OpenCodeRow_RateLimit_KeepsRetryAfter_AndRedactsAnEchoedKeyFromTheCapturedText()
    {
        var provider = NewRowsProvider(new StubHandler(_ =>
        {
            var response = JsonResponse(
                (HttpStatusCode)429,
                "{\"error\":{\"type\":\"RateLimitError\",\"message\":\"slow down " + FakeGatewayKey + "\"}}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(9));
            return Task.FromResult(response);
        }));

        var ex = await Assert.ThrowsAsync<AiProviderHttpException>(
            () => provider.CompleteAsync(OpenCodeRequest(), CancellationToken.None));

        Assert.Equal(AiProviderErrorClass.RateLimited, ex.ErrorClass);
        Assert.Equal(TimeSpan.FromSeconds(9), ex.RetryAfter);
        Assert.DoesNotContain(FakeGatewayKey, ex.Message);
        Assert.NotNull(ex.ProviderError!.Message);
        Assert.DoesNotContain(FakeGatewayKey, ex.ProviderError.Message);
        Assert.Contains("***REDACTED***", ex.ProviderError.Message);
    }

    [Fact]
    public async Task OpenCodeRow_DoesNotTakeAPlatformGatePermit()
    {
        var gate = await SaturatedGateAsync();
        var provider = NewRowsProvider(
            new StubHandler(_ => Task.FromResult(JsonResponse(HttpStatusCode.OK, OpenAiTextBody))),
            gate: gate);

        // Every shared permit is held by other platform-key calls: a slow OpenCode call must neither wait
        // behind them nor hold one, so it cannot stall Claude/Writing traffic.
        var completion = await provider
            .CompleteAsync(OpenCodeRequest(), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("hello", completion.Text);
    }

    [Fact]
    public async Task OpenCodeRow_FifthConcurrentCall_FailsFastWithoutBeingSent_AndTheLaneRecovers()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = 0;
        var provider = NewRowsProvider(new StubHandler(async _ =>
        {
            Interlocked.Increment(ref sent);
            await release.Task;
            return JsonResponse(HttpStatusCode.OK, OpenAiTextBody);
        }));
        var inFlight = new List<Task<AiProviderCompletion>>();
        try
        {
            for (var i = 0; i < 4; i++)
                inFlight.Add(provider.CompleteAsync(OpenCodeRequest(), CancellationToken.None));
            await WaitUntilAsync(() => Volatile.Read(ref sent) == 4);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.CompleteAsync(OpenCodeRequest(), CancellationToken.None));

            Assert.Contains("concurrency limit", ex.Message);
            Assert.Equal(4, Volatile.Read(ref sent));   // the fifth never reached the network
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(inFlight.Select(task => task.ContinueWith(_ => { })));
        }

        Assert.All(inFlight, task => Assert.Equal("hello", task.Result.Text));
        // Every permit came back: a new call goes straight through.
        var again = await provider.CompleteAsync(OpenCodeRequest(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("hello", again.Text);
    }

    [Fact]
    public async Task OpenCodeRow_LaneIsReleasedWhenACallFails()
    {
        var calls = 0;
        var provider = NewRowsProvider(new StubHandler(_ => Task.FromResult(
            Interlocked.Increment(ref calls) <= 5
                ? JsonResponse(HttpStatusCode.ServiceUnavailable, """{"error":{"type":"api_error","message":"down"}}""")
                : JsonResponse(HttpStatusCode.OK, OpenAiTextBody))));

        // More failures than the lane has permits: a leaked permit would make the sixth call wait and fail.
        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<AiProviderHttpException>(
                () => provider.CompleteAsync(OpenCodeRequest(), CancellationToken.None));
        }

        var completion = await provider.CompleteAsync(OpenCodeRequest(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("hello", completion.Text);
    }

    // ── Default selection: explicit-only and marker rows are never picked implicitly ────────────

    [Fact]
    public async Task CompleteAsync_WithoutAProviderCode_NeverPicksAnExplicitOnlyOrKeylessSidecarRow()
    {
        Uri? seen = null;
        var sidecar = GenericRow(WritingSubscriptionProviderDefaults.CodexCode, 2);
        sidecar.EncryptedApiKey = WritingSubscriptionProviderDefaults.MarkerKey;
        sidecar.BaseUrl = WritingSubscriptionProviderDefaults.CodexBaseUrl;
        var provider = NewRowsProvider(
            new StubHandler(req =>
            {
                seen = req.RequestUri;
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, OpenAiTextBody));
            }),
            new[] { OpenCodeRow(), sidecar, GenericRow("openai-platform", 10) });

        await provider.CompleteAsync(new AiProviderRequest { Model = "glm-5", SystemPrompt = "s", UserPrompt = "u" }, CancellationToken.None);

        Assert.Equal("https://example.test/v1/chat/completions", seen!.ToString());
    }

    [Fact]
    public async Task CompleteAsync_WithoutAProviderCode_AndOnlyIneligibleRowsActive_Throws_WithoutCallingAnyone()
    {
        var called = false;
        var sidecar = GenericRow(WritingSubscriptionProviderDefaults.CodexCode, 2);
        sidecar.EncryptedApiKey = WritingSubscriptionProviderDefaults.MarkerKey;
        var provider = NewRowsProvider(
            new StubHandler(_ =>
            {
                called = true;
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, OpenAiTextBody));
            }),
            new[] { OpenCodeRow(), sidecar });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(
            new AiProviderRequest { Model = "glm-5", SystemPrompt = "s", UserPrompt = "u" }, CancellationToken.None));

        Assert.Contains("No active OpenAI-compatible AI provider", ex.Message);
        Assert.False(called);
    }

    [Fact]
    public async Task CompleteAsync_ExplicitOpenCodeCode_SelectsTheRow_AndAnInactiveRowIsRefused()
    {
        var called = false;
        var active = NewRowsProvider(new StubHandler(_ => Task.FromResult(JsonResponse(HttpStatusCode.OK, OpenAiTextBody))));
        Assert.Equal("hello", (await active.CompleteAsync(OpenCodeRequest(), CancellationToken.None)).Text);

        var inactive = NewRowsProvider(
            new StubHandler(_ =>
            {
                called = true;
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, OpenAiTextBody));
            }),
            new[] { OpenCodeRow(active: false) });
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => inactive.CompleteAsync(OpenCodeRequest(), CancellationToken.None));

        Assert.Contains("not active", ex.Message);
        Assert.False(called);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The condition was not met within 10 seconds.");
            await Task.Delay(10);
        }
    }

    private sealed class FixedRegistry(IReadOnlyList<AiProvider> rows) : IAiProviderRegistry
    {
        public Task<AiProvider?> FindByCodeAsync(string code, CancellationToken ct)
            => Task.FromResult(rows.FirstOrDefault(row =>
                row.IsActive && string.Equals(row.Code, code, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<AiProvider>> ListActiveAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AiProvider>>(rows
                .Where(row => row.IsActive)
                .OrderBy(row => row.FailoverPriority)
                .ToList());

        public Task<IReadOnlyList<AiProvider>> ListByCategoryAsync(AiProviderCategory category, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AiProvider>>(rows
                .Where(row => row.IsActive && row.Category == category)
                .OrderBy(row => row.FailoverPriority)
                .ToList());

        public Task<string?> GetPlatformKeyAsync(string providerCode, CancellationToken ct)
            => Task.FromResult<string?>(FakeGatewayKey);
    }

    private sealed class CapturingHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient? LastClient { get; private set; }

        public HttpClient CreateClient(string name)
        {
            LastClient = new HttpClient(handler, disposeHandler: false);
            return LastClient;
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
