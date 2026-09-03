using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Services;
using OetLearner.Api.Services.ExamSession;
using OetLearner.Api.Services.Scoring;
using Xunit;

namespace OetLearner.Api.Tests;

public class MultiExamStrategyTests
{
    private readonly OetScoringStrategy _oetStrategy = new();
    private readonly IeltsScoringStrategy _ieltsStrategy = new();
    private readonly PteScoringStrategy _pteStrategy = new(new PteScoring(NullLogger<PteScoring>.Instance));
    private readonly ToeflScoringStrategy _toeflStrategy = new(new ToeflScoring(NullLogger<ToeflScoring>.Instance));

    private readonly ExamScoringStrategyFactory _factory;
    private readonly ExamSessionDriverFactory _driverFactory;

    public MultiExamStrategyTests()
    {
        _factory = new ExamScoringStrategyFactory(_oetStrategy, _ieltsStrategy, _pteStrategy, _toeflStrategy);
        _driverFactory = new ExamSessionDriverFactory(
            new OetExamSessionDriver(),
            new IeltsExamSessionDriver(),
            new PteExamSessionDriver(),
            new ToeflExamSessionDriver());
    }

    [Fact]
    public void Factory_ResolvesAllFourStrategiesCorrectly()
    {
        Assert.IsType<OetScoringStrategy>(_factory.GetStrategy("OET"));
        Assert.IsType<OetScoringStrategy>(_factory.GetStrategy("oet"));
        Assert.IsType<IeltsScoringStrategy>(_factory.GetStrategy("IELTS"));
        Assert.IsType<IeltsScoringStrategy>(_factory.GetStrategy("ielts"));
        Assert.IsType<PteScoringStrategy>(_factory.GetStrategy("PTE"));
        Assert.IsType<PteScoringStrategy>(_factory.GetStrategy("pte"));
        Assert.IsType<ToeflScoringStrategy>(_factory.GetStrategy("TOEFL"));
        Assert.IsType<ToeflScoringStrategy>(_factory.GetStrategy("toefl"));
        Assert.IsType<OetScoringStrategy>(_factory.GetStrategy(null));
        Assert.IsType<OetScoringStrategy>(_factory.GetStrategy("unknown"));
    }

    [Fact]
    public void OetStrategy_CalculatesScoreAndMaintains30Of42Benchmark()
    {
        var passScore = _oetStrategy.CalculateScore("reading", 30, 42);
        Assert.Equal(350, passScore.ScaledScore);
        Assert.Equal("Grade B", passScore.GradeLabel);
        Assert.True(passScore.IsPass);

        var zeroScore = _oetStrategy.CalculateScore("listening", 0, 42);
        Assert.Equal(0, zeroScore.ScaledScore);
        Assert.Equal("Grade E", zeroScore.GradeLabel);
        Assert.False(zeroScore.IsPass);

        var maxScore = _oetStrategy.CalculateScore("reading", 42, 42);
        Assert.Equal(500, maxScore.ScaledScore);
        Assert.Equal("Grade A", maxScore.GradeLabel);
        Assert.True(maxScore.IsPass);
    }

    [Fact]
    public void OetStrategy_WritingCountryResolution()
    {
        // 300 scaled score is Grade C+
        Assert.True(_oetStrategy.IsPass("writing", 300, "US"));
        Assert.True(_oetStrategy.IsPass("writing", 300, "QA"));
        Assert.False(_oetStrategy.IsPass("writing", 300, "GB"));
        Assert.False(_oetStrategy.IsPass("writing", 300, "AU"));
        Assert.True(_oetStrategy.IsPass("writing", 350, "GB"));
    }

    [Fact]
    public void IeltsStrategy_CalculatesBandsAndHalfBandRounding()
    {
        var result = _ieltsStrategy.CalculateScore("reading", 32, 40);
        Assert.InRange(result.ScaledScore, 7.0, 7.5);
        Assert.StartsWith("Band ", result.GradeLabel);
        Assert.True(result.IsPass);

        Assert.True(_ieltsStrategy.IsPass("overall", 70)); // scaled 7.0
        Assert.False(_ieltsStrategy.IsPass("overall", 65)); // scaled 6.5
    }

    [Fact]
    public void PteStrategy_CalculatesScoresOn10To90Scale()
    {
        var result = _pteStrategy.CalculateScore("reading", 65, 90);
        Assert.Equal(68, result.ScaledScore);
        Assert.True(result.IsPass);

        Assert.True(_pteStrategy.IsPass("overall", 65));
        Assert.False(_pteStrategy.IsPass("overall", 64));
    }

    [Fact]
    public void ToeflStrategy_CalculatesScoresOn0To120Scale()
    {
        var sectionResult = _toeflStrategy.CalculateScore("reading", 25, 30);
        Assert.Equal(25, sectionResult.ScaledScore);
        Assert.Contains("Advanced", sectionResult.GradeLabel);
        Assert.True(sectionResult.IsPass);

        Assert.True(_toeflStrategy.IsPass("overall", 80));
        Assert.False(_toeflStrategy.IsPass("overall", 79));
        Assert.True(_toeflStrategy.IsPass("reading", 22));
    }

    [Fact]
    public void DriverFactory_ResolvesAllDriversCorrectly()
    {
        var oetDriver = _driverFactory.GetDriver("OET");
        Assert.IsType<OetExamSessionDriver>(oetDriver);
        var oetConfig = oetDriver.CreateSessionConfig("reading", "exam");
        Assert.Equal(60, oetConfig.TimingRules.TotalDurationMinutes);
        Assert.Equal(15, oetConfig.TimingRules.PartADurationMinutes);
        Assert.True(oetConfig.TimingRules.EnforceHardLock);

        var ieltsDriver = _driverFactory.GetDriver("IELTS");
        Assert.IsType<IeltsExamSessionDriver>(ieltsDriver);

        var pteDriver = _driverFactory.GetDriver("PTE");
        Assert.IsType<PteExamSessionDriver>(pteDriver);

        var toeflDriver = _driverFactory.GetDriver("TOEFL");
        Assert.IsType<ToeflExamSessionDriver>(toeflDriver);
    }

    [Fact]
    public void ScoringService_DelegatesPolymorphicallyThroughFactory()
    {
        var service = new ScoringService(_factory);
        Assert.Contains("Grade B", service.FormatScoreDisplay("OET", 380));
        Assert.Contains("Band Score", service.FormatScoreDisplay("IELTS", 7.5));
        Assert.Contains("/ 90", service.FormatScoreDisplay("PTE", 65));
        Assert.Contains("/ 120", service.FormatScoreDisplay("TOEFL", 90));
    }
}
