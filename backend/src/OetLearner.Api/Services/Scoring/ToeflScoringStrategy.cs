namespace OetLearner.Api.Services.Scoring;

public sealed class ToeflScoringStrategy(IToeflScoring toeflScoring) : IExamScoringStrategy
{
    public string ExamTypeCode => "TOEFL";
    public double MinScore => 0;
    public double MaxScore => 120;
    public double DefaultPassThreshold => 80;
    public string GradeScaleName => "0–120 Scale (4 x 0–30)";

    private static readonly GradeBandDefinition[] Bands =
    [
        new("95–120", 95, 120, "Advanced (C1+)"),
        new("80–94", 80, 94, "High-Intermediate (B2 - Pass)"),
        new("60–79", 60, 79, "Low-Intermediate (B1)"),
        new("0–59", 0, 59, "Below Low-Intermediate")
    ];

    public ExamScoreResult CalculateScore(string subtestCode, int rawScore, int maxRawScore, string? countryCode = null)
    {
        var max = maxRawScore > 0 ? maxRawScore : 30;
        var scaled = toeflScoring.ScaleSectionScore(rawScore, max);
        var level = toeflScoring.GetSectionLevel(subtestCode, scaled);
        var isPass = scaled >= 20;
        var passReason = isPass ? "Met TOEFL section benchmark (20+)" : "Below TOEFL section benchmark (20)";
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
        var threshold = scaledScore > 30 ? (int)DefaultPassThreshold : 20;
        return toeflScoring.IsPassing(scaledScore, threshold);
    }

    public string FormatScoreDisplay(string subtestCode, int scaledScore, string? gradeLetter = null)
    {
        if (scaledScore > 30)
        {
            return $"{scaledScore} / 120";
        }
        return $"{scaledScore} / 30";
    }

    public string GetReadinessBandCode(double scaledScore)
    {
        return toeflScoring.GetReadinessBand((int)Math.Round(scaledScore));
    }
}
