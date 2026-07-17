using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.AiTools;
using OetWithDrHesham.Api.Services.Rulebook;

namespace OetWithDrHesham.Api.Tests;

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
