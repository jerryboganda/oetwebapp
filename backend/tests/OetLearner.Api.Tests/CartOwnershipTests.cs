using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain.Billing;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// Object-level authorization (IDOR) regression tests for the cart endpoints
/// (<c>/v1/cart/*</c>). cartId is supplied by the client, so every mutation must
/// be scoped to the owning user — before the fix any authenticated user could
/// read/modify/clear another user's cart by its GUID.
/// </summary>
public class CartOwnershipTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CartOwnershipTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RemoveItem_FromAnotherUsersCart_IsRejected_AndItemRemains()
    {
        var ownerUserId = $"cart-owner-{Guid.NewGuid():N}";
        var attackerUserId = $"cart-attacker-{Guid.NewGuid():N}";
        using var ownerClient = await CreateClientForUserAsync(ownerUserId);
        using var attackerClient = await CreateClientForUserAsync(attackerUserId);

        var (cartId, itemId) = await SeedCartWithItemAsync(ownerUserId);

        var response = await attackerClient.DeleteAsync($"/v1/cart/items/{itemId}?cartId={cartId}");

        // The owner-scoped cart lookup makes the foreign cart invisible to the
        // attacker, so the mutation is rejected (not a success status)...
        Assert.False(response.IsSuccessStatusCode);

        // ...and the real regression guard: the owner's item is still there.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal(1, await db.CartItems.CountAsync(i => i.CartId == cartId));
    }

    [Fact]
    public async Task RemoveItem_FromOwnCart_Succeeds()
    {
        // Positive control: the owner can still mutate their own cart.
        var ownerUserId = $"cart-owner-{Guid.NewGuid():N}";
        using var ownerClient = await CreateClientForUserAsync(ownerUserId);

        var (cartId, itemId) = await SeedCartWithItemAsync(ownerUserId);

        var response = await ownerClient.DeleteAsync($"/v1/cart/items/{itemId}?cartId={cartId}");

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal(0, await db.CartItems.CountAsync(i => i.CartId == cartId));
    }

    private async Task<(Guid cartId, Guid itemId)> SeedCartWithItemAsync(string userId)
    {
        var cartId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var priceId = Guid.NewGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;

        // Carts always reference real products/prices in production; seed them so
        // LoadCartAsync's Include(Items).ThenInclude(BillingProduct/BillingPrice)
        // materialises the item.
        db.BillingProducts.Add(new BillingProduct
        {
            Id = productId,
            Code = $"prod-{productId:N}",
            Name = "Test Product",
            ProductType = "package",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.BillingPrices.Add(new BillingPrice
        {
            Id = priceId,
            BillingProductId = productId,
            Currency = "AUD",
            Amount = 20m,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Carts.Add(new Cart
        {
            Id = cartId,
            UserId = userId,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddDays(7),
            Items =
            {
                new CartItem
                {
                    Id = itemId,
                    CartId = cartId,
                    BillingProductId = productId,
                    BillingPriceId = priceId,
                    Quantity = 1,
                    CreatedAt = now,
                    UpdatedAt = now
                }
            }
        });
        await db.SaveChangesAsync();
        return (cartId, itemId);
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
