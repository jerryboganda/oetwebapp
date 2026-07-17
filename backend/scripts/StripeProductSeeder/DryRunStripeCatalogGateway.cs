namespace OetWithDrHesham.Scripts.StripeProductSeeder;

/// <summary>
/// In-memory gateway used by <c>--dry-run</c> and by the test suite. Treats
/// every product as missing and every price as new, so the resulting log
/// shows exactly what a fresh seed would do.
/// </summary>
public sealed class DryRunStripeCatalogGateway : IStripeCatalogGateway
{
    private readonly Dictionary<string, StripeProductRecord> _productsByCode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<StripePriceRecord>> _pricesByProductId = new(StringComparer.Ordinal);
    private int _nextProduct = 1;
    private int _nextPrice = 1;

    public Task<StripeProductRecord?> FindProductByCodeAsync(string code, CancellationToken ct)
        => Task.FromResult(_productsByCode.GetValueOrDefault(code));

    public Task<StripeProductRecord> CreateProductAsync(StripeProductUpsert input, CancellationToken ct)
    {
        var id = $"prod_dryrun_{_nextProduct++:D4}";
        var product = new StripeProductRecord(id, input.Name, input.Description, input.Metadata, Active: true);
        if (input.Metadata.TryGetValue("code", out var code))
        {
            _productsByCode[code] = product;
        }
        _pricesByProductId[id] = new List<StripePriceRecord>();
        return Task.FromResult(product);
    }

    public Task UpdateProductAsync(string productId, StripeProductUpsert input, CancellationToken ct)
    {
        var existing = _productsByCode.Values.FirstOrDefault(p => p.Id == productId);
        if (existing is null) return Task.CompletedTask;
        var patched = existing with { Name = input.Name, Description = input.Description, Metadata = input.Metadata };
        if (input.Metadata.TryGetValue("code", out var code))
        {
            _productsByCode[code] = patched;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StripePriceRecord>> ListPricesAsync(string productId, CancellationToken ct)
    {
        var list = _pricesByProductId.GetValueOrDefault(productId) ?? new List<StripePriceRecord>();
        return Task.FromResult<IReadOnlyList<StripePriceRecord>>(list);
    }

    public Task<StripePriceRecord> CreatePriceAsync(string productId, StripePriceCreate input, CancellationToken ct)
    {
        var id = $"price_dryrun_{_nextPrice++:D4}";
        var price = new StripePriceRecord(id, input.Currency, input.UnitAmount, input.Interval, input.IntervalCount, Active: true, input.Metadata);
        if (!_pricesByProductId.TryGetValue(productId, out var list))
        {
            list = new List<StripePriceRecord>();
            _pricesByProductId[productId] = list;
        }
        list.Add(price);
        return Task.FromResult(price);
    }
}
