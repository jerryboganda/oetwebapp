using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// Review item — one unit of spaced repetition across ALL skills.
///
/// MISSION CRITICAL: never write these directly. Always use
/// <c>IReviewItemSeeder</c>. Idempotency key is (UserId, SourceType, SourceId).
/// Canonical spec: <c>docs/REVIEW-MODULE.md</c>.
/// </summary>
public class ReviewItem
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    /// <summary>Canonical value from <see cref="ReviewSourceTypes"/>.</summary>
    [MaxLength(32)]
    public string SourceType { get; set; } = default!;

    [MaxLength(128)]
    public string? SourceId { get; set; }                  // Reference to originating entity (see REVIEW-MODULE.md §2)

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(32)]
    public string? CriterionCode { get; set; }

    /// <summary>Short learner-facing title (e.g. "Filler words at handover opening").</summary>
    [MaxLength(180)]
    public string? Title { get; set; }

    /// <summary>
    /// Discriminator for the polymorphic <c>ReviewItemRenderer</c> in the UI.
    /// Canonical values mirror <see cref="ReviewSourceTypes"/>:
    /// <c>grammar | vocabulary | pronunciation | writing_issue | speaking_issue |
    /// reading_miss | listening_miss | mock_miss</c>.
    /// </summary>
    [MaxLength(32)]
    public string? PromptKind { get; set; }

    public string QuestionJson { get; set; } = "{}";       // What to show (the mistake or concept)
    public string AnswerJson { get; set; } = "{}";         // Correct version + explanation

    /// <summary>
    /// Free-form JSON payload carrying the rich context needed by the renderer
    /// (audio URL, IPA, passage excerpt, transcript snippet, rule anchor, etc.).
    /// Unbounded; schema owned by the renderer per-prompt-kind.
    /// </summary>
    public string? RichContentJson { get; set; }

    public double EaseFactor { get; set; } = 2.5;          // SM-2 ease factor
    public int IntervalDays { get; set; } = 1;             // Current interval
    public int ReviewCount { get; set; }
    public int ConsecutiveCorrect { get; set; }
    public DateOnly DueDate { get; set; }
    public DateTimeOffset? LastReviewedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last quality submitted (0-5), useful for analytics and undo UI.</summary>
    public int? LastQuality { get; set; }

    public DateTimeOffset? SuspendedAt { get; set; }

    [MaxLength(120)]
    public string? SuspendedReason { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "active";         // "active", "mastered", "suspended"
}

/// <summary>
/// Captures the pre-state of a <see cref="ReviewItem"/> before its most recent
/// SM-2 transition. Used by <c>POST /v1/review/items/{id}/undo</c> to revert a
/// single submission. Only the latest transition per item is retained (older
/// rows are pruned on write).
/// </summary>
public class ReviewItemTransition
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ReviewItemId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public double PrevEaseFactor { get; set; }
    public int PrevIntervalDays { get; set; }
    public int PrevReviewCount { get; set; }
    public int PrevConsecutiveCorrect { get; set; }
    public DateOnly PrevDueDate { get; set; }
    public DateTimeOffset? PrevLastReviewedAt { get; set; }
    public int? PrevLastQuality { get; set; }

    [MaxLength(16)]
    public string PrevStatus { get; set; } = "active";

    public int AppliedQuality { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
}

/// <summary>
/// Canonical closed enum of <see cref="ReviewItem.SourceType"/> values.
/// Adding a new value requires updating <c>docs/REVIEW-MODULE.md</c>.
/// </summary>
public static class ReviewSourceTypes
{
    public const string GrammarError = "grammar_error";
    public const string ReadingMiss = "reading_miss";
    public const string ListeningMiss = "listening_miss";
    public const string WritingIssue = "writing_issue";
    public const string SpeakingIssue = "speaking_issue";
    public const string PronunciationFinding = "pronunciation_finding";
    public const string MockMiss = "mock_miss";
    public const string Vocabulary = "vocabulary"; // projection only — never written

    public static readonly IReadOnlyCollection<string> All = new[]
    {
        GrammarError,
        ReadingMiss,
        ListeningMiss,
        WritingIssue,
        SpeakingIssue,
        PronunciationFinding,
        MockMiss,
        Vocabulary,
    };

    public static bool IsValid(string? source)
        => !string.IsNullOrWhiteSpace(source) && All.Contains(source);

    /// <summary>Maps a source type to a default PromptKind for the renderer.</summary>
    public static string PromptKindFor(string sourceType) => sourceType switch
    {
        GrammarError => "grammar",
        ReadingMiss => "reading_miss",
        ListeningMiss => "listening_miss",
        WritingIssue => "writing_issue",
        SpeakingIssue => "speaking_issue",
        PronunciationFinding => "pronunciation",
        MockMiss => "mock_miss",
        Vocabulary => "vocabulary",
        _ => "generic",
    };
}
