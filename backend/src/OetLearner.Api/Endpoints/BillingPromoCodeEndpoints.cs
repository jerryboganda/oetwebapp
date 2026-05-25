using Microsoft.AspNetCore.Http.HttpResults;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Endpoints;

public static class BillingPromoCodeEndpoints
{
    public static IEndpointRouteBuilder MapBillingPromoCodeEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");

        v1.MapPost("/promo-codes/validate", ValidatePromoCode).AllowAnonymous();

        var admin = v1.MapGroup("/admin/billing/promo-codes");
        admin.MapPost("/", AdminCreatePromoCode).RequireAuthorization("AdminBillingCatalogWrite");
        admin.MapDelete("/{code}", AdminDeactivatePromoCode).RequireAuthorization("AdminBillingCatalogWrite");

        return app;
    }

    private static async Task<Ok<PromoCodeValidationResult>> ValidatePromoCode(
        ValidateRequest request, IPromoCodeService svc, CancellationToken ct)
    {
        var result = await svc.ValidateAsync(request.Code, null, ct);
        return TypedResults.Ok(result);
    }

    private static async Task<Ok<string>> AdminCreatePromoCode(
        CreatePromoCodeRequest request, IPromoCodeService svc, CancellationToken ct)
    {
        var id = await svc.CreatePromoCodeAsync(request, ct);
        return TypedResults.Ok(id);
    }

    private static async Task<NoContent> AdminDeactivatePromoCode(
        string code, IPromoCodeService svc, CancellationToken ct)
    {
        await svc.DeactivateAsync(code, ct);
        return TypedResults.NoContent();
    }

    private sealed record ValidateRequest(string Code);
}
