using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>Canonical <see cref="WritingDraftV2.Status"/> values (column is varchar(16)).</summary>
public static class WritingDraftStatuses
{
    public const string Active = "active";

    /// <summary>Consumed by <see cref="WritingDraftV2.SubmissionId"/>; a new attempt starts only via a matching version.</summary>
    public const string Submitted = "submitted";
}

public class WritingDraftV2
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public Guid ScenarioId { get; set; }

    [MaxLength(16)]
    public string Mode { get; set; } = "practice";

    public string Content { get; set; } = string.Empty;

    public int WordCount { get; set; }

    public int TimeSpentSeconds { get; set; }

    public DateTimeOffset LastSavedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Optimistic-concurrency token: 1 after the first write, +1 on every accepted write.</summary>
    [ConcurrencyCheck]
    public int Version { get; set; } = 1;

    /// <summary><see cref="WritingDraftStatuses"/>.</summary>
    [MaxLength(16)]
    public string Status { get; set; } = WritingDraftStatuses.Active;

    /// <summary>The submission that consumed this draft (set with <see cref="WritingDraftStatuses.Submitted"/>).</summary>
    public Guid? SubmissionId { get; set; }

    /// <summary>Exam-clock phase at the last save: reading | writing. Null on legacy rows.</summary>
    [MaxLength(16)]
    public string? Phase { get; set; }

    /// <summary>Pause-while-away timer: seconds left in each window at the last save.</summary>
    public int? ReadingSecondsRemaining { get; set; }

    public int? WritingSecondsRemaining { get; set; }

    /// <summary>When the current attempt started (row creation or a new attempt). Null on legacy rows.</summary>
    public DateTimeOffset? AttemptStartedAt { get; set; }
}
