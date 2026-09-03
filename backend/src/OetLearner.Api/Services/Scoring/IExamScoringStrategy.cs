namespace OetLearner.Api.Services.Scoring;

/// <summary>
/// Definition of a single grade or band level for an exam family.
/// </summary>
public sealed record GradeBandDefinition(
    string Grade,
    double MinScore,
    double MaxScore,
    string Label);

/// <summary>
/// Result of an exam-family-specific score calculation.
/// </summary>
public sealed record ExamScoreResult(
    string ExamTypeCode,
    string SubtestCode,
    double RawScore,
    double ScaledScore,
    string GradeLabel,
    bool? IsPass,
    string? PassReason,
    string FormattedDisplay,
    IReadOnlyList<GradeBandDefinition> AvailableBands);

/// <summary>
/// Strategy interface for polymorphic exam-family scoring (OET, IELTS, PTE, TOEFL).
/// </summary>
public interface IExamScoringStrategy
{
    string ExamTypeCode { get; }
    double MinScore { get; }
    double MaxScore { get; }
    double DefaultPassThreshold { get; }
    string GradeScaleName { get; }

    ExamScoreResult CalculateScore(string subtestCode, int rawScore, int maxRawScore, string? countryCode = null);
    IReadOnlyList<GradeBandDefinition> GetGradeBands(string subtestCode);
    bool IsPass(string subtestCode, int scaledScore, string? countryCode = null);
    string FormatScoreDisplay(string subtestCode, int scaledScore, string? gradeLetter = null);
    string GetReadinessBandCode(double scaledScore);
}
