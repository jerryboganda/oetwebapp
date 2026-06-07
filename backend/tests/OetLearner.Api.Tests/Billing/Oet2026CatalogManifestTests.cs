using System.Text.Json;
using Xunit;

namespace OetLearner.Api.Tests.Billing;

public sealed class Oet2026CatalogManifestTests
{
    private static readonly string[] ExpectedPlanCodes =
    [
        "full-condensed-medicine",
        "full-condensed-medicine-tbook",
        "full-nursing",
        "full-nursing-assessment",
        "full-nursing-premium",
        "full-pharmacy",
        "basic-english",
        "crash-course",
        "crash-3letters",
        "crash-5letters",
        "writing-crash",
        "writing-crash-2",
        "writing-crash-3",
        "writing-crash-5",
        "writing-crash-7",
        "writing-crash-10",
        "speaking-crash",
        "speaking-1session",
        "speaking-2sessions",
        "double-special",
        "mega-special",
        "tutor-book"
    ];

    private static readonly string[] ExpectedPortfolioAddOnCodes =
    [
        "addon-3-letters",
        "addon-5-letters",
        "addon-7-letters",
        "addon-10-letters",
        "addon-speaking-1session",
        "addon-speaking-2sessions",
        "tutor-book-addon"
    ];

    [Fact]
    public async Task PortfolioPlanCodes_MatchSpecExactlyOnce()
    {
        var manifest = await LoadManifestAsync();
        var codes = manifest.RootElement
            .GetProperty("plans")
            .EnumerateArray()
            .Select(plan => plan.GetProperty("code").GetString())
            .ToArray();

        Assert.Equal(ExpectedPlanCodes.OrderBy(code => code, StringComparer.Ordinal), codes.OrderBy(code => code, StringComparer.Ordinal));
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task PortfolioAddOnCodes_AreParentRequiredOnlyAndMatchSpec()
    {
        var manifest = await LoadManifestAsync();
        var addOns = manifest.RootElement
            .GetProperty("addOns")
            .EnumerateArray()
            .Where(addOn => addOn.TryGetProperty("requiresEligibleParent", out var requiresParent)
                && requiresParent.GetBoolean())
            .ToArray();
        var codes = addOns
            .Select(addOn => addOn.GetProperty("code").GetString())
            .ToArray();

        Assert.Equal(ExpectedPortfolioAddOnCodes.OrderBy(code => code, StringComparer.Ordinal), codes.OrderBy(code => code, StringComparer.Ordinal));
        Assert.All(addOns, addOn =>
        {
            var flag = addOn.GetProperty("eligibilityFlag").GetString();
            Assert.Contains(flag ?? string.Empty, new[] { "writing_addons", "speaking_addons", "tutor_book_discount" });
        });
    }

    [Fact]
    public async Task ZeroPricePlans_AreNotPubliclyVisible()
    {
        var manifest = await LoadManifestAsync();
        var visibleZeroPricePlans = manifest.RootElement
            .GetProperty("plans")
            .EnumerateArray()
            .Where(plan =>
                plan.GetProperty("price").GetDecimal() <= 0 &&
                !plan.GetProperty("isDraft").GetBoolean() &&
                plan.GetProperty("isVisible").GetBoolean())
            .Select(plan => plan.GetProperty("code").GetString())
            .ToArray();

        Assert.Empty(visibleZeroPricePlans);
    }

    private static async Task<JsonDocument> LoadManifestAsync()
    {
        var repoRoot = FindRepoRoot();
        var seedPath = Path.Combine(repoRoot, "backend", "src", "OetLearner.Api", "Data", "Seeds", "oet-2026-catalog.json");
        await using var stream = File.OpenRead(seedPath);
        return await JsonDocument.ParseAsync(stream);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(current.FullName, "backend")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root for OET 2026 catalog manifest test.");
    }
}
