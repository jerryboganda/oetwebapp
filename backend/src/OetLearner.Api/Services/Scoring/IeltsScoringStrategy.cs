namespace OetLearner.Api.Services.Scoring;

public sealed class IeltsScoringStrategy(IIeltsMockEngine? mockEngine = null) : IExamScoringStrategy
{
    public string ExamTypeCode => "IELTS";
    public double MinScore => 0;
    public double MaxScore => 9.0;
    public double DefaultPassThreshold => 7.0;
    public string GradeScaleName => "0–9 Band Scale";

    private static readonly GradeBandDefinition[] Bands =
    [
        new("Band 9", 9.0, 9.0, "Expert"),
        new("Band 8", 8.0, 8.9, "Very Good"),
        new("Band 7", 7.0, 7.9, "Good (Pass)"),
        new("Band 6", 6.0, 6.9, "Competent"),
        new("Band 5", 5.0, 5.9, "Modest"),
        new("Band 4", 4.0, 4.9, "Limited")
    ];

    public ExamScoreResult CalculateScore(string subtestCode, int rawScore, int maxRawScore, string? countryCode = null)
    {
        double band;
        if (maxRawScore > 0 && maxRawScore != 9)
        {
            var ratio = (double)rawScore / maxRawScore;
            band = Math.Clamp(ratio * 9.0, 0, 9.0);
        }
        else
        {
            band = Math.Clamp(rawScore, 0, 9.0);
        }

        var roundedBand = Math.Round(band * 2, MidpointRounding.AwayFromZero) / 2.0;
        var gradeLabel = $"Band {roundedBand:0.0}";
        var isPass = roundedBand >= DefaultPassThreshold;
        var passReason = isPass ? "Met IELTS 7.0 benchmark" : "Below IELTS 7.0 benchmark";
        var formatted = FormatScoreDisplay(subtestCode, (int)(roundedBand * 10), gradeLabel);

        return new ExamScoreResult(
            ExamTypeCode,
            subtestCode,
            rawScore,
            roundedBand,
            gradeLabel,
            isPass,
            passReason,
            formatted,
            Bands);
    }

    public IReadOnlyList<GradeBandDefinition> GetGradeBands(string subtestCode) => Bands;

    public bool IsPass(string subtestCode, int scaledScore, string? countryCode = null)
    {
        var band = scaledScore > 9 ? scaledScore / 10.0 : scaledScore;
        return band >= DefaultPassThreshold;
    }

    public string FormatScoreDisplay(string subtestCode, int scaledScore, string? gradeLetter = null)
    {
        var band = scaledScore > 9 ? scaledScore / 10.0 : scaledScore;
        return $"{band:0.0} Band Score";
    }

    public string GetReadinessBandCode(double scaledScore)
    {
        var band = scaledScore > 9 ? scaledScore / 10.0 : scaledScore;
        return band switch
        {
            < 5.0 => "not_ready",
            < 6.0 => "developing",
            < 7.0 => "borderline",
            < 8.0 => "exam_ready",
            _ => "strong"
        };
    }
}
