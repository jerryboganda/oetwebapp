namespace OetLearner.Api.Contracts;

public sealed record SpeakingSimulationV11TutorOverrideRequest(
    string AssessmentId,
    int EstimatedPracticeScore,
    int ScoreRangeLow,
    int ScoreRangeHigh,
    string Reason,
    string OverrideReportJson);

public sealed record SpeakingSimulationV11TutorOverrideResponse(
    string OverrideId,
    string AssessmentId,
    string SpeakingSessionId,
    string TutorId,
    int EstimatedPracticeScore,
    int ScoreRangeLow,
    int ScoreRangeHigh,
    string Reason,
    string OriginalReportJson,
    string OverrideReportJson,
    string OriginalSpecVersion,
    string OriginalRubricVersion,
    string OriginalCalibrationVersion,
    string? OriginalProvider,
    string? OriginalModelName,
    DateTimeOffset CreatedAt);

public sealed record SpeakingSimulationV11LearnerTutorOverrideResponse(
    string OverrideId,
    string AssessmentId,
    int EstimatedPracticeScore,
    int ScoreRangeLow,
    int ScoreRangeHigh,
    string Reason,
    string OverrideReportJson,
    DateTimeOffset CreatedAt);
