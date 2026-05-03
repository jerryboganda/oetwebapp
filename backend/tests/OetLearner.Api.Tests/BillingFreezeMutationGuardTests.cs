using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class BillingFreezeMutationGuardTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BillingFreezeMutationGuardTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BillingQuote_FrozenLearner_IsRejectedBeforePersistingQuote()
    {
        var userId = $"quote-frozen-{Guid.NewGuid():N}";
        using var client = await CreateFrozenLearnerClientAsync(userId);

        var response = await client.GetAsync("/v1/billing/quote?productType=review_credits&quantity=1");

        await AssertAccountFrozenAsync(response);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.False(await db.BillingQuotes.AnyAsync(x => x.UserId == userId));
    }

    [Fact]
    public async Task ScoreGuaranteeActivate_FrozenLearner_IsRejected()
    {
        var userId = $"sg-activate-frozen-{Guid.NewGuid():N}";
        using var client = await CreateFrozenLearnerClientAsync(userId);

        var response = await client.PostAsJsonAsync("/v1/learner/score-guarantee/activate", new
        {
            baselineScore = 320
        });

        await AssertAccountFrozenAsync(response);
    }

    [Fact]
    public async Task ScoreGuaranteeClaim_FrozenLearner_IsRejected()
    {
        var userId = $"sg-claim-frozen-{Guid.NewGuid():N}";
        using var client = await CreateFrozenLearnerClientAsync(userId);

        var response = await client.PostAsJsonAsync("/v1/learner/score-guarantee/claim", new
        {
            actualScore = 330,
            note = "official result uploaded"
        });

        await AssertAccountFrozenAsync(response);
    }

    [Fact]
    public async Task ReferralGenerate_FrozenLearner_IsRejected()
    {
        var userId = $"referral-frozen-{Guid.NewGuid():N}";
        using var client = await CreateFrozenLearnerClientAsync(userId);

        var response = await client.PostAsync("/v1/learner/referral/generate", null);

        await AssertAccountFrozenAsync(response);
    }

    private async Task<HttpClient> CreateFrozenLearnerClientAsync(string userId)
    {
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;
        db.AccountFreezeRecords.Add(new AccountFreezeRecord
        {
            Id = $"freeze-{Guid.NewGuid():N}",
            UserId = userId,
            Status = FreezeStatus.Active,
            IsCurrent = true,
            IsSelfService = true,
            EntitlementConsumed = true,
            RequestedAt = now,
            StartedAt = now,
            DurationDays = 14,
            Reason = "Billing mutation guard test",
            PolicySnapshotJson = "{}",
            EligibilitySnapshotJson = "{}"
        });
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }

    private static async Task AssertAccountFrozenAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("account_frozen", json.RootElement.GetProperty("code").GetString());
    }
}
