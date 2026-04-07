using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public class WritingCoachSession
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string AttemptId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public int SuggestionsGenerated { get; set; }
    public int SuggestionsAccepted { get; set; }
    public int SuggestionsDismissed { get; set; }
    public DateTimeOffset StartedAt { get; set; }
}

public class WritingCoachSuggestion
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string SessionId { get; set; } = default!;

    [MaxLength(64)]
    public string AttemptId { get; set; } = default!;

    [MaxLength(32)]
    public string SuggestionType { get; set; } = default!; // "grammar", "vocabulary", "structure", "tone", "conciseness", "format"

    public string OriginalText { get; set; } = default!;
    public string SuggestedText { get; set; } = default!;

    [MaxLength(512)]
    public string Explanation { get; set; } = default!;

    public int StartOffset { get; set; }                   // Character offset in document
    public int EndOffset { get; set; }

    [MaxLength(16)]
    public string? Resolution { get; set; }                // "accepted", "dismissed", null (pending)

    public DateTimeOffset CreatedAt { get; set; }
}
