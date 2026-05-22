using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Resolves the correct <see cref="IPaymentGateway"/> for a given checkout
/// context (region, currency, product type) by consulting <see cref="GatewayRoutingConfig"/>.
/// </summary>
public interface IGatewayRegistry
{
    /// <summary>
    /// Returns the highest-priority enabled gateway matching the route.
    /// Throws <see cref="NoMatchingGatewayException"/> if nothing routes.
    /// </summary>
    Task<IPaymentGateway> ResolveAsync(GatewayRouteRequest request, CancellationToken ct);

    /// <summary>Returns every viable gateway for the route, highest priority first.</summary>
    Task<IReadOnlyList<IPaymentGateway>> ResolveAllAsync(GatewayRouteRequest request, CancellationToken ct);
}

public sealed record GatewayRouteRequest(
    string Region,
    string Currency,
    string ProductType);

public sealed class NoMatchingGatewayException : Exception
{
    public NoMatchingGatewayException(GatewayRouteRequest request)
        : base($"No payment gateway configured for region={request.Region}, currency={request.Currency}, productType={request.ProductType}.")
    {
        Request = request;
    }

    public GatewayRouteRequest Request { get; }
}

public sealed class GatewayRegistry : IGatewayRegistry
{
    private readonly LearnerDbContext _db;
    private readonly IPaymentGatewayProvider _gateways;

    public GatewayRegistry(LearnerDbContext db, IPaymentGatewayProvider gateways)
    {
        _db = db;
        _gateways = gateways;
    }

    public async Task<IPaymentGateway> ResolveAsync(GatewayRouteRequest request, CancellationToken ct)
    {
        var all = await ResolveAllAsync(request, ct);
        if (all.Count == 0)
        {
            throw new NoMatchingGatewayException(request);
        }
        return all[0];
    }

    public async Task<IReadOnlyList<IPaymentGateway>> ResolveAllAsync(GatewayRouteRequest request, CancellationToken ct)
    {
        var region = string.IsNullOrWhiteSpace(request.Region) ? BillingRegions.RestOfWorld : request.Region.ToUpperInvariant();
        var currency = string.IsNullOrWhiteSpace(request.Currency) ? string.Empty : request.Currency.ToUpperInvariant();
        var productType = string.IsNullOrWhiteSpace(request.ProductType) ? GatewayProductTypes.Any : request.ProductType;

        var rows = await _db.GatewayRoutingConfigs
            .Where(r => r.IsEnabled
                && (r.Region == region || r.Region == BillingRegions.RestOfWorld)
                && (r.Currency == currency || r.Currency == GatewayProductTypes.Any)
                && (r.ProductType == productType || r.ProductType == GatewayProductTypes.Any))
            .OrderBy(r => r.Region == region ? 0 : 1)
            .ThenBy(r => r.Currency == currency ? 0 : 1)
            .ThenBy(r => r.ProductType == productType ? 0 : 1)
            .ThenBy(r => r.Priority)
            .ToListAsync(ct);

        var resolved = new List<IPaymentGateway>(rows.Count);
        var supported = new HashSet<string>(_gateways.SupportedGateways, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (!supported.Contains(row.GatewayName))
            {
                continue;
            }
            if (!seen.Add(row.GatewayName))
            {
                continue;
            }
            resolved.Add(_gateways.GetGateway(row.GatewayName));
        }
        return resolved;
    }
}
