using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

// ═══════════════════════════════════════════════════════════════════════════
// Grammar Module — Phase 1 domain extensions
//
// These entities extend the original flat GrammarLesson with a proper
// topic taxonomy, structured content blocks, typed server-graded
// exercises, per-attempt analytics rows, and evaluation-driven
// recommendations. See docs/GRAMMAR.md for the canonical contract.
// ═══════════════════════════════════════════════════════════════════════════

public class GrammarTopic
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;   // "oet" | "ielts" | "pte"

    [MaxLength(96)]
    public string Slug { get; set; } = default!;            // "tenses", "articles" …

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    [MaxLength(512)]
    public string? Description { get; set; }

    [MaxLength(512)]
    public string? IconEmoji { get; set; }

    [MaxLength(16)]
    public string LevelHint { get; set; } = "all";          // all | beginner | intermediate | advanced

    public int SortOrder { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "draft";           // draft | published | archived

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class GrammarContentBlock
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string LessonId { get; set; } = default!;

    public int SortOrder { get; set; }

    [MaxLength(32)]
    public string Type { get; set; } = "prose";             // prose | callout | example | table | note

    public string ContentMarkdown { get; set; } = default!;
    public string ContentJson { get; set; } = "{}";         // Optional typed payload (e.g. table cells)
}

public class GrammarExercise
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string LessonId { get; set; } = default!;

    public int SortOrder { get; set; }

    [MaxLength(32)]
    public string Type { get; set; } = default!;            // mcq | fill_blank | error_correction | sentence_transformation | matching

    public string PromptMarkdown { get; set; } = default!;

    public string OptionsJson { get; set; } = "[]";         // [{id,label}]  (MCQ) or [{left,right}] (matching)

    // SECURITY: never projected through the learner DTO until after submission.
    public string CorrectAnswerJson { get; set; } = "[]";
    public string AcceptedAnswersJson { get; set; } = "[]"; // synonyms / alternate wordings
    public string ExplanationMarkdown { get; set; } = "";

    [MaxLength(16)]
    public string Difficulty { get; set; } = "intermediate"; // beginner | intermediate | advanced

    public int Points { get; set; } = 1;
}

public class GrammarExerciseAttempt
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string LessonId { get; set; } = default!;

    [MaxLength(64)]
    public string ExerciseId { get; set; } = default!;

    public string UserAnswerJson { get; set; } = "{}";
    public bool IsCorrect { get; set; }
    public int PointsEarned { get; set; }
    public int AttemptIndex { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
}

public class GrammarRecommendation
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string LessonId { get; set; } = default!;

    [MaxLength(16)]
    public string Source { get; set; } = default!;          // writing | speaking | diagnostic | manual

    [MaxLength(64)]
    public string? SourceRefId { get; set; }

    [MaxLength(32)]
    public string? RuleId { get; set; }

    public double Relevance { get; set; } = 1.0;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DismissedAt { get; set; }
    public DateTimeOffset? ActedOnAt { get; set; }
}
