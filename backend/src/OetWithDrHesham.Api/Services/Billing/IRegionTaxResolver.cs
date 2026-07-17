namespace OetWithDrHesham.Api.Services.Billing;

/// <summary>
/// Resolves tax (VAT/GST/withholding) for a checkout context. Phase 1 ships a
/// no-op implementation; Phase 3 swaps in a real <c>TaxResolver</c> backed by
/// the <c>TaxRule</c> table and optional Stripe Tax acceleration.
/// </summary>
public interface IRegionTaxResolver
{
    Task<TaxBreakdown> ResolveAsync(TaxResolutionRequest request, CancellationToken ct);
}

public sealed record TaxResolutionRequest(
    string BuyerCountry,
    string? BuyerVatId,
    decimal SubtotalAmount,
    string Currency,
    string ProductType,
    bool TreatAsB2B);

public sealed record TaxBreakdown(IReadOnlyList<TaxLine> Lines, decimal TotalTaxAmount)
{
    public static TaxBreakdown Empty { get; } = new(Array.Empty<TaxLine>(), 0m);
    public bool IsEmpty => Lines.Count == 0 && TotalTaxAmount == 0m;
}

public sealed record TaxLine(string TaxType, string Description, decimal RatePercent, decimal Amount);

/// <summary>Phase 1 placeholder. Returns zero tax until Phase 3 ships.</summary>
public sealed class NoTaxResolver : IRegionTaxResolver
{
    public Task<TaxBreakdown> ResolveAsync(TaxResolutionRequest request, CancellationToken ct)
        => Task.FromResult(TaxBreakdown.Empty);
}
