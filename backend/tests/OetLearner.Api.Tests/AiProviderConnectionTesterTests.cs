using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

/// <summary>
/// Phase 4 — connectivity probe classifier matrix and persistence.
/// </summary>
public sealed class AiProviderConnectionTesterTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;
    private readonly EphemeralDataProtectionProvider _dp = new();
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-05-09T12:00:00Z"));

    public AiProviderConnectionTesterTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Theory]
    [InlineData(HttpStatusCode.OK, AiProviderTestStatuses.Ok)]
    [InlineData(HttpStatusCode.Unauthorized, AiProviderTestStatuses.Auth)]
    [InlineData(HttpStatusCode.Forbidden, AiProviderTestStatuses.Auth)]
    [InlineData(HttpStatusCode.TooManyRequests, AiProviderTestStatuses.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, AiProviderTestStatuses.Network)]
    [InlineData(HttpStatusCode.InternalServerError, AiProviderTestStatuses.Network)]
    [InlineData((HttpStatusCode)418, AiProviderTestStatuses.Unknown)]
    public async Task ClassifierMatrix_MapsHttpStatusToVocabulary(HttpStatusCode http, string expected)
    {
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db, "secret-key-1234567890");
        var tester = NewTester(db, _ => Task.FromResult(BuildResponse(http)));

        var result = await tester.TestProviderAsync("copilot", default);

        Assert.Equal(expected, result.Status);
        Assert.True(result.LatencyMs >= 0);
        var persisted = await db.AiProviders.AsNoTracking().FirstAsync(p => p.Code == "copilot");
        Assert.Equal(expected, persisted.LastTestStatus);
        Assert.NotNull(persisted.LastTestedAt);
    }

    [Fact]
    public async Task NoApiKey_ReturnsAuthAndDoesNotCallNetwork()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db, apiKey: null);
        var called = false;
        var tester = NewTester(db, _ => { called = true; return Task.FromResult(BuildResponse(HttpStatusCode.OK)); });

        var result = await tester.TestProviderAsync("copilot", default);

        Assert.Equal(AiProviderTestStatuses.Auth, result.Status);
        Assert.False(called);
        Assert.Contains("No API key", result.ErrorMessage);
    }

    [Fact]
    public async Task NetworkException_ReturnsNetwork()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db, "secret-key-1234567890");
        var tester = NewTester(db, _ => Task.FromException<HttpResponseMessage>(
            new HttpRequestException("connection refused")));

        var result = await tester.TestProviderAsync("copilot", default);

        Assert.Equal(AiProviderTestStatuses.Network, result.Status);
        Assert.Contains("connection refused", result.ErrorMessage);
    }

    [Fact]
    public async Task TestAccount_PersistsOnAccountRowNotProviderRow()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db, "secret-provider-key12345");
        var providerId = await db.AiProviders.AsNoTracking()
            .Where(p => p.Code == "copilot").Select(p => p.Id).FirstAsync();
        var accountId = await SeedAccountAsync(db, providerId, "primary", "secret-account-key12345");
        var tester = NewTester(db, _ => Task.FromResult(BuildResponse(HttpStatusCode.Unauthorized)));

        var result = await tester.TestAccountAsync(providerId, accountId, default);

        Assert.Equal(AiProviderTestStatuses.Auth, result.Status);
        var account = await db.AiProviderAccounts.AsNoTracking().FirstAsync(a => a.Id == accountId);
        Assert.Equal(AiProviderTestStatuses.Auth, account.LastTestStatus);
        Assert.NotNull(account.LastTestedAt);
        // Provider row stays untouched.
        var provider = await db.AiProviders.AsNoTracking().FirstAsync(p => p.Id == providerId);
        Assert.Null(provider.LastTestStatus);
    }

    [Fact]
    public async Task ErrorMessage_TruncatedTo512Chars()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db, "secret-key-1234567890");
        var tester = NewTester(db, _ => Task.FromException<HttpResponseMessage>(
            new HttpRequestException(new string('x', 4096))));

        var result = await tester.TestProviderAsync("copilot", default);

        Assert.NotNull(result.ErrorMessage);
        Assert.True(result.ErrorMessage!.Length <= 512);
    }

    // ── RW-019: secret-redaction proofs ────────────────────────────
    // Providers commonly echo the offending Authorization header back
    // in error bodies. Anything we persist into LastTestError or hand
    // back to the admin UI must have the live secret + standard PAT/key
    // patterns scrubbed BEFORE it leaves the tester.

    [Fact]
    public async Task ProviderProbe_RedactsLiveApiKeyEchoedInErrorBody()
    {
        const string liveKey = "github_pat_AAAAAAAAAAAAAAAAAA_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db, liveKey);
        var tester = NewTester(db, _ => Task.FromResult(BuildJsonError(
            HttpStatusCode.Unauthorized,
            $"Invalid Authorization header value: Bearer {liveKey}")));

        var result = await tester.TestProviderAsync("copilot", default);

        Assert.Equal(AiProviderTestStatuses.Auth, result.Status);
        Assert.NotNull(result.ErrorMessage);
        Assert.DoesNotContain(liveKey, result.ErrorMessage);
        Assert.Contains("***REDACTED***", result.ErrorMessage);

        var persisted = await db.AiProviders.AsNoTracking().FirstAsync(p => p.Code == "copilot");
        Assert.NotNull(persisted.LastTestError);
        Assert.DoesNotContain(liveKey, persisted.LastTestError!);
    }

    [Fact]
    public async Task ProviderProbe_RedactsForeignPatPatternsEvenWithoutLiveKeyMatch()
    {
        // Provider's own key is something innocuous; provider error body
        // accidentally contains a different vendor's token. We still scrub
        // it because pattern-based redaction is the safer default.
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db, "harmless-key-1234567890");
        const string foreignToken = "sk-ant-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var tester = NewTester(db, _ => Task.FromResult(BuildJsonError(
            HttpStatusCode.Unauthorized,
            $"Upstream rejected token {foreignToken}; please rotate.")));

        var result = await tester.TestProviderAsync("copilot", default);

        Assert.NotNull(result.ErrorMessage);
        Assert.DoesNotContain(foreignToken, result.ErrorMessage);
        Assert.Contains("***REDACTED***", result.ErrorMessage);
    }

    [Fact]
    public async Task NetworkException_RedactsLiveApiKeyEchoedInExceptionMessage()
    {
        const string liveKey = "ghp_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db, liveKey);
        var tester = NewTester(db, _ => Task.FromException<HttpResponseMessage>(
            new HttpRequestException($"connection refused while sending Bearer {liveKey}")));

        var result = await tester.TestProviderAsync("copilot", default);

        Assert.Equal(AiProviderTestStatuses.Network, result.Status);
        Assert.NotNull(result.ErrorMessage);
        Assert.DoesNotContain(liveKey, result.ErrorMessage);

        var persisted = await db.AiProviders.AsNoTracking().FirstAsync(p => p.Code == "copilot");
        Assert.DoesNotContain(liveKey, persisted.LastTestError ?? string.Empty);
    }

    [Fact]
    public async Task AccountProbe_RedactsLiveAccountKeyEchoedInErrorBody()
    {
        const string accountKey = "github_pat_CCCCCCCCCCCCCCCCCC_DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD";
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db, "secret-provider-key12345");
        var providerId = await db.AiProviders.AsNoTracking()
            .Where(p => p.Code == "copilot").Select(p => p.Id).FirstAsync();
        var accountId = await SeedAccountAsync(db, providerId, "primary", accountKey);
        var tester = NewTester(db, _ => Task.FromResult(BuildJsonError(
            HttpStatusCode.Unauthorized,
            $"Bad credentials: {accountKey}")));

        var result = await tester.TestAccountAsync(providerId, accountId, default);

        Assert.Equal(AiProviderTestStatuses.Auth, result.Status);
        Assert.NotNull(result.ErrorMessage);
        Assert.DoesNotContain(accountKey, result.ErrorMessage);

        var account = await db.AiProviderAccounts.AsNoTracking().FirstAsync(a => a.Id == accountId);
        Assert.NotNull(account.LastTestError);
        Assert.DoesNotContain(accountKey, account.LastTestError!);
    }

    // ── Helpers ────────────────────────────────────────────────────

    private AiProviderConnectionTester NewTester(
        LearnerDbContext db,
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
    {
        var stub = new StubHandler(responder);
        var factory = new SingleClientFactory(new HttpClient(stub));
        return new AiProviderConnectionTester(db, _dp, factory, _clock,
            NullLogger<AiProviderConnectionTester>.Instance);
    }

    private async Task SeedProviderAsync(LearnerDbContext db, string? apiKey)
    {
        var protector = _dp.CreateProtector("AiProvider.PlatformKey.v1");
        db.AiProviders.Add(new AiProvider
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = "copilot",
            Name = "GitHub Copilot",
            Dialect = AiProviderDialect.Copilot,
            BaseUrl = "https://models.github.ai/inference",
            EncryptedApiKey = string.IsNullOrEmpty(apiKey) ? string.Empty : protector.Protect(apiKey),
            ApiKeyHint = string.Empty,
            DefaultModel = "openai/gpt-4o-mini",
            IsActive = true,
            CreatedAt = _clock.GetUtcNow(),
            UpdatedAt = _clock.GetUtcNow(),
        });
        await db.SaveChangesAsync();
    }

    private async Task<string> SeedAccountAsync(LearnerDbContext db, string providerId, string label, string pat)
    {
        var protector = _dp.CreateProtector("AiProvider.PlatformKey.v1");
        var acc = new AiProviderAccount
        {
            Id = Guid.NewGuid().ToString("N"),
            ProviderId = providerId,
            Label = label,
            EncryptedApiKey = protector.Protect(pat),
            ApiKeyHint = "…" + pat[^Math.Min(4, pat.Length)..],
            MonthlyRequestCap = 100,
            Priority = 0,
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
        var body = status == HttpStatusCode.OK
            ? """{"choices":[{"message":{"role":"assistant","content":"ok"}}]}"""
            : "{\"error\":{\"message\":\"status " + (int)status + "\"}}";
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private static HttpResponseMessage BuildJsonError(HttpStatusCode status, string message)
    {
        var json = JsonSerializer.Serialize(new { error = new { message } });
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
