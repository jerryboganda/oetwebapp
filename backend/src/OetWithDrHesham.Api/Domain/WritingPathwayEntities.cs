using System.ComponentModel.DataAnnotations;

namespace OetWithDrHesham.Api.Domain;

public class LearnerWritingProfile
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string Profession { get; set; } = "medicine";

    [MaxLength(8)]
    public string TargetBand { get; set; } = "B";

    public DateTimeOffset? ExamDate { get; set; }

    public int DaysPerWeek { get; set; } = 5;

    public int MinutesPerDay { get; set; } = 45;

    [MaxLength(32)]
    public string TargetCountry { get; set; } = "GB";

    public string LetterTypeFocusJson { get; set; } = "[]";

    [MaxLength(32)]
    public string CurrentStage { get; set; } = "onboarding";

    public int? CurrentReadinessScore { get; set; }

    public int? PredictedScore { get; set; }

    [MaxLength(64)]
    public string? LastDiagnosticEvaluationId { get; set; }
    public DateTimeOffset? OnboardingCompletedAt { get; set; }
    public DateTimeOffset? PathwayGeneratedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string? SubDiscipline { get; set; }

    public int? YearsExperience { get; set; }

    public bool OptInCommunity { get; set; } = false;

    public bool OptInLeaderboard { get; set; } = false;

    public bool OptInDataForTraining { get; set; } = false;

    /// <summary>
    /// Buddy System opt-in (spec §23.5). Until the learner explicitly
    /// flips this to <c>true</c>, the matcher will skip them.
    /// </summary>
    public bool OptInBuddy { get; set; } = false;

    public string AccommodationProfileJson { get; set; } = "{}";

    [MaxLength(32)]
    public string? CanonVersionPinned { get; set; }
}

public class LearnerWritingPathway
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public int TotalWeeks { get; set; } = 10;
    public DateTimeOffset GeneratedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string WeeksJson { get; set; } = "[]";

    public string WeaknessVectorJson { get; set; } = "{}";

    public string SubSkillMasteryJson { get; set; } = "{}";

    public DateTimeOffset? LastRecalculatedAt { get; set; }

    public Guid? DiagnosticSubmissionId { get; set; }
}

public class WritingDailyPlanItem
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public DateOnly PlanDate { get; set; }
    public int Ordinal { get; set; }

    [MaxLength(32)]
    public string ItemType { get; set; } = "practice";

    [MaxLength(4)]
    public string? FocusSkill { get; set; }

    [MaxLength(32)]
    public string? FocusCriterion { get; set; }

    public int EstimatedMinutes { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(512)]
    public string Description { get; set; } = default!;

    [MaxLength(512)]
    public string ActionHref { get; set; } = default!;

    [MaxLength(64)]
    public string? ContentId { get; set; }

    public string PayloadJson { get; set; } = "{}";

    [MaxLength(16)]
    public string Status { get; set; } = "pending";

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? SkippedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class WritingLesson
{
    public Guid Id { get; set; }

    [MaxLength(128)]
    public string Slug { get; set; } = default!;

    [MaxLength(256)]
    public string Title { get; set; } = default!;

    [MaxLength(4)]
    public string SkillCode { get; set; } = default!;

    public int OrderIndex { get; set; }
    public int EstimatedMinutes { get; set; }
    public string BodyMarkdownEn { get; set; } = "";
    public string DrillPrompt { get; set; } = "";
    public string QuizJson { get; set; } = "[]";
    public Guid? PrerequisiteLessonId { get; set; }
    public bool IsPublished { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class LearnerWritingLessonProgress
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public Guid LessonId { get; set; }
    public bool BodyRead { get; set; }
    public bool DrillCompleted { get; set; }
    public int? QuizScore { get; set; }
    public int QuizAttempts { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}