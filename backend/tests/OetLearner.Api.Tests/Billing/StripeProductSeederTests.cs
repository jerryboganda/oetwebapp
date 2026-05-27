using OetLearner.Scripts.StripeProductSeeder;
using Xunit;

namespace OetLearner.Api.Tests.Billing;

/// <summary>
/// Wave B4 — verifies the Stripe seeder against a fake gateway. The CLI
/// itself never hits Stripe in this suite — that is the explicit constraint
/// from the wave plan.
/// </summary>
public class StripeProductSeederTests
{
    [Fact]
    public async Task FirstRun_CreatesProductAndPrice()
    {
        var gateway = new FakeStripeCatalogGateway();
        var seeder = new StripeCatalogSeeder(gateway, log: _ => { });
        var manifest = SmallManifest();

        var result = await seeder.SeedAsync(manifest, CancellationToken.None);

        Assert.Equal(1, result.ProductsCreated);
        Assert.Equal(0, result.ProductsUpdated);
        Assert.Equal(0, result.ProductsUnchanged);
        Assert.Equal(1, result.PricesCreated);
        Assert.Equal(0, result.PricesReused);

        var product = Assert.Single(gateway.Products);
        Assert.Equal("pkg_quick_check", product.Metadata["code"]);
        Assert.Equal("package", product.Metadata["category"]);
        Assert.Equal("Quick Check", product.Name);

        var price = Assert.Single(gateway.PricesByProductId[product.Id]);
        Assert.Equal(1900, price.UnitAmount);
        Assert.Equal("usd", price.Currency);
        Assert.Null(price.Interval);
        Assert.Equal("pkg_quick_check", price.Metadata["productCode"]);
    }

    [Fact]
    public async Task SecondRun_NoSourceChanges_IsNoop()
    {
        var gateway = new FakeStripeCatalogGateway();
        var seeder = new StripeCatalogSeeder(gateway, log: _ => { });
        var manifest = SmallManifest();

        await seeder.SeedAsync(manifest, CancellationToken.None);
        var second = await seeder.SeedAsync(manifest, CancellationToken.None);

        Assert.Equal(0, second.ProductsCreated);
        Assert.Equal(0, second.ProductsUpdated);
        Assert.Equal(1, second.ProductsUnchanged);
        Assert.Equal(0, second.PricesCreated);
        Assert.Equal(1, second.PricesReused);
    }

    [Fact]
    public async Task NameChange_PatchesProduct_WithoutCreatingNewPrice()
    {
        var gateway = new FakeStripeCatalogGateway();
        var seeder = new StripeCatalogSeeder(gateway, log: _ => { });
        await seeder.SeedAsync(SmallManifest(), CancellationToken.None);

        var edited = SmallManifest();
        edited.Products[0].Name = "Quick Check (Renamed)";
        edited.Products[0].Description = "Edited description.";

        var result = await seeder.SeedAsync(edited, CancellationToken.None);

        Assert.Equal(0, result.ProductsCreated);
        Assert.Equal(1, result.ProductsUpdated);
        Assert.Equal(1, result.PricesReused);
        Assert.Equal(0, result.PricesCreated);
        var product = Assert.Single(gateway.Products);
        Assert.Equal("Quick Check (Renamed)", product.Name);
        Assert.Equal("Edited description.", product.Description);
    }

    [Fact]
    public async Task PriceChange_CreatesNewPriceAndKeepsOldOne()
    {
        var gateway = new FakeStripeCatalogGateway();
        var seeder = new StripeCatalogSeeder(gateway, log: _ => { });
        await seeder.SeedAsync(SmallManifest(), CancellationToken.None);

        var edited = SmallManifest();
        edited.Products[0].Prices[0].UnitAmount = 2500;

        var result = await seeder.SeedAsync(edited, CancellationToken.None);

        Assert.Equal(0, result.ProductsCreated);
        Assert.Equal(1, result.PricesCreated);
        var product = Assert.Single(gateway.Products);
        Assert.Equal(2, gateway.PricesByProductId[product.Id].Count);
        // Both prices exist — Stripe Prices are immutable, the seeder never
        // archives the old one.
        Assert.Contains(gateway.PricesByProductId[product.Id], p => p.UnitAmount == 1900);
        Assert.Contains(gateway.PricesByProductId[product.Id], p => p.UnitAmount == 2500);
    }

