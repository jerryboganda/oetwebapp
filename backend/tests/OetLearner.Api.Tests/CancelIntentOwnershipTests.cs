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
/// Object-level authorization (IDOR) regression tests for the subscription
/// cancellation-intent deflection flow
/// (<c>/v1/billing/subscription/cancel-intent/{id}/confirm|retain</c>).
///
/// A learner must only be able to confirm/retain their OWN cancellation intent.
/// Before the fix, the handlers loaded the intent by id alone, so any
/// authenticated user could cancel a victim's subscription (confirm) or tamper
/// with their intent (retain) by supplying the victim's intent id.
/// </summary>
public class CancelIntentOwnershipTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CancelIntentOwnershipTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ConfirmCancelIntent_ForAnotherUsersIntent_IsRejected_AndVictimSubscriptionStaysActive()
    {
        var victimUserId = $"ci-victim-{Guid.NewGuid():N}";
        var attackerUserId = $"ci-attacker-{Guid.NewGuid():N}";
        using var victimClient = await CreateClientForUserAsync(victimUserId);
        using var attackerClient = await CreateClientForUserAsync(attackerUserId);

        var intentId = await CreateCancelIntentAsync(victimClient);

        var response = await attackerClient.PostAsJsonAsync(
            $"/v1/billing/subscription/cancel-intent/{intentId}/confirm",
            new { });

        // Cross-user access is surfaced as the same "Intent not found." rejection
        // as a missing intent — no existence leak, and the contract shape (400) is
        // unchanged for legitimate callers.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The real regression guard: the victim's subscription must NOT have been
        // cancelled by the attacker.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var victimSub = await db.Subscriptions.FirstAsync(x => x.UserId == victimUserId);
        Assert.Equal(SubscriptionStatus.Active, victimSub.Status);
    }

    [Fact]
    public async Task RetainCancelIntent_ForAnotherUsersIntent_IsRejected_AndIntentUnchanged()
    {
        var victimUserId = $"ci-victim-{Guid.NewGuid():N}";
        var attackerUserId = $"ci-attacker-{Guid.NewGuid():N}";
        using var victimClient = await CreateClientForUserAsync(victimUserId);
        using var attackerClient = await CreateClientForUserAsync(attackerUserId);

        var intentId = await CreateCancelIntentAsync(victimClient);

        var response = await attackerClient.PostAsJsonAsync(
            $"/v1/billing/subscription/cancel-intent/{intentId}/retain",
            new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var intent = await db.CancellationIntents.FirstAsync(x => x.Id == intentId);
        Assert.NotEqual("retained", intent.Status);
    }

    [Fact]
    public async Task ConfirmCancelIntent_ByOwner_CancelsOwnSubscription()
    {
        // Positive control: the owner can still confirm-cancel their own intent.
        // Guards against over-restricting the legitimate path.
        var userId = $"ci-owner-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);

        var intentId = await CreateCancelIntentAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/v1/billing/subscription/cancel-intent/{intentId}/confirm",
            new { });

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var sub = await db.Subscriptions.FirstAsync(x => x.UserId == userId);
        Assert.Equal(SubscriptionStatus.Cancelled, sub.Status);
    }

    private static async Task<string> CreateCancelIntentAsync(HttpClient client)
    {
        var create = await client.PostAsJsonAsync(
            "/v1/billing/subscription/cancel-intent",
            new { reason = "too_expensive", reasonDetail = (string?)null });
        var body = await create.Content.ReadAsStringAsync();
        Assert.True(create.IsSuccessStatusCode, body);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<HttpClient> CreateClientForUserAsync(string userId)
    {
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }
}
