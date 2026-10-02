using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Settings;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// WAI-02 (owner directive 2026-10-02): the platform spend-cap switch on
/// <c>/v1/admin/ai/global-policy</c>, and the Claude Max weekly estimate on
/// <c>/v1/admin/ai/writing-provider</c> being information only.
/// </summary>
[Collection("AuthFlows")]
public sealed class AiUsageAdminEndpointsTests
{
    [Fact]
    public async Task GlobalPolicy_EnforceSpendCaps_IsOffOnAFreshDatabase_AndRoundTrips()
    {
        using var env = DevAuthEnv.Enable();
        using var factory = new TestWebApplicationFactory();
        using var client = CreateAiConfigAdminClient(factory);

        var fresh = await client.GetFromJsonAsync<JsonElement>("/v1/admin/ai/global-policy");
        Assert.False(fresh.GetProperty("enforceSpendCaps").GetBoolean());

        var on = await client.PutAsJsonAsync("/v1/admin/ai/global-policy", PolicyBody(enforceSpendCaps: true));
        on.EnsureSuccessStatusCode();
        Assert.True((await on.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("enforceSpendCaps").GetBoolean());
        Assert.True(await StoredSwitchAsync(factory));
        // The quota service's 15 s policy cache is dropped on save: the very next read sees it.
        var reread = await client.GetFromJsonAsync<JsonElement>("/v1/admin/ai/global-policy");
        Assert.True(reread.GetProperty("enforceSpendCaps").GetBoolean());

        // A client that predates the switch (field absent) must not flip it.
        var legacy = await client.PutAsJsonAsync("/v1/admin/ai/global-policy", PolicyBody(enforceSpendCaps: null));
        legacy.EnsureSuccessStatusCode();
        Assert.True(await StoredSwitchAsync(factory));

        var off = await client.PutAsJsonAsync("/v1/admin/ai/global-policy", PolicyBody(enforceSpendCaps: false));
        off.EnsureSuccessStatusCode();
        Assert.False(await StoredSwitchAsync(factory));
    }

    [Fact]
    public async Task KillSwitch_TakesEffectAtOnce_AndScopeTravelsAsItsName()
    {
        using var env = DevAuthEnv.Enable();
        using var factory = new TestWebApplicationFactory();
        using var client = CreateAiConfigAdminClient(factory);

        var before = await client.GetFromJsonAsync<JsonElement>("/v1/admin/ai/global-policy");
        Assert.False(before.GetProperty("killSwitchEnabled").GetBoolean());
        Assert.Equal("PlatformKeysOnly", before.GetProperty("killSwitchScope").GetString());

        // The admin page sends the enum by name.
        var engage = await client.PostAsJsonAsync("/v1/admin/ai/kill-switch",
            new { enabled = true, scope = "AllCalls", reason = "drill" });
        engage.EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<JsonElement>("/v1/admin/ai/global-policy");
        Assert.True(after.GetProperty("killSwitchEnabled").GetBoolean());
        Assert.Equal("AllCalls", after.GetProperty("killSwitchScope").GetString());

        using var services = factory.Services.CreateScope();
        var decision = await services.ServiceProvider.GetRequiredService<IAiQuotaService>()
            .TryReserveAsync(null, AiFeatureCodes.WritingGrade, AiKeySource.Platform, default);
        Assert.False(decision.Allowed);
        Assert.Equal("kill_switch", decision.ErrorCode);
    }

    [Fact]
    public async Task WritingProvider_FullWeekAndQuotaMarker_NeverReportFailover()
    {
        using var env = DevAuthEnv.Enable();
        using var baseFactory = new TestWebApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IWritingSubscriptionQuotaService>();
            services.AddSingleton<IWritingSubscriptionQuotaService>(new FullWeekQuota());
        }));
        using var client = CreateAiConfigAdminClient(factory);
        var markerUntil = DateTimeOffset.UtcNow.AddDays(2);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.RuntimeSettings.Add(new RuntimeSettingsRow
            {
                Id = "default",
                WritingAiClaudeQuotaExceededUntil = markerUntil,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<IRuntimeSettingsProvider>().Invalidate();
        }

        var status = await client.GetFromJsonAsync<JsonElement>("/v1/admin/ai/writing-provider");

        Assert.Equal(100d, status.GetProperty("quota").GetProperty("utilizationPct").GetDouble());
        Assert.Equal(JsonValueKind.String, status.GetProperty("quotaExceededUntil").ValueKind);
        Assert.False(status.GetProperty("failoverActive").GetBoolean());
        Assert.Equal(
            WritingSubscriptionProviders.Claude,
            status.GetProperty("currentPrimary").GetProperty("provider").GetString());
    }

    [Fact]
    public async Task WritingProvider_Put_RefusesCodexMode_AndAcceptsAutoClaudeAndInertFields()
    {
        using var env = DevAuthEnv.Enable();
        using var factory = new TestWebApplicationFactory();
        using var client = CreateAiConfigAdminClient(factory);

        foreach (var refused in new[] { "codex", "anthropic" })
        {
            var response = await client.PutAsJsonAsync("/v1/admin/ai/writing-provider", new { mode = refused });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("max_subscription_always_on", await ErrorCodeAsync(response));
        }

        foreach (var body in new object[] { new { mode = "auto" }, new { mode = "claude" }, new { warnPct = 80, failoverPct = 90, clearQuotaMarker = true } })
        {
            var response = await client.PutAsJsonAsync("/v1/admin/ai/writing-provider", body);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task MaxProviderRow_CannotBeDeactivatedOrDeleted_ButOtherFieldsStillSave()
    {
        using var env = DevAuthEnv.Enable();
        using var factory = new TestWebApplicationFactory();
        using var client = CreateAiConfigAdminClient(factory);
        const string id = "max-row-test";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.AiProviders.Add(new AiProvider
            {
                Id = id,
                Code = WritingSubscriptionProviders.Claude,
                Name = "Claude Max subscription",
                Dialect = AiProviderDialect.Anthropic,
                BaseUrl = "http://oet-writing-claude:8080",
                DefaultModel = "claude-opus-5-5",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var deactivate = await client.PutAsJsonAsync($"/v1/admin/ai/providers/{id}", ProviderBody(isActive: false, retryCount: 1));
        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);
        Assert.Equal("max_subscription_always_on", await ErrorCodeAsync(deactivate));

        var delete = await client.DeleteAsync($"/v1/admin/ai/providers/{id}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal("max_subscription_always_on", await ErrorCodeAsync(delete));

        var edit = await client.PutAsJsonAsync($"/v1/admin/ai/providers/{id}", ProviderBody(isActive: true, retryCount: 3));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        using var check = factory.Services.CreateScope();
        var row = await check.ServiceProvider.GetRequiredService<LearnerDbContext>()
            .AiProviders.AsNoTracking().SingleAsync(p => p.Id == id);
        Assert.True(row.IsActive);
        Assert.Equal(3, row.RetryCount);
    }

    private static object ProviderBody(bool isActive, int retryCount) => new
    {
        code = WritingSubscriptionProviders.Claude,
        name = "Claude Max subscription",
        dialect = AiProviderDialect.Anthropic,
        category = AiProviderCategory.TextChat,
        baseUrl = "",
        apiKey = (string?)null,
        defaultModel = "claude-opus-5-5",
        reasoningEffort = (string?)null,
        allowedModelsCsv = "",
        pricePer1kPromptTokens = 0m,
        pricePer1kCompletionTokens = 0m,
        retryCount,
        circuitBreakerThreshold = 5,
        circuitBreakerWindowSeconds = 30,
        failoverPriority = 1,
        isActive,
    };

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static Dictionary<string, object?> PolicyBody(bool? enforceSpendCaps)
    {
        var body = new Dictionary<string, object?>
        {
            ["killSwitchEnabled"] = false,
            ["killSwitchScope"] = (int)AiKillSwitchScope.PlatformKeysOnly,
            ["killSwitchReason"] = null,
            ["disabledFeaturesCsv"] = "",
            ["monthlyBudgetUsd"] = 10m,
            ["softWarnPct"] = 80,
            ["hardKillPct"] = 100,
            ["allowByokOnScoringFeatures"] = false,
            ["allowByokOnNonScoringFeatures"] = true,
            ["defaultPlatformProviderId"] = "digitalocean-serverless",
            ["byokErrorCooldownHours"] = 24,
            ["byokTransientRetryCount"] = 2,
            ["anomalyDetectionEnabled"] = true,
            ["anomalyMultiplierX"] = 10m,
        };
        if (enforceSpendCaps is { } enforce) body["enforceSpendCaps"] = enforce;
        return body;
    }

    private static async Task<bool> StoredSwitchAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        return (await db.AiGlobalPolicies.AsNoTracking().SingleAsync(p => p.Id == "global")).EnforceSpendCaps;
    }

    private static HttpClient CreateAiConfigAdminClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
        client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", AdminPermissions.AiConfig);
        client.DefaultRequestHeaders.Add("X-Debug-UserId", $"admin-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add("X-Debug-Email", "spend-cap-switch@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", "Spend Cap Switch Probe");
        return client;
    }

    private sealed class FullWeekQuota : IWritingSubscriptionQuotaService
    {
        public Task<WritingQuotaSnapshot> GetSnapshotAsync(CancellationToken ct)
            => Task.FromResult(new WritingQuotaSnapshot(100d, null, "reported", 0, 0, DateTimeOffset.UtcNow));
    }
}
