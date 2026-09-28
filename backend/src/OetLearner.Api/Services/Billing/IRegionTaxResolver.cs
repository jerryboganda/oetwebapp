namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Resolves tax (VAT/GST/withholding) for a checkout context. Implemented by
/// <c>TaxResolver</c>, backed by the <c>TaxRule</c> table.
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
