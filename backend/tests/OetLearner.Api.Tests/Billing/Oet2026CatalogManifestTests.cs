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

    private static readonly string[] CanonicalAiPackageCodes =
    [
        "pkg_quick_check", "pkg_exam_prep_pro", "pkg_oet_mastery",
        "pkg_mock_1", "pkg_mock_3", "pkg_mock_5",
        "pkg_listening_starter", "pkg_listening_standard", "pkg_listening_pro",
        "pkg_reading_starter", "pkg_reading_standard", "pkg_reading_pro",
        "pkg_writing_starter", "pkg_writing_standard", "pkg_writing_pro",
        "pkg_speaking_starter", "pkg_speaking_standard", "pkg_speaking_pro"
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

    [Fact]
    public async Task CanonicalAiPackages_AreExactlyEighteen_WithSourceGroupsAndFeatures()
    {
        var manifest = await LoadManifestAsync();
        var aiPackages = manifest.RootElement
            .GetProperty("addOns")
            .EnumerateArray()
            .Where(addOn => addOn.GetProperty("addonKind").GetString() == "ai_package")
            .ToArray();
        var codes = aiPackages.Select(addOn => addOn.GetProperty("code").GetString()).ToArray();

        Assert.Equal(
            CanonicalAiPackageCodes.OrderBy(code => code, StringComparer.Ordinal),
            codes.OrderBy(code => code, StringComparer.Ordinal));
        Assert.DoesNotContain(codes, code => code?.StartsWith("pkg_speaking_ai_", StringComparison.Ordinal) == true);
        Assert.All(aiPackages, addOn =>
        {
            Assert.False(string.IsNullOrWhiteSpace(addOn.GetProperty("aiPackageGroup").GetString()));
            Assert.NotEmpty(addOn.GetProperty("aiFeatures").EnumerateArray());
        });

        Assert.Equal(3, aiPackages.Count(addOn => addOn.GetProperty("aiPackageGroup").GetString() == "full"));
        Assert.Equal(3, aiPackages.Count(addOn => addOn.GetProperty("aiPackageGroup").GetString() == "mock"));
        Assert.Equal(3, aiPackages.Count(addOn => addOn.GetProperty("aiPackageGroup").GetString() == "listening"));
        Assert.Equal(3, aiPackages.Count(addOn => addOn.GetProperty("aiPackageGroup").GetString() == "reading"));
        Assert.Equal(3, aiPackages.Count(addOn => addOn.GetProperty("aiPackageGroup").GetString() == "writing"));
        Assert.Equal(3, aiPackages.Count(addOn => addOn.GetProperty("aiPackageGroup").GetString() == "speaking"));
    }

    [Fact]
    public async Task AiPackageGrants_MatchAdvertisedUnlimitedAndWritingItemSemantics()
    {
        var manifest = await LoadManifestAsync();
        var addOns = manifest.RootElement.GetProperty("addOns").EnumerateArray().ToArray();

        var mastery = addOns.Single(addOn => addOn.GetProperty("code").GetString() == "pkg_oet_mastery");
        Assert.True(mastery.GetProperty("unlimitedGrading").GetBoolean());
        Assert.True(mastery.GetProperty("unlimitedListening").GetBoolean());
        Assert.True(mastery.GetProperty("unlimitedReading").GetBoolean());
        Assert.Equal(JsonValueKind.Null, mastery.GetProperty("listeningTests").ValueKind);
        Assert.Equal(JsonValueKind.Null, mastery.GetProperty("readingTests").ValueKind);
        Assert.Equal(0, mastery.GetProperty("grantCredits").GetInt32());

        AssertWritingPackage("pkg_writing_starter", displayedItems: 3, debitUnits: 6);
        AssertWritingPackage("pkg_writing_standard", displayedItems: 8, debitUnits: 16);
        AssertWritingPackage("pkg_writing_pro", displayedItems: 15, debitUnits: 30);

        void AssertWritingPackage(string code, int displayedItems, int debitUnits)
        {
            var addOn = addOns.Single(row => row.GetProperty("code").GetString() == code);
            Assert.Equal(displayedItems, addOn.GetProperty("grantCredits").GetInt32());
            Assert.Equal(displayedItems, addOn.GetProperty("writingItems").GetInt32());
            Assert.Equal(debitUnits, addOn.GetProperty("writingOnlyCredits").GetInt32());
        }
    }

    [Fact]
    public async Task PhysiotherapyAndAlliedHealth_IncludeAdvertisedAssessmentBonuses()
    {
        var manifest = await LoadManifestAsync();
        var plans = manifest.RootElement.GetProperty("plans").EnumerateArray().ToArray();

        foreach (var code in new[] { "full-physiotherapy", "full-allied-health" })
        {
            var bundled = plans.Single(plan => plan.GetProperty("code").GetString() == code)
                .GetProperty("bundled");
            Assert.Equal(5, bundled.GetProperty("writingAssessments").GetInt32());
            Assert.Equal(1, bundled.GetProperty("speakingSessions").GetInt32());
            Assert.Equal(5, bundled.GetProperty("aiCredits").GetInt32());
        }
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
