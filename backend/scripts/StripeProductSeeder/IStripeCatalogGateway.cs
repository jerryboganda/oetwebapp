using System.Threading;
using System.Threading.Tasks;

namespace OetWithDrHesham.Scripts.StripeProductSeeder;

/// <summary>
/// Narrow surface the seeder needs from Stripe so tests can swap in a fake
/// without touching the network or shipping the Stripe.net SDK to the test
/// project.
/// </summary>
public interface IStripeCatalogGateway
{
    /// <summary>Look up a Product by its <c>metadata.code</c>.</summary>
    Task<StripeProductRecord?> FindProductByCodeAsync(string code, CancellationToken ct);

    /// <summary>Create a Product. Returns the newly assigned Stripe id.</summary>
    Task<StripeProductRecord> CreateProductAsync(StripeProductUpsert input, CancellationToken ct);

    /// <summary>Patch Name/Description/Metadata on an existing Product.</summary>
    Task UpdateProductAsync(string productId, StripeProductUpsert input, CancellationToken ct);

    /// <summary>List active prices currently attached to <paramref name="productId"/>.</summary>
    Task<IReadOnlyList<StripePriceRecord>> ListPricesAsync(string productId, CancellationToken ct);

    /// <summary>Create a new Price on <paramref name="productId"/>. Stripe Prices are immutable.</summary>
    Task<StripePriceRecord> CreatePriceAsync(string productId, StripePriceCreate input, CancellationToken ct);
}

public sealed record StripeProductRecord(
    string Id,
    string Name,
    string? Description,
    IReadOnlyDictionary<string, string> Metadata,
    bool Active);

public sealed record StripePriceRecord(
    string Id,
    string Currency,
    long UnitAmount,
    string? Interval,
    long? IntervalCount,
    bool Active,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record StripeProductUpsert(
    string Name,
    string? Description,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record StripePriceCreate(
    string Currency,
    long UnitAmount,
    string? Interval,
    long? IntervalCount,
    IReadOnlyDictionary<string, string> Metadata);
