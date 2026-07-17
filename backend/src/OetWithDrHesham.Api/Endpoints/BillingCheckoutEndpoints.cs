using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using OetWithDrHesham.Api.Services.Billing;

namespace OetWithDrHesham.Api.Endpoints;

public static class BillingCheckoutEndpoints
{
    public static IEndpointRouteBuilder MapBillingCheckoutEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");

        var checkout = v1.MapGroup("/checkout").RequireAuthorization();
        checkout.MapPost("/sessions", CreateCheckoutSession);
        // PayPal (embedded) cart checkout: creates a PayPal order for the cart; the browser
        // SDK approves it and the existing /v1/billing/checkout-sessions/{orderId}/capture
        // endpoint captures + fulfils (the PAYMENT.CAPTURE.COMPLETED webhook is the backstop).
        checkout.MapPost("/sessions/paypal", CreatePayPalCartOrder);
        checkout.MapGet("/sessions/{sessionId}/status", GetSessionStatus);

        return app;
    }

    private static async Task<Results<Ok<PayPalCartOrderDto>, BadRequest<string>>> CreatePayPalCartOrder(
        HttpContext http,
        CreatePayPalCartOrderRequest request,
        ICheckoutService checkoutService,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var email = http.User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        try
        {
            var order = await checkoutService.CreatePayPalCartOrderAsync(userId, email, request.CartId, ct);
            return TypedResults.Ok(order);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<Ok<CheckoutSessionDto>, BadRequest<string>>> CreateCheckoutSession(
        HttpContext http,
        CreateCheckoutRequest request,
        ICheckoutService checkoutService,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)!;
        var email = http.User.FindFirstValue(System.Security.Claims.ClaimTypes.Email) ?? string.Empty;
        try
        {
            var session = await checkoutService.CreateCheckoutSessionAsync(userId, email, request.CartId, request.SuccessUrl, request.CancelUrl, ct);
            return TypedResults.Ok(session);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<Ok<CheckoutSessionStatusDto>, NotFound>> GetSessionStatus(
        HttpContext http,
        string sessionId,
        ICheckoutService checkoutService,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var status = await checkoutService.GetSessionStatusAsync(userId, sessionId, ct);
        return status is null ? TypedResults.NotFound() : TypedResults.Ok(status);
    }

    private sealed record CreateCheckoutRequest(string CartId, string? SuccessUrl = null, string? CancelUrl = null);

    private sealed record CreatePayPalCartOrderRequest(string CartId);
}
