using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain.Billing;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Wave B4 — startup reconciler that mirrors the canonical Stripe seed
/// catalogue (<c>Data/Seeds/stripe-product-catalog.v1.json</c>, linked from
/// the StripeProductSeeder script project) into the <c>BillingProducts</c>
/// and <c>BillingPrices</c> tables.
///
/// <para>Idempotent UPSERT on metadata <c>code</c>. Rows are matched by
/// <see cref="BillingProduct.Code"/>; child <see cref="BillingPrice"/> rows
/// are matched on (Currency, Amount, Interval, IntervalCount). Re-running
/// after no source changes is a no-op — that property is asserted in
/// <c>BillingCatalogSyncStartupTaskTests</c>.</para>
///
/// <para>Logs every create/update so dashboards can spot drift.</para>
/// </summary>
public sealed class BillingCatalogSyncStartupTask(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment env,
    ILogger<BillingCatalogSyncStartupTask> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private const string CatalogFileName = "stripe-product-catalog.v1.json";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ReconcileAsync(stoppingToken);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            // Boot-time reconciler must NEVER bring down the API host on a
            // malformed manifest. Log and continue. The admin reseed endpoint
            // (and the tests) call ReconcileAsync directly and prefer the
            // exception so callers see exactly what failed.
            logger.LogError(ex, "BillingCatalogSyncStartupTask failed.");
        }
    }

    /// <summary>Public entry point so the admin reseed endpoint and tests can call directly.</summary>
    public async Task<SyncResult> ReconcileAsync(CancellationToken ct)
    {
        var path = ResolveCatalogPath();
        if (!File.Exists(path))
        {
            logger.LogWarning("BillingCatalogSyncStartupTask: manifest not found at {Path}.", path);
            return SyncResult.Empty;
        }

        StripeCatalogManifest manifest;
        await using (var stream = File.OpenRead(path))
        {
            manifest = await JsonSerializer.DeserializeAsync<StripeCatalogManifest>(stream, JsonOptions, ct)
                       ?? throw new InvalidOperationException("Stripe catalog manifest deserialized to null.");
        }
        if (manifest.Products is null || manifest.Products.Count == 0)
        {
            logger.LogWarning(
                "BillingCatalogSyncStartupTask: manifest at {Path} contained 0 products — skipping.",
                path);
            return SyncResult.Empty;
        }

        var result = new SyncResult();
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var now = DateTimeOffset.UtcNow;
        foreach (var dto in manifest.Products)
        {
            ct.ThrowIfCancellationRequested();
            await UpsertProductAsync(db, dto, now, result, ct);
        }

        await db.SaveChangesAsync(ct);

        if (result.ProductsCreated + result.ProductsUpdated + result.PricesCreated + result.PricesUpdated > 0)
        {
            logger.LogInformation(
                "BillingCatalogSyncStartupTask done: products created={ProductsCreated}/updated={ProductsUpdated}/unchanged={ProductsUnchanged}, prices created={PricesCreated}/updated={PricesUpdated}/unchanged={PricesUnchanged}.",
                result.ProductsCreated, result.ProductsUpdated, result.ProductsUnchanged,
                result.PricesCreated, result.PricesUpdated, result.PricesUnchanged);
        }
        else
        {
            logger.LogDebug(
                "BillingCatalogSyncStartupTask done: no changes (products unchanged={ProductsUnchanged}, prices unchanged={PricesUnchanged}).",
                result.ProductsUnchanged, result.PricesUnchanged);
        }
        return result;
    }

    private string ResolveCatalogPath()
        => Path.Combine(env.ContentRootPath, "Data", "Seeds", CatalogFileName);

    private async Task UpsertProductAsync(
        LearnerDbContext db,
        StripeCatalogProduct dto,
        DateTimeOffset now,
        SyncResult result,
        CancellationToken ct)
    {
        var product = await db.BillingProducts
            .Include(p => p.Prices)
            .FirstOrDefaultAsync(p => p.Code == dto.Code, ct);

        var metadataJson = JsonSerializer.Serialize(new
        {
            category = dto.Category,
            source = "stripe-product-catalog.v1.json"
        });

        if (product is null)
        {
            product = new BillingProduct
            {
                Id = Guid.NewGuid(),
                Code = dto.Code,
                Name = dto.Name,
                Description = dto.Description,
                ProductType = MapCategoryToProductType(dto.Category),
                IsActive = true,
                MetadataJson = metadataJson,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.BillingProducts.Add(product);
            result.ProductsCreated++;
            logger.LogInformation("BillingCatalogSync: created product {Code}.", dto.Code);
        }
        else
        {
            var dirty = false;
            var targetType = MapCategoryToProductType(dto.Category);
            if (!string.Equals(product.Name, dto.Name, StringComparison.Ordinal)) { product.Name = dto.Name; dirty = true; }
            if (!string.Equals(product.Description ?? string.Empty, dto.Description ?? string.Empty, StringComparison.Ordinal)) { product.Description = dto.Description; dirty = true; }
            if (!string.Equals(product.ProductType, targetType, StringComparison.Ordinal)) { product.ProductType = targetType; dirty = true; }
            if (!string.Equals(product.MetadataJson ?? string.Empty, metadataJson, StringComparison.Ordinal)) { product.MetadataJson = metadataJson; dirty = true; }
            if (!product.IsActive) { product.IsActive = true; dirty = true; }

            if (dirty)
            {
                product.UpdatedAt = now;
                result.ProductsUpdated++;
                logger.LogInformation("BillingCatalogSync: updated product {Code}.", dto.Code);
            }
            else
            {
                result.ProductsUnchanged++;
            }
        }

        foreach (var priceDto in dto.Prices)
        {
            var existing = product.Prices.FirstOrDefault(p => PriceMatches(p, priceDto));
            if (existing is null)
            {
                product.Prices.Add(new BillingPrice
                {
                    Id = Guid.NewGuid(),
                    BillingProductId = product.Id,
                    Currency = NormaliseCurrency(priceDto.Currency),
                    Amount = priceDto.UnitAmount,
                    Interval = priceDto.Interval,
                    IntervalCount = (int)(priceDto.IntervalCount ?? 1),
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                result.PricesCreated++;
                logger.LogInformation(
                    "BillingCatalogSync: created price for {Code} ({Currency} {Amount} {Interval}).",
                    dto.Code, priceDto.Currency, priceDto.UnitAmount, priceDto.Interval ?? "one_time");
            }
            else if (!existing.IsActive)
            {
                existing.IsActive = true;
                existing.UpdatedAt = now;
                result.PricesUpdated++;
            }
            else
            {
                result.PricesUnchanged++;
            }
        }
    }

    private static bool PriceMatches(BillingPrice existing, StripeCatalogPrice dto)
    {
        if (!string.Equals(NormaliseCurrency(existing.Currency), NormaliseCurrency(dto.Currency), StringComparison.OrdinalIgnoreCase)) return false;
        if (existing.Amount != dto.UnitAmount) return false;
        if (!string.Equals(existing.Interval ?? string.Empty, dto.Interval ?? string.Empty, StringComparison.OrdinalIgnoreCase)) return false;
        if (dto.Interval is null) return true;
        var dtoCount = (int)(dto.IntervalCount ?? 1);
        return existing.IntervalCount == dtoCount;
    }

    private static string MapCategoryToProductType(string category) => category switch
    {
        "subscription" => "subscription",
        "package" => "package",
        "class_pack" or "addon" => "addon",
        _ => "addon"
    };

    private static string NormaliseCurrency(string? currency)
    {
        // BillingPrice.Currency caps at 3 chars; Stripe uses lower-case ISO 4217.
        if (string.IsNullOrWhiteSpace(currency)) return "USD";
        var trimmed = currency.Trim().ToUpperInvariant();
        return trimmed.Length <= 3 ? trimmed : trimmed[..3];
    }

    public sealed class SyncResult
    {
        public int ProductsCreated { get; set; }
        public int ProductsUpdated { get; set; }
        public int ProductsUnchanged { get; set; }
        public int PricesCreated { get; set; }
        public int PricesUpdated { get; set; }
        public int PricesUnchanged { get; set; }

        public static SyncResult Empty => new();
    }

    // Local copies of the catalog manifest types — kept here so the API
    // project never references the StripeProductSeeder script project.
    public sealed class StripeCatalogManifest
    {
        [JsonPropertyName("$schema")] public string? Schema { get; set; }
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("defaultCurrency")] public string DefaultCurrency { get; set; } = "usd";
        [JsonPropertyName("products")] public List<StripeCatalogProduct> Products { get; set; } = new();
    }

    public sealed class StripeCatalogProduct
    {
        [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("category")] public string Category { get; set; } = "package";
        [JsonPropertyName("prices")] public List<StripeCatalogPrice> Prices { get; set; } = new();
    }

    public sealed class StripeCatalogPrice
    {
        [JsonPropertyName("unitAmount")] public long UnitAmount { get; set; }
        [JsonPropertyName("currency")] public string Currency { get; set; } = "usd";
        [JsonPropertyName("interval")] public string? Interval { get; set; }
        [JsonPropertyName("intervalCount")] public long? IntervalCount { get; set; }
    }
}
