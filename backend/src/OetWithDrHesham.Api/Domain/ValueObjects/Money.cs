using System.Globalization;

namespace OetWithDrHesham.Api.Domain.ValueObjects;

/// <summary>
/// Currency-aware money value. Stores the amount in the currency's minor unit
/// (pence, halala, piaster) as a 64-bit integer to avoid floating-point drift
/// across quote/invoice arithmetic. All operators reject mismatched currencies.
/// </summary>
/// <remarks>
/// Phase 1 introduces this type alongside the existing `decimal Price` columns.
/// The Money-to-PriceMinor column migration ships in a follow-up so the system
/// stays revenue-producing while the rewrite proceeds.
/// </remarks>
public readonly record struct Money(long AmountMinor, string Currency)
{
    public static Money Zero(string currency) => new(0, NormalizeCurrency(currency));

    public static Money FromMajor(decimal amountMajor, string currency)
    {
        var normalized = NormalizeCurrency(currency);
        var scale = MinorUnitScale(normalized);
        var scaled = decimal.Round(amountMajor * scale, 0, MidpointRounding.AwayFromZero);
        return new Money(checked((long)scaled), normalized);
    }

    public static Money FromMinor(long amountMinor, string currency)
        => new(amountMinor, NormalizeCurrency(currency));

    public decimal ToMajor() => (decimal)AmountMinor / MinorUnitScale(Currency);

    public static Money operator +(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(checked(a.AmountMinor + b.AmountMinor), a.Currency);
    }

    public static Money operator -(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(checked(a.AmountMinor - b.AmountMinor), a.Currency);
    }

    public static Money operator *(Money a, int multiplier)
        => new(checked(a.AmountMinor * multiplier), a.Currency);

    public static bool operator >(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return a.AmountMinor > b.AmountMinor;
    }

    public static bool operator <(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return a.AmountMinor < b.AmountMinor;
    }

    public static bool operator >=(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return a.AmountMinor >= b.AmountMinor;
    }

    public static bool operator <=(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return a.AmountMinor <= b.AmountMinor;
    }

    public bool IsZero => AmountMinor == 0;
    public bool IsNegative => AmountMinor < 0;

    public override string ToString()
        => $"{ToMajor().ToString("F" + MinorUnitDecimals(Currency), CultureInfo.InvariantCulture)} {Currency}";

    private static void EnsureSameCurrency(Money a, Money b)
    {
        if (!string.Equals(a.Currency, b.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot operate on Money values with mismatched currencies: {a.Currency} vs {b.Currency}.");
        }
    }

    private static string NormalizeCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
        {
            throw new ArgumentException("Currency must be a 3-letter ISO 4217 code.", nameof(currency));
        }
        return currency.ToUpperInvariant();
    }

    private static int MinorUnitDecimals(string currency) => currency switch
    {
        // Three-decimal currencies (gulf dinars).
        "BHD" or "KWD" or "OMR" or "JOD" or "TND" or "IQD" or "LYD" => 3,
        // Zero-decimal currencies.
        "JPY" or "KRW" or "VND" or "ISK" or "CLP" or "PYG" or "UGX" or "RWF" or "DJF" or "GNF" or "BIF" or "XAF" or "XOF" or "XPF" => 0,
        _ => 2,
    };

    private static decimal MinorUnitScale(string currency) => MinorUnitDecimals(currency) switch
    {
        0 => 1m,
        2 => 100m,
        3 => 1000m,
        _ => 100m,
    };
}
