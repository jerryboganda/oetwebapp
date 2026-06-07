using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Endpoints;

public static class BillingCatalogEndpoints
{
    public static IEndpointRouteBuilder MapBillingCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");

        // Public product catalog — anonymous access for pricing page.
        v1.MapGet("/catalog/products", GetProducts).AllowAnonymous();

        // Admin product management.
        var adminCatalog = v1.MapGroup("/admin/billing/catalog");
        adminCatalog.MapPost("/products", AdminCreateProduct)
            .WithAdminWrite("AdminBillingCatalogWrite");
        adminCatalog.MapPatch("/products/{productId:guid}/active", AdminSetProductActive)
            .WithAdminWrite("AdminBillingCatalogWrite");
        adminCatalog.MapGet("/products/{code}", AdminGetProductByCode)
            .RequireAuthorization("AdminBillingRead");

        return app;
    }

    private static async Task<Ok<CatalogResponse>> GetProducts(
        [FromQuery] string? country,
        [FromQuery] string? currency,
        IBillingCatalogService catalog,
        CancellationToken ct)
    {
        var result = await catalog.GetCatalogAsync(country, currency, ct);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<ProductDto>, NotFound>> AdminGetProductByCode(
        string code,
        IBillingCatalogService catalog,
        CancellationToken ct)
    {
        var product = await catalog.GetProductByCodeAsync(code, ct);
        return product is null ? TypedResults.NotFound() : TypedResults.Ok(product);
    }

    private static async Task<Created<ProductDto>> AdminCreateProduct(
        CreateProductRequest request,
        IBillingCatalogService catalog,
        CancellationToken ct)
    {
        var product = await catalog.CreateProductAsync(request, ct);
        return TypedResults.Created($"/v1/catalog/products/{product.Code}", product);
    }

    private static async Task<Results<NoContent, NotFound>> AdminSetProductActive(
        Guid productId,
        [FromBody] SetProductActiveRequest request,
        IBillingCatalogService catalog,
        CancellationToken ct)
    {
        await catalog.SetProductActiveAsync(productId, request.IsActive, ct);
        return TypedResults.NoContent();
    }

    private sealed record SetProductActiveRequest(bool IsActive);
}
