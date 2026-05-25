using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Endpoints;

public static class BillingCartEndpoints
{
    public static IEndpointRouteBuilder MapBillingCartEndpoints(this IEndpointRouteBuilder app)
    {
        var cart = app.MapGroup("/v1/cart").RequireAuthorization();

        cart.MapGet("/", GetCart);
        cart.MapPost("/items", AddItem);
        cart.MapPatch("/items/{itemId:guid}", UpdateItemQuantity);
        cart.MapDelete("/items/{itemId:guid}", RemoveItem);
        cart.MapPost("/promo-codes", ApplyPromoCode);
        cart.MapDelete("/promo-codes/{code}", RemovePromoCode);
        cart.MapDelete("/", ClearCart);
        cart.MapPost("/merge", MergeAnonymousCart);

        return app;
    }

    private static string? GetUserId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier);

    private static async Task<Ok<CartDto>> GetCart(
        HttpContext http,
        [FromQuery] string? sessionToken,
        ICartService cartService,
        CancellationToken ct)
    {
        var userId = GetUserId(http);
        var cart = await cartService.GetOrCreateCartAsync(userId, sessionToken, ct);
        return TypedResults.Ok(cart);
    }

    private static async Task<Ok<CartDto>> AddItem(
        HttpContext http,
        [FromQuery] string? cartId,
        AddCartItemRequest request,
        ICartService cartService,
        CancellationToken ct)
    {
        var userId = GetUserId(http);
        if (string.IsNullOrWhiteSpace(cartId))
        {
            var newCart = await cartService.GetOrCreateCartAsync(userId, null, ct);
            cartId = newCart.Id;
        }
        var cart = await cartService.AddItemAsync(cartId, request, ct);
        return TypedResults.Ok(cart);
    }

    private static async Task<Results<Ok<CartDto>, NotFound>> UpdateItemQuantity(
        HttpContext http,
        Guid itemId,
        [FromQuery] string cartId,
        [FromBody] UpdateQuantityRequest request,
        ICartService cartService,
        CancellationToken ct)
    {
        var cart = await cartService.UpdateItemQuantityAsync(cartId, itemId, request.Quantity, ct);
        return TypedResults.Ok(cart);
    }

    private static async Task<Results<Ok<CartDto>, NotFound>> RemoveItem(
        Guid itemId,
        [FromQuery] string cartId,
        ICartService cartService,
        CancellationToken ct)
    {
        var cart = await cartService.RemoveItemAsync(cartId, itemId, ct);
        return TypedResults.Ok(cart);
    }

    private static async Task<Ok<CartDto>> ApplyPromoCode(
        [FromQuery] string cartId,
        ApplyPromoCodeRequest request,
        ICartService cartService,
        CancellationToken ct)
    {
        var cart = await cartService.ApplyPromoCodeAsync(cartId, request.Code, ct);
        return TypedResults.Ok(cart);
    }

    private static async Task<Ok<CartDto>> RemovePromoCode(
        [FromQuery] string cartId,
        string code,
        ICartService cartService,
        CancellationToken ct)
    {
        var cart = await cartService.RemovePromoCodeAsync(cartId, code, ct);
        return TypedResults.Ok(cart);
    }

    private static async Task<NoContent> ClearCart(
        [FromQuery] string cartId,
        ICartService cartService,
        CancellationToken ct)
    {
        await cartService.ClearCartAsync(cartId, ct);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> MergeAnonymousCart(
        HttpContext http,
        MergeCartRequest request,
        ICartService cartService,
        CancellationToken ct)
    {
        var userId = GetUserId(http)
            ?? throw new InvalidOperationException("User not authenticated.");
        await cartService.MergeAnonymousCartAsync(request.SessionToken, userId, ct);
        return TypedResults.NoContent();
    }

    private sealed record UpdateQuantityRequest(int Quantity);
    private sealed record ApplyPromoCodeRequest(string Code);
    private sealed record MergeCartRequest(string SessionToken);
}
