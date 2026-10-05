using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

// Live AI Speaking admission control (owner decision 5 Oct 2026: at most N live AI patient
// sessions at once, a FIFO wait queue beyond that).
//
// One row per SUBJECT (an AI exam or a standalone AI practice card). The row id is
// "{SubjectKind}:{SubjectId}", so a subject can never hold two places in the line, and a retry after an
// expiry reuses the same row. The row is written ONLY under the admission service's Postgres advisory
// lock, so the line and the head-count are decided atomically across the blue and green API slots and
// the ai-worker. A waiting row holds no credit and starts no clock: the credit hold and the exam timer
// are taken by the very request that is admitted (see SpeakingLiveAdmissionService).

public enum SpeakingLiveAdmissionState
{
    /// <summary>In the line. Counts towards position, never towards capacity.</summary>
    Waiting = 0,

    /// <summary>Holds one of the capacity slots while its subject is running.</summary>
    Admitted = 1,

    /// <summary>The subject ended or the kill switch let it through; the slot is free.</summary>
    Released = 2,

    /// <summary>An abandoned waiter (no heartbeat) or an admission past its safety TTL.</summary>
    Expired = 3,
}

public static class SpeakingLiveAdmissionKinds
{
    public const string Exam = "exam";
    public const string Practice = "practice";

    public static bool IsKnown(string? kind)
        => string.Equals(kind, Exam, StringComparison.Ordinal)
           || string.Equals(kind, Practice, StringComparison.Ordinal);
}

[Index(nameof(Seq), IsUnique = true)]
[Index(nameof(State), nameof(Seq))]
[Index(nameof(UserId))]
public class SpeakingLiveAdmission
{
    /// <summary><c>{SubjectKind}:{SubjectId}</c> (see <c>SpeakingLiveAdmissionService.RowId</c>).</summary>
    [Key]
    [MaxLength(96)]
    public string Id { get; set; } = default!;

    /// <summary><see cref="SpeakingLiveAdmissionKinds"/>: <c>exam</c> or <c>practice</c>.</summary>
    [MaxLength(16)]
    public string SubjectKind { get; set; } = default!;

    /// <summary>The <c>SpeakingExamSession</c> id (exam) or <c>SpeakingSession</c> id (practice).</summary>
    [MaxLength(64)]
    public string SubjectId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public SpeakingLiveAdmissionState State { get; set; } = SpeakingLiveAdmissionState.Waiting;

    /// <summary>The FIFO ticket: strictly increasing, assigned under the admission lock. A waiter's
    /// position is the number of live waiters with a smaller ticket.</summary>
    public long Seq { get; set; }

    public DateTimeOffset EnqueuedAt { get; set; }

    /// <summary>The waiter's last poll. A waiter silent for the heartbeat window has abandoned the line.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset? AdmittedAt { get; set; }

    /// <summary>Safety TTL of an admission: past it the slot stops counting even if the subject never
    /// reached a terminal state. It never evicts a running session.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Singleton row (id = <c>global</c>): the owner-tunable cap and kill switch of the live Speaking
/// admission gate. No row means "enabled, cap from configuration" (default 100). Edited through
/// <c>PUT /v1/admin/ai/live-voice/admission</c>, which audits every change.
/// </summary>
public class SpeakingLiveAdmissionSettings
{
    [Key]
    [MaxLength(32)]
    public string Id { get; set; } = "global";

    /// <summary>False = the kill switch: nobody waits and nobody is counted (today's behaviour).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Concurrent live AI Speaking sessions allowed (1..10000).</summary>
    public int MaxConcurrent { get; set; } = 100;

    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string? UpdatedById { get; set; }
}
