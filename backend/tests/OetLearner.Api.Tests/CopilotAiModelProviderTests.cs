using System.Net;
using System.Text;
using System.Text.Json;
using Azure.AI.Inference;
using Azure.Core.Pipeline;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

/// <summary>
/// Focused unit tests for <see cref="CopilotAiModelProvider"/>. Validates
/// the GitHub Models request shape, response parsing, error handling, and
/// dialect-based registry resolution. Uses an in-memory
/// <see cref="HttpMessageHandler"/> stub injected via
/// <see cref="HttpClientTransport"/> on the SDK's
/// <see cref="ChatCompletionsClientOptions"/> so no network call is made.
/// </summary>
public class CopilotAiModelProviderTests
{
    private const string DefaultBaseUrl = "https://models.github.ai/inference";
    private const string TestPat = "github_pat_11ABCDEFG_thisisalongtokenstring";

    [Fact]
    public async Task CompleteAsync_HappyPath_ReturnsTextAndUsage()
    {
        var capturedRequests = new List<(string Method, string Url, string Body, string? AuthHeader)>();
        var stub = new StubHandler(async req =>
        {
            var body = req.Content is null ? "" : await req.Content.ReadAsStringAsync();
            string? auth = req.Headers.Authorization?.ToString();
            if (auth is null && req.Headers.TryGetValues("api-key", out var ak)) auth = string.Join(",", ak);
            capturedRequests.Add((req.Method.Method, req.RequestUri!.ToString(), body, auth));

            var responseJson = """
                {
                  "id": "chatcmpl-1",
                  "object": "chat.completion",
                  "choices": [
                    { "index": 0, "message": { "role": "assistant", "content": "Hello from Copilot." }, "finish_reason": "stop" }
                  ],
                  "usage": { "prompt_tokens": 18, "completion_tokens": 9, "total_tokens": 27 }
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        });

        var (provider, _) = await NewProviderAsync(stub);

        var completion = await provider.CompleteAsync(new AiProviderRequest
        {
            Model = "openai/gpt-4o-mini",
            SystemPrompt = "system",
            UserPrompt = "user",
            Temperature = 0.2,
            MaxTokens = 256,
        }, CancellationToken.None);

        Assert.Equal("Hello from Copilot.", completion.Text);
        Assert.NotNull(completion.Usage);
        Assert.Equal(18, completion.Usage!.PromptTokens);
        Assert.Equal(9, completion.Usage!.CompletionTokens);

        var captured = Assert.Single(capturedRequests);
        Assert.Equal("POST", captured.Method);
        // The Azure.AI.Inference SDK appends ?api-version=... to the URL,
        // so the path is /chat/completions but the full URL has a query string.
        Assert.Contains("/chat/completions", captured.Url);
        // The SDK injects the PAT through whichever auth header
        // AzureKeyCredential routes to (api-key on inference endpoints).
        // Assert the PAT made it onto the wire.
        Assert.True(
            (captured.AuthHeader ?? "").Contains(TestPat, StringComparison.Ordinal),
            $"Expected PAT in auth header, got: {captured.AuthHeader}");

        using var doc = JsonDocument.Parse(captured.Body);
        Assert.Equal("openai/gpt-4o-mini", doc.RootElement.GetProperty("model").GetString());
        Assert.Equal(256, doc.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task CompleteAsync_NoCopilotRowRegistered_Throws()
    {
        var stub = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var (provider, _) = await NewProviderAsync(stub, seedCopilotRow: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(new AiProviderRequest
        {
            Model = "openai/gpt-4o-mini",
            SystemPrompt = "s",
            UserPrompt = "u",
        }, CancellationToken.None));
        Assert.Contains("not registered", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteAsync_NonOkStatus_Throws()
    {
        var stub = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":{\"code\":\"unauthorized\",\"message\":\"Bad credentials\"}}", Encoding.UTF8, "application/json"),
        }));
        var (provider, _) = await NewProviderAsync(stub);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(new AiProviderRequest
        {
            Model = "openai/gpt-4o-mini",
            SystemPrompt = "s",
            UserPrompt = "u",
        }, CancellationToken.None));
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_OverrideBaseUrlAndApiKey_AreUsed()
    {
        // BYOK path: caller pins the key and base URL. The provider must
        // honor the override even if no registered row exists.
        string? capturedAuth = null;
        string? capturedHost = null;
        var stub = new StubHandler(req =>
        {
            capturedAuth = req.Headers.Authorization?.ToString()
                ?? (req.Headers.TryGetValues("api-key", out var ak) ? string.Join(",", ak) : null);
            capturedHost = req.RequestUri!.Host;
            var responseJson = """
                {"choices":[{"message":{"role":"assistant","content":"ok"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
        });
        var (provider, _) = await NewProviderAsync(stub, seedCopilotRow: false);

        await provider.CompleteAsync(new AiProviderRequest
        {
            Model = "openai/gpt-4o-mini",
            SystemPrompt = "s",
            UserPrompt = "u",
            ApiKeyOverride = "byok-pat-1234567890123456",
            BaseUrlOverride = "https://override.example.com/inference",
        }, CancellationToken.None);

        Assert.NotNull(capturedAuth);
        Assert.Contains("byok-pat-1234567890123456", capturedAuth);
        Assert.Equal("override.example.com", capturedHost);
    }

    [Fact]
    public async Task CompleteAsync_NoModelOnRequestOrRow_Throws()
    {
        var stub = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        // Seed a row with empty DefaultModel and pass an empty Model.
        var (provider, _) = await NewProviderAsync(stub, defaultModel: "");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(new AiProviderRequest
        {
            Model = "",
            SystemPrompt = "s",
            UserPrompt = "u",
        }, CancellationToken.None));
        Assert.Contains("model", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helpers ────────────────────────────────────────────────────

    private static async Task<(CopilotAiModelProvider provider, LearnerDbContext db)> NewProviderAsync(
        StubHandler handler,
        bool seedCopilotRow = true,
        string defaultModel = "openai/gpt-4o-mini")
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"copilot-{Guid.NewGuid():N}")
            .Options;
        var db = new LearnerDbContext(options);

        var dpProvider = new EphemeralDataProtectionProvider();
        var protector = dpProvider.CreateProtector("AiProvider.PlatformKey.v1");

        if (seedCopilotRow)
        {
            db.AiProviders.Add(new AiProvider
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = "copilot",
                Name = "GitHub Copilot / Models",
                Dialect = AiProviderDialect.Copilot,
                BaseUrl = DefaultBaseUrl,
                EncryptedApiKey = protector.Protect(TestPat),
                ApiKeyHint = "…" + TestPat[^4..],
                DefaultModel = defaultModel,
                IsActive = true,
                FailoverPriority = 100,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var registry = new AiProviderRegistry(db, dpProvider);

        // Inject the stub handler via the SDK's ChatCompletionsClientOptions.
        // HttpClientTransport adapts a System.Net.Http.HttpClient (and its
        // HttpMessageHandler) into the Azure.Core pipeline so the SDK's
        // request flows through our recorder/responder. Retry is disabled so
        // a single 4xx surfaces as RequestFailedException without retries.
        var transport = new HttpClientTransport(new HttpClient(handler));
        var clientOptions = new AzureAIInferenceClientOptions { Transport = transport };
        clientOptions.Retry.MaxRetries = 0;

        var provider = new CopilotAiModelProvider(registry, clientOptions);
        return (provider, db);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => await responder(request);
    }
}
