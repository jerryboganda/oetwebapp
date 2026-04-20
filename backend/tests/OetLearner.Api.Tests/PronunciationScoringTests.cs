using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

/// <summary>
/// Pronunciation scoring projection tests. Must stay green — breaches the
/// canonical pronunciation anchor table documented in
/// <c>rulebooks/pronunciation/common/assessment-criteria.json</c>.
/// </summary>
public class PronunciationScoringTests
{
    [Fact]
    public void PronunciationProjectedScaled_Anchor_0_Maps_To_0()
    {
        Assert.Equal(0, OetScoring.PronunciationProjectedScaled(0));
    }

    [Fact]
    public void PronunciationProjectedScaled_Anchor_70_Maps_To_350_Exactly()
    {
        Assert.Equal(350, OetScoring.PronunciationProjectedScaled(70));
    }

    [Fact]
    public void PronunciationProjectedScaled_Anchor_60_Maps_To_300()
    {
        Assert.Equal(300, OetScoring.PronunciationProjectedScaled(60));
    }

    [Fact]
    public void PronunciationProjectedScaled_Anchor_100_Maps_To_500()
    {
        Assert.Equal(500, OetScoring.PronunciationProjectedScaled(100));
    }

    [Theory]
    [InlineData(75, 375)]   // halfway between 70 and 80 → halfway between 350 and 400
    [InlineData(85, 425)]   // halfway between 80 and 90 → halfway between 400 and 450
    [InlineData(95, 475)]
    public void PronunciationProjectedScaled_Interpolates_Linearly_Between_Anchors(double overall, int expected)
    {
        Assert.Equal(expected, OetScoring.PronunciationProjectedScaled(overall));
    }

    [Fact]
    public void PronunciationProjectedScaled_Clamps_Negative_To_Zero()
    {
        Assert.Equal(0, OetScoring.PronunciationProjectedScaled(-10));
    }

    [Fact]
    public void PronunciationProjectedScaled_Clamps_Over_100_To_500()
    {
        Assert.Equal(500, OetScoring.PronunciationProjectedScaled(150));
    }

    [Fact]
    public void PronunciationProjectedScaled_Handles_NaN()
    {
        Assert.Equal(0, OetScoring.PronunciationProjectedScaled(double.NaN));
    }

    [Fact]
    public void PronunciationProjectedBand_At_70_Returns_Speaking_Pass_B()
    {
        var r = OetScoring.PronunciationProjectedBand(70);
        Assert.Equal(350, r.ScaledScore);
        Assert.True(r.Passed);
        Assert.Equal("B", r.RequiredGrade);
    }

    [Fact]
    public void PronunciationProjectedBand_Just_Below_Anchor_Fails()
    {
        var r = OetScoring.PronunciationProjectedBand(69);
        Assert.True(r.ScaledScore < 350);
        Assert.False(r.Passed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(50)]
    [InlineData(65)]
    [InlineData(69)]
    public void PronunciationProjectedBand_Never_Passes_Below_70(double overall)
    {
        Assert.False(OetScoring.PronunciationProjectedBand(overall).Passed);
    }
}
