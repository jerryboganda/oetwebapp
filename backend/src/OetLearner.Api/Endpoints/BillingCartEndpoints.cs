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

    // Cart routes all RequireAuthorization, so the caller id is always present.
    // Every cartId mutation is scoped to this owner (object-level authorization).
    private static string RequireUserId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");

    private static async Task<Ok<CartDto>> GetCart(
        HttpContext http,
        [FromQuery] string? sessionToken,
        [FromServices] ICartService cartService,
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
        [FromServices] ICartService cartService,
        CancellationToken ct)
    {
        var userId = RequireUserId(http);
        if (string.IsNullOrWhiteSpace(cartId))
        {
            var newCart = await cartService.GetOrCreateCartAsync(userId, null, ct);
            cartId = newCart.Id;
        }
        var cart = await cartService.AddItemAsync(cartId, userId, request, ct);
        return TypedResults.Ok(cart);
    }

    private static async Task<Results<Ok<CartDto>, NotFound>> UpdateItemQuantity(
        HttpContext http,
        Guid itemId,
        [FromQuery] string cartId,
        [FromBody] UpdateQuantityRequest request,
        [FromServices] ICartService cartService,
        CancellationToken ct)
    {
        var cart = await cartService.UpdateItemQuantityAsync(cartId, RequireUserId(http), itemId, request.Quantity, ct);
        return TypedResults.Ok(cart);
    }

    private static async Task<Results<Ok<CartDto>, NotFound>> RemoveItem(
        HttpContext http,
        Guid itemId,
        [FromQuery] string cartId,
        [FromServices] ICartService cartService,
        CancellationToken ct)
    {
        var cart = await cartService.RemoveItemAsync(cartId, RequireUserId(http), itemId, ct);
        return TypedResults.Ok(cart);
    }

    private static async Task<Ok<CartDto>> ApplyPromoCode(
        HttpContext http,
        [FromQuery] string cartId,
        ApplyPromoCodeRequest request,
        [FromServices] ICartService cartService,
        CancellationToken ct)
    {
        var cart = await cartService.ApplyPromoCodeAsync(cartId, RequireUserId(http), request.Code, ct);
        return TypedResults.Ok(cart);
    }

    private static async Task<Ok<CartDto>> RemovePromoCode(
        HttpContext http,
        [FromQuery] string cartId,
        string code,
        [FromServices] ICartService cartService,
        CancellationToken ct)
    {
        var cart = await cartService.RemovePromoCodeAsync(cartId, RequireUserId(http), code, ct);
        return TypedResults.Ok(cart);
    }

    private static async Task<NoContent> ClearCart(
        HttpContext http,
        [FromQuery] string cartId,
        [FromServices] ICartService cartService,
        CancellationToken ct)
    {
        await cartService.ClearCartAsync(cartId, RequireUserId(http), ct);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> MergeAnonymousCart(
        HttpContext http,
        MergeCartRequest request,
        [FromServices] ICartService cartService,
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
