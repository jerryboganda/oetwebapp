using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain;

public enum LiveClassType
{
    Masterclass,
    GroupClass,
    OneToOne,
    MockReview,
    OfficeHours
}

public enum LiveClassStatus
{
    Draft,
    Published,
    Archived
}

public enum LiveClassSessionStatus
{
    Scheduled,
    Live,
    Completed,
    Cancelled,
    NoShow
}

public enum LiveClassEnrollmentStatus
{
    Active,
    Cancelled,
    Refunded,
    NoShow,
    Attended
}

public enum LiveClassRecordingStatus
{
    Pending,
    Downloading,
    Processing,
    Ready,
    Failed,
    Expired
}

public enum LiveClassWebhookStatus
{
    Received,
    Processing,
    Processed,
    Failed,
    Duplicate
}

[Index(nameof(Slug), IsUnique = true)]
[Index(nameof(Status), nameof(ProfessionTrack), nameof(Level))]
public class LiveClass
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(160)]
    public string Slug { get; set; } = default!;

    [MaxLength(180)]
    public string Title { get; set; } = default!;

    [MaxLength(180)]
    public string? TitleAr { get; set; }

    [MaxLength(4096)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(4096)]
    public string? DescriptionAr { get; set; }

    public LiveClassType Type { get; set; }

    [MaxLength(64)]
    public string ProfessionTrack { get; set; } = "All";

    [MaxLength(32)]
    public string Level { get; set; } = "All";

    [MaxLength(64)]
    public string? TutorProfileId { get; set; }

    [MaxLength(128)]
    public string? TutorDisplayName { get; set; }

    public int DefaultDurationMinutes { get; set; } = 60;
    public int DefaultCapacity { get; set; } = 100;
    public int CreditCost { get; set; } = 5;
    public decimal? PriceUsd { get; set; }
    public bool IsRecurring { get; set; }

    [MaxLength(2048)]
    public string RecurrenceJson { get; set; } = "{}";

    public LiveClassStatus Status { get; set; } = LiveClassStatus.Draft;

    [MaxLength(512)]
    public string? CoverImageUrl { get; set; }

    [MaxLength(2048)]
    public string TagsJson { get; set; } = "[]";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public PrivateSpeakingTutorProfile? TutorProfile { get; set; }
    public List<LiveClassSession> Sessions { get; set; } = [];
}

[Index(nameof(LiveClassId), nameof(ScheduledStartAt))]
[Index(nameof(ScheduledStartAt))]
[Index(nameof(Status), nameof(ScheduledStartAt))]
[Index(nameof(ZoomMeetingId))]
public class LiveClassSession
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string LiveClassId { get; set; } = default!;

    public DateTimeOffset ScheduledStartAt { get; set; }
    public DateTimeOffset ScheduledEndAt { get; set; }
    public int Capacity { get; set; }
    public int EnrolledCount { get; set; }
    public LiveClassSessionStatus Status { get; set; } = LiveClassSessionStatus.Scheduled;

    public long? ZoomMeetingId { get; set; }

    [MaxLength(64)]
    public string? ZoomMeetingNumber { get; set; }

    [MaxLength(512)]
    public string? ZoomJoinUrl { get; set; }

    [MaxLength(512)]
    public string? ZoomStartUrl { get; set; }
    [MaxLength(64)]
    public string? ZoomPasscode { get; set; }
    [MaxLength(512)]
    public string? ZoomError { get; set; }
    public int ZoomRetryCount { get; set; }

    public DateTimeOffset? ActualStartAt { get; set; }
    public DateTimeOffset? ActualEndAt { get; set; }
    public int? DurationMinutes { get; set; }
    [MaxLength(64)]
    public string? RecordingId { get; set; }
    [MaxLength(512)]
    public string? CancellationReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public LiveClass LiveClass { get; set; } = default!;
    public LiveClassRecording? Recording { get; set; }
    public List<LiveClassEnrollment> Enrollments { get; set; } = [];
}

[Index(nameof(UserId), nameof(Status))]
[Index(nameof(ClassSessionId), nameof(UserId), IsUnique = true)]
[Index(nameof(IdempotencyKey), IsUnique = true)]
public class LiveClassEnrollment
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ClassSessionId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public DateTimeOffset EnrolledAt { get; set; }
    public int CreditsCharged { get; set; }
    public Guid? WalletTransactionId { get; set; }
    public Guid? RefundWalletTransactionId { get; set; }
    public LiveClassEnrollmentStatus Status { get; set; } = LiveClassEnrollmentStatus.Active;
    public DateTimeOffset? CancelledAt { get; set; }
    [MaxLength(512)]
    public string? CancellationReason { get; set; }
    [MaxLength(128)]
    public string IdempotencyKey { get; set; } = default!;

    public LiveClassSession ClassSession { get; set; } = default!;
}

