using Stripe;

namespace OetLearner.Scripts.StripeProductSeeder;

/// <summary>
/// Production gateway backed by the Stripe.net SDK. Tests never instantiate
/// this — they use an in-memory fake against <see cref="IStripeCatalogGateway"/>.
/// </summary>
public sealed class StripeCatalogGateway : IStripeCatalogGateway
{
    private readonly ProductService _products = new();
    private readonly PriceService _prices = new();

    public async Task<StripeProductRecord?> FindProductByCodeAsync(string code, CancellationToken ct)
    {
        // Stripe Products.List doesn't filter by metadata; we page through actives
        // looking for metadata.code. The seed catalogue is small (~20 entries) so
        // a single page is enough in practice.
        var page = await _products.ListAsync(new ProductListOptions
        {
            Active = true,
            Limit = 100
        }, cancellationToken: ct);

        foreach (var product in page.Data)
        {
            if (product.Metadata != null
                && product.Metadata.TryGetValue("code", out var found)
                && string.Equals(found, code, StringComparison.Ordinal))
            {
                return ToRecord(product);
            }
        }
        return null;
    }

    public async Task<StripeProductRecord> CreateProductAsync(StripeProductUpsert input, CancellationToken ct)
    {
        var created = await _products.CreateAsync(new ProductCreateOptions
        {
            Name = input.Name,
            Description = input.Description,
            Metadata = input.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value)
        }, cancellationToken: ct);
        return ToRecord(created);
    }

    public async Task UpdateProductAsync(string productId, StripeProductUpsert input, CancellationToken ct)
    {
        await _products.UpdateAsync(productId, new ProductUpdateOptions
        {
            Name = input.Name,
            Description = input.Description,
            Metadata = input.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value)
        }, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<StripePriceRecord>> ListPricesAsync(string productId, CancellationToken ct)
    {
        var page = await _prices.ListAsync(new PriceListOptions
        {
            Product = productId,
            Active = true,
            Limit = 100
        }, cancellationToken: ct);
        return page.Data.Select(ToRecord).ToList();
    }

    public async Task<StripePriceRecord> CreatePriceAsync(string productId, StripePriceCreate input, CancellationToken ct)
    {
        var options = new PriceCreateOptions
        {
            Product = productId,
            Currency = input.Currency,
            UnitAmount = input.UnitAmount,
            Metadata = input.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value)
        };
        if (!string.IsNullOrWhiteSpace(input.Interval))
        {
            options.Recurring = new PriceRecurringOptions
            {
                Interval = input.Interval,
                IntervalCount = input.IntervalCount ?? 1
            };
        }
        var created = await _prices.CreateAsync(options, cancellationToken: ct);
        return ToRecord(created);
    }

    private static StripeProductRecord ToRecord(Product product) => new(
        product.Id,
        product.Name,
        product.Description,
        product.Metadata is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(product.Metadata),
        product.Active);

    private static StripePriceRecord ToRecord(Price price) => new(
        price.Id,
        price.Currency,
        price.UnitAmount ?? 0,
        price.Recurring?.Interval,
        price.Recurring?.IntervalCount,
        price.Active,
        price.Metadata is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(price.Metadata));
}
