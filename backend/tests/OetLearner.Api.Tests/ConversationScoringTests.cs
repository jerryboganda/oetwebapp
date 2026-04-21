using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public class ConversationScoringTests
{
    [Fact]
    public void Anchor_0_Mean_Projects_To_0()
        => Assert.Equal(0, OetScoring.ConversationProjectedScaled(0));

    [Fact]
    public void Anchor_3_Mean_Projects_To_250()
        => Assert.Equal(250, OetScoring.ConversationProjectedScaled(3.0));

    [Fact]
    public void Anchor_4_Point_2_Mean_Projects_To_350_Pass()
        => Assert.Equal(350, OetScoring.ConversationProjectedScaled(4.2));

    [Fact]
    public void Anchor_5_Mean_Projects_To_417()
        => Assert.Equal(417, OetScoring.ConversationProjectedScaled(5.0));

    [Fact]
    public void Anchor_6_Mean_Projects_To_500()
        => Assert.Equal(500, OetScoring.ConversationProjectedScaled(6.0));

    [Theory]
    [InlineData(-1.0, 0)]
    [InlineData(7.0, 500)]
    public void OutOfRange_Mean_Is_Clamped(double mean, int expectedScaled)
        => Assert.Equal(expectedScaled, OetScoring.ConversationProjectedScaled(mean));

    [Fact]
    public void Projection_Is_Monotonic()
    {
        var prev = -1;
        for (var i = 0; i <= 60; i++)
        {
            var scaled = OetScoring.ConversationProjectedScaled(i / 10.0);
            Assert.True(scaled >= prev);
            prev = scaled;
        }
    }

    [Fact]
    public void ProjectedBand_Returns_Speaking_Result()
    {
        var band = OetScoring.ConversationProjectedBand(4.2, 4.2, 4.2, 4.2);
        Assert.Equal(350, band.ScaledScore);
        Assert.True(band.Passed);
        Assert.Equal("B", band.Grade);
    }

    [Fact]
    public void ProjectedBand_Fails_Below_4Point2_Mean()
    {
        var band = OetScoring.ConversationProjectedBand(3.0, 3.5, 3.0, 3.5);
        Assert.False(band.Passed);
        Assert.True(band.ScaledScore < 350);
    }
}
