using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

/// <summary>
/// Focused unit tests for <see cref="CloudflareWorkersAiProvider"/>. Validates
/// the request shape, response parsing, error handling, and dialect-based
/// registry resolution. Uses an in-memory <see cref="HttpMessageHandler"/>
/// stub so no network call is made.
/// </summary>
public class CloudflareWorkersAiProviderTests
{
    private const string AccountId = "acct-test-1234";
    private static string BaseUrl => $"https://api.cloudflare.com/client/v4/accounts/{AccountId}/ai";

    [Fact]
    public async Task CompleteAsync_HappyPath_ReturnsTextAndUsageFromCloudflareResponse()
    {
        // ── Arrange ────────────────────────────────────────────────
        var capturedRequests = new List<(HttpMethod Method, string Url, string Body, string? AuthHeader)>();
        var stub = new StubHandler(async req =>
        {
            var body = req.Content is null ? "" : await req.Content.ReadAsStringAsync();
            capturedRequests.Add((req.Method, req.RequestUri!.ToString(), body, req.Headers.Authorization?.ToString()));
            var responseJson = """
                {
                  "result": {
                    "response": "Hello from CF Workers AI.",
                    "usage": { "prompt_tokens": 12, "completion_tokens": 7 }
                  },
                  "success": true,
                  "errors": [],
                  "messages": []
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        });

        var (provider, _) = await NewProviderAsync(stub);

        // ── Act ────────────────────────────────────────────────────
        var completion = await provider.CompleteAsync(new AiProviderRequest
        {
            Model = "@cf/meta/llama-3.1-8b-instruct",
            SystemPrompt = "system",
            UserPrompt = "user",
            Temperature = 0.5,
            MaxTokens = 256,
        }, CancellationToken.None);

        // ── Assert ─────────────────────────────────────────────────
        Assert.Equal("Hello from CF Workers AI.", completion.Text);
        Assert.NotNull(completion.Usage);
        Assert.Equal(12, completion.Usage!.PromptTokens);
        Assert.Equal(7, completion.Usage!.CompletionTokens);

        // The provider must POST to /run/{model} with messages + max_tokens.
        var captured = Assert.Single(capturedRequests);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.EndsWith("/run/@cf/meta/llama-3.1-8b-instruct", captured.Url);
        Assert.StartsWith("Bearer ", captured.AuthHeader);
        using var doc = JsonDocument.Parse(captured.Body);
        Assert.Equal(256, doc.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task CompleteAsync_NoModel_Throws()
    {
        var (provider, _) = await NewProviderAsync(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(new AiProviderRequest
        {
            Model = "",
            SystemPrompt = "s",
            UserPrompt = "u",
        }, CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_CloudflareSuccessFalse_Throws()
    {
        var stub = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"result":null,"success":false,"errors":[{"code":7000,"message":"No route for the URI"}],"messages":[]}""",
                Encoding.UTF8, "application/json"),
        }));
        var (provider, _) = await NewProviderAsync(stub);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(new AiProviderRequest
        {
            Model = "@cf/meta/llama-3.1-8b-instruct",
            SystemPrompt = "s",
            UserPrompt = "u",
        }, CancellationToken.None));
        Assert.Contains("success=false", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_NonOkStatus_Throws()
    {
        var stub = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":\"bad token\"}", Encoding.UTF8, "application/json"),
        }));
        var (provider, _) = await NewProviderAsync(stub);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(new AiProviderRequest
        {
            Model = "@cf/meta/llama-3.1-8b-instruct",
            SystemPrompt = "s",
            UserPrompt = "u",
        }, CancellationToken.None));
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_NoCloudflareRowRegistered_Throws()
    {
        // Build provider without seeding a Cloudflare-dialect row in the registry.
        var stub = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var (provider, _) = await NewProviderAsync(stub, seedCloudflareRow: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(new AiProviderRequest
        {
            Model = "@cf/meta/llama-3.1-8b-instruct",
            SystemPrompt = "s",
            UserPrompt = "u",
        }, CancellationToken.None));
    }

    // ── Helpers ────────────────────────────────────────────────────

    private static async Task<(CloudflareWorkersAiProvider provider, LearnerDbContext db)> NewProviderAsync(
        StubHandler handler, bool seedCloudflareRow = true)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"cf-{Guid.NewGuid():N}")
            .Options;
        var db = new LearnerDbContext(options);

        var dpProvider = new EphemeralDataProtectionProvider();
        var protector = dpProvider.CreateProtector("AiProvider.PlatformKey.v1");

        if (seedCloudflareRow)
        {
            db.AiProviders.Add(new AiProvider
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = "cloudflare-workers-ai",
                Name = "Cloudflare Workers AI",
                Dialect = AiProviderDialect.Cloudflare,
                BaseUrl = BaseUrl,
                EncryptedApiKey = protector.Protect("test-cf-token-1234567890"),
                ApiKeyHint = "…1234",
                DefaultModel = "@cf/meta/llama-3.1-8b-instruct",
                IsActive = true,
                FailoverPriority = 100,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var registry = new AiProviderRegistry(db, dpProvider);
        var httpFactory = new StubHttpClientFactory(handler);
        var provider = new CloudflareWorkersAiProvider(httpFactory, registry);
        return (provider, db);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => await responder(request);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