    [Fact]
    public async Task Metadata_Includes_Code_And_Category_On_Product_And_Price()
    {
        var gateway = new FakeStripeCatalogGateway();
        var seeder = new StripeCatalogSeeder(gateway, log: _ => { });
        await seeder.SeedAsync(SmallManifest(), CancellationToken.None);

        var product = Assert.Single(gateway.Products);
        Assert.Equal("pkg_quick_check", product.Metadata["code"]);
        Assert.Equal("package", product.Metadata["category"]);
        var price = Assert.Single(gateway.PricesByProductId[product.Id]);
        Assert.Equal("pkg_quick_check", price.Metadata["productCode"]);
        Assert.Equal("package", price.Metadata["category"]);
    }

    [Fact]
    public async Task LoadingTheShippedCatalog_ProducesNineteenPlusProductsOnFirstRun()
    {
        // Loads the canonical catalog.json from the StripeProductSeeder
        // build output so we don't drift if §10 grows additional SKUs.
        var manifest = await LoadShippedManifestAsync();
        var gateway = new FakeStripeCatalogGateway();
        var seeder = new StripeCatalogSeeder(gateway, log: _ => { });

        var result = await seeder.SeedAsync(manifest, CancellationToken.None);

        Assert.Equal(manifest.Products.Count, result.ProductsCreated);
        Assert.True(manifest.Products.Count >= 20,
            "Catalog should contain the full §10.2 SKU set (20+ products).");
        Assert.Contains(manifest.Products, p => p.Code == "pkg_quick_check");
        Assert.Contains(manifest.Products, p => p.Code == "sub_mastery_annual");
        Assert.Contains(manifest.Products, p => p.Code == "addon_priority_grade");

        var rerun = await seeder.SeedAsync(manifest, CancellationToken.None);
        Assert.Equal(0, rerun.ProductsCreated);
        Assert.Equal(0, rerun.ProductsUpdated);
        Assert.Equal(manifest.Products.Count, rerun.ProductsUnchanged);
    }

    private static CatalogManifest SmallManifest() => new()
    {
        Version = "test",
        DefaultCurrency = "usd",
        Products = new()
        {
            new CatalogProduct
            {
                Code = "pkg_quick_check",
                Name = "Quick Check",
                Description = "5 AI grading credits, 30-day validity",
                Category = "package",
                Prices = new()
                {
                    new CatalogPrice { UnitAmount = 1900, Currency = "usd", Interval = null }
                }
            }
        }
    };

    private static async Task<CatalogManifest> LoadShippedManifestAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "catalog.json");
        Assert.True(File.Exists(path), $"Shipped catalog should be alongside the test binary at {path}.");
        await using var stream = File.OpenRead(path);
        var manifest = await System.Text.Json.JsonSerializer.DeserializeAsync<CatalogManifest>(
            stream, CatalogManifest.JsonOptions);
        Assert.NotNull(manifest);
        return manifest!;
    }

    private sealed class FakeStripeCatalogGateway : IStripeCatalogGateway
    {
        public List<StripeProductRecord> Products { get; } = new();
        public Dictionary<string, List<StripePriceRecord>> PricesByProductId { get; } = new(StringComparer.Ordinal);
        private int _next = 1;

        public Task<StripeProductRecord?> FindProductByCodeAsync(string code, CancellationToken ct)
            => Task.FromResult(Products.FirstOrDefault(p =>
                p.Metadata.TryGetValue("code", out var c) && c == code));

        public Task<StripeProductRecord> CreateProductAsync(StripeProductUpsert input, CancellationToken ct)
        {
            var id = $"prod_test_{_next++:D4}";
            var product = new StripeProductRecord(id, input.Name, input.Description, input.Metadata, Active: true);
            Products.Add(product);
            PricesByProductId[id] = new List<StripePriceRecord>();
            return Task.FromResult(product);
        }

        public Task UpdateProductAsync(string productId, StripeProductUpsert input, CancellationToken ct)
        {
            var index = Products.FindIndex(p => p.Id == productId);
            if (index >= 0)
            {
                Products[index] = Products[index] with
                {
                    Name = input.Name,
                    Description = input.Description,
                    Metadata = input.Metadata
                };
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StripePriceRecord>> ListPricesAsync(string productId, CancellationToken ct)
        {
            var list = PricesByProductId.GetValueOrDefault(productId) ?? new List<StripePriceRecord>();
            return Task.FromResult<IReadOnlyList<StripePriceRecord>>(list);
        }

        public Task<StripePriceRecord> CreatePriceAsync(string productId, StripePriceCreate input, CancellationToken ct)
        {
            var id = $"price_test_{_next++:D4}";
            var price = new StripePriceRecord(
                id,
                input.Currency,
                input.UnitAmount,
                input.Interval,
                input.IntervalCount,
                Active: true,
                input.Metadata);
            PricesByProductId[productId].Add(price);
            return Task.FromResult(price);
        }
    }
}
