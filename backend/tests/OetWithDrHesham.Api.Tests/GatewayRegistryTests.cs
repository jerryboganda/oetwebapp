using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.Billing;

namespace OetWithDrHesham.Api.Tests;

public class GatewayRegistryTests
{
    private static LearnerDbContext NewContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LearnerDbContext(options);
    }

    private static FakeGatewayProvider NewProvider(params string[] supported)
        => new(supported);

    private static GatewayRoutingConfig Route(string id, string region, string currency, string product, string gateway, int priority, bool enabled = true)
        => new()
        {
            Id = id,
            Region = region,
            Currency = currency,
            ProductType = product,
            GatewayName = gateway,
            Priority = priority,
            IsEnabled = enabled,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task Resolve_PicksHighestPriorityMatch()
    {
        await using var db = NewContext(nameof(Resolve_PicksHighestPriorityMatch));
        db.GatewayRoutingConfigs.AddRange(
            Route("a", "UK", "GBP", "subscription", "stripe", 10),
            Route("b", "UK", "GBP", "subscription", "paypal", 20));
        await db.SaveChangesAsync();

        var registry = new GatewayRegistry(db, NewProvider("stripe", "paypal"));
        var gateway = await registry.ResolveAsync(new GatewayRouteRequest("UK", "GBP", "subscription"), CancellationToken.None);

        Assert.Equal("stripe", gateway.GatewayName);
    }

    [Fact]
    public async Task Resolve_PrefersExactRegionOverRowFallback()
    {
        await using var db = NewContext(nameof(Resolve_PrefersExactRegionOverRowFallback));
        db.GatewayRoutingConfigs.AddRange(
            Route("a", "ROW", "*", "*", "paypal", 1),
            Route("b", "UK", "GBP", "subscription", "stripe", 99));
        await db.SaveChangesAsync();

        var registry = new GatewayRegistry(db, NewProvider("stripe", "paypal"));
        var gateway = await registry.ResolveAsync(new GatewayRouteRequest("UK", "GBP", "subscription"), CancellationToken.None);

        Assert.Equal("stripe", gateway.GatewayName);
    }

    [Fact]
    public async Task Resolve_FallsBackToRowWhenRegionHasNoRoute()
    {
        await using var db = NewContext(nameof(Resolve_FallsBackToRowWhenRegionHasNoRoute));
        db.GatewayRoutingConfigs.Add(Route("a", "ROW", "*", "*", "stripe", 10));
        await db.SaveChangesAsync();

        var registry = new GatewayRegistry(db, NewProvider("stripe"));
        var gateway = await registry.ResolveAsync(new GatewayRouteRequest("EGYPT", "EGP", "subscription"), CancellationToken.None);

        Assert.Equal("stripe", gateway.GatewayName);
    }

    [Fact]
    public async Task Resolve_SkipsDisabledRoutes()
    {
        await using var db = NewContext(nameof(Resolve_SkipsDisabledRoutes));
        db.GatewayRoutingConfigs.AddRange(
            Route("a", "UK", "GBP", "subscription", "stripe", 10, enabled: false),
            Route("b", "UK", "GBP", "subscription", "paypal", 20));
        await db.SaveChangesAsync();

        var registry = new GatewayRegistry(db, NewProvider("stripe", "paypal"));
        var gateway = await registry.ResolveAsync(new GatewayRouteRequest("UK", "GBP", "subscription"), CancellationToken.None);

        Assert.Equal("paypal", gateway.GatewayName);
    }

    [Fact]
    public async Task Resolve_SkipsRouteWhoseGatewayIsNotRegistered()
    {
        await using var db = NewContext(nameof(Resolve_SkipsRouteWhoseGatewayIsNotRegistered));
        db.GatewayRoutingConfigs.AddRange(
            Route("a", "UK", "GBP", "subscription", "paytabs", 10),
            Route("b", "UK", "GBP", "subscription", "stripe", 20));
        await db.SaveChangesAsync();

        var registry = new GatewayRegistry(db, NewProvider("stripe"));
        var gateway = await registry.ResolveAsync(new GatewayRouteRequest("UK", "GBP", "subscription"), CancellationToken.None);

        Assert.Equal("stripe", gateway.GatewayName);
    }

    [Fact]
    public async Task Resolve_ThrowsWhenNoRouteMatches()
    {
        await using var db = NewContext(nameof(Resolve_ThrowsWhenNoRouteMatches));
        var registry = new GatewayRegistry(db, NewProvider("stripe"));

        await Assert.ThrowsAsync<NoMatchingGatewayException>(() =>
            registry.ResolveAsync(new GatewayRouteRequest("GULF", "AED", "subscription"), CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAll_ReturnsOrderedByRouteSpecificityAndPriority()
    {
        await using var db = NewContext(nameof(ResolveAll_ReturnsOrderedByRouteSpecificityAndPriority));
        db.GatewayRoutingConfigs.AddRange(
            Route("a", "ROW", "*", "*", "paypal", 1),
            Route("b", "UK", "*", "*", "stripe", 50),
            Route("c", "UK", "GBP", "subscription", "stripe", 10));
        await db.SaveChangesAsync();

        var registry = new GatewayRegistry(db, NewProvider("stripe", "paypal"));
        var ordered = await registry.ResolveAllAsync(new GatewayRouteRequest("UK", "GBP", "subscription"), CancellationToken.None);

        // Most specific route wins first; stripe appears once (deduped); paypal ROW fallback comes last.
        Assert.Equal(new[] { "stripe", "paypal" }, ordered.Select(g => g.GatewayName).ToArray());
    }

    private sealed class FakeGatewayProvider : IPaymentGatewayProvider
    {
        private readonly Dictionary<string, IPaymentGateway> _gateways;

        public FakeGatewayProvider(string[] supported)
        {
            _gateways = supported.ToDictionary(s => s, s => (IPaymentGateway)new FakeGateway(s), StringComparer.OrdinalIgnoreCase);
        }

        public IPaymentGateway GetGateway(string name) => _gateways[name];
        public IReadOnlyList<string> SupportedGateways => _gateways.Keys.ToList();
    }

    private sealed class FakeGateway : IPaymentGateway
    {
        public FakeGateway(string name) => GatewayName = name;
        public string GatewayName { get; }

        public Task<PaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<WebhookProcessResult> HandleWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RefundResult> ProcessRefundAsync(string transactionId, decimal amount, string currency, string reason, string idempotencyKey, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
