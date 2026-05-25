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
