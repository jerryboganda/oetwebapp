namespace OetWithDrHesham.Api.Services.Billing;

public interface ICartService
{
    Task<CartDto> GetOrCreateCartAsync(string? userId, string? sessionToken, CancellationToken ct = default);
    // cartId mutations are scoped to the owning userId so a learner can only act
    // on their own cart (object-level authorization — guards against IDOR).
    Task<CartDto> AddItemAsync(string cartId, string userId, AddCartItemRequest request, CancellationToken ct = default);
    Task<CartDto> UpdateItemQuantityAsync(string cartId, string userId, Guid itemId, int quantity, CancellationToken ct = default);
    Task<CartDto> RemoveItemAsync(string cartId, string userId, Guid itemId, CancellationToken ct = default);
    Task<CartDto> ApplyPromoCodeAsync(string cartId, string userId, string code, CancellationToken ct = default);
    Task<CartDto> RemovePromoCodeAsync(string cartId, string userId, string code, CancellationToken ct = default);
    Task ClearCartAsync(string cartId, string userId, CancellationToken ct = default);
    Task MergeAnonymousCartAsync(string sessionToken, string userId, CancellationToken ct = default);
    Task<CartDto?> GetCartByIdAsync(string cartId, string userId, CancellationToken ct = default);
}

public sealed record CartDto(
    string Id,
    string? UserId,
    string Status,
    IReadOnlyList<CartItemDto> Items,
    IReadOnlyList<string> AppliedPromoCodes,
    decimal Subtotal,
    decimal Discount,
    decimal Total,
    string Currency,
    DateTimeOffset ExpiresAt
);

public sealed record CartItemDto(
    Guid Id,
    Guid BillingProductId,
    string ProductCode,
    string ProductName,
    string ProductType,
    Guid BillingPriceId,
    decimal UnitPrice,
    string Currency,
    string? Interval,
    int Quantity,
    decimal LineTotal
);

public sealed record AddCartItemRequest(
    string ProductCode,
    Guid BillingPriceId,
    int Quantity = 1
);
