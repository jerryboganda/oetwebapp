using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Tests;

/// <summary>
/// Source-key vocabulary for package-lot revocation (TB, #194).
/// One definition of a subscription's plan-key set, shared by removal,
/// refund, and orphan-sweep paths.
/// </summary>
public sealed class AiPackageCreditSourcesTests
{
    [Fact]
    public void PlanKeys_ContainsPlanAndAdminPackageKeys()
    {
        var keys = AiPackageCreditSources.PlanKeys("sub-1", "pro");

        Assert.Equal(2, keys.Count);
        Assert.Contains(AiPackageCreditSources.Plan("sub-1", "pro"), keys);
        Assert.Contains(AiPackageCreditSources.AdminPackage("sub-1", "pro"), keys);
    }

    [Fact]
    public void PlanKeys_MatchHistoricalKeyStrings()
    {
        var keys = AiPackageCreditSources.PlanKeys("sub-1", "pro");

        Assert.Contains("plan:sub-1:pro", keys);
        Assert.Contains("admin-package:sub-1:pro", keys);
    }
}