[Index(nameof(ClassSessionId), nameof(UserId))]
[Index(nameof(ZoomParticipantUuid))]
public class LiveClassAttendance
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ClassSessionId { get; set; } = default!;
    [MaxLength(64)]
    public string UserId { get; set; } = default!;
    [MaxLength(64)]
    public string? EnrollmentId { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }
    public int DurationSeconds { get; set; }
    [MaxLength(128)]
    public string? ZoomParticipantUuid { get; set; }
    public bool ReceivedRecordingAccess { get; set; }

    public LiveClassSession ClassSession { get; set; } = default!;
}

[Index(nameof(ClassSessionId), IsUnique = true)]
[Index(nameof(ZoomRecordingId))]
public class LiveClassRecording
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ClassSessionId { get; set; } = default!;
    [MaxLength(128)]
    public string? ZoomRecordingId { get; set; }
    public LiveClassRecordingStatus Status { get; set; } = LiveClassRecordingStatus.Pending;
    [MaxLength(512)]
    public string? S3VideoKey { get; set; }
    [MaxLength(512)]
    public string? S3AudioKey { get; set; }
    [MaxLength(512)]
    public string? S3TranscriptKey { get; set; }
    public string? TranscriptText { get; set; }
    public string? AiSummary { get; set; }
    public string? AiSummaryAr { get; set; }
    public string ChaptersJson { get; set; } = "[]";
    public string ActionItemsJson { get; set; } = "[]";
    public int DurationSeconds { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    [MaxLength(512)]
    public string? FailureReason { get; set; }

    public LiveClassSession ClassSession { get; set; } = default!;
}

[Index(nameof(ClassSessionId), nameof(Position))]
[Index(nameof(ClassSessionId), nameof(UserId), IsUnique = true)]
public class LiveClassWaitlistEntry
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ClassSessionId { get; set; } = default!;
    [MaxLength(64)]
    public string UserId { get; set; } = default!;
    public int Position { get; set; }
    public DateTimeOffset JoinedWaitlistAt { get; set; }
    public bool NotifiedOfOpening { get; set; }
}

[Index(nameof(PayloadHash), IsUnique = true)]
[Index(nameof(EventType), nameof(ReceivedAt))]
public class LiveClassWebhookEvent
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(96)]
    public string EventType { get; set; } = default!;
    [MaxLength(128)]
    public string PayloadHash { get; set; } = default!;
    public string RawPayload { get; set; } = default!;
    public LiveClassWebhookStatus Status { get; set; } = LiveClassWebhookStatus.Received;
    [MaxLength(1024)]
    public string? ErrorMessage { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

/// <summary>
/// Wave A2 — transcript chunk embeddings for the "Ask AI about this class"
/// RAG retrieval surface. One row per ~500-token chunk of a recording's
/// transcript text. The embedding vector is stored as a JSON array of floats
/// for v1 portability (pgvector adoption deferred per orchestrator note in
/// PROGRESS.md).
///
/// <para>
/// Retrieval flow: at Q&amp;A time the orchestrator embeds the question with
/// the same model (<c>text-embedding-3-small</c>, 1536 dims), pulls the top-K
/// chunks by cosine similarity in-process, then prompts <c>class.assistant.qna.v1</c>
/// with the matched chunks as context. v2 will move similarity to pgvector.
/// </para>
/// </summary>
[Index(nameof(ClassRecordingId), nameof(ChunkIndex), IsUnique = true)]
[Index(nameof(ClassRecordingId))]
public class ClassRecordingEmbedding
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ClassRecordingId { get; set; } = default!;

    /// <summary>Zero-based index within the recording's chunk sequence.</summary>
    public int ChunkIndex { get; set; }

    /// <summary>Raw transcript text for this chunk (≤ ~500 tokens of source text).</summary>
    public string ChunkText { get; set; } = string.Empty;

    /// <summary>
    /// Embedding vector serialised as a JSON array of 1536 single-precision
    /// floats. Stored as text/jsonb for v1 portability — v2 should migrate to
    /// pgvector(1536) once that extension is provisioned in the target DB.
    /// </summary>
    public string EmbeddingJson { get; set; } = "[]";

    /// <summary>Identifier of the embedding model that produced the vector
    /// (e.g. <c>text-embedding-3-small</c>). Stored so a future re-index can
    /// detect stale vectors after a model swap.</summary>
    [MaxLength(64)]
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    /// <summary>Approximate transcript start time (seconds from recording
    /// origin) for this chunk. Used to deep-link Q&amp;A citations into the
    /// recording player at the right timestamp.</summary>
    public int StartTimeSeconds { get; set; }

    /// <summary>Approximate transcript end time (seconds).</summary>
    public int EndTimeSeconds { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public LiveClassRecording Recording { get; set; } = default!;
}