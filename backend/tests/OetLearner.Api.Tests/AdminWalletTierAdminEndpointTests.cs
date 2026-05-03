using System.Net;
using System.Net.Http.Json;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// Coverage for the admin wallet-tier CMS endpoints surfaced under
/// /v1/admin/billing/wallet-tiers. These tests are skipped pending Impl A
/// landing the production endpoints — re-enable by removing the Skip attribute.
/// The frontend page lives at app/admin/billing/wallet-tiers/page.tsx.
/// </summary>
[Collection("AuthFlows")]
public class AdminWalletTierAdminEndpointTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public AdminWalletTierAdminEndpointTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetWalletTiers_RequiresManageBillingPermission()
    {
        // Once Impl A lands, sign in as a non-billing admin (or learner with admin
        // role lacking ManageBilling) and assert 403.
        using var client = _factory.CreateAuthenticatedClient(
            SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");
        var response = await client.GetAsync("/v1/admin/billing/wallet-tiers");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PutWalletTiers_RejectsNegativeValuesAndOverlongCurrency()
    {
        using var client = _factory.CreateAuthenticatedClient(
            SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");

        var response = await client.PutAsJsonAsync("/v1/admin/billing/wallet-tiers", new
        {
            currency = "AUDD", // > 3 chars
            tiers = new[]
            {
                new { amount = -10, credits = -1, bonus = -1, label = "Bad", isPopular = false }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
