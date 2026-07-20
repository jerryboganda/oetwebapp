namespace OetLearner.Api.Services.Billing;

public interface IBillingCatalogService
{
    /// <summary>Get all active products with their prices, optionally filtered by country.</summary>
    Task<CatalogResponse> GetCatalogAsync(string? country = null, string? preferredCurrency = null, CancellationToken ct = default);

    /// <summary>Get a single product by code. Returns null if not found.</summary>
    Task<ProductDto?> GetProductByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>Create a product with its prices locally (Stripe sync happens separately).</summary>
    Task<ProductDto> CreateProductAsync(CreateProductRequest request, CancellationToken ct = default);

    /// <summary>Update a product's active status.</summary>
    Task SetProductActiveAsync(Guid productId, bool isActive, CancellationToken ct = default);

    /// <summary>Admin: list every product (active and inactive) with its price snapshots.</summary>
    Task<IReadOnlyList<AdminCatalogProductDto>> ListAdminProductsAsync(
        string? status = null, string? search = null, CancellationToken ct = default);

    /// <summary>Admin: get a single product by code, including inactive ones. Null if not found.</summary>
    Task<AdminCatalogProductDto?> GetAdminProductByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>Admin: replace a product's editable metadata. Null if not found.</summary>
    Task<AdminCatalogProductDto?> UpdateAdminProductAsync(
        string code, AdminCatalogProductUpdateRequest request, CancellationToken ct = default);
}

public sealed record CatalogResponse(
    IReadOnlyList<ProductDto> Products,
    DateTimeOffset GeneratedAt
);

public sealed record ProductDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string ProductType,
    string? StripeProductId,
    bool IsActive,
    IReadOnlyList<PriceDto> Prices
);

public sealed record PriceDto(
    Guid Id,
    string? StripePriceId,
    string Currency,
    decimal Amount,
    string? Interval,
    int IntervalCount,
    bool IsActive,
    string? Country
);

public sealed record CreateProductRequest(
    string Code,
    string Name,
    string? Description,
    string ProductType,
    string? StripeProductId,
    IReadOnlyList<CreatePriceRequest> Prices
);

public sealed record CreatePriceRequest(
    string? StripePriceId,
    string Currency,
    decimal Amount,
    string? Interval,
    int IntervalCount = 1,
    string? Country = null
);

// ── Admin product editor (/v1/admin/billing/products) ────────────────────────
// Field names below mirror the deployed admin UI contract exactly
// (productCode / status / prices[].priceId), which differs from the public
// <see cref="ProductDto"/> shape. Do not rename without changing the frontend.

/// <summary>
/// Admin-facing product row. <c>Status</c> is derived from
/// <see cref="Domain.Billing.BillingProduct.IsActive"/>; <c>ImageUrl</c> and
/// <c>DisplayOrder</c> have no backing column yet and are always null/0 on read.
/// </summary>
public sealed record AdminCatalogProductDto(
    string ProductCode,
    string Name,
    string? Description,
    string ProductType,
    string Status,
    string? ImageUrl,
    int DisplayOrder,
    IReadOnlyList<AdminCatalogPriceDto> Prices,
    IReadOnlyDictionary<string, object?> Metadata
);

public sealed record AdminCatalogPriceDto(
    string PriceId,
    decimal Amount,
    string Currency,
    string Interval
);

/// <summary>
/// PUT payload from the product editor. The page always sends every field, so
/// this is replace-semantics: <c>Name</c>, <c>ProductType</c> and <c>Status</c>
/// are required, and a null <c>Description</c> clears the column.
/// </summary>
public sealed record AdminCatalogProductUpdateRequest(
    string? Name,
    string? Description,
    string? ProductType,
    string? Status,
    string? ImageUrl,
    int? DisplayOrder
);
