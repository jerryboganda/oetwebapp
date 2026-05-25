using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Endpoints;

public static class BillingSubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapBillingSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var subs = app.MapGroup("/v1/subscriptions/me").RequireAuthorization();

        subs.MapGet("/", GetMySubscription);
        subs.MapGet("/invoices", GetMyInvoices);
        subs.MapPost("/portal-session", CreatePortalSession);
        subs.MapPost("/cancel", CancelSubscription);
        subs.MapPost("/change-plan", ChangePlan);
        subs.MapPost("/pause", PauseSubscription);
        subs.MapPost("/resume", ResumeSubscription);

        return app;
    }

    private static string GetUserId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private static async Task<Results<Ok<CustomerSubscriptionDto>, NotFound>> GetMySubscription(
        HttpContext http, ISubscriptionService svc, CancellationToken ct)
    {
        var sub = await svc.GetActiveSubscriptionAsync(GetUserId(http), ct);
        return sub is null ? TypedResults.NotFound() : TypedResults.Ok(sub);
    }

    private static async Task<Ok<IEnumerable<SubscriptionInvoiceDto>>> GetMyInvoices(
        HttpContext http, ISubscriptionService svc, CancellationToken ct)
    {
        var invoices = await svc.ListInvoicesAsync(GetUserId(http), ct);
        return TypedResults.Ok(invoices);
    }

    private static async Task<Results<Ok<string>, BadRequest<string>>> CreatePortalSession(
        HttpContext http, PortalSessionRequest request, ISubscriptionService svc, CancellationToken ct)
    {
        try
        {
            var url = await svc.CreatePortalSessionAsync(GetUserId(http), request.ReturnUrl, ct);
            return TypedResults.Ok(url);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<NoContent, BadRequest<string>>> CancelSubscription(
        HttpContext http, CancelRequest request, ISubscriptionService svc, CancellationToken ct)
    {
        try
        {
            await svc.CancelAsync(GetUserId(http), request.AtPeriodEnd, ct);
            return TypedResults.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<NoContent, BadRequest<string>>> ChangePlan(
        HttpContext http, ChangePlanRequest request, ISubscriptionService svc, CancellationToken ct)
    {
        try
        {
            await svc.ChangePlanAsync(GetUserId(http), request.NewStripePriceId, ct);
            return TypedResults.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<NoContent, BadRequest<string>>> PauseSubscription(
        HttpContext http, ISubscriptionService svc, CancellationToken ct)
    {
        try
        {
            await svc.PauseAsync(GetUserId(http), ct);
            return TypedResults.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<NoContent, BadRequest<string>>> ResumeSubscription(
        HttpContext http, ISubscriptionService svc, CancellationToken ct)
    {
        try
        {
            await svc.ResumeAsync(GetUserId(http), ct);
            return TypedResults.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private sealed record PortalSessionRequest(string ReturnUrl);
    private sealed record CancelRequest(bool AtPeriodEnd = true);
    private sealed record ChangePlanRequest(string NewStripePriceId);
}
