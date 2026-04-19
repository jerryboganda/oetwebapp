using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Reusable authored task in the admin-managed task library. Each task is the
/// atomic unit of work that can be composed into a <see cref="StudyPlanTemplate"/>
/// or selected by an assignment rule.
///
/// <para>
/// Lifecycle: admin creates a task with <see cref="RationaleMarkdown"/>, optional
/// default <see cref="DefaultContentPaperId"/>, profession + country scope. Archived
/// tasks stop being selected by the generator but remain as references on historic
/// plans so `StudyPlanItem.TaskTemplateId` never dangles.
/// </para>
///
/// <para>
/// See <c>docs/STUDY-PLANNER-SPEC.md</c> §2. Never author tasks in code.
/// </para>
/// </summary>
[Index(nameof(Slug), IsUnique = true)]
[Index(nameof(SubtestCode), nameof(IsArchived))]
public class StudyPlanTaskTemplate
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(128)]
    public string Slug { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(32)]
    public string ItemType { get; set; } = default!;

    public int DurationMinutes { get; set; }

    [MaxLength(4000)]
    public string RationaleMarkdown { get; set; } = default!;

    /// <summary>JSON array of profession codes. Empty/`[]` means all professions.</summary>
    public string ProfessionScopeJson { get; set; } = "[]";

    [MaxLength(16)]
    public string ExamFamilyCode { get; set; } = "oet";

    /// <summary>JSON array of ISO-3166 country codes. Empty/`[]` means all countries.</summary>
    public string TargetCountriesJson { get; set; } = "[]";

    public int DifficultyMin { get; set; } = 1;
    public int DifficultyMax { get; set; } = 5;

    /// <summary>Preferred section when scheduled (today | thisWeek | nextCheckpoint | weakSkillFocus).</summary>
    [MaxLength(32)]
    public string DefaultSection { get; set; } = "thisWeek";

    /// <summary>Deep-link target — FK to ContentPaper.Id. Null = start at subtest module root.</summary>
    [MaxLength(64)]
    public string? DefaultContentPaperId { get; set; }

    [MaxLength(512)]
    public string TagsCsv { get; set; } = "";

    public bool IsArchived { get; set; }

    [MaxLength(64)]
    public string CreatedByAdminId { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A named, ordered sequence of task templates. Assignment rules route a learner
/// to one of these templates; the generator materialises it into concrete
/// <see cref="StudyPlanItem"/> rows with due-dates relative to today or target-exam.
/// </summary>
[Index(nameof(Slug), IsUnique = true)]
public class StudyPlanTemplate
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(128)]
    public string Slug { get; set; } = default!;

    [MaxLength(200)]
    public string Name { get; set; } = default!;

    [MaxLength(2000)]
    public string Description { get; set; } = "";

    public int DurationWeeks { get; set; } = 8;
    public int DefaultHoursPerWeek { get; set; } = 10;

    [MaxLength(16)]
    public string ExamFamilyCode { get; set; } = "oet";

    public bool IsArchived { get; set; }

    [MaxLength(64)]
    public string CreatedByAdminId { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

[Index(nameof(PlanTemplateId), nameof(Ordering))]
public class StudyPlanTemplateItem
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string PlanTemplateId { get; set; } = default!;

    [MaxLength(64)]
    public string TaskTemplateId { get; set; } = default!;

    public int WeekOffset { get; set; }
    public int DayOffsetWithinWeek { get; set; }

    [MaxLength(32)]
    public string Section { get; set; } = "thisWeek";

    public int Priority { get; set; } = 50;
    public bool IsMandatory { get; set; } = true;

    [MaxLength(64)]
    public string? PrerequisiteItemTemplateId { get; set; }

    public int Ordering { get; set; }
}

