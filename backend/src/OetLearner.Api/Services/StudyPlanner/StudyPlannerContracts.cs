using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.StudyPlanner;

// ── Task Template ───────────────────────────────────────────────────────────

public sealed record TaskTemplateCreate(
    string Slug,
    string Title,
    string SubtestCode,
    string ItemType,
    int DurationMinutes,
    string RationaleMarkdown,
    IReadOnlyList<string>? ProfessionScope,
    string? ExamFamilyCode,
    IReadOnlyList<string>? TargetCountries,
    int? DifficultyMin,
    int? DifficultyMax,
    string? DefaultSection,
    string? DefaultContentPaperId,
    string? TagsCsv);

public sealed record TaskTemplateUpdate(
    string? Title,
    string? ItemType,
    int? DurationMinutes,
    string? RationaleMarkdown,
    IReadOnlyList<string>? ProfessionScope,
    IReadOnlyList<string>? TargetCountries,
    int? DifficultyMin,
    int? DifficultyMax,
    string? DefaultSection,
    string? DefaultContentPaperId,
    string? TagsCsv,
    bool? IsArchived);

public sealed record TaskTemplateListQuery(
    string? SubtestCode,
    string? ExamFamilyCode,
    bool? IncludeArchived,
    string? Search,
    int Page,
    int PageSize);

// ── Plan Template ───────────────────────────────────────────────────────────

public sealed record PlanTemplateCreate(
    string Slug,
    string Name,
    string? Description,
    int? DurationWeeks,
    int? DefaultHoursPerWeek,
    string? ExamFamilyCode);

public sealed record PlanTemplateUpdate(
    string? Name,
    string? Description,
    int? DurationWeeks,
    int? DefaultHoursPerWeek,
    bool? IsArchived);

public sealed record PlanTemplateItemUpsert(
    string? Id,
    string TaskTemplateId,
    int WeekOffset,
    int DayOffsetWithinWeek,
    string Section,
    int Priority,
    bool IsMandatory,
    string? PrerequisiteItemTemplateId,
    int Ordering);

// ── Assignment Rules ────────────────────────────────────────────────────────

public sealed record AssignmentRuleCreate(
    string Name,
    string? ExamFamilyCode,
    int? Priority,
    int? Weight,
    string ConditionJson,
    string TargetTemplateId,
    bool? IsActive);

public sealed record AssignmentRuleUpdate(
    string? Name,
    int? Priority,
    int? Weight,
    string? ConditionJson,
    string? TargetTemplateId,
    bool? IsActive);

// ── Drift Policy ────────────────────────────────────────────────────────────

public sealed record DriftPolicyUpdate(
    int? MildDays,
    int? ModerateDays,
    int? SevereDays,
    string? MildCopy,
    string? ModerateCopy,
    string? SevereCopy,
    string? OnTrackCopy,
    bool? AutoRegenerateOnModerate,
    bool? AutoRegenerateOnSevere);

// ── Rule matching input (for preview + generator) ──────────────────────────

public sealed record LearnerPlanContext(
    string UserId,
    string? ProfessionId,
    string ExamFamilyCode,
    string? TargetCountry,
    int? WeeksToExam,
    int? HoursPerWeek,
    int? TargetWritingScore,
    int? TargetSpeakingScore,
    int? TargetReadingScore,
    int? TargetListeningScore,
    IReadOnlyList<string> WeakSubtests);

public sealed record RuleMatchResult(
    string? TemplateId,
    IReadOnlyList<string> MatchedRuleIds,
    IReadOnlyList<string> ConsideredRuleIds,
    string Reason);

// ── DTO used by learner + admin listing ─────────────────────────────────────

public sealed record StudyPlanItemView(
    string Id,
    string? TaskTemplateId,
    string Title,
    string SubtestCode,
    string ItemType,
    int DurationMinutes,
    string Rationale,
    string? AiRationaleAddendum,
    DateOnly DueDate,
    string Status,
    string Section,
    string? ContentId,
    string? ContentPaperId,
    string? StartUrl,
    int Priority,
    string? PrerequisiteItemId,
    DateTimeOffset? StartedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? SnoozedUntil);
