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
