using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Contracts;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Module Pathway — Phase 1 DTOs (Foundation)
//
// Covers early flow → audio-check → diagnostic → pathway-generation → results.
// Mirrors the Reading pathway DTOs (ReadingPathwayContracts.cs) in shape and
// naming. Backed by entities in Domain/ListeningPathwayEntities.cs.
// Question/option projections are LEARNER-SAFE — they never leak correct
// answers, accepted synonyms, transcripts, or explanation markdown.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Flattened projection of <see cref="Domain.LearnerListeningProfile"/>.</summary>
public class LearnerListeningProfileResponse
{
    public string UserId { get; set; } = default!;
    public string TargetBand { get; set; } = default!;
    public DateTimeOffset? ExamDate { get; set; }
    public int HoursPerWeek { get; set; }
    public string Profession { get; set; } = default!;
    public string EnglishExposureSource { get; set; } = default!;
    public int ComfortBritish { get; set; }
    public int ComfortAustralian { get; set; }
    public int ComfortVarious { get; set; }
    public bool HasTakenBefore { get; set; }
    public int? PreviousScore { get; set; }
    public int SelfRatedSpeed { get; set; }
    public int SelfRatedNoteTaking { get; set; }
    public int SelfRatedSpelling { get; set; }

    /// <summary>audio_check | diagnostic | foundation | practice | mastery</summary>
    public string CurrentStage { get; set; } = default!;
    public int? CurrentReadinessScore { get; set; }
    public int? PredictedScore { get; set; }

    public DateTimeOffset OnboardingCompletedAt { get; set; }
    public DateTimeOffset? AudioCheckPassedAt { get; set; }
    public DateTimeOffset? PathwayGeneratedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Self-reported audio playback check outcome (§5.4).</summary>
public sealed record AudioCheckRequest(
    [property: Required, StringLength(16)] string Outcome,   // "clear" | "quiet" | "failed"
    [property: Range(0, 100)] int? VolumeLevel);

/// <summary>Result of an <see cref="AudioCheckRequest"/> submission.</summary>
public class AudioCheckResponse
{
    public bool Success { get; set; }
    public string CurrentStage { get; set; } = default!;
    public DateTimeOffset? AudioCheckPassedAt { get; set; }
}

/// <summary>
/// Post-submit, question-scoped Listening Q&amp;A. The attempt and question
/// identifiers are route parameters so the server can derive all assessment
/// evidence from the owned submitted attempt.
/// </summary>
public sealed record ListeningQuestionQnaRequest(
    string Message,
    List<ChatMessageDto> History,
    string? ClientTurnId = null);

public sealed record ListeningQuestionQnaResponse(
    string Reply,
    IReadOnlyList<ChatMessageDto> History,
    bool Grounded,
    bool AdvisoryOnly,
    bool MarksUnaffected,
    string? AiOperationId = null,
    string? AiState = null,
    bool? Cached = null,
    int? RetryAfterSeconds = null);

/// <summary>Single per-question answer payload inside a diagnostic submission.</summary>
public sealed record DiagnosticAnswerSubmission(
    [property: Required, StringLength(64)] string QuestionId,
    [property: StringLength(8)] string? SelectedOption,
    [property: StringLength(256)] string? LearnerAnswer,
    bool IsUnknown,
    [property: Range(0, 36000)] int TimeSpentSeconds,
    [property: Range(0, 50)] int ReplaysUsed,
    bool MarkedForReview);

/// <summary>Rolling per-sub-skill mastery score (L1..L8).</summary>
public class SkillScoreDto
{
    /// <summary>L1..L8 — see <see cref="Domain.LearnerListeningSkillScore"/>.</summary>
    public string SkillCode { get; set; } = default!;

    /// <summary>Display label (e.g. "Detail capture", "Note-taking speed").</summary>
    public string Label { get; set; } = default!;

    /// <summary>0.00 – 10.00, current rolling score.</summary>
    public decimal CurrentScore { get; set; }

    /// <summary>0.00 – 10.00, baseline from the initial diagnostic.</summary>
    public decimal DiagnosticScore { get; set; }

    public int QuestionsAttempted { get; set; }
    public int QuestionsCorrect { get; set; }
}

/// <summary>Per-accent learner competence breakdown.</summary>
public class AccentProgressDto
{
    /// <summary>british | australian | us | non_native</summary>
    public string Accent { get; set; } = default!;

    /// <summary>Display label (e.g. "British (UK)").</summary>
    public string Label { get; set; } = default!;

    /// <summary>0.00 – 100.00.</summary>
    public decimal AccuracyPercentage { get; set; }

    public int QuestionsAttempted { get; set; }
    public int MinutesListened { get; set; }

    /// <summary>1–5 self-reported confidence at early flow.</summary>
    public int SelfConfidenceRating { get; set; }
}

/// <summary>One week of the generated 12-week roadmap (§6.4, §27).</summary>
public class RoadmapWeekDto
{
    [Range(1, 12)] public int WeekNumber { get; set; }

    /// <summary>foundation | practice | mastery</summary>
    public string Phase { get; set; } = default!;

    /// <summary>L1..L8 sub-skill codes targeted this week.</summary>
    public string[] FocusSkills { get; set; } = Array.Empty<string>();

    /// <summary>Accent codes targeted this week.</summary>
    public string[] FocusAccents { get; set; } = Array.Empty<string>();

