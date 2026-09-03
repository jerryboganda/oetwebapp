namespace OetLearner.Api.Services.Scoring;

public sealed class OetScoringStrategy : IExamScoringStrategy
{
    public string ExamTypeCode => "OET";
    public double MinScore => 0;
    public double MaxScore => 500;
    public double DefaultPassThreshold => 350;
    public string GradeScaleName => "A–E (0–500)";

    private static readonly GradeBandDefinition[] Bands =
    [
        new("A", 450, 500, "Very High"),
        new("B", 350, 449, "High (Pass)"),
        new("C+", 300, 349, "Satisfactory"),
        new("C", 200, 299, "Adequate"),
        new("D", 100, 199, "Limited"),
        new("E", 0, 99, "Minimal")
    ];

    public ExamScoreResult CalculateScore(string subtestCode, int rawScore, int maxRawScore, string? countryCode = null)
    {
        var sub = (subtestCode ?? "reading").Trim().ToLowerInvariant();
        int scaledInt;
        if (sub is "reading" or "listening")
        {
            scaledInt = OetScoring.OetRawToScaled(rawScore);
        }
        else if (sub == "writing")
        {
            var max = maxRawScore > 0 ? maxRawScore : 38;
            scaledInt = (int)Math.Round(Math.Clamp((double)rawScore * 500.0 / max, 0.0, 500.0), MidpointRounding.AwayFromZero);
        }
        else
        {
            var max = maxRawScore > 0 ? maxRawScore : OetScoring.SpeakingRubricMax;
            scaledInt = (int)Math.Round(Math.Clamp((double)rawScore * 500.0 / max, 0.0, 500.0), MidpointRounding.AwayFromZero);
        }

        var gradeLetter = OetScoring.OetGradeLetterFromScaled(scaledInt);
        var gradeLabel = OetScoring.OetGradeLabel(gradeLetter);

        bool? isPass;
        string? passReason;
        if (sub == "writing" && !string.IsNullOrWhiteSpace(countryCode))
        {
            var res = OetScoring.GradeWriting(scaledInt, countryCode);
            isPass = res.Passed;
            passReason = res.Reason ?? (isPass == true ? $"Met {res.ProvidedCountry} pass mark ({res.RequiredScaled}+)" : $"Below {res.ProvidedCountry} pass mark ({res.RequiredScaled})");
        }
        else
        {
            isPass = scaledInt >= (int)DefaultPassThreshold;
            passReason = isPass.Value ? "Met standard grade B threshold (350+)" : "Below grade B threshold (350)";
        }

        var formatted = FormatScoreDisplay(subtestCode, scaledInt, gradeLabel);

        return new ExamScoreResult(
            ExamTypeCode,
            subtestCode,
            rawScore,
            scaledInt,
            gradeLabel,
            isPass,
            passReason,
            formatted,
            Bands);
    }

    public IReadOnlyList<GradeBandDefinition> GetGradeBands(string subtestCode) => Bands;

    public bool IsPass(string subtestCode, int scaledScore, string? countryCode = null)
    {
        var sub = (subtestCode ?? "").Trim().ToLowerInvariant();
        if (sub == "writing" && !string.IsNullOrWhiteSpace(countryCode))
        {
            var res = OetScoring.GradeWriting(scaledScore, countryCode);
            return res.Passed ?? (scaledScore >= 350);
        }
        return scaledScore >= DefaultPassThreshold;
    }

    public string FormatScoreDisplay(string subtestCode, int scaledScore, string? gradeLetter = null)
    {
        var grade = gradeLetter ?? OetScoring.OetGradeLabel(OetScoring.OetGradeLetterFromScaled(scaledScore));
        return $"{scaledScore} / {grade}";
    }

    public string GetReadinessBandCode(double scaledScore)
    {
        var band = OetScoring.SpeakingReadinessBandFromScaled((int)Math.Round(scaledScore));
        return OetScoring.SpeakingReadinessBandCode(band);
    }
}
