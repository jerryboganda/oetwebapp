namespace OetLearner.Api.Contracts;

public sealed record SpeakingSimulationV11EvidenceResult(
    string EvidenceType,
    string EvidenceStatus,
    string PrimaryCriterionCode,
    int? TurnNumber,
    string QuoteText,
    int? StartMs,
    int? EndMs,
    string? Finding,
    string? Action,
    string ConfidenceLabel,
    decimal? ConfidenceScore,
    string? SourceTranscriptId = null,
    string? SourceRecordingId = null,
    bool IsPrimary = true);

public sealed record SpeakingSimulationV11CriterionResult(
    string CriterionCode,
    string Label,
    int Weight,
    decimal RawScore,
    decimal WeightedScore,
    string ScoreBand,
    string Rationale,
    IReadOnlyList<SpeakingSimulationV11EvidenceResult> Evidence,
    string? Strength = null,
    string? Weakness = null,
    string? Action = null,
    string ConfidenceLabel = "medium",
    decimal? ConfidenceScore = null);

public sealed record SpeakingSimulationV11TaskResult(
    int TaskNumber,
    string Status,
    string? Evidence);

public sealed record SpeakingSimulationV11TimelineItem(
    string Label,
    int? StartMs,
    int? EndMs,
    string? Note);

public sealed record SpeakingSimulationV11Alternative(
    string OriginalQuote,
    string BetterAlternative,
    int? TurnNumber,
    int? StartMs,
    int? EndMs);

public sealed record SpeakingSimulationV11PracticePlanItem(
    string Focus,
    string Action,
    string Frequency,
    string SuccessMeasure);

public sealed record SpeakingSimulationV11CardBreakdown(
    string CardSlot,
    string SpeakingSessionId,
    string AssessmentId,
    int? EstimatedPracticeScore,
    int? ScoreRangeLow,
    int? ScoreRangeHigh,
    string ConfidenceLabel,
    decimal? ConfidenceScore,
    IReadOnlyList<SpeakingSimulationV11CriterionResult> Criteria,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Weaknesses,
    IReadOnlyList<SpeakingSimulationV11TaskResult> TaskMap,
    IReadOnlyList<SpeakingSimulationV11TimelineItem> Timeline,
    IReadOnlyDictionary<string, object?> LanguageAnalysis,
    IReadOnlyDictionary<string, object?> TimeManagement,
    IReadOnlyList<string> TopFive,
    IReadOnlyList<SpeakingSimulationV11Alternative> BetterAlternatives,
    IReadOnlyList<string> Tips,
    IReadOnlyList<SpeakingSimulationV11PracticePlanItem> PracticePlan,
    string? SourceTranscriptId,
    string? SourceRecordingId,
    string? CardVersion);

public sealed record SpeakingSimulationV11AssessmentReport(
    string AssessmentId,
    string AssessmentKind,
    string CardSlot,
    string SpecVersion,
    string RubricVersion,
    string CalibrationVersion,
    string GraphDisclaimer,
    int? EstimatedPracticeScore,
    int? ScoreRangeLow,
    int? ScoreRangeHigh,
    string ConfidenceLabel,
    decimal? ConfidenceScore,
    string? OverallSummary,
    IReadOnlyList<SpeakingSimulationV11CriterionResult> Criteria,
    IReadOnlyList<SpeakingSimulationV11CardBreakdown> CardBreakdowns,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Weaknesses,
    IReadOnlyList<SpeakingSimulationV11TaskResult> TaskMap,
    IReadOnlyList<SpeakingSimulationV11TimelineItem> Timeline,
    IReadOnlyDictionary<string, object?> LanguageAnalysis,
    IReadOnlyDictionary<string, object?> TimeManagement,
    IReadOnlyList<string> TopFive,
    IReadOnlyList<SpeakingSimulationV11Alternative> BetterAlternatives,
    IReadOnlyList<string> Tips,
    IReadOnlyList<SpeakingSimulationV11PracticePlanItem> PracticePlan,
    string? SourceTranscriptId,
    string? SourceRecordingId,
    string? CardVersion,
    DateTimeOffset GeneratedAt);

public sealed record SpeakingSimulationV11AssessmentResponse(
    string AssessmentId,
    string Status,
    string AssessmentKind,
    string CardSlot,
    int? EstimatedPracticeScore,
    int? ScoreRangeLow,
    int? ScoreRangeHigh,
    string GraphDisclaimer,
    string ConfidenceLabel,
    decimal? ConfidenceScore,
    SpeakingSimulationV11AssessmentReport? Report,
    string? TechnicalReviewCode,
    DateTimeOffset GeneratedAt);