    public int DailyMinutes { get; set; }
    public bool MockAtEndOfWeek { get; set; }
    public string Notes { get; set; } = "";
}

/// <summary>Spelling-vs-meaning example pair for the spelling-tolerance widget.</summary>
public sealed record SpellingExampleDto(string Wrong, string Right);

/// <summary>Note-taking volume + dropped-detail analytics block (§6.4).</summary>
public class NoteTakingStatsDto
{
    public int CharactersTyped { get; set; }
    public int TypicalRangeLow { get; set; }
    public int TypicalRangeHigh { get; set; }

    /// <summary>Short labels of details the learner missed (e.g. "dose", "frequency").</summary>
    public string[] DroppedDetails { get; set; } = Array.Empty<string>();
}

/// <summary>Spelling-tolerance analytics block (§6.4).</summary>
public class SpellingStatsDto
{
    /// <summary>Count of items where meaning was right but spelling penalised.</summary>
    public int MeaningCorrectSpellingWrong { get; set; }
    public List<SpellingExampleDto> Examples { get; set; } = new();
}

/// <summary>Lightweight pathway-status probe used by the listening landing page.</summary>
public class PathwayStatusResponse
{
    public bool HasProfile { get; set; }
    public string CurrentStage { get; set; } = "audio_check";
    public DateTimeOffset? DiagnosticCompletedAt { get; set; }
    public DateTimeOffset? PathwayGeneratedAt { get; set; }
    public int? CurrentReadinessScore { get; set; }
    public int? DaysUntilExam { get; set; }
}

/// <summary>Full deserialised pathway response for the roadmap screen.</summary>
public class PathwayResponse
{
    public int TotalWeeks { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public List<RoadmapWeekDto> Weeks { get; set; } = new();
}

/// <summary>Auto-save endpoint payload for learner Part-A scratch notes (§25.7).</summary>
public sealed record SaveNotesRequest(
    [property: StringLength(64)] string? QuestionId,
    [property: Required, StringLength(4096)] string NoteMarkdown);

// ═════════════════════════════════════════════════════════════════════════════
// Phase 5 — Mock Test + Analytics Dashboard DTOs (§9, §19).
//
// Mirrors the Phase 1 DTO conventions above: PascalCase records, no
// answer-bearing fields surfaced to the learner client. The 42-question mock
// reuses ListeningPracticeSession (SessionType="mock") and the existing
// IListeningLearnerGradingService grader.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Catalog entry for a published mock test template (§9.1).</summary>
public sealed record MockTemplateDto(
    Guid Id,
    string Title,
    int Difficulty,
    int DurationSeconds,
    int TotalQuestions);

/// <summary>Returned when a learner begins a 42-question full mock (§9.2).</summary>
public sealed record StartMockResponse(
    Guid SessionId,
    int TotalQuestions,
    int DurationSeconds,
    DateTimeOffset? DeadlineAt = null);

/// <summary>Bulk submission of all 42 mock answers (§9.3).</summary>
public sealed record MockSubmitRequest(
    IReadOnlyList<DiagnosticAnswerSubmission> Answers,
    [property: Range(0, 36000)] int TotalDurationSeconds);

/// <summary>Multi-section mock results envelope (§9.4).</summary>
public sealed record MockResultResponse(
    Guid SessionId,
    int RawScore,
    int? ScaledScore,
    string GradeLabel,
    IReadOnlyList<SkillScoreDto> SkillRadar,
    IReadOnlyList<AccentProgressDto> AccentChart,
    int? PredictedScoreLow,
    int? PredictedScoreHigh,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? DeadlineAt = null,
    bool TimedOut = false,
    string? ScoreConversionTableVersionKey = null,
    string? ScoreConversionErrorCode = null,
    string? MarkingPolicyVersionKey = null,
    bool? ScoreConversionPassed = null,
    int? DurationSeconds = null,
    IReadOnlyList<MockPartBreakdownResponse>? PartBreakdown = null,
    MockTimeUsedResponse? TimeUsed = null,
    IReadOnlyList<MockReviewItemResponse>? ItemReview = null,
    IReadOnlyList<MockErrorSummaryResponse>? ErrorSummary = null,
    MockNextStepResponse? NextStep = null,
    string? StudyPlanRoute = null,
    int? TotalQuestions = null);

/// <summary>Hero block of the analytics dashboard (§19.2).</summary>
public sealed record ListeningDashboardDto(
    int ReadinessScore,
    string CurrentStage,
    int? DaysUntilExam,
    int? LastMockScaledScore,
    decimal? AveragePronunciationRetention);

/// <summary>Skill radar payload — wraps <see cref="SkillScoreDto"/>.</summary>
public sealed record SkillRadarDto(IReadOnlyList<SkillScoreDto> Skills);

/// <summary>Accent chart payload — wraps <see cref="AccentProgressDto"/>.</summary>
public sealed record AccentChartDto(IReadOnlyList<AccentProgressDto> Accents);

/// <summary>Score-history points across diagnostics + mocks (§19.4).</summary>
public sealed record ScoreHistoryDto(IReadOnlyList<MockHistoryPoint> Points);

/// <summary>Single data point on the score-history line chart.</summary>
public sealed record MockHistoryPoint(
    DateTimeOffset At,
    int RawScore,
    int? ScaledScore);

/// <summary>Per-day activity counts for the calendar heatmap (§19.7).</summary>
public sealed record CalendarHeatmapDto(IReadOnlyList<CalendarDay> Days);

/// <summary>One day of question-attempt activity.</summary>
public sealed record CalendarDay(DateOnly Date, int QuestionsAttempted);
