using System.Net;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

[Collection("AuthFlows")]
public class BillingWalletTopUpAuthTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public BillingWalletTopUpAuthTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TopUpTiers_RequiresAuthentication()
    {
        // First-party auth factory disables the dev-auth fallback, so an unauthenticated
        // request to a LearnerOnly-protected endpoint must return 401.
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/v1/billing/wallet/top-up-tiers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
