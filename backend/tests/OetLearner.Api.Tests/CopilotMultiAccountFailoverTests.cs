using System.Net;
using System.Text;
using Azure.AI.Inference;
using Azure.Core.Pipeline;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

/// <summary>
/// Copilot multi-account failover behaviour. Tests the loop in
/// <see cref="CopilotAiModelProvider.TryCompleteWithAccountFailoverAsync"/>:
/// 429 quarantines the slot then retries against the next; 401 deactivates;
/// running out of accounts surfaces a descriptive InvalidOperationException
/// whose message includes the failover trail so the gateway can render
/// PolicyTrace.
/// </summary>
public sealed class CopilotMultiAccountFailoverTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;
    private readonly EphemeralDataProtectionProvider _dpProvider = new();
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-05-08T12:00:00Z"));

    public CopilotMultiAccountFailoverTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task FailoverFrom429_TriesNextAccount_ReturnsHappyCompletion()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db);
        var idA = await SeedAccountAsync(db, "primary", "pat-A", priority: 0);
        var idB = await SeedAccountAsync(db, "backup", "pat-B", priority: 1);

        var keysSeen = new List<string>();
        var responses = new Queue<HttpStatusCode>(new[] { HttpStatusCode.TooManyRequests, HttpStatusCode.OK });
        var stub = new StubHandler(req =>
        {
            var auth = req.Headers.Authorization?.ToString()
                ?? (req.Headers.TryGetValues("api-key", out var ak) ? string.Join(",", ak) : null) ?? "";
            keysSeen.Add(ExtractKey(auth));
            var status = responses.Dequeue();
            return Task.FromResult(BuildResponse(status));
        });

        var provider = NewProvider(db, stub);
        var completion = await provider.CompleteAsync(new AiProviderRequest
        {
            Model = "openai/gpt-4o-mini",
            SystemPrompt = "s",
            UserPrompt = "u",
        }, default);

        Assert.Contains("ok", completion.Text);
        Assert.Equal(2, keysSeen.Count);
        Assert.Equal("pat-A", keysSeen[0]);
        Assert.Equal("pat-B", keysSeen[1]);

        // Phase 3: completion exposes the winning account + failover trail.
        Assert.Equal(idB, completion.AccountId);
        Assert.NotNull(completion.FailoverTrace);
        Assert.Contains("primary:429", completion.FailoverTrace);
        Assert.Contains("backup:success", completion.FailoverTrace!);

        var rateLimited = await db.AiProviderAccounts.AsNoTracking().FirstAsync(a => a.Id == idA);
        Assert.NotNull(rateLimited.ExhaustedUntil);
        Assert.True(rateLimited.IsActive); // 429 does NOT deactivate
        var serving = await db.AiProviderAccounts.AsNoTracking().FirstAsync(a => a.Id == idB);
        Assert.True(serving.IsActive);
    }

    [Fact]
    public async Task FailoverFrom401_DeactivatesAccount_ContinuesOnNext()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db);
        var idA = await SeedAccountAsync(db, "primary", "pat-A", priority: 0);
        await SeedAccountAsync(db, "backup", "pat-B", priority: 1);

        var responses = new Queue<HttpStatusCode>(new[] { HttpStatusCode.Unauthorized, HttpStatusCode.OK });
        var stub = new StubHandler(_ => Task.FromResult(BuildResponse(responses.Dequeue())));

        var provider = NewProvider(db, stub);
        var completion = await provider.CompleteAsync(new AiProviderRequest
        {
            Model = "openai/gpt-4o-mini",
            SystemPrompt = "s",
            UserPrompt = "u",
        }, default);

        Assert.Contains("ok", completion.Text);
        var deactivated = await db.AiProviderAccounts.AsNoTracking().FirstAsync(a => a.Id == idA);
        Assert.False(deactivated.IsActive);
    }

    [Fact]
    public async Task PoolExhausted_ThrowsWithFailoverTrailInMessage()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db);
        await SeedAccountAsync(db, "primary", "pat-A", priority: 0);
        await SeedAccountAsync(db, "backup", "pat-B", priority: 1);

        var stub = new StubHandler(_ => Task.FromResult(BuildResponse(HttpStatusCode.TooManyRequests)));
        var provider = NewProvider(db, stub);

        var ex = await Assert.ThrowsAsync<AiProviderFailoverException>(() =>
            provider.CompleteAsync(new AiProviderRequest
            {
                Model = "openai/gpt-4o-mini",
                SystemPrompt = "s",
                UserPrompt = "u",
            }, default));

        Assert.Contains("primary", ex.Message);
        Assert.Contains("backup", ex.Message);
        Assert.Contains("429", ex.Message);

        // Phase 3: typed exception exposes structured trail + last account.
        Assert.NotNull(ex.FailoverTrace);
        Assert.Contains("primary:429", ex.FailoverTrace);
        Assert.Contains("backup:429", ex.FailoverTrace);
        Assert.NotNull(ex.LastAccountId);
    }

    [Fact]
    public async Task SingleAccountSuccess_HasNoFailoverTrace()
    {
        // Phase 3 invariant: the trace is only set when more than one
        // account was attempted. Single-shot wins must not pollute the
        // analytics with empty traces.
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db);
        var idA = await SeedAccountAsync(db, "primary", "pat-A", priority: 0);

        var stub = new StubHandler(_ => Task.FromResult(BuildResponse(HttpStatusCode.OK)));
        var provider = NewProvider(db, stub);

        var completion = await provider.CompleteAsync(new AiProviderRequest
        {
            Model = "openai/gpt-4o-mini",
            SystemPrompt = "s",
            UserPrompt = "u",
        }, default);

        Assert.Equal(idA, completion.AccountId);
        Assert.Null(completion.FailoverTrace);
    }

    [Fact]
    public async Task EmptyPool_FallsBackToSingleRowAiProviderRow()
    {
        // No AiProviderAccount rows at all → the failover loop returns null
        // and the provider falls back to AiProvider.EncryptedApiKey. This
        // preserves existing single-PAT setups.
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db, withSingleRowKey: "single-row-pat");

        string? keySeen = null;
        var stub = new StubHandler(req =>
        {
            var auth = req.Headers.Authorization?.ToString()
                ?? (req.Headers.TryGetValues("api-key", out var ak) ? string.Join(",", ak) : null) ?? "";
            keySeen = ExtractKey(auth);
            return Task.FromResult(BuildResponse(HttpStatusCode.OK));
        });

        var provider = NewProvider(db, stub);
        var completion = await provider.CompleteAsync(new AiProviderRequest
        {
            Model = "openai/gpt-4o-mini",
            SystemPrompt = "s",
            UserPrompt = "u",
        }, default);

        Assert.Contains("ok", completion.Text);
        Assert.Equal("single-row-pat", keySeen);
    }

    // ── Helpers ────────────────────────────────────────────────────

    private CopilotAiModelProvider NewProvider(LearnerDbContext db, StubHandler stub)
    {
        var registry = new AiProviderRegistry(db, _dpProvider);
        var accountRegistry = new AiProviderAccountRegistry(db, _dpProvider, _clock);
        var transport = new HttpClientTransport(new HttpClient(stub));
        var clientOptions = new AzureAIInferenceClientOptions { Transport = transport };
        clientOptions.Retry.MaxRetries = 0;
        return new CopilotAiModelProvider(registry, clientOptions, accountRegistry);
    }

    private async Task SeedProviderAsync(LearnerDbContext db, string? withSingleRowKey = null)
    {
        if (await db.AiProviders.AnyAsync(p => p.Code == "copilot")) return;
        var protector = _dpProvider.CreateProtector("AiProvider.PlatformKey.v1");
        db.AiProviders.Add(new AiProvider
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = "copilot",
            Name = "GitHub Copilot",
            Dialect = AiProviderDialect.Copilot,
            BaseUrl = "https://models.github.ai/inference",
            EncryptedApiKey = withSingleRowKey is null ? string.Empty : protector.Protect(withSingleRowKey),
            ApiKeyHint = string.Empty,
            DefaultModel = "openai/gpt-4o-mini",
            IsActive = true,
            CreatedAt = _clock.GetUtcNow(),
            UpdatedAt = _clock.GetUtcNow(),
        });
        await db.SaveChangesAsync();
    }

    private async Task<string> SeedAccountAsync(LearnerDbContext db, string label, string pat, int priority)
    {
        var providerId = await db.AiProviders.AsNoTracking()
            .Where(p => p.Code == "copilot").Select(p => p.Id).FirstAsync();
        var protector = _dpProvider.CreateProtector("AiProvider.PlatformKey.v1");
        var acc = new AiProviderAccount
        {
            Id = Guid.NewGuid().ToString("N"),
            ProviderId = providerId,
            Label = label,
            EncryptedApiKey = protector.Protect(pat),
            ApiKeyHint = "…" + pat[^Math.Min(4, pat.Length)..],
            MonthlyRequestCap = 100,
            RequestsUsedThisMonth = 0,
            Priority = priority,
            IsActive = true,
            PeriodMonthKey = "2026-05",
            CreatedAt = _clock.GetUtcNow(),
            UpdatedAt = _clock.GetUtcNow(),
        };
        db.AiProviderAccounts.Add(acc);
        await db.SaveChangesAsync();
        return acc.Id;
    }

    private static HttpResponseMessage BuildResponse(HttpStatusCode status)
    {
        if (status == HttpStatusCode.OK)
        {
            var ok = """
                {"choices":[{"message":{"role":"assistant","content":"ok"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ok, Encoding.UTF8, "application/json"),
            };
        }
        var body = $"{{\"error\":{{\"code\":\"err\",\"message\":\"status {(int)status}\"}}}}";
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private static string ExtractKey(string auth)
    {
        // Authorization values from AzureKeyCredential look like "Bearer <pat>" on
        // OpenAI-compatible routes; on inference endpoints the SDK may instead
        // emit an "api-key" header. ExtractKey strips the scheme prefix.
        if (string.IsNullOrEmpty(auth)) return "";
        var space = auth.LastIndexOf(' ');
        return space < 0 ? auth : auth[(space + 1)..];
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => await responder(request);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
