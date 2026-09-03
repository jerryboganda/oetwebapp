using OetLearner.Api.Services.Common;

namespace OetLearner.Api.Services.Scoring;

public interface IExamScoringStrategyFactory
{
    IExamScoringStrategy GetStrategy(string? examTypeCode);
}

public sealed class ExamScoringStrategyFactory(
    OetScoringStrategy oetStrategy,
    IeltsScoringStrategy ieltsStrategy,
    PteScoringStrategy pteStrategy,
    ToeflScoringStrategy toeflStrategy) : IExamScoringStrategyFactory
{
    public IExamScoringStrategy GetStrategy(string? examTypeCode)
    {
        var normalized = ExamCodes.Normalize(examTypeCode);
        return normalized switch
        {
            "OET" => oetStrategy,
            "IELTS" => ieltsStrategy,
            "PTE" => pteStrategy,
            "TOEFL" => toeflStrategy,
            _ => oetStrategy
        };
    }
}
