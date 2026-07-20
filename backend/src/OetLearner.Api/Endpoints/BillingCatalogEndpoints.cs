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

        // Admin product editor. Mapped at /v1/admin/billing/products because that
        // is the path the deployed admin UI calls (app/admin/billing/products);
        // the group above is /v1/admin/billing/catalog and stays as-is.
        var adminProducts = v1.MapGroup("/admin/billing/products");
        adminProducts.MapGet("/", AdminListProducts)
            .RequireAuthorization("AdminBillingRead");
        adminProducts.MapGet("/{code}", AdminGetProduct)
            .RequireAuthorization("AdminBillingRead");
        adminProducts.MapPut("/{code}", AdminUpdateProduct)
            .WithAdminWrite("AdminBillingCatalogWrite");

        return app;
    }

    private static async Task<Ok<AdminProductListResponse>> AdminListProducts(
        [FromQuery] string? status,
        [FromQuery] string? search,
        IBillingCatalogService catalog,
        CancellationToken ct)
    {
        var items = await catalog.ListAdminProductsAsync(status, search, ct);
        return TypedResults.Ok(new AdminProductListResponse(items));
    }

    private static async Task<Results<Ok<AdminCatalogProductDto>, NotFound>> AdminGetProduct(
        string code,
        IBillingCatalogService catalog,
        CancellationToken ct)
    {
        var product = await catalog.GetAdminProductByCodeAsync(code, ct);
        return product is null ? TypedResults.NotFound() : TypedResults.Ok(product);
    }

    private static async Task<Results<Ok<AdminCatalogProductDto>, NotFound>> AdminUpdateProduct(
        string code,
        [FromBody] AdminCatalogProductUpdateRequest request,
        IBillingCatalogService catalog,
        CancellationToken ct)
    {
        var product = await catalog.UpdateAdminProductAsync(code, request, ct);
        return product is null ? TypedResults.NotFound() : TypedResults.Ok(product);
    }

    private sealed record AdminProductListResponse(IReadOnlyList<AdminCatalogProductDto> Items);

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
