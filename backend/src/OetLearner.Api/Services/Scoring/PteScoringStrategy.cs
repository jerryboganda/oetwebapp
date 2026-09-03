namespace OetLearner.Api.Services.Scoring;

public sealed class PteScoringStrategy(IPteScoring pteScoring) : IExamScoringStrategy
{
    public string ExamTypeCode => "PTE";
    public double MinScore => 10;
    public double MaxScore => 90;
    public double DefaultPassThreshold => 65;
    public string GradeScaleName => "10–90 Scale";

    private static readonly GradeBandDefinition[] Bands =
    [
        new("80–90", 80, 90, "Expert"),
        new("65–79", 65, 79, "Advanced (Pass)"),
        new("50–64", 50, 64, "Upper Intermediate"),
        new("36–49", 36, 49, "Intermediate"),
        new("10–35", 10, 35, "Beginner")
    ];

    public ExamScoreResult CalculateScore(string subtestCode, int rawScore, int maxRawScore, string? countryCode = null)
    {
        var max = maxRawScore > 0 ? maxRawScore : 90;
        var scaled = pteScoring.ScaleToPte(rawScore, max);
        var level = pteScoring.GetSkillLevel(scaled);
        var isPass = pteScoring.IsPassing(scaled, (int)DefaultPassThreshold);
        var passReason = isPass ? "Met PTE 65 benchmark" : "Below PTE 65 benchmark";
        var formatted = FormatScoreDisplay(subtestCode, scaled);

        return new ExamScoreResult(
            ExamTypeCode,
            subtestCode,
            rawScore,
            scaled,
            $"Score {scaled} ({level})",
            isPass,
            passReason,
            formatted,
            Bands);
    }

    public IReadOnlyList<GradeBandDefinition> GetGradeBands(string subtestCode) => Bands;

    public bool IsPass(string subtestCode, int scaledScore, string? countryCode = null)
    {
        return pteScoring.IsPassing(scaledScore, (int)DefaultPassThreshold);
    }

    public string FormatScoreDisplay(string subtestCode, int scaledScore, string? gradeLetter = null)
    {
        return $"{scaledScore} / 90";
    }

    public string GetReadinessBandCode(double scaledScore)
    {
        return scaledScore switch
        {
            < 36 => "not_ready",
            < 50 => "developing",
            < 65 => "borderline",
            < 80 => "exam_ready",
            _ => "strong"
        };
    }
}