/// <summary>
/// Routes a learner profile (goal + readiness) to a plan template. Multiple rules
/// may match; highest priority + weight wins (see StudyPlannerRuleEngine). Rules are
/// pure read of an immutable condition JSON — no DB lookups during matching.
/// </summary>
[Index(nameof(ExamFamilyCode), nameof(IsActive), nameof(Priority))]
public class StudyPlanAssignmentRule
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(200)]
    public string Name { get; set; } = default!;

    [MaxLength(16)]
    public string ExamFamilyCode { get; set; } = "oet";

    public int Priority { get; set; } = 100;
    public int Weight { get; set; } = 50;

    /// <summary>
    /// JSON shape:
    /// <code>
    /// {
    ///   "professions": ["medicine","nursing"],
    ///   "countries":   ["UK","IE"],
    ///   "minWeeksToExam": 4, "maxWeeksToExam": 16,
    ///   "minHoursPerWeek": 5, "maxHoursPerWeek": 40,
    ///   "minOverallTarget": 350,
    ///   "weakSubtests": ["writing","speaking"],
    ///   "requireWeakSubtests": "any"  // or "all"
    /// }
    /// </code>
    /// All fields optional. Missing = matches anything.
    /// </summary>
    public string ConditionJson { get; set; } = "{}";

    [MaxLength(64)]
    public string TargetTemplateId { get; set; } = default!;

    public bool IsActive { get; set; } = true;

    [MaxLength(64)]
    public string CreatedByAdminId { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Admin-configurable drift thresholds + copy. Singleton per exam family.
/// Replaces the old hardcoded `>3/>7/>14` constants in LearnerService.
/// </summary>
[Index(nameof(ExamFamilyCode), IsUnique = true)]
public class StudyPlanDriftPolicy
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(16)]
    public string ExamFamilyCode { get; set; } = "oet";

    public int MildDays { get; set; } = 3;
    public int ModerateDays { get; set; } = 7;
    public int SevereDays { get; set; } = 14;

    [MaxLength(1024)]
    public string MildCopy { get; set; } = "You're slightly behind — spend 15 minutes today to catch up.";

    [MaxLength(1024)]
    public string ModerateCopy { get; set; } = "You're falling behind. We recommend regenerating your plan to refocus.";

    [MaxLength(1024)]
    public string SevereCopy { get; set; } = "Your plan needs a reset. Regenerate to tailor a fresh path forward.";

    [MaxLength(1024)]
    public string OnTrackCopy { get; set; } = "You're on track — keep going.";

    public bool AutoRegenerateOnModerate { get; set; }
    public bool AutoRegenerateOnSevere { get; set; } = true;

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Append-only audit of every plan generation. Lets admin see why a learner got
/// their current plan (which rule matched, whether AI was used, which AiUsageRecord row).
/// </summary>
[Index(nameof(UserId), nameof(CreatedAt))]
[Index(nameof(PlanId))]
public class StudyPlanGenerationLog
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string PlanId { get; set; } = default!;

    [MaxLength(32)]
    public string TriggeredBy { get; set; } = default!; // goal_saved | attempt | drift | manual | admin_override

    [MaxLength(1024)]
    public string RuleIdsMatchedCsv { get; set; } = "";

    [MaxLength(64)]
    public string? TemplateId { get; set; }

    public bool AiUsed { get; set; }

    [MaxLength(64)]
    public string? AiFeatureCode { get; set; }

    [MaxLength(64)]
    public string? AiUsageRecordId { get; set; }

    public int ItemCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Support agent / admin override action against a specific learner's plan.
/// Emitted alongside a normal AuditEvent row. Purely advisory for UI — the
/// actual state change is on <see cref="StudyPlanItem"/>.
/// </summary>
[Index(nameof(UserId), nameof(CreatedAt))]
public class StudyPlanAdminOverride
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string? ItemId { get; set; }

    [MaxLength(64)]
    public string AdminId { get; set; } = default!;

    [MaxLength(32)]
    public string Action { get; set; } = default!; // regenerate | add_item | edit_item | mark_complete | remove

    [MaxLength(1024)]
    public string Reason { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
