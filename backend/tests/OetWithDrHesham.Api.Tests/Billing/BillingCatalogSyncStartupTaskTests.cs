using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Services.Billing;
using Xunit;

namespace OetWithDrHesham.Api.Tests.Billing;

/// <summary>
/// Wave B4 — verifies the startup reconciler mirrors stripe-product-catalog.v1.json
/// into the local DB without drift on re-runs.
/// </summary>
public sealed class BillingCatalogSyncStartupTaskTests : IDisposable
{
    private readonly string _contentRoot;
    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServiceProvider _provider;

    public BillingCatalogSyncStartupTaskTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), $"oet-catalog-sync-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_contentRoot, "Data", "Seeds"));

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<LearnerDbContext>(opt => opt.UseSqlite(_connection));
        _provider = services.BuildServiceProvider();
        _scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
        var rootScope = _provider.CreateScope();
        _db = rootScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
        if (Directory.Exists(_contentRoot))
        {
            try { Directory.Delete(_contentRoot, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task Reconcile_FromManifest_CreatesProductsAndPrices()
    {
        WriteManifest(SampleManifestJson);
        var path = Path.Combine(_contentRoot, "Data", "Seeds", "stripe-product-catalog.v1.json");
        Assert.True(File.Exists(path), $"Expected manifest at {path}");
        var raw = File.ReadAllText(path);
        Assert.Contains("\"pkg_quick_check\"", raw);
        var task = CreateTask();

        var first = await task.ReconcileAsync(CancellationToken.None);

        Assert.Equal(2, first.ProductsCreated);
        Assert.Equal(0, first.ProductsUpdated);
        Assert.Equal(2, first.PricesCreated);

        var products = await _db.BillingProducts.Include(p => p.Prices).ToListAsync();
        Assert.Equal(2, products.Count);
        var monthly = products.Single(p => p.Code == "sub_mastery_monthly");
        Assert.Equal("subscription", monthly.ProductType);
        Assert.True(monthly.IsActive);
        var price = Assert.Single(monthly.Prices);
        Assert.Equal(10000m, price.Amount);
        Assert.Equal("USD", price.Currency);
        Assert.Equal("month", price.Interval);

        var quickCheck = products.Single(p => p.Code == "pkg_quick_check");
        Assert.Equal("package", quickCheck.ProductType);
        var oneTime = Assert.Single(quickCheck.Prices);
        Assert.Null(oneTime.Interval);
    }

    [Fact]
    public async Task Reconcile_RunTwice_NoDiffs()
    {
        WriteManifest(SampleManifestJson);
        var task = CreateTask();

        var first = await task.ReconcileAsync(CancellationToken.None);
        Assert.True(first.ProductsCreated + first.PricesCreated > 0);

        var second = await task.ReconcileAsync(CancellationToken.None);
        Assert.Equal(0, second.ProductsCreated);
        Assert.Equal(0, second.ProductsUpdated);
        Assert.Equal(2, second.ProductsUnchanged);
        Assert.Equal(0, second.PricesCreated);
        Assert.Equal(0, second.PricesUpdated);
        Assert.Equal(2, second.PricesUnchanged);
    }

    [Fact]
    public async Task Reconcile_UpdatesNameAndDescription_WhenManifestChanges()
    {
        WriteManifest(SampleManifestJson);
        var task = CreateTask();
        await task.ReconcileAsync(CancellationToken.None);

        WriteManifest(SampleManifestJson
            .Replace("\"Quick Check\"", "\"Quick Check Renamed\"")
            .Replace("\"5 AI grading credits, 30-day validity\"", "\"Edited.\""));

        var result = await task.ReconcileAsync(CancellationToken.None);
        Assert.Equal(1, result.ProductsUpdated);
        Assert.Equal(1, result.ProductsUnchanged);
        Assert.Equal(0, result.PricesCreated);

        var updated = await _db.BillingProducts.SingleAsync(p => p.Code == "pkg_quick_check");
        Assert.Equal("Quick Check Renamed", updated.Name);
        Assert.Equal("Edited.", updated.Description);
    }

    [Fact]
    public async Task Reconcile_AddingNewPrice_OnlyCreatesTheNewOne()
    {
        WriteManifest(SampleManifestJson);
        var task = CreateTask();
        await task.ReconcileAsync(CancellationToken.None);

        // Add a new price for the monthly subscription (e.g., a discounted price)
        var withExtraPrice = SampleManifestJson.Replace(
            "{ \"unitAmount\": 10000, \"currency\": \"usd\", \"interval\": \"month\", \"intervalCount\": 1 }",
            "{ \"unitAmount\": 10000, \"currency\": \"usd\", \"interval\": \"month\", \"intervalCount\": 1 },\n        { \"unitAmount\": 8000, \"currency\": \"usd\", \"interval\": \"month\", \"intervalCount\": 1 }");
        WriteManifest(withExtraPrice);

        var result = await task.ReconcileAsync(CancellationToken.None);
        Assert.Equal(1, result.PricesCreated);
        Assert.Equal(2, result.PricesUnchanged);

        var monthly = await _db.BillingProducts.Include(p => p.Prices).SingleAsync(p => p.Code == "sub_mastery_monthly");
        Assert.Equal(2, monthly.Prices.Count);
        Assert.Contains(monthly.Prices, p => p.Amount == 10000m);
        Assert.Contains(monthly.Prices, p => p.Amount == 8000m);
    }

    [Fact]
    public async Task Reconcile_MissingManifest_IsHarmless()
    {
        // No file written.
        var task = CreateTask();
        var result = await task.ReconcileAsync(CancellationToken.None);
        Assert.Equal(0, result.ProductsCreated);
        Assert.Equal(0, result.ProductsUpdated);
        Assert.Equal(0, result.PricesCreated);
    }

    private BillingCatalogSyncStartupTask CreateTask()
    {
        var env = new TestHostEnvironment(_contentRoot);
        return new BillingCatalogSyncStartupTask(_scopeFactory, env, NullLogger<BillingCatalogSyncStartupTask>.Instance);
    }

    private void WriteManifest(string json)
    {
        var path = Path.Combine(_contentRoot, "Data", "Seeds", "stripe-product-catalog.v1.json");
        File.WriteAllText(path, json);
    }

    private const string SampleManifestJson = @"{
  ""version"": ""1.0.0"",
  ""defaultCurrency"": ""usd"",
  ""products"": [
    {
      ""code"": ""pkg_quick_check"",
      ""name"": ""Quick Check"",
      ""description"": ""5 AI grading credits, 30-day validity"",
      ""category"": ""package"",
      ""prices"": [
        { ""unitAmount"": 1900, ""currency"": ""usd"", ""interval"": null, ""intervalCount"": null }
      ]
    },
    {
      ""code"": ""sub_mastery_monthly"",
      ""name"": ""OET Mastery Subscription (Monthly)"",
      ""description"": ""Monthly Mastery"",
      ""category"": ""subscription"",
      ""prices"": [
        { ""unitAmount"": 10000, ""currency"": ""usd"", ""interval"": ""month"", ""intervalCount"": 1 }
      ]
    }
  ]
}";

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string contentRoot)
        {
            ContentRootPath = contentRoot;
            EnvironmentName = "Development";
            ApplicationName = "OetWithDrHesham.Api.Tests";
            ContentRootFileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(contentRoot);
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; }
        public string ContentRootPath { get; set; }
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
    }
}
