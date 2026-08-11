using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// Structured, source-linked transcript turn captured from the latest
/// SpeakingTranscript. No text is invented when the source transcript is
/// absent or malformed.
/// </summary>
public sealed class SpeakingSimulationV11TurnEvidence
{
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SpeakingSessionId { get; set; } = default!;

    [MaxLength(64)]
    public string? AssessmentId { get; set; }

    [MaxLength(64)]
    public string SourceTranscriptId { get; set; } = default!;

    [MaxLength(64)]
    public string? SourceRecordingId { get; set; }

    [MaxLength(64)]
    public string CardVersion { get; set; } = string.Empty;

    public int TurnNumber { get; set; }

    [MaxLength(32)]
    public string Speaker { get; set; } = "candidate";

    public long StartMs { get; set; }
    public long EndMs { get; set; }

    public string Text { get; set; } = string.Empty;

    /// <summary>Original word-level confidence payload when supplied by ASR.</summary>
    public string WordConfidenceJson { get; set; } = "[]";

    [MaxLength(32)]
    public string AsrProvider { get; set; } = string.Empty;

    public bool IsInterrupted { get; set; }
    public bool IsOverlap { get; set; }
    public bool IsMonologue { get; set; }

    public int FillerCount { get; set; }
    public int PauseCount { get; set; }
    public int FalseStartCount { get; set; }
    public int RepetitionCount { get; set; }
    public int JargonCount { get; set; }

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Quality decision for the original recording attached to a session.
/// A missing or unverifiable original is never reported as a clean pass.
/// </summary>
public sealed class SpeakingSimulationV11AudioQualityCheck
{
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SpeakingSessionId { get; set; } = default!;

    [MaxLength(64)]
    public string? AssessmentId { get; set; }

    [MaxLength(64)]
    public string? SourceRecordingId { get; set; }

    [MaxLength(64)]
    public string? SourceMediaAssetId { get; set; }

    public SpeakingSimulationV11AudioQualityStatus Status { get; set; } = SpeakingSimulationV11AudioQualityStatus.Pending;

    [MaxLength(64)]
    public string? OriginalSha256 { get; set; }

    [MaxLength(96)]
    public string? MimeType { get; set; }

    public long? SizeBytes { get; set; }
    public int? DurationSeconds { get; set; }
    public int? SampleRateHz { get; set; }
    public int? Channels { get; set; }

    [MaxLength(64)]
    public string? Codec { get; set; }

    [MaxLength(64)]
    public string? IssueCode { get; set; }

    public string DetailsJson { get; set; } = "{}";
    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Server-authoritative timing evidence for one card. Client clocks are
/// diagnostic only and never replace these persisted timestamps.
/// </summary>
public sealed class SpeakingSimulationV11CardTimingSnapshot
{
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string? ExamSessionId { get; set; }

    [MaxLength(64)]
    public string SpeakingSessionId { get; set; } = default!;

    [MaxLength(2)]
    public string CardSlot { get; set; } = "standalone";

    public DateTimeOffset? PrepStartedAt { get; set; }
    public DateTimeOffset? ActiveStartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }

    public int PrepSeconds { get; set; }
    public int RolePlaySeconds { get; set; }

    public DateTimeOffset? PrepDeadlineAt { get; set; }
    public DateTimeOffset? RolePlayDeadlineAt { get; set; }

    public int? ServerElapsedSeconds { get; set; }
    public bool ServerAuthoritative { get; set; } = true;

    [MaxLength(64)]
    public string? SourceCardVersion { get; set; }

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
