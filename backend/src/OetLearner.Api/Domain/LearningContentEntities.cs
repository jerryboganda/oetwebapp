using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

// ── Grammar Lessons ──

public class GrammarLesson
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(128)]
    public string Title { get; set; } = default!;

    [MaxLength(512)]
    public string Description { get; set; } = default!;

    [MaxLength(32)]
    public string Category { get; set; } = default!;      // "tenses", "articles", "prepositions", "passive_voice", "conditionals", "modals", "formal_register"

    [MaxLength(16)]
    public string Level { get; set; } = "intermediate";   // "beginner", "intermediate", "advanced"

    public string ContentHtml { get; set; } = default!;   // Lesson content (rich text)
    public string ExercisesJson { get; set; } = "[]";     // Interactive exercises

    public int EstimatedMinutes { get; set; }
    public int SortOrder { get; set; }

    [MaxLength(32)]
    public string? PrerequisiteLessonId { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "active";
}

public class LearnerGrammarProgress
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string LessonId { get; set; } = default!;

    [MaxLength(16)]
    public string Status { get; set; } = "not_started";   // "not_started", "in_progress", "completed"

    public int? ExerciseScore { get; set; }               // Percentage score on exercises
    public string AnswersJson { get; set; } = "{}";
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

// ── Video Lessons ──

public class VideoLesson
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string? SubtestCode { get; set; }              // null = general/overview

    [MaxLength(128)]
    public string Title { get; set; } = default!;

    [MaxLength(1024)]
    public string Description { get; set; } = default!;

    [MaxLength(512)]
    public string VideoUrl { get; set; } = default!;      // CDN URL or streaming service URL

    [MaxLength(512)]
    public string? ThumbnailUrl { get; set; }

    public int DurationSeconds { get; set; }

    [MaxLength(32)]
    public string Category { get; set; } = default!;     // "strategy", "technique", "walkthrough", "masterclass", "tips"

    [MaxLength(32)]
    public string? InstructorName { get; set; }

    public string ChaptersJson { get; set; } = "[]";     // Video chapters with timestamps
    public string ResourcesJson { get; set; } = "[]";    // Downloadable resources

    public int SortOrder { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "active";

    public DateTimeOffset PublishedAt { get; set; }
}

public class LearnerVideoProgress
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string VideoLessonId { get; set; } = default!;

    public int WatchedSeconds { get; set; }
    public bool Completed { get; set; }
    public DateTimeOffset LastWatchedAt { get; set; }
}

// ── Strategy Guides ──

public class StrategyGuide
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string? SubtestCode { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(512)]
    public string Summary { get; set; } = default!;

    public string ContentHtml { get; set; } = default!;   // Full article content

    [MaxLength(32)]
    public string Category { get; set; } = default!;     // "exam_overview", "subtest_strategy", "time_management", "scoring_guide", "common_mistakes", "exam_day"

    public int ReadingTimeMinutes { get; set; }
    public int SortOrder { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "active";

    public DateTimeOffset PublishedAt { get; set; }
}
