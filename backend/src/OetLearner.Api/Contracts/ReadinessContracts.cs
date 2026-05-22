namespace OetLearner.Api.Contracts;

public record ReadinessResponse(
    string TargetDate,
    int WeeksRemaining,
    decimal OverallReadiness,
    string OverallRisk,
    decimal? TargetDateProbability,
    string? WeakestSubtest,
    int RecommendedStudyHoursPerWeek,
    string RecommendedStudyHoursRationale,
    string ConfidenceLevel,
    int DataPointCount,
    SubtestReadinessDto[] SubTests,
    VocabularyReadinessDto Vocabulary,
    BlockerDto[] Blockers,
    RiskFactorResponseDto[] RiskFactors,
    EvidenceDto Evidence,
    DateTimeOffset ComputedAt,
    DateTimeOffset ExpiresAt,
    int Version);

public record SubtestReadinessDto(
    string Code,
    string Name,
    decimal Current,
    decimal Target,
    string Status,
    bool IsWeakest,
    string ConfidenceBand,
    int DataPoints);

public record VocabularyReadinessDto(
    decimal Readiness,
    decimal Target,
    int Mastered,
    int MasteryTarget,
    decimal Accuracy30d,
    int DataPoints);

public record BlockerDto(
    string Id,
    string Title,
    string Description,
    string ActionLabel,
    string ActionHref,
    decimal ImpactScore,
    string Severity);

public record RiskFactorResponseDto(
    string Label,
    string Severity,
    decimal Impact,
    string Description,
    string? ActionHref);

public record EvidenceDto(
    string Source,
    int MocksCompleted,
    int PracticeQuestions,
    int ExpertReviews,
    int VocabReviewed30d,
    string RecentTrend,
    DateTimeOffset LastUpdated);

public record ReadinessHistoryResponseDto(
    DateOnly WeekStartDate,
    decimal Overall,
    decimal Writing,
    decimal Speaking,
    decimal Reading,
    decimal Listening,
    decimal Vocabulary,
    string Risk,
    decimal? TargetDateProbability);

public record ReadinessForecastResponse(
    decimal Probability,
    decimal WeeksNeeded,
    int WeeksAvailable,
    decimal RequiredImprovement,
    decimal SlopePerWeek,
    ForecastScenarioResponseDto[] Scenarios);

public record ForecastScenarioResponseDto(
    string Label,
    int HoursPerWeek,
    decimal ProjectedReadinessAtTarget,
    decimal Probability);

public record AdminReadinessLearnerRow(
    string UserId,
    string DisplayName,
    DateOnly? TargetExamDate,
    decimal OverallReadiness,
    string OverallRisk,
    string? WeakestSubtest,
    decimal? TargetDateProbability,
    DateTimeOffset ComputedAt,
    DateTimeOffset ExpiresAt);

public record AdminReadinessLearnerListResponse(
    int Page,
    int PageSize,
    int Total,
    AdminReadinessLearnerRow[] Items);

public record AdminReadinessMetricsResponse(
    int LearnersWithSnapshot,
    int HighRisk,
    int ModerateRisk,
    int LowRisk,
    int UnknownRisk,
    int InterventionCandidates,
    int StaleSnapshots,
    decimal AvgWriting,
    decimal AvgSpeaking,
    decimal AvgReading,
    decimal AvgListening,
    decimal AvgVocabulary,
    decimal AvgOverall,
    DateTimeOffset GeneratedAt);
