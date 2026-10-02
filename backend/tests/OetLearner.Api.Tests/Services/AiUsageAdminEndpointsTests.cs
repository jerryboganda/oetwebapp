using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
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

        // A client that predates the switch (field absent) must not flip it.
        var legacy = await client.PutAsJsonAsync("/v1/admin/ai/global-policy", PolicyBody(enforceSpendCaps: null));
        legacy.EnsureSuccessStatusCode();
        Assert.True(await StoredSwitchAsync(factory));

        var off = await client.PutAsJsonAsync("/v1/admin/ai/global-policy", PolicyBody(enforceSpendCaps: false));
        off.EnsureSuccessStatusCode();
        Assert.False(await StoredSwitchAsync(factory));
    }

    [Fact]
    public async Task WritingProvider_FullWeeklyEstimate_DoesNotReportFailover()
    {
        using var env = DevAuthEnv.Enable();
        using var baseFactory = new TestWebApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IWritingSubscriptionQuotaService>();
            services.AddSingleton<IWritingSubscriptionQuotaService>(new FullWeekQuota());
        }));
        using var client = CreateAiConfigAdminClient(factory);

        var status = await client.GetFromJsonAsync<JsonElement>("/v1/admin/ai/writing-provider");

        Assert.Equal(100d, status.GetProperty("quota").GetProperty("utilizationPct").GetDouble());
        Assert.False(status.GetProperty("failoverActive").GetBoolean());
        Assert.Equal(
            WritingSubscriptionProviders.Claude,
            status.GetProperty("currentPrimary").GetProperty("provider").GetString());
    }

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
