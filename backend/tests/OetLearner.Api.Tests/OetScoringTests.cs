using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

/// <summary>
/// Enforces the mission-critical OET scoring rules on the .NET side.
/// These tests MUST stay green on every commit. A failure here indicates
/// a breach of the canonical scoring specification documented in
/// docs/SCORING.md.
/// </summary>
public class OetScoringTests
{
    // --- Constants ---------------------------------------------------------

    [Fact]
    public void Constants_Match_Specification()
    {
        Assert.Equal(42, OetScoring.ListeningReadingRawMax);
        Assert.Equal(30, OetScoring.ListeningReadingRawPass);
        Assert.Equal(350, OetScoring.ScaledPassGradeB);
        Assert.Equal(300, OetScoring.ScaledPassGradeCPlus);
        Assert.Equal(0, OetScoring.ScaledMin);
        Assert.Equal(500, OetScoring.ScaledMax);
    }

    [Fact]
    public void GradeB_Countries_Are_Exactly_UK_IE_AU_NZ_CA()
    {
        Assert.Equal(
            new[] { "AU", "CA", "GB", "IE", "NZ" },
            OetScoring.WritingGradeBCountries.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void GradeCPlus_Countries_Are_Exactly_US_and_QA()
    {
        Assert.Equal(
            new[] { "QA", "US" },
            OetScoring.WritingGradeCPlusCountries.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void GradeB_And_GradeCPlus_Country_Sets_Do_Not_Overlap()
    {
        foreach (var c in OetScoring.WritingGradeBCountries)
            Assert.DoesNotContain(c, OetScoring.WritingGradeCPlusCountries);
    }

    // --- Raw ↔ Scaled conversion ------------------------------------------

    [Fact]
    public void OetRawToScaled_30_Exactly_Equals_350()
    {
        Assert.Equal(350, OetScoring.OetRawToScaled(30));
    }

    [Fact]
    public void OetRawToScaled_0_Equals_0()
    {
        Assert.Equal(0, OetScoring.OetRawToScaled(0));
    }

    [Fact]
    public void OetRawToScaled_42_Equals_500()
    {
        Assert.Equal(500, OetScoring.OetRawToScaled(42));
    }

    [Fact]
    public void OetRawToScaled_29_Is_Below_350()
    {
        Assert.True(OetScoring.OetRawToScaled(29) < 350);
    }

    [Fact]
    public void OetRawToScaled_31_Is_Above_350()
    {
        Assert.True(OetScoring.OetRawToScaled(31) > 350);
    }

    [Fact]
    public void OetRawToScaled_Is_Monotonically_NonDecreasing_Across_FullRange()
    {
        var prev = -1;
        for (var r = 0; r <= 42; r++)
        {
            var s = OetScoring.OetRawToScaled(r);
            Assert.True(s >= prev, $"OetRawToScaled({r})={s} < previous={prev}");
            prev = s;
        }
    }

    [Fact]
    public void OetRawToScaled_Clamps_OutOfRange()
    {
        Assert.Equal(500, OetScoring.OetRawToScaled(999));
        Assert.Equal(0, OetScoring.OetRawToScaled(-5));
    }

    // --- Grade band derivation --------------------------------------------

    [Theory]
    [InlineData(500, "A")]
    [InlineData(450, "A")]
    [InlineData(449, "B")]
    [InlineData(350, "B")]
    [InlineData(349, "C+")]
    [InlineData(300, "C+")]
    [InlineData(299, "C")]
    [InlineData(200, "C")]
    [InlineData(199, "D")]
    [InlineData(100, "D")]
    [InlineData(99, "E")]
    [InlineData(0, "E")]
    public void OetGradeLetterFromScaled_Produces_Correct_Band(int scaled, string expected)
    {
        Assert.Equal(expected, OetScoring.OetGradeLetterFromScaled(scaled));
    }

    [Fact]
    public void OetGradeLabel_Prefixes_Grade()
    {
        Assert.Equal("Grade B", OetScoring.OetGradeLabel("B"));
        Assert.Equal("Grade C+", OetScoring.OetGradeLabel("C+"));
    }

    // --- Listening / Reading pass logic -----------------------------------

    [Theory]
    [InlineData(30, true)]
    [InlineData(31, true)]
    [InlineData(42, true)]
    [InlineData(29, false)]
    [InlineData(0, false)]
    public void IsListeningReadingPassByRaw_Respects_30_Of_42_Threshold(int raw, bool expected)
    {
        Assert.Equal(expected, OetScoring.IsListeningReadingPassByRaw(raw));
    }

    [Theory]
    [InlineData(350, true)]
    [InlineData(351, true)]
    [InlineData(500, true)]
    [InlineData(349, false)]
    [InlineData(0, false)]
    public void IsListeningReadingPassByScaled_Respects_350_Of_500_Threshold(int scaled, bool expected)
    {
        Assert.Equal(expected, OetScoring.IsListeningReadingPassByScaled(scaled));
    }

    [Fact]
    public void GradeListeningReading_At_30_Returns_Pass_At_GradeB()
    {
        var r = OetScoring.GradeListeningReading("listening", 30);
        Assert.Equal("listening", r.Subtest);
        Assert.Equal(30, r.RawCorrect);
        Assert.Equal(42, r.RawMax);
        Assert.Equal(350, r.ScaledScore);
        Assert.Equal(350, r.RequiredScaled);
        Assert.Equal("B", r.RequiredGrade);
        Assert.Equal("B", r.Grade);
        Assert.True(r.Passed);
    }

    [Fact]
    public void GradeListeningReading_At_29_Returns_Fail()
    {
        var r = OetScoring.GradeListeningReading("reading", 29);
        Assert.False(r.Passed);
        Assert.True(r.ScaledScore < 350);
    }

    [Fact]
    public void GradeListeningReading_At_42_Returns_GradeA()
    {
        var r = OetScoring.GradeListeningReading("reading", 42);
        Assert.Equal(500, r.ScaledScore);
        Assert.Equal("A", r.Grade);
        Assert.True(r.Passed);
    }

    // --- Country normalization --------------------------------------------

    [Theory]
    [InlineData("GB", "GB")]
    [InlineData("UK", "GB")]
    [InlineData("United Kingdom", "GB")]
    [InlineData("Great Britain", "GB")]
    [InlineData("england", "GB")]
    [InlineData("IE", "IE")]
    [InlineData("Ireland", "IE")]
    [InlineData("AU", "AU")]
    [InlineData("Australia", "AU")]
    [InlineData("NZ", "NZ")]
    [InlineData("New Zealand", "NZ")]
    [InlineData("CA", "CA")]
    [InlineData("Canada", "CA")]
    [InlineData("US", "US")]
    [InlineData("USA", "US")]
    [InlineData("United States", "US")]
    [InlineData("united states of america", "US")]
    [InlineData("America", "US")]
    [InlineData("QA", "QA")]
    [InlineData("Qatar", "QA")]
    [InlineData("  qatar  ", "QA")]
    [InlineData("Gulf Countries", "GB")]
    [InlineData("Other Countries", "GB")]
    public void NormalizeWritingCountry_Maps_Aliases(string input, string expected)
    {
        Assert.Equal(expected, OetScoring.NormalizeWritingCountry(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Germany")]
    [InlineData("France")]
    [InlineData("India")]
    public void NormalizeWritingCountry_Returns_Null_For_Unsupported(string? input)
    {
        Assert.Null(OetScoring.NormalizeWritingCountry(input));
    }

    // --- Writing thresholds (country-aware) -------------------------------

    [Theory]
    [InlineData("GB")]
    [InlineData("UK")]
    [InlineData("IE")]
    [InlineData("AU")]
    [InlineData("NZ")]
    [InlineData("CA")]
    [InlineData("Gulf Countries")]
    [InlineData("Other Countries")]
    public void GetWritingPassThreshold_GradeB_Countries_Return_350(string country)
    {
        var t = OetScoring.GetWritingPassThreshold(country);
        Assert.NotNull(t);
        Assert.Equal(350, t!.Threshold);
        Assert.Equal("B", t.Grade);
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USA")]
    [InlineData("QA")]
    [InlineData("Qatar")]
    public void GetWritingPassThreshold_GradeCPlus_Countries_Return_300(string country)
    {
        var t = OetScoring.GetWritingPassThreshold(country);
        Assert.NotNull(t);
        Assert.Equal(300, t!.Threshold);
        Assert.Equal("C+", t.Grade);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("DE")]
    [InlineData("Germany")]
    public void GetWritingPassThreshold_Returns_Null_For_Unsupported(string? country)
    {
        Assert.Null(OetScoring.GetWritingPassThreshold(country));
    }

    // --- Writing grading --------------------------------------------------

    [Fact]
    public void GradeWriting_UK_At_350_Passes_As_GradeB()
    {
        var r = OetScoring.GradeWriting(350, "UK");
        Assert.True(r.Passed);
        Assert.Equal(350, r.RequiredScaled);
        Assert.Equal("B", r.RequiredGrade);
        Assert.Equal("B", r.Grade);
    }

    [Fact]
    public void GradeWriting_UK_At_349_Fails_Despite_Being_CPlus()
    {
        var r = OetScoring.GradeWriting(349, "UK");
        Assert.False(r.Passed);
        Assert.Equal("C+", r.Grade);
        Assert.Equal("B", r.RequiredGrade);
    }

    [Fact]
    public void GradeWriting_USA_At_300_Passes_As_GradeCPlus()
    {
        var r = OetScoring.GradeWriting(300, "USA");
        Assert.True(r.Passed);
        Assert.Equal(300, r.RequiredScaled);
        Assert.Equal("C+", r.RequiredGrade);
    }

    [Fact]
    public void GradeWriting_USA_At_299_Fails()
    {
        var r = OetScoring.GradeWriting(299, "US");
        Assert.False(r.Passed);
    }

    [Fact]
    public void GradeWriting_Qatar_At_310_Passes()
    {
        var r = OetScoring.GradeWriting(310, "Qatar");
        Assert.True(r.Passed);
        Assert.Equal(300, r.RequiredScaled);
    }

    [Fact]
    public void GradeWriting_CRITICAL_Same320_FailsUK_PassesUS()
    {
        var uk = OetScoring.GradeWriting(320, "UK");
        var us = OetScoring.GradeWriting(320, "USA");
        Assert.False(uk.Passed);
        Assert.True(us.Passed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GradeWriting_Missing_Country_Returns_CountryRequired(string? country)
    {
        var r = OetScoring.GradeWriting(400, country);
        Assert.Null(r.Passed);
        Assert.Equal("country_required", r.Reason);
        Assert.Null(r.RequiredScaled);
        Assert.Null(r.RequiredGrade);
        Assert.True(r.SupportedCountries.Count >= 7);
    }

    [Fact]
    public void GradeWriting_Unsupported_Country_Returns_CountryUnsupported()
    {
        var r = OetScoring.GradeWriting(400, "Germany");
        Assert.Null(r.Passed);
        Assert.Equal("country_unsupported", r.Reason);
        Assert.Equal("Germany", r.ProvidedCountry);
    }

    // --- Speaking (universal) ---------------------------------------------

    [Theory]
    [InlineData(350, true)]
    [InlineData(500, true)]
    [InlineData(349, false)]
    [InlineData(0, false)]
    public void IsSpeakingPass_Respects_350_Of_500_Threshold(int scaled, bool expected)
    {
        Assert.Equal(expected, OetScoring.IsSpeakingPass(scaled));
    }

    [Fact]
    public void GradeSpeaking_At_350_Passes_As_GradeB()
    {
        var r = OetScoring.GradeSpeaking(350);
        Assert.True(r.Passed);
        Assert.Equal(350, r.RequiredScaled);
        Assert.Equal("B", r.RequiredGrade);
        Assert.Equal("B", r.Grade);
        Assert.Equal("speaking", r.Subtest);
    }

    [Fact]
    public void GradeSpeaking_At_349_Fails()
    {
        Assert.False(OetScoring.GradeSpeaking(349).Passed);
    }

    // --- Formatters --------------------------------------------------------

    [Fact]
    public void FormatRawLrScore_Produces_Correct_String()
    {
        Assert.Equal("35/42", OetScoring.FormatRawLrScore(35));
        Assert.Equal("0/42", OetScoring.FormatRawLrScore(0));
        Assert.Equal("42/42", OetScoring.FormatRawLrScore(42));
    }

    [Fact]
    public void FormatScaledScore_Produces_Correct_String()
    {
        Assert.Equal("350/500", OetScoring.FormatScaledScore(350));
        Assert.Equal("0/500", OetScoring.FormatScaledScore(0));
        Assert.Equal("500/500", OetScoring.FormatScaledScore(500));
    }

    [Fact]
    public void FormatListeningReadingDisplay_Shows_All_Three_Pieces()
    {
        var line = OetScoring.FormatListeningReadingDisplay(30);
        Assert.Equal("30/42 \u2022 350/500 \u2022 Grade B", line);
    }

    // --- Cross-consistency with legacy OetGrade ---------------------------

    [Fact]
    public void Legacy_OetGrade_And_OetGradeLetterFromScaled_Agree()
    {
        // The legacy helper returns "Grade X"; the canonical helper returns "X".
        foreach (var score in new[] { 0, 99, 100, 199, 200, 299, 300, 349, 350, 449, 450, 500 })
        {
            var legacy = ScoringService.OetGrade(score);
            var canonical = OetScoring.OetGradeLabel(OetScoring.OetGradeLetterFromScaled(score));
            Assert.Equal(legacy, canonical);
        }
    }
}
