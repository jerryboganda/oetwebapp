namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Two-proportion z-test for pricing-experiment conversion-rate comparisons
/// vs a control variant. Returns z, p-value (two-tailed), and a
/// 95% confidence interval for the difference in proportions.
/// </summary>
public static class ExperimentStatistics
{
    public sealed record VariantStats(string VariantCode, int Assignments, int Conversions)
    {
        public double Rate => Assignments > 0 ? (double)Conversions / Assignments : 0d;
    }

    public sealed record ZTestResult(
        string ControlCode,
        string VariantCode,
        double ControlRate,
        double VariantRate,
        double Difference,
        double Z,
        double PValueTwoTailed,
        double CiLower95,
        double CiUpper95,
        bool SignificantAt95);

    public static ZTestResult Compare(VariantStats control, VariantStats variant)
    {
        var n1 = control.Assignments;
        var n2 = variant.Assignments;
        var p1 = control.Rate;
        var p2 = variant.Rate;
        double z = 0d, p = 1d, lo = 0d, hi = 0d;

        if (n1 > 0 && n2 > 0)
        {
            var pooled = (control.Conversions + variant.Conversions) / (double)(n1 + n2);
            var seZ = Math.Sqrt(pooled * (1 - pooled) * (1.0 / n1 + 1.0 / n2));
            z = seZ == 0d ? 0d : (p2 - p1) / seZ;
            p = TwoTailedPValue(z);

            // Wald 95% CI on (p2 - p1).
            var seCi = Math.Sqrt(p1 * (1 - p1) / n1 + p2 * (1 - p2) / n2);
            lo = (p2 - p1) - 1.96 * seCi;
            hi = (p2 - p1) + 1.96 * seCi;
        }

        return new ZTestResult(
            ControlCode: control.VariantCode,
            VariantCode: variant.VariantCode,
            ControlRate: p1,
            VariantRate: p2,
            Difference: p2 - p1,
            Z: z,
            PValueTwoTailed: p,
            CiLower95: lo,
            CiUpper95: hi,
            SignificantAt95: p < 0.05);
    }

    /// <summary>Two-tailed p-value from a z-score using Abramowitz-Stegun
    /// 26.2.17 approximation of the standard normal CDF (max abs error ~7.5e-8).</summary>
    private static double TwoTailedPValue(double z)
    {
        var absZ = Math.Abs(z);
        var p1 = 1 - NormalCdf(absZ);
        return Math.Min(1d, 2d * p1);
    }

    private static double NormalCdf(double x)
    {
        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;
        const double p = 0.3275911;

        var sign = x < 0 ? -1 : 1;
        var ax = Math.Abs(x) / Math.Sqrt(2);
        var t = 1.0 / (1.0 + p * ax);
        var y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-ax * ax);
        return 0.5 * (1.0 + sign * y);
    }
}
