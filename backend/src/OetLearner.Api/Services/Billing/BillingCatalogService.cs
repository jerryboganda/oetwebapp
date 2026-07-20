using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain.Billing;

namespace OetLearner.Api.Services.Billing;

public sealed class BillingCatalogService : IBillingCatalogService
{
    private readonly LearnerDbContext _db;

    public BillingCatalogService(LearnerDbContext db)
    {
        _db = db;
    }

    public async Task<CatalogResponse> GetCatalogAsync(
        string? country = null, string? preferredCurrency = null, CancellationToken ct = default)
    {
        var products = await _db.BillingProducts
            .Include(p => p.Prices)
            .Where(p => p.IsActive)
            .OrderBy(p => p.ProductType)
            .ThenBy(p => p.Name)
            .AsNoTracking()
            .ToListAsync(ct);

        var dtos = products.Select(p => MapProduct(p, country, preferredCurrency)).ToList();
        return new CatalogResponse(dtos, DateTimeOffset.UtcNow);
    }

    public async Task<ProductDto?> GetProductByCodeAsync(string code, CancellationToken ct = default)
    {
        var product = await _db.BillingProducts
            .Include(p => p.Prices)
            .Where(p => p.Code == code)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        return product is null ? null : MapProduct(product);
    }

    public async Task<ProductDto> CreateProductAsync(CreateProductRequest request, CancellationToken ct = default)
    {
        var product = new BillingProduct
        {
            Id = Guid.NewGuid(),
            Code = request.Code,
            Name = request.Name,
            Description = request.Description,
            ProductType = request.ProductType,
            StripeProductId = request.StripeProductId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        foreach (var p in request.Prices)
        {
            product.Prices.Add(new BillingPrice
            {
                Id = Guid.NewGuid(),
                BillingProductId = product.Id,
                StripePriceId = p.StripePriceId,
                Currency = p.Currency,
                Amount = p.Amount,
                Interval = p.Interval,
                IntervalCount = p.IntervalCount,
                Country = p.Country,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        _db.BillingProducts.Add(product);
        await _db.SaveChangesAsync(ct);

        return MapProduct(product);
    }

    public async Task SetProductActiveAsync(Guid productId, bool isActive, CancellationToken ct = default)
    {
        var product = await _db.BillingProducts.FindAsync([productId], ct)
            ?? throw new InvalidOperationException($"Product {productId} not found.");
        product.IsActive = isActive;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // ── Admin product editor ────────────────────────────────────────────────

    private const string StatusActive = "active";
    private const string StatusDraft = "draft";
    private const string StatusArchived = "archived";

    /// <summary>Prices with a null Interval are one-time purchases; the UI renders this label.</summary>
    private const string OneTimeInterval = "one_time";

    public async Task<IReadOnlyList<AdminCatalogProductDto>> ListAdminProductsAsync(
        string? status = null, string? search = null, CancellationToken ct = default)
    {
        var query = _db.BillingProducts
            .Include(p => p.Prices)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            // Only "active" is distinguishable in storage; "draft" and "archived"
            // both live behind IsActive == false (see UpdateAdminProductAsync).
            var wantActive = string.Equals(status.Trim(), StatusActive, StringComparison.OrdinalIgnoreCase);
            query = query.Where(p => p.IsActive == wantActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // ToLower() rather than ILike so this stays provider-portable.
            var needle = search.Trim().ToLowerInvariant();
            query = query.Where(p =>
                p.Code.ToLower().Contains(needle) ||
                p.Name.ToLower().Contains(needle));
        }

        var products = await query
            .OrderBy(p => p.ProductType)
            .ThenBy(p => p.Name)
            .ToListAsync(ct);

        return products.Select(MapAdminProduct).ToList();
    }

    public async Task<AdminCatalogProductDto?> GetAdminProductByCodeAsync(
        string code, CancellationToken ct = default)
    {
        var product = await _db.BillingProducts
            .Include(p => p.Prices)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == code, ct);

        return product is null ? null : MapAdminProduct(product);
    }

    public async Task<AdminCatalogProductDto?> UpdateAdminProductAsync(
        string code, AdminCatalogProductUpdateRequest request, CancellationToken ct = default)
    {
        // BillingProduct has no image or ordering column. MetadataJson is NOT a
        // fallback: BillingCatalogSyncStartupTask rewrites it wholesale on every
        // boot for seeded products, and FulfillmentService parses it for credit
        // and mock entitlement grants. Reject loudly instead of dropping the
        // value or corrupting fulfillment metadata.
        if (!string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            throw ApiException.Validation(
                "product_image_not_supported",
                "Product image URLs cannot be saved: BillingProduct has no image column.",
                [new ApiFieldError("imageUrl", "unsupported_field", "Storing an image URL requires a schema migration.")]);
        }

        if (request.DisplayOrder is not null && request.DisplayOrder.Value != 0)
        {
            throw ApiException.Validation(
                "product_display_order_not_supported",
                "Product display order cannot be saved: BillingProduct has no ordering column.",
                [new ApiFieldError("displayOrder", "unsupported_field", "Storing a display order requires a schema migration.")]);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw ApiException.Validation(
                "product_name_required",
                "Product name is required.",
                [new ApiFieldError("name", "required", "Enter a display name.")]);
        }

        if (string.IsNullOrWhiteSpace(request.ProductType))
        {
            throw ApiException.Validation(
                "product_type_required",
                "Product type is required.",
                [new ApiFieldError("productType", "required", "Enter a product type (package, subscription or addon).")]);
        }

        var isActive = ResolveIsActive(request.Status);

        var product = await _db.BillingProducts
            .Include(p => p.Prices)
            .FirstOrDefaultAsync(p => p.Code == code, ct);
        if (product is null) return null;

        product.Name = request.Name.Trim();
        product.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        product.ProductType = request.ProductType.Trim();
        product.IsActive = isActive;
        product.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return MapAdminProduct(product);
    }

    /// <summary>
    /// The UI offers active/draft/archived but the entity only has a bool, so
    /// draft and archived both persist as inactive and both read back as
    /// "archived". Storing the literal status needs a schema migration.
    /// </summary>
    private static bool ResolveIsActive(string? status) => (status ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        StatusActive => true,
        StatusDraft or StatusArchived => false,
        _ => throw ApiException.Validation(
            "invalid_product_status",
            $"Unsupported product status '{status}'.",
            [new ApiFieldError("status", "invalid_status", "Use active, draft or archived.")])
    };

    private static AdminCatalogProductDto MapAdminProduct(BillingProduct p)
    {
        var prices = p.Prices
            .Where(pr => pr.IsActive)
            .OrderBy(pr => pr.Currency)
            .ThenBy(pr => pr.Amount)
            .Select(pr => new AdminCatalogPriceDto(
                pr.Id.ToString(),
                pr.Amount,
                pr.Currency,
                pr.Interval ?? OneTimeInterval))
            .ToList();

        return new AdminCatalogProductDto(
            p.Code,
            p.Name,
            p.Description,
            p.ProductType,
            p.IsActive ? StatusActive : StatusArchived,
            ImageUrl: null,
            DisplayOrder: 0,
            prices,
            JsonSupport.Deserialize(p.MetadataJson, new Dictionary<string, object?>()));
    }

    private static ProductDto MapProduct(BillingProduct p, string? country = null, string? currency = null)
    {
        var prices = p.Prices
            .Where(pr => pr.IsActive)
            .Where(pr => pr.Country is null || pr.Country == country)
            .Select(pr => new PriceDto(pr.Id, pr.StripePriceId, pr.Currency, pr.Amount,
                pr.Interval, pr.IntervalCount, pr.IsActive, pr.Country))
            .ToList();

        return new ProductDto(p.Id, p.Code, p.Name, p.Description, p.ProductType,
            p.StripeProductId, p.IsActive, prices);
    }
}
