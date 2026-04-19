namespace OetLearner.Api.Services.Progress;

/// <summary>
/// Wire contracts for the Progress v2 learner endpoint. These DTOs are pinned
/// by contract tests — the frontend mapper relies on exact property names.
/// </summary>
public sealed record ProgressPayload(
    ProgressMeta Meta,
    IReadOnlyList<SubtestSummary> Subtests,
    IReadOnlyList<WeeklyTrendPoint> Trend,
    IReadOnlyList<CriterionTrendPoint> CriterionTrend,
    IReadOnlyList<CompletionPoint> Completion,
    IReadOnlyList<SubmissionVolumePoint> SubmissionVolume,
    ReviewUsage ReviewUsage,
    Goals Goals,
    ComparativeBlock? Comparative,
    Totals Totals,
    ProgressFreshness Freshness);

public sealed record ProgressMeta(
    string Range,                // "14d" | "30d" | "90d" | "all"
    string ExamFamilyCode,
    string? TargetCountry,
    int ScoreAxisMin,
    int ScoreAxisMax,
    int GradeBThreshold,
    int? WritingThreshold,       // country-aware; null when country unknown
    string? WritingThresholdGrade,
    string? WritingThresholdReason,
    bool ShowScoreGuaranteeStrip,
    bool ShowCriterionConfidenceBand,
    int MinEvaluationsForTrend);

public sealed record SubtestSummary(
    string SubtestCode,
    int? LatestScaled,
    string? LatestGrade,
    int? TargetScaled,
    int? GapToTarget,
    int? DeltaLast30Days,
    int AttemptCount,
    int EvaluationCount,
    int? ThresholdScaled,        // passing threshold for this subtest (country-aware for writing)
    string? ThresholdReason);

public sealed record WeeklyTrendPoint(
    string WeekKey,              // "2026-W16"
    DateTimeOffset WeekStart,
    Dictionary<string, int?> SubtestScaled,       // "writing" → 340 (null when no data)
    Dictionary<string, int> SubtestCount,         // "writing" → 2
    Dictionary<string, int?> MockScaled,          // "writing" → 360 (null when no mock)
    Dictionary<string, int> MockCount);

public sealed record CriterionTrendPoint(
    string WeekKey,
    DateTimeOffset WeekStart,
    string SubtestCode,
    string CriterionCode,
    string CriterionLabel,
    int AverageScaled,
    int SampleCount,
    /// <summary>Lower bound of the 95% confidence interval, clamped to 0. Equals <c>AverageScaled</c> when N &lt; 3.</summary>
    int LowerCi95,
    /// <summary>Upper bound of the 95% confidence interval, clamped to 500. Equals <c>AverageScaled</c> when N &lt; 3.</summary>
    int UpperCi95);

public sealed record CompletionPoint(
    DateOnly Date,
    int Completed);

public sealed record SubmissionVolumePoint(
    string WeekKey,
    DateTimeOffset WeekStart,
    int Writing,
    int Speaking);

public sealed record ReviewUsage(
    int TotalRequests,
    int CompletedRequests,
    double? AverageTurnaroundHours,
    int CreditsConsumed);

public sealed record Goals(
    int? TargetWritingScore,
    int? TargetSpeakingScore,
    int? TargetReadingScore,
    int? TargetListeningScore,
    DateOnly? TargetExamDate,
    int? DaysToExam,
    string? TargetCountry);

public sealed record ComparativeBlock(
    IReadOnlyList<SubtestComparative> Subtests,
    int CohortSize,
    int MinCohortSize,
    bool HasSufficientCohort,
    string CohortScopeDescription);

public sealed record SubtestComparative(
    string SubtestCode,
    int YourScaled,
    int CohortAverage,
    int CohortMedian,
    double Percentile,
    string Tier);

public sealed record Totals(
    int CompletedAttempts,
    int CompletedEvaluations,
    int MockAttempts,
    int WritingSubmissions,
    int SpeakingSubmissions);

public sealed record ProgressFreshness(
    DateTimeOffset GeneratedAt,
    bool UsesFallbackSeries,
    string ETag);
