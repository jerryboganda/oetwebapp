using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Vocabulary bridge between the LT-* Writing catalogue and the legacy-token
/// assessment-pack / rule stores (<see cref="WritingLetterTypeTaxonomy.ToPackLetterType"/>).
/// An LT-* task must resolve the same pack/rules as its legacy-token
/// equivalent; retired Response-like tokens and anything unknown must fall
/// back to the neutral <c>other</c> token — never to a guessed known type and
/// never to the retired <c>advice_to_patient</c> genre.
/// </summary>
public sealed class WritingPackLetterTypeTests
{
    [Theory]
    [InlineData("LT-RR", "routine_referral")]
    [InlineData("lt-rr", "routine_referral")]
    [InlineData("routine_referral", "routine_referral")]
    [InlineData("routine", "routine_referral")]
    [InlineData("LT-UR", "urgent_referral")]
    [InlineData("urgent_referral", "urgent_referral")]
    [InlineData("urgent", "urgent_referral")]
    [InlineData("LT-DG", "discharge")]
    [InlineData("discharge", "discharge")]
    [InlineData("update_discharge", "discharge")]
    [InlineData("LT-TR", "transfer")]
    [InlineData("transfer", "transfer")]
    [InlineData("transfer_letter", "transfer")]
    [InlineData("LT-NM", "non_medical_referral")]
    [InlineData("non_medical_referral", "non_medical_referral")]
    [InlineData("non_medical", "non_medical_referral")]
    [InlineData("referral_to_gp", "referral_to_gp")]
    [InlineData("LT-OT", "other")]
    [InlineData("other_letters", "other")]
    public void ToPackLetterType_MapsKnownTokensToCanonicalPackToken(string input, string expected)
    {
        Assert.Equal(expected, WritingLetterTypeTaxonomy.ToPackLetterType(input));
    }

    [Theory]
    [InlineData("LT-RP")]
    [InlineData("RESPONSE")]
    [InlineData("UPDATE")]
    [InlineData("REPLY")]
    [InlineData("advice_to_patient")]
    [InlineData("some unknown label")]
    [InlineData("")]
    [InlineData(null)]
    public void ToPackLetterType_MapsRetiredAndUnknownTokensToOther(string? input)
    {
        Assert.Equal("other", WritingLetterTypeTaxonomy.ToPackLetterType(input));
    }

    [Fact]
    public void ToPackLetterType_NeverProducesRetiredGenreToken()
    {
        var inputs = new[]
        {
            "LT-RR", "LT-UR", "LT-DG", "LT-TR", "LT-NM", "LT-OT", "LT-RP",
            "routine_referral", "urgent_referral", "update_discharge",
            "transfer_letter", "non_medical_referral", "response", "other",
        };

        foreach (var input in inputs)
        {
            Assert.NotEqual("advice_to_patient", WritingLetterTypeTaxonomy.ToPackLetterType(input));
        }
    }
}
