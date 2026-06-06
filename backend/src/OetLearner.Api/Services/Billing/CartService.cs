using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain.Billing;

namespace OetLearner.Api.Services.Billing;

public sealed class CartService : ICartService
{
    private readonly LearnerDbContext _db;
    private static readonly TimeSpan CartTtl = TimeSpan.FromDays(7);
    private const int MaxCartItemQuantity = 100;

    public CartService(LearnerDbContext db)
    {
        _db = db;
    }

    public async Task<CartDto> GetOrCreateCartAsync(string? userId, string? sessionToken, CancellationToken ct = default)
    {
        Cart? cart = null;

        if (!string.IsNullOrWhiteSpace(userId))
        {
            cart = await _db.Carts
                .Include(c => c.Items).ThenInclude(i => i.BillingProduct)
                .Include(c => c.Items).ThenInclude(i => i.BillingPrice)
                .Include(c => c.AppliedPromoCodes)
                .Where(c => c.UserId == userId && c.Status == "active")
                .OrderByDescending(c => c.UpdatedAt)
                .FirstOrDefaultAsync(ct);
        }
        else if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            cart = await _db.Carts
                .Include(c => c.Items).ThenInclude(i => i.BillingProduct)
                .Include(c => c.Items).ThenInclude(i => i.BillingPrice)
                .Include(c => c.AppliedPromoCodes)
                .Where(c => c.SessionToken == sessionToken && c.Status == "active")
                .OrderByDescending(c => c.UpdatedAt)
                .FirstOrDefaultAsync(ct);
        }

