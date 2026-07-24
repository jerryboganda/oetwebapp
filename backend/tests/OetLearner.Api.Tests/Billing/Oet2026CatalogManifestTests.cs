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
        "full-physiotherapy",
        "full-allied-health",
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

    private static readonly string[] PortfolioEligibilityFlags =
    [
        "writing_addons",
        "speaking_addons",
        "tutor_book_discount"
    ];

    private static readonly string[] RecallPlanCodes =
    [
        "full-condensed-medicine",
        "full-condensed-medicine-tbook",
        "full-nursing",
        "full-nursing-assessment",
        "full-nursing-premium",
        "full-pharmacy",
        "full-physiotherapy",
        "full-allied-health",
        "crash-course",
        "crash-3letters",
        "crash-5letters"
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
                && requiresParent.GetBoolean()
                && addOn.TryGetProperty("eligibilityFlag", out var eligibilityFlag)
                && PortfolioEligibilityFlags.Contains(eligibilityFlag.GetString(), StringComparer.Ordinal))
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

    [Fact]
    public async Task StandaloneTutorBook_IsExternalOnlyManualWhatsAppDelivery()
    {
        var manifest = await LoadManifestAsync();
        var tutorBook = manifest.RootElement
            .GetProperty("plans")
            .EnumerateArray()
            .Single(plan => plan.GetProperty("code").GetString() == "tutor-book");

        Assert.Equal("manual_material", tutorBook.GetProperty("deliveryMethod").GetString());
        Assert.Equal(["none"], tutorBook.GetProperty("includedSubtests").EnumerateArray().Select(x => x.GetString()));
        Assert.Empty(tutorBook.GetProperty("dashboardModules").EnumerateArray());
        Assert.False(tutorBook.GetProperty("bundled").GetProperty("tutorBook").GetBoolean());
        Assert.False(tutorBook.GetProperty("recallUpdatesEnabled").GetBoolean());
        Assert.Contains("WhatsApp", tutorBook.GetProperty("deliveryInstructions").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CoursePlans_NeverGrantMocks_AndRecallsMatchPricingList()
    {
        var manifest = await LoadManifestAsync();
        var plans = manifest.RootElement.GetProperty("plans").EnumerateArray().ToArray();

        Assert.All(plans, plan =>
        {
            var modules = plan.GetProperty("dashboardModules")
                .EnumerateArray()
                .Select(module => module.GetString())
                .ToArray();
            Assert.DoesNotContain("Mocks", modules, StringComparer.OrdinalIgnoreCase);
        });

        var actualRecallCodes = plans
            .Where(plan => plan.GetProperty("dashboardModules")
                .EnumerateArray()
                .Any(module => string.Equals(module.GetString(), "Recalls", StringComparison.OrdinalIgnoreCase)))
            .Select(plan => plan.GetProperty("code").GetString())
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            RecallPlanCodes.OrderBy(code => code, StringComparer.Ordinal),
            actualRecallCodes);
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
