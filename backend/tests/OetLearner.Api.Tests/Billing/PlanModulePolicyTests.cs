using System.Text.Json;
using OetLearner.Api.Services.Entitlements;
using Xunit;

namespace OetLearner.Api.Tests.Billing;

public sealed class PlanModulePolicyTests
{
    [Theory]
    [InlineData("mega-special")]
    [InlineData("writing-crash")]
    [InlineData("speaking-crash")]
    [InlineData("basic-english")]
    [InlineData("unknown-plan")]
    public void Normalize_StripsMocksAndUnapprovedRecalls(string planCode)
    {
        var normalized = PlanModulePolicy.NormalizeDashboardModulesJson(
            planCode,
            """["Recalls","MaterialsLibrary","VideoLibrary","Mocks"]""");
        var modules = JsonSerializer.Deserialize<string[]>(normalized)!;

        Assert.DoesNotContain("Mocks", modules, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Recalls", modules, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("MaterialsLibrary", modules);
        Assert.Contains("VideoLibrary", modules);
    }

    [Theory]
    [InlineData("full-condensed-medicine")]
    [InlineData("full-nursing")]
    [InlineData("full-pharmacy")]
    [InlineData("full-physiotherapy")]
    [InlineData("full-allied-health")]
    [InlineData("crash-course")]
    public void Normalize_PreservesApprovedRecallsButNeverMocks(string planCode)
    {
        var normalized = PlanModulePolicy.NormalizeDashboardModulesJson(
            planCode,
            """["Recalls","Mocks"]""");
        var modules = JsonSerializer.Deserialize<string[]>(normalized)!;

        Assert.Contains("Recalls", modules);
        Assert.DoesNotContain("Mocks", modules, StringComparer.OrdinalIgnoreCase);
    }
}