        if (cart is null)
        {
            cart = new Cart
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SessionToken = sessionToken,
                Status = "active",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.Add(CartTtl)
            };
            _db.Carts.Add(cart);
            await _db.SaveChangesAsync(ct);
        }

        return MapCart(cart);
    }

    public async Task<CartDto> AddItemAsync(string cartId, string userId, AddCartItemRequest request, CancellationToken ct = default)
    {
        if (request.Quantity is < 1 or > MaxCartItemQuantity)
            throw ApiException.Validation("invalid_quantity", $"Quantity must be between 1 and {MaxCartItemQuantity}.");

        var cart = await LoadCartAsync(cartId, userId, ct);

        // Validate product exists and is active
        var product = await _db.BillingProducts
            .Include(p => p.Prices)
            .Where(p => p.Code == request.ProductCode && p.IsActive)
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("product_not_found", $"Product '{request.ProductCode}' not found or inactive.");

        var price = product.Prices.FirstOrDefault(p => p.Id == request.BillingPriceId && p.IsActive)
            ?? throw ApiException.NotFound("price_not_found", $"Price {request.BillingPriceId} not found for product '{request.ProductCode}'.");

        // Enforce: max 1 subscription per cart
        if (product.ProductType == "subscription")
        {
            var hasSubscription = cart.Items.Any(i => i.BillingProduct.ProductType == "subscription");
            if (hasSubscription)
                throw ApiException.Validation("cart_one_subscription", "Only one subscription can be added to the cart.");
        }

        var existing = cart.Items.FirstOrDefault(i =>
            i.BillingProductId == product.Id && i.BillingPriceId == price.Id);

        if (existing is not null)
        {
            existing.Quantity += request.Quantity;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            cart.Items.Add(new CartItem
            {
                Id = Guid.NewGuid(),
                CartId = cart.Id,
                BillingProductId = product.Id,
                BillingPriceId = price.Id,
                Quantity = request.Quantity,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        cart.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return MapCart(cart);
    }

    public async Task<CartDto> UpdateItemQuantityAsync(string cartId, string userId, Guid itemId, int quantity, CancellationToken ct = default)
    {
        if (quantity is < 1 or > MaxCartItemQuantity)
            throw ApiException.Validation("invalid_quantity", $"Quantity must be between 1 and {MaxCartItemQuantity}.");

        var cart = await LoadCartAsync(cartId, userId, ct);
        var item = cart.Items.FirstOrDefault(i => i.Id == itemId)
            ?? throw ApiException.NotFound("cart_item_not_found", $"Cart item {itemId} not found.");

        item.Quantity = quantity;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        cart.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return MapCart(cart);
    }

    public async Task<CartDto> RemoveItemAsync(string cartId, string userId, Guid itemId, CancellationToken ct = default)
    {
        var cart = await LoadCartAsync(cartId, userId, ct);
        var item = cart.Items.FirstOrDefault(i => i.Id == itemId)
            ?? throw ApiException.NotFound("cart_item_not_found", $"Cart item {itemId} not found.");

        _db.CartItems.Remove(item);
        cart.Items.Remove(item);
        cart.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return MapCart(cart);
    }

    public async Task<CartDto> ApplyPromoCodeAsync(string cartId, string userId, string code, CancellationToken ct = default)
    {
        var cart = await LoadCartAsync(cartId, userId, ct);

        // Validate against BillingCoupon (EndsAt is the expiry field in this entity)
        var coupon = await _db.BillingCoupons
            .Where(c => c.Code == code && c.Status == OetLearner.Api.Domain.BillingCouponStatus.Active)
            .Where(c => c.StartsAt == null || c.StartsAt <= DateTimeOffset.UtcNow)
            .Where(c => c.EndsAt == null || c.EndsAt >= DateTimeOffset.UtcNow)
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.Validation("promo_code_invalid", $"Promo code '{code}' is not valid or expired.");

        // Don't apply the same code twice
        if (cart.AppliedPromoCodes.Any(p => p.Code == code))
            return MapCart(cart);

        decimal? discountAmount = coupon.DiscountType == OetLearner.Api.Domain.BillingDiscountType.FixedAmount
            ? coupon.DiscountValue : null;
        decimal? discountPercent = coupon.DiscountType == OetLearner.Api.Domain.BillingDiscountType.Percentage
            ? coupon.DiscountValue : null;

        cart.AppliedPromoCodes.Add(new AppliedPromoCode
        {
            Id = Guid.NewGuid(),
            CartId = cart.Id,
            Code = code,
            DiscountAmount = discountAmount,
            DiscountPercent = discountPercent,
            CreatedAt = DateTimeOffset.UtcNow
        });

        cart.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return MapCart(cart);
    }

    public async Task<CartDto> RemovePromoCodeAsync(string cartId, string userId, string code, CancellationToken ct = default)
    {
        var cart = await LoadCartAsync(cartId, userId, ct);
        var promo = cart.AppliedPromoCodes.FirstOrDefault(p => p.Code == code);
        if (promo is not null)
        {
            _db.AppliedPromoCodes.Remove(promo);
            cart.AppliedPromoCodes.Remove(promo);
            cart.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return MapCart(cart);
    }

    public async Task ClearCartAsync(string cartId, string userId, CancellationToken ct = default)
    {
        var cart = await LoadCartAsync(cartId, userId, ct);
        _db.CartItems.RemoveRange(cart.Items);
        _db.AppliedPromoCodes.RemoveRange(cart.AppliedPromoCodes);
        cart.Items.Clear();
        cart.AppliedPromoCodes.Clear();
        cart.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task MergeAnonymousCartAsync(string sessionToken, string userId, CancellationToken ct = default)
    {
        var anonCart = await _db.Carts
            .Include(c => c.Items)
            .Include(c => c.AppliedPromoCodes)
            .Where(c => c.SessionToken == sessionToken && c.Status == "active")
            .FirstOrDefaultAsync(ct);

        if (anonCart is null) return;

        var userCart = await _db.Carts
            .Include(c => c.Items)
            .Where(c => c.UserId == userId && c.Status == "active")
            .FirstOrDefaultAsync(ct);

        if (userCart is null)
        {
            // Claim the anon cart
            anonCart.UserId = userId;
            anonCart.SessionToken = null;
            anonCart.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            // Merge anon items into user cart
            foreach (var item in anonCart.Items)
            {
                var existing = userCart.Items.FirstOrDefault(i =>
                    i.BillingProductId == item.BillingProductId &&
                    i.BillingPriceId == item.BillingPriceId);

                if (existing is not null)
                    existing.Quantity += item.Quantity;
                else
                    userCart.Items.Add(new CartItem
                    {
                        Id = Guid.NewGuid(),
                        CartId = userCart.Id,
                        BillingProductId = item.BillingProductId,
                        BillingPriceId = item.BillingPriceId,
                        Quantity = item.Quantity,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    });
            }

            anonCart.Status = "converted";
            userCart.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<CartDto?> GetCartByIdAsync(string cartId, string userId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(cartId, out var id)) return null;
        // Owner-scoped: checkout may only read the caller's own cart by id.
        var cart = await _db.Carts
            .Include(c => c.Items).ThenInclude(i => i.BillingProduct)
            .Include(c => c.Items).ThenInclude(i => i.BillingPrice)
            .Include(c => c.AppliedPromoCodes)
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);
        return cart is null ? null : MapCart(cart);
    }

    private async Task<Cart> LoadCartAsync(string cartId, string userId, CancellationToken ct)
    {
        if (!Guid.TryParse(cartId, out var id))
            throw ApiException.Validation("invalid_cart_id", "Invalid cart ID.");

        // Scope by owner so a learner can only load (and therefore mutate) their
        // own cart — a foreign cartId is indistinguishable from a missing one.
        return await _db.Carts
            .Include(c => c.Items).ThenInclude(i => i.BillingProduct)
            .Include(c => c.Items).ThenInclude(i => i.BillingPrice)
            .Include(c => c.AppliedPromoCodes)
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId && c.Status == "active", ct)
            ?? throw ApiException.NotFound("cart_not_found", "Cart not found.");
    }

    private static CartDto MapCart(Cart cart)
    {
        var items = cart.Items.Select(i => new CartItemDto(
            i.Id,
            i.BillingProductId,
            i.BillingProduct?.Code ?? string.Empty,
            i.BillingProduct?.Name ?? string.Empty,
            i.BillingProduct?.ProductType ?? string.Empty,
            i.BillingPriceId,
            i.BillingPrice?.Amount ?? 0,
            i.BillingPrice?.Currency ?? "AUD",
            i.BillingPrice?.Interval,
            i.Quantity,
            (i.BillingPrice?.Amount ?? 0) * i.Quantity
        )).ToList();

        var subtotal = items.Sum(i => i.LineTotal);
        var currency = items.FirstOrDefault()?.Currency ?? "AUD";

        // Apply promo discounts
        var discount = 0m;
        foreach (var promo in cart.AppliedPromoCodes)
        {
            if (promo.DiscountAmount.HasValue)
                discount += promo.DiscountAmount.Value;
            else if (promo.DiscountPercent.HasValue)
                discount += subtotal * (promo.DiscountPercent.Value / 100m);
        }

        var total = Math.Max(0, subtotal - discount);

        return new CartDto(
            cart.Id.ToString(),
            cart.UserId,
            cart.Status,
            items,
            cart.AppliedPromoCodes.Select(p => p.Code).ToList(),
            subtotal,
            discount,
            total,
            currency,
            cart.ExpiresAt
        );
    }
}
