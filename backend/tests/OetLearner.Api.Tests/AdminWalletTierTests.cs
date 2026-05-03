using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

[Collection("AuthFlows")]
public class AdminWalletTierTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;
    private readonly HttpClient _adminClient;

    public AdminWalletTierTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
        _adminClient = factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");
    }

    [Fact]
    public async Task Get_ReturnsAppsettingsFallback_WhenNoDbRows()
    {
        await ClearTiersAsync();

        var response = await _adminClient.GetAsync("/v1/admin/billing/wallet-tiers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("appsettings", json.RootElement.GetProperty("source").GetString());
        var tiers = json.RootElement.GetProperty("tiers");
        Assert.True(tiers.GetArrayLength() > 0, "appsettings fallback should expose default tiers");
    }

    [Fact]
    public async Task Put_ReplacesTierSetAtomically_AndReturnsDbSource()
    {
        await ClearTiersAsync();

        var firstPayload = new
        {
            tiers = new[]
            {
                new { amount = 15, credits = 15, bonus = 1, label = "Tier A", isPopular = false, displayOrder = 0, isActive = true, currency = "AUD" },
                new { amount = 40, credits = 45, bonus = 5, label = "Tier B", isPopular = true, displayOrder = 1, isActive = true, currency = "AUD" },
            }
        };
        var first = await _adminClient.PutAsJsonAsync("/v1/admin/billing/wallet-tiers", firstPayload);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var secondPayload = new
        {
            tiers = new[]
            {
                new { amount = 20, credits = 22, bonus = 2, label = "Solo", isPopular = false, displayOrder = 0, isActive = true, currency = "AUD" },
            }
        };
        var second = await _adminClient.PutAsJsonAsync("/v1/admin/billing/wallet-tiers", secondPayload);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var json = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal("database", json.RootElement.GetProperty("source").GetString());
        var tiers = json.RootElement.GetProperty("tiers");
        Assert.Equal(1, tiers.GetArrayLength());
        Assert.Equal(20, tiers[0].GetProperty("amount").GetInt32());

        // DB row count should match the latest PUT (atomic replacement).
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var count = await db.WalletTopUpTierConfigs.CountAsync();
        Assert.Equal(1, count);

        // Audit event must be recorded for both PUTs.
        var auditCount = await db.AuditEvents.CountAsync(e => e.Action == "wallet_tiers.replace");
        Assert.True(auditCount >= 2, $"expected >=2 wallet_tiers.replace audit rows, got {auditCount}");
    }

    [Fact]
    public async Task Put_RejectsNegativeAmountAndCredits()
    {
        await ClearTiersAsync();
        var payload = new
        {
            tiers = new[]
            {
                new { amount = -5, credits = -1, bonus = -2, label = "Bad", isPopular = false, displayOrder = 0, isActive = true, currency = "AUD" },
            }
        };

        var response = await _adminClient.PutAsJsonAsync("/v1/admin/billing/wallet-tiers", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("wallet_tier_invalid", json.RootElement.GetProperty("code").GetString());
        var errors = json.RootElement.GetProperty("errors");
        Assert.True(errors.GetArrayLength() >= 3);
    }

    [Fact]
    public async Task Put_RejectsEmptyOrAllInactiveTierSet()
    {
        await ClearTiersAsync();

        var empty = await _adminClient.PutAsJsonAsync("/v1/admin/billing/wallet-tiers", new { tiers = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var allInactive = await _adminClient.PutAsJsonAsync("/v1/admin/billing/wallet-tiers", new
        {
            tiers = new[]
            {
                new { amount = 15, credits = 15, bonus = 0, label = "Inactive", isPopular = false, displayOrder = 0, isActive = false, currency = "AUD" },
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, allInactive.StatusCode);
    }

    [Fact]
    public async Task Put_RejectsTierCurrencyThatDiffersFromWalletCurrency()
    {
        await ClearTiersAsync();
        var payload = new
        {
            tiers = new[]
            {
                new { amount = 15, credits = 15, bonus = 0, label = "USD", isPopular = false, displayOrder = 0, isActive = true, currency = "USD" },
            }
        };

        var response = await _adminClient.PutAsJsonAsync("/v1/admin/billing/wallet-tiers", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_RejectsLearnerWithoutBillingPermission()
    {
        var learnerClient = _factory.CreateAuthenticatedClient(SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");
        var payload = new
        {
            tiers = new[]
            {
                new { amount = 10, credits = 10, bonus = 0, label = "X", isPopular = false, displayOrder = 0, isActive = true, currency = "AUD" },
            }
        };

        var response = await learnerClient.PutAsJsonAsync("/v1/admin/billing/wallet-tiers", payload);
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"learner should not be able to PUT wallet tiers — got {response.StatusCode}");
    }

    private async Task ClearTiersAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var rows = await db.WalletTopUpTierConfigs.ToListAsync();
        if (rows.Count > 0)
        {
            db.WalletTopUpTierConfigs.RemoveRange(rows);
            await db.SaveChangesAsync();
        }
    }
}
