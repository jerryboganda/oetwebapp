namespace OetLearner.Scripts.StripeProductSeeder;

/// <summary>
/// Reconciles the catalogue manifest against Stripe. Idempotent —
/// re-running with no source changes produces zero side effects.
///
/// <para>Behaviour:</para>
/// <list type="bullet">
///   <item>Product matched on metadata.code. Missing: create. Existing: patch
///         Name/Description/Metadata in place.</item>
///   <item>Price matched on (currency, unitAmount, interval, intervalCount).
///         Stripe Prices are immutable — when a match is found we reuse it.
///         When none matches we create a new one and leave any obsolete
///         prices alone (the operator deactivates them manually if needed).</item>
/// </list>
/// </summary>
public sealed class StripeCatalogSeeder
{
    private readonly IStripeCatalogGateway _gateway;
    private readonly Action<string> _log;

    public StripeCatalogSeeder(IStripeCatalogGateway gateway, Action<string>? log = null)
    {
        _gateway = gateway;
        _log = log ?? Console.WriteLine;
    }

    public async Task<SeedResult> SeedAsync(CatalogManifest manifest, CancellationToken ct)
    {
        var result = new SeedResult();
        foreach (var product in manifest.Products)
        {
            ct.ThrowIfCancellationRequested();
            var productResult = await ReconcileProductAsync(product, ct);
            result.Items.Add(productResult);
            if (productResult.ProductCreated) result.ProductsCreated++;
            else if (productResult.ProductUpdated) result.ProductsUpdated++;
            else result.ProductsUnchanged++;
            result.PricesCreated += productResult.PricesCreated;
            result.PricesReused += productResult.PricesReused;
        }
        _log($"Stripe seed done: products created={result.ProductsCreated}, updated={result.ProductsUpdated}, unchanged={result.ProductsUnchanged}; prices created={result.PricesCreated}, reused={result.PricesReused}.");
        return result;
    }

    private async Task<SeedItemResult> ReconcileProductAsync(CatalogProduct dto, CancellationToken ct)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["code"] = dto.Code,
            ["category"] = dto.Category
        };
        var upsert = new StripeProductUpsert(dto.Name, dto.Description, metadata);

        var existing = await _gateway.FindProductByCodeAsync(dto.Code, ct);
        StripeProductRecord product;
        var created = false;
        var updated = false;

        if (existing is null)
        {
            product = await _gateway.CreateProductAsync(upsert, ct);
            created = true;
            _log($"  + created product {dto.Code} -> {product.Id}");
        }
        else if (NeedsProductPatch(existing, upsert))
        {
            await _gateway.UpdateProductAsync(existing.Id, upsert, ct);
            // Re-fetch a fresh view so subsequent steps see the patched metadata.
            product = new StripeProductRecord(
                existing.Id,
                upsert.Name,
                upsert.Description,
                upsert.Metadata,
                existing.Active);
            updated = true;
            _log($"  ~ patched product {dto.Code} ({product.Id})");
        }
        else
        {
            product = existing;
            _log($"  = product {dto.Code} unchanged ({product.Id})");
        }

        var pricesNow = await _gateway.ListPricesAsync(product.Id, ct);
        var pricesCreated = 0;
        var pricesReused = 0;

        foreach (var price in dto.Prices)
        {
            var match = pricesNow.FirstOrDefault(p => PriceMatches(p, price));
            if (match is not null)
            {
                pricesReused++;
                continue;
            }

            var priceMeta = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["productCode"] = dto.Code,
                ["category"] = dto.Category
            };
            var createdPrice = await _gateway.CreatePriceAsync(product.Id, new StripePriceCreate(
                price.Currency,
                price.UnitAmount,
                price.Interval,
                price.IntervalCount,
                priceMeta), ct);
            pricesCreated++;
            _log($"    + created price {createdPrice.Id} ({price.Currency} {price.UnitAmount} {price.Interval ?? "one_time"})");
        }

        return new SeedItemResult(
            dto.Code,
            product.Id,
            ProductCreated: created,
            ProductUpdated: updated,
            PricesCreated: pricesCreated,
            PricesReused: pricesReused);
    }

    private static bool NeedsProductPatch(StripeProductRecord existing, StripeProductUpsert upsert)
    {
        if (!string.Equals(existing.Name, upsert.Name, StringComparison.Ordinal)) return true;
        if (!string.Equals(existing.Description ?? string.Empty, upsert.Description ?? string.Empty, StringComparison.Ordinal)) return true;
        foreach (var kv in upsert.Metadata)
        {
            if (!existing.Metadata.TryGetValue(kv.Key, out var current) || !string.Equals(current, kv.Value, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static bool PriceMatches(StripePriceRecord existing, CatalogPrice dto)
    {
        if (!string.Equals(existing.Currency, dto.Currency, StringComparison.OrdinalIgnoreCase)) return false;
        if (existing.UnitAmount != dto.UnitAmount) return false;
        var existingInterval = existing.Interval;
        var dtoInterval = dto.Interval;
        if (!string.Equals(existingInterval, dtoInterval, StringComparison.OrdinalIgnoreCase)) return false;
        if (dtoInterval is null) return true; // one-time prices: ignore intervalCount
        var existingCount = existing.IntervalCount ?? 1;
        var dtoCount = dto.IntervalCount ?? 1;
        return existingCount == dtoCount;
    }
}

public sealed class SeedResult
{
    public int ProductsCreated { get; set; }
    public int ProductsUpdated { get; set; }
    public int ProductsUnchanged { get; set; }
    public int PricesCreated { get; set; }
    public int PricesReused { get; set; }
    public List<SeedItemResult> Items { get; } = new();
}

public sealed record SeedItemResult(
    string Code,
    string ProductId,
    bool ProductCreated,
    bool ProductUpdated,
    int PricesCreated,
    int PricesReused);
