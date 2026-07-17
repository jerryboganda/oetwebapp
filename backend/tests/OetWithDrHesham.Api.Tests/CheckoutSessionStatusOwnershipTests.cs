using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain.Billing;
using OetWithDrHesham.Api.Tests.Infrastructure;

namespace OetWithDrHesham.Api.Tests;

/// <summary>
/// Object-level authorization (IDOR) regression tests for
/// <c>GET /v1/checkout/sessions/{sessionId}/status</c>. The status/fulfilment of
/// a checkout session must only be readable by the user who owns it — before the
/// fix the lookup was by guid alone, so any authenticated user could read any
/// other user's checkout-session status.
/// </summary>
public class CheckoutSessionStatusOwnershipTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CheckoutSessionStatusOwnershipTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SessionStatus_OwnedByAnotherUser_ReturnsNotFound()
    {
        var ownerUserId = $"cs-owner-{Guid.NewGuid():N}";
        var attackerUserId = $"cs-attacker-{Guid.NewGuid():N}";
        using var ownerClient = await CreateClientForUserAsync(ownerUserId);
        using var attackerClient = await CreateClientForUserAsync(attackerUserId);

        var sessionId = await SeedCheckoutSessionAsync(ownerUserId);

        var response = await attackerClient.GetAsync($"/v1/checkout/sessions/{sessionId}/status");

        // Cross-user reads are rejected as NotFound — the lookup is owner-scoped,
        // so a foreign session is indistinguishable from a missing one (no leak).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SessionStatus_OwnedByCaller_ReturnsOk()
    {
        // Positive control: the owner can still read their own session status.
        var ownerUserId = $"cs-owner-{Guid.NewGuid():N}";
        using var ownerClient = await CreateClientForUserAsync(ownerUserId);

        var sessionId = await SeedCheckoutSessionAsync(ownerUserId);

        var response = await ownerClient.GetAsync($"/v1/checkout/sessions/{sessionId}/status");

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("pending", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task SessionStatus_OwnedByCaller_AllowsStripeSessionIdLookup()
    {
        var ownerUserId = $"cs-owner-{Guid.NewGuid():N}";
        using var ownerClient = await CreateClientForUserAsync(ownerUserId);

        var stripeSessionId = $"cs_test_{Guid.NewGuid():N}";
        await SeedCheckoutSessionAsync(ownerUserId, stripeSessionId);

        var response = await ownerClient.GetAsync($"/v1/checkout/sessions/{stripeSessionId}/status");

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(stripeSessionId, json.RootElement.GetProperty("sessionId").GetString());
        Assert.Equal(stripeSessionId, json.RootElement.GetProperty("stripeSessionId").GetString());
        Assert.Equal("pending", json.RootElement.GetProperty("status").GetString());
    }

    private async Task<Guid> SeedCheckoutSessionAsync(string userId, string? stripeSessionId = null)
    {
        var sessionId = Guid.NewGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;
        db.CheckoutSessions.Add(new CheckoutSession
        {
            Id = sessionId,
            UserId = userId,
            StripeSessionId = stripeSessionId,
            IdempotencyKey = $"idem-{Guid.NewGuid():N}",
            Status = "pending",
            TotalAmount = 20m,
            Currency = "AUD",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return sessionId;
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
