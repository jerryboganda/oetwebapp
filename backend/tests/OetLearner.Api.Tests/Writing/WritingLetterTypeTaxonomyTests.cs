using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Writing catalogue taxonomy revision (2026-09):
/// Response (LT-RP) retired, Other Letters (LT-OT) added as the universal
/// fallback, uncertain cases must never be forced into a known category.
/// </summary>
public sealed class WritingLetterTypeTaxonomyTests
{
    // WR-TAX-01: retired Response is absent from the valid catalogue set.
    [Fact]
    public void ValidCatalogueLetterTypes_ExcludeResponse()
    {
        Assert.DoesNotContain("LT-RP", WritingLetterTypeTaxonomy.ValidCatalogueLetterTypes);
        Assert.False(WritingLetterTypeTaxonomy.IsValidCatalogueLetterType("LT-RP"));
        Assert.False(WritingLetterTypeTaxonomy.IsValidCatalogueLetterType("response"));
    }

    // WR-TAX-02: Other Letters is a valid catalogue letter type.
    [Fact]
    public void ValidCatalogueLetterTypes_IncludeOtherLetters()
    {
        Assert.Contains("LT-OT", WritingLetterTypeTaxonomy.ValidCatalogueLetterTypes);
        Assert.Equal(6, WritingLetterTypeTaxonomy.ValidCatalogueLetterTypes.Count);
        Assert.True(WritingLetterTypeTaxonomy.IsValidCatalogueLetterType("LT-OT"));
        Assert.True(WritingLetterTypeTaxonomy.IsValidCatalogueLetterType("lt-ot"));
    }

    // WR-TAX-04: "All" is a filter option, never a stored classification.
    [Theory]
    [InlineData("all")]
    [InlineData("ALL")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void All_IsNeverAValidStoredLetterType(string? value)
    {
        Assert.False(WritingLetterTypeTaxonomy.IsValidCatalogueLetterType(value));
    }

    // WR-CLS-01..05: clearly identifiable known categories keep their codes.
    [Theory]
    [InlineData("LT-RR", "LT-RR")]
    [InlineData("routine_referral", "LT-RR")]
    [InlineData("REFERRAL", "LT-RR")]
    [InlineData("lt-rr", "LT-RR")]
    [InlineData("LT-UR", "LT-UR")]
    [InlineData("urgent_referral", "LT-UR")]
    [InlineData("LT-DG", "LT-DG")]
    [InlineData("discharge", "LT-DG")]
    [InlineData("update_discharge", "LT-DG")]
    [InlineData("LT-TR", "LT-TR")]
    [InlineData("transfer", "LT-TR")]
    [InlineData("transfer_letter", "LT-TR")]
    [InlineData("LT-NM", "LT-NM")]
    [InlineData("non_medical_referral", "LT-NM")]
    public void Normalize_MapsKnownCategoriesToCatalogueCodes(string input, string expected)
    {
        Assert.Equal(expected, WritingLetterTypeTaxonomy.NormalizeCatalogueLetterType(input));
    }

    // WR-CLS-06/07/08: ambiguous, unsupported, or retired-Response inputs route
    // to Other Letters — LT-RP can never be emitted.
    [Theory]
    [InlineData("LT-RP")]
    [InlineData("RESPONSE")]
    [InlineData("response")]
    [InlineData("UPDATE")]
    [InlineData("REPLY")]
    [InlineData("advice_letter")]
    [InlineData("letter of advice")]
    [InlineData("something-entirely-unknown")]
    [InlineData("referral_to_gp")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Normalize_RoutesUncertainAndRetiredInputsToOtherLetters(string? input)
    {
        var result = WritingLetterTypeTaxonomy.NormalizeCatalogueLetterType(input);
        Assert.Equal(WritingLetterTypeTaxonomy.OtherLetters, result);
        Assert.NotEqual("LT-RP", result);
    }

    [Fact]
    public void NormalizeOrEmpty_PreservesEmptyForRequiredFieldValidation()
    {
        Assert.Equal(string.Empty, WritingLetterTypeTaxonomy.NormalizeCatalogueLetterTypeOrEmpty(null));
        Assert.Equal(string.Empty, WritingLetterTypeTaxonomy.NormalizeCatalogueLetterTypeOrEmpty("  "));
        Assert.Equal("LT-OT", WritingLetterTypeTaxonomy.NormalizeCatalogueLetterTypeOrEmpty("LT-RP"));
        Assert.Equal("LT-DG", WritingLetterTypeTaxonomy.NormalizeCatalogueLetterTypeOrEmpty("discharge"));
    }

    [Fact]
    public void ToLegacy_NeverProducesResponseToken()
    {
        Assert.Equal("routine_referral", WritingLetterTypeTaxonomy.ToLegacyLetterType("LT-RR"));
        Assert.Equal("urgent_referral", WritingLetterTypeTaxonomy.ToLegacyLetterType("LT-UR"));
        Assert.Equal("discharge", WritingLetterTypeTaxonomy.ToLegacyLetterType("LT-DG"));
        Assert.Equal("transfer_letter", WritingLetterTypeTaxonomy.ToLegacyLetterType("LT-TR"));
        Assert.Equal("non_medical_referral", WritingLetterTypeTaxonomy.ToLegacyLetterType("LT-NM"));
        Assert.Equal("other_letters", WritingLetterTypeTaxonomy.ToLegacyLetterType("LT-OT"));
        // Retired inputs resolve through the fallback, never to "update".
        Assert.Equal("other_letters", WritingLetterTypeTaxonomy.ToLegacyLetterType("LT-RP"));
        Assert.Equal("other_letters", WritingLetterTypeTaxonomy.ToLegacyLetterType("RESPONSE"));
        Assert.NotEqual("update", WritingLetterTypeTaxonomy.ToLegacyLetterType("LT-RP"));
    }

    // WR-RP-05 analogue at the service seam: onboarding focus can no longer
    // persist LT-RP; retired values normalise to LT-OT.
    [Fact]
    public async Task SaveOnboarding_NormalisesRetiredResponseFocusToOtherLetters()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new LearnerDbContext(options);
        var service = new WritingLearnerPathwayService(db, TimeProvider.System, new RulebookLoader());

        var profile = await service.SaveOnboardingAsync(
            "taxonomy-learner-1",
            new WritingStartOnboardingRequest(
                "Pharmacy",
                "B",
                null,
                5,
                45,
                "GB",
                ["LT-RP", "LT-RR", "LT-OT"]),
            CancellationToken.None);

        Assert.DoesNotContain("LT-RP", profile.LetterTypeFocus);
        Assert.Contains("LT-OT", profile.LetterTypeFocus);
        Assert.Contains("LT-RR", profile.LetterTypeFocus);
    }
}
