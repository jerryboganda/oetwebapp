using OetWithDrHesham.Api.Domain.ValueObjects;

namespace OetWithDrHesham.Api.Tests;

public class MoneyTests
{
    [Fact]
    public void FromMajor_ConvertsTwoDecimalCurrencyToMinorUnits()
    {
        var m = Money.FromMajor(12.34m, "GBP");
        Assert.Equal(1234L, m.AmountMinor);
        Assert.Equal("GBP", m.Currency);
    }

    [Fact]
    public void FromMajor_HandlesThreeDecimalCurrency()
    {
        var m = Money.FromMajor(5.123m, "KWD");
        Assert.Equal(5123L, m.AmountMinor);
    }

    [Fact]
    public void FromMajor_HandlesZeroDecimalCurrency()
    {
        var m = Money.FromMajor(1000m, "JPY");
        Assert.Equal(1000L, m.AmountMinor);
    }

    [Fact]
    public void ToMajor_RoundtripsTwoDecimal()
    {
        var m = Money.FromMajor(99.99m, "USD");
        Assert.Equal(99.99m, m.ToMajor());
    }

    [Fact]
    public void Addition_OnSameCurrency_Sums()
    {
        var a = Money.FromMajor(10m, "EGP");
        var b = Money.FromMajor(2.5m, "EGP");
        var sum = a + b;
        Assert.Equal(1250L, sum.AmountMinor);
        Assert.Equal("EGP", sum.Currency);
    }

    [Fact]
    public void Addition_OnDifferentCurrencies_Throws()
    {
        var gbp = Money.FromMajor(10m, "GBP");
        var usd = Money.FromMajor(10m, "USD");
        Assert.Throws<InvalidOperationException>(() => { var _ = gbp + usd; });
    }

    [Fact]
    public void Subtraction_OnDifferentCurrencies_Throws()
    {
        var aed = Money.FromMajor(10m, "AED");
        var sar = Money.FromMajor(10m, "SAR");
        Assert.Throws<InvalidOperationException>(() => { var _ = aed - sar; });
    }

    [Fact]
    public void Comparison_OnDifferentCurrencies_Throws()
    {
        var a = Money.FromMajor(5m, "GBP");
        var b = Money.FromMajor(5m, "USD");
        Assert.Throws<InvalidOperationException>(() => { var _ = a > b; });
    }

    [Theory]
    [InlineData("", typeof(ArgumentException))]
    [InlineData("US", typeof(ArgumentException))]
    [InlineData("USDX", typeof(ArgumentException))]
    public void InvalidCurrencyCode_Throws(string code, Type expected)
    {
        Assert.Throws(expected, () => Money.FromMajor(1m, code));
    }

    [Fact]
    public void Multiplication_ByInteger_Scales()
    {
        var unit = Money.FromMajor(7.5m, "USD");
        var pack = unit * 3;
        Assert.Equal(2250L, pack.AmountMinor);
    }

    [Fact]
    public void Currency_IsNormalizedToUppercase()
    {
        var m = Money.FromMajor(1m, "gbp");
        Assert.Equal("GBP", m.Currency);
    }

    [Fact]
    public void Zero_HasZeroAmountAndCorrectCurrency()
    {
        var z = Money.Zero("AED");
        Assert.Equal(0L, z.AmountMinor);
        Assert.True(z.IsZero);
        Assert.Equal("AED", z.Currency);
    }

    [Fact]
    public void ToString_FormatsTwoDecimalsForUsdLikeCurrencies()
    {
        var m = Money.FromMajor(12.5m, "USD");
        Assert.Equal("12.50 USD", m.ToString());
    }

    [Fact]
    public void ToString_FormatsThreeDecimalsForGulfDinarCurrencies()
    {
        var m = Money.FromMajor(1.5m, "KWD");
        Assert.Equal("1.500 KWD", m.ToString());
    }
}
