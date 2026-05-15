using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// RW-019 — admin "manage provider secrets" surface must never expose the
/// raw or encrypted API key in any response, audit row, or persisted
/// connection-test error message. The admin UI is allowed to show a
/// 4-character hint (<c>…abcd</c>) and the masked status; everything else
/// is server-side only.
///
/// These tests round-trip a synthetic key through the public admin
/// endpoints + database to prove the contract holds end-to-end.
/// </summary>
[Collection("AuthFlows")]
public sealed class AdminAiProviderSecretsTests
{
    private const string SyntheticProviderKey = "github_pat_AAAAAAAAAAAAAAAAAA_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string SyntheticAccountKey = "github_pat_CCCCCCCCCCCCCCCCCC_DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD";

    [Fact]
    public async Task PostProvider_DoesNotReturnRawOrEncryptedKey()
    {
        using var env = DevAuthEnv.Enable();
        using var factory = new TestWebApplicationFactory();
        using var client = CreateAiConfigAdminClient(factory);

        var response = await client.PostAsJsonAsync("/v1/admin/ai/providers", BuildProviderUpsert(SyntheticProviderKey));
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        AssertNoSecretLeak(body, SyntheticProviderKey);

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("apiKeyHint", out var hint));
        // Hint must be exactly "…" + last 4 chars of the key — anything
        // longer would mean we are echoing more of the secret than the
        // contract permits.
        Assert.Equal("…" + SyntheticProviderKey[^4..], hint.GetString());
    }

    [Fact]
    public async Task GetProviders_DoesNotIncludeRawOrEncryptedKey()
    {
        using var env = DevAuthEnv.Enable();
        using var factory = new TestWebApplicationFactory();
        using var client = CreateAiConfigAdminClient(factory);

        var create = await client.PostAsJsonAsync("/v1/admin/ai/providers", BuildProviderUpsert(SyntheticProviderKey));
        create.EnsureSuccessStatusCode();

        var list = await client.GetAsync("/v1/admin/ai/providers");
        list.EnsureSuccessStatusCode();
        var body = await list.Content.ReadAsStringAsync();

        AssertNoSecretLeak(body, SyntheticProviderKey);
        using var doc = JsonDocument.Parse(body);
        var found = false;
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            if (!row.TryGetProperty("code", out var code)) continue;
            if (!string.Equals(code.GetString(), "secret-leak-probe", StringComparison.Ordinal)) continue;
            found = true;
            Assert.False(HasProperty(row, "encryptedApiKey"));
            Assert.False(HasProperty(row, "apiKey"));
            Assert.True(row.TryGetProperty("apiKeyHint", out var hint));
            Assert.Equal("…" + SyntheticProviderKey[^4..], hint.GetString());
        }
        Assert.True(found, "Newly created provider was not present in /v1/admin/ai/providers list response.");
    }

    [Fact]
    public async Task PostProvider_PersistsEncryptedKeyOnly_AndAuditDoesNotLeakIt()
    {
        using var env = DevAuthEnv.Enable();
        using var factory = new TestWebApplicationFactory();
        using var client = CreateAiConfigAdminClient(factory);

        var response = await client.PostAsJsonAsync("/v1/admin/ai/providers", BuildProviderUpsert(SyntheticProviderKey));
        response.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var row = await db.AiProviders.AsNoTracking()
            .FirstAsync(p => p.Code == "secret-leak-probe");
        Assert.False(string.IsNullOrEmpty(row.EncryptedApiKey));
        // Persisted column must be the protected blob, never the raw key.
        Assert.NotEqual(SyntheticProviderKey, row.EncryptedApiKey);
        Assert.DoesNotContain(SyntheticProviderKey, row.EncryptedApiKey, StringComparison.Ordinal);

        // Hint is the only thing the admin UI is allowed to see.
        Assert.Equal("…" + SyntheticProviderKey[^4..], row.ApiKeyHint);

        // Audit detail line must not echo the secret either.
        var audit = await db.AuditEvents.AsNoTracking()
            .Where(a => a.Action == "AiProviderCreated" && a.ResourceId == row.Id)
            .OrderByDescending(a => a.OccurredAt)
            .FirstAsync();
        Assert.False(string.IsNullOrEmpty(audit.Details));
        Assert.DoesNotContain(SyntheticProviderKey, audit.Details ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(row.EncryptedApiKey, audit.Details ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PutProvider_RotatedKey_DoesNotLeakInResponseOrPersistence()
    {
        using var env = DevAuthEnv.Enable();
        using var factory = new TestWebApplicationFactory();
        using var client = CreateAiConfigAdminClient(factory);

        var create = await client.PostAsJsonAsync("/v1/admin/ai/providers", BuildProviderUpsert(SyntheticProviderKey));
        create.EnsureSuccessStatusCode();
        using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var providerId = createDoc.RootElement.GetProperty("id").GetString()!;

        const string rotatedKey = "ghp_ROTATEDROTATEDROTATEDROTATEDROTATED";
        var update = await client.PutAsJsonAsync(
            $"/v1/admin/ai/providers/{providerId}",
            BuildProviderUpsert(rotatedKey));
        update.EnsureSuccessStatusCode();
        var updateBody = await update.Content.ReadAsStringAsync();

        AssertNoSecretLeak(updateBody, rotatedKey);
        AssertNoSecretLeak(updateBody, SyntheticProviderKey);
        using var updateDoc = JsonDocument.Parse(updateBody);
        Assert.Equal("…" + rotatedKey[^4..], updateDoc.RootElement.GetProperty("apiKeyHint").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var row = await db.AiProviders.AsNoTracking().FirstAsync(p => p.Id == providerId);
        Assert.NotEqual(rotatedKey, row.EncryptedApiKey);
        Assert.DoesNotContain(rotatedKey, row.EncryptedApiKey, StringComparison.Ordinal);
        Assert.DoesNotContain(SyntheticProviderKey, row.EncryptedApiKey, StringComparison.Ordinal);
        Assert.Equal("…" + rotatedKey[^4..], row.ApiKeyHint);
    }

    [Fact]
    public async Task PutProvider_WithShortApiKey_ReturnsBadRequest()
    {
        using var env = DevAuthEnv.Enable();
        using var factory = new TestWebApplicationFactory();
        using var client = CreateAiConfigAdminClient(factory);

        var create = await client.PostAsJsonAsync("/v1/admin/ai/providers", BuildProviderUpsert(SyntheticProviderKey));
        create.EnsureSuccessStatusCode();
        using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var providerId = createDoc.RootElement.GetProperty("id").GetString()!;

        var update = await client.PutAsJsonAsync(
            $"/v1/admin/ai/providers/{providerId}",
            BuildProviderUpsert("short"));

        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        Assert.Contains("ApiKey", await update.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostProviderAccount_DoesNotReturnRawOrEncryptedKey_AndPersistsEncryptedOnly()
    {
        using var env = DevAuthEnv.Enable();
        using var factory = new TestWebApplicationFactory();
        using var client = CreateAiConfigAdminClient(factory);

        var providerResponse = await client.PostAsJsonAsync("/v1/admin/ai/providers", BuildProviderUpsert(SyntheticProviderKey));
        providerResponse.EnsureSuccessStatusCode();
        using var providerJson = JsonDocument.Parse(await providerResponse.Content.ReadAsStringAsync());
        var providerId = providerJson.RootElement.GetProperty("id").GetString()!;

        var accountResponse = await client.PostAsJsonAsync(
            $"/v1/admin/ai/providers/{providerId}/accounts",
            new
            {
                label = "secret-leak-probe-account",
                apiKey = SyntheticAccountKey,
                monthlyRequestCap = (int?)null,
                priority = 0,
                isActive = true,
            });
        accountResponse.EnsureSuccessStatusCode();
        var accountBody = await accountResponse.Content.ReadAsStringAsync();
        AssertNoSecretLeak(accountBody, SyntheticAccountKey);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var account = await db.AiProviderAccounts.AsNoTracking()
            .FirstAsync(a => a.ProviderId == providerId && a.Label == "secret-leak-probe-account");

        Assert.False(string.IsNullOrEmpty(account.EncryptedApiKey));
        Assert.NotEqual(SyntheticAccountKey, account.EncryptedApiKey);
        Assert.DoesNotContain(SyntheticAccountKey, account.EncryptedApiKey, StringComparison.Ordinal);
        Assert.Equal("…" + SyntheticAccountKey[^4..], account.ApiKeyHint);

        // List response must also stay clean.
        var list = await client.GetAsync($"/v1/admin/ai/providers/{providerId}/accounts");
        list.EnsureSuccessStatusCode();
        AssertNoSecretLeak(await list.Content.ReadAsStringAsync(), SyntheticAccountKey);
    }

    [Fact]
    public async Task PutProviderAccount_RotatedKey_DoesNotLeakInResponseOrPersistence()
    {
        using var env = DevAuthEnv.Enable();
        using var factory = new TestWebApplicationFactory();
        using var client = CreateAiConfigAdminClient(factory);

        var providerResponse = await client.PostAsJsonAsync("/v1/admin/ai/providers", BuildProviderUpsert(SyntheticProviderKey));
        providerResponse.EnsureSuccessStatusCode();
        using var providerJson = JsonDocument.Parse(await providerResponse.Content.ReadAsStringAsync());
        var providerId = providerJson.RootElement.GetProperty("id").GetString()!;

        var createAccount = await client.PostAsJsonAsync(
            $"/v1/admin/ai/providers/{providerId}/accounts",
            new { label = "rot-account", apiKey = SyntheticAccountKey, monthlyRequestCap = (int?)null, priority = 0, isActive = true });
        createAccount.EnsureSuccessStatusCode();
        using var accountJson = JsonDocument.Parse(await createAccount.Content.ReadAsStringAsync());
        var accountId = accountJson.RootElement.GetProperty("id").GetString()!;

        const string rotatedAccountKey = "github_pat_EEEEEEEEEEEEEEEEEE_FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF";
        var update = await client.PutAsJsonAsync(
            $"/v1/admin/ai/providers/{providerId}/accounts/{accountId}",
            new { label = "rot-account", apiKey = rotatedAccountKey, monthlyRequestCap = (int?)null, priority = 0, isActive = true });
        update.EnsureSuccessStatusCode();
        var updateBody = await update.Content.ReadAsStringAsync();
        AssertNoSecretLeak(updateBody, rotatedAccountKey);
        AssertNoSecretLeak(updateBody, SyntheticAccountKey);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var account = await db.AiProviderAccounts.AsNoTracking().FirstAsync(a => a.Id == accountId);
        Assert.NotEqual(rotatedAccountKey, account.EncryptedApiKey);
        Assert.DoesNotContain(rotatedAccountKey, account.EncryptedApiKey, StringComparison.Ordinal);
        Assert.DoesNotContain(SyntheticAccountKey, account.EncryptedApiKey, StringComparison.Ordinal);
        Assert.Equal("…" + rotatedAccountKey[^4..], account.ApiKeyHint);
    }

    // ── Helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Captures the previous value of <c>Auth__UseDevelopmentAuth</c>,
    /// flips it on, and restores it on dispose. This mirrors the
    /// save/restore pattern used by AdminFlowsTests so the env var
    /// cannot leak into other test collections that expect production
    /// auth behavior.
    /// </summary>
    private sealed class DevAuthEnv : IDisposable
    {
        private const string Key = "Auth__UseDevelopmentAuth";
        private readonly string? _previous;
        private DevAuthEnv()
        {
            _previous = Environment.GetEnvironmentVariable(Key);
            Environment.SetEnvironmentVariable(Key, "true");
        }
        public static DevAuthEnv Enable() => new();
        public void Dispose() => Environment.SetEnvironmentVariable(Key, _previous);
    }

    private static HttpClient CreateAiConfigAdminClient(TestWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
        client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", AdminPermissions.AiConfig);
        client.DefaultRequestHeaders.Add("X-Debug-UserId", $"admin-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add("X-Debug-Email", "secret-leak-probe@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", "Secret Leak Probe");
        return client;
    }

    private static object BuildProviderUpsert(string apiKey) => new
    {
        code = "secret-leak-probe",
        name = "Secret Leak Probe Provider",
        dialect = AiProviderDialect.Copilot,
        category = AiProviderCategory.TextChat,
        baseUrl = "https://example.test/inference",
        apiKey,
        defaultModel = "openai/gpt-4o-mini",
        reasoningEffort = (string?)null,
        allowedModelsCsv = "openai/gpt-4o-mini",
        pricePer1kPromptTokens = 0m,
        pricePer1kCompletionTokens = 0m,
        retryCount = 0,
        circuitBreakerThreshold = 0,
        circuitBreakerWindowSeconds = 60,
        failoverPriority = 100,
        isActive = true,
    };

    private static void AssertNoSecretLeak(string payload, string secret)
    {
        Assert.DoesNotContain(secret, payload, StringComparison.Ordinal);
        Assert.DoesNotContain("\"encryptedApiKey\"", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"apiKey\"", payload, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasProperty(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out _);
}
