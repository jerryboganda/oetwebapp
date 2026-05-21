using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

// Phase 1 of the OET Speaking module roadmap.
//
// `SpeakingSession` is the unified attempt object for both delivery modes
// described in the plan:
//
//   * `ai_self_practice` / `ai_exam` — AI patient via `ConversationHub`
//   * `live_tutor`                    — human tutor via LiveKit room
//
// Each session links to (1) an underlying `Attempt` so the legacy speaking
// pipeline keeps working, (2) a `RolePlayCard`, (3) zero-or-more
// `SpeakingRecording` rows, (4) zero-or-more `SpeakingTranscript`
// revisions, and (5) the dual scoring tracks (`SpeakingAiAssessment` and
// `SpeakingTutorAssessment`), introduced in Phase 2/4.
//
// Phase 1 ships the session + recording + transcript tables only; the
// assessment tables ship in Phase 2.

public enum SpeakingSessionMode
{
    AiSelfPractice = 0,
    AiExam = 1,
    LiveTutor = 2,
}

public enum SpeakingSessionState
{
    Prep = 0,
    Active = 1,
    Finished = 2,
    Cancelled = 3,
    Expired = 4,
}

public enum SpeakingRecordingKind
{
    Audio = 0,
    Video = 1,
    Mixed = 2,
}

public enum SpeakingRecordingSource
{
    ClientMediaRecorder = 0,
    LiveKitEgress = 1,
    ConversationHub = 2,
}

[Index(nameof(UserId), nameof(State))]
[Index(nameof(RolePlayCardId), nameof(State))]
[Index(nameof(MockSessionId))]
public class SpeakingSession
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string RolePlayCardId { get; set; } = default!;

    public RolePlayCard? RolePlayCard { get; set; }

    /// <summary>Optional pairing into a two-role-play mock. When set,
    /// `SpeakingMockSession` orchestrates the second role-play.</summary>
    [MaxLength(64)]
    public string? MockSetId { get; set; }

    [MaxLength(64)]
    public string? MockSessionId { get; set; }

    public SpeakingSessionMode Mode { get; set; } = SpeakingSessionMode.AiSelfPractice;

    public SpeakingSessionState State { get; set; } = SpeakingSessionState.Prep;

    /// <summary>The expert acting as the interlocutor when
    /// <see cref="Mode"/> is <c>LiveTutor</c>. Null for AI sessions.</summary>
    [MaxLength(64)]
    public string? InterlocutorActorId { get; set; }

    /// <summary>FK to `SpeakingLiveRoom`. Null until a LiveKit room is
    /// provisioned in Phase 3.</summary>
    [MaxLength(64)]
    public string? LiveRoomId { get; set; }

    /// <summary>The legacy `Attempt` row this session wraps. Allows the
    /// existing speaking pipeline (criterion scores, transcripts,
    /// evaluations) to keep functioning during the migration; once
    /// Phase 4 ships, new sessions stop writing through to `Attempt`.</summary>
    [MaxLength(64)]
    public string? AttemptId { get; set; }

    public DateTimeOffset? PrepStartedAt { get; set; }
    public DateTimeOffset? RolePlayStartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Snapshot of elapsed seconds at end-of-session. Useful
    /// for analytics queries that would otherwise need to recompute
    /// the timing window per row.</summary>
    public int ElapsedSeconds { get; set; }

    /// <summary>Version code of the consent flow the learner accepted
    /// (e.g. `recording.v1`). Stored on the session and copied onto
    /// every `SpeakingRecording` for regulatory traceability.</summary>
    [MaxLength(32)]
    public string ConsentVersion { get; set; } = "recording.v1";

    public DateTimeOffset? ConsentAcceptedAt { get; set; }

    /// <summary>Paper-destruction acknowledgement timestamp for in-person
    /// mock sessions (kept in the new schema for parity with the
    /// existing `app/speaking/task/[id]/page.tsx` CBT compliance
    /// flow).</summary>
    public DateTimeOffset? PaperDestroyedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

[Index(nameof(SpeakingSessionId))]
[Index(nameof(MediaAssetId))]
[Index(nameof(Sha256))]
public class SpeakingRecording
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SpeakingSessionId { get; set; } = default!;

    public SpeakingSession? SpeakingSession { get; set; }

    /// <summary>FK to the existing `MediaAsset` row that holds the
    /// blob storage pointer. Reuses the content-addressed
    /// `IFileStorage` flow so deduplication still works.</summary>
    [MaxLength(64)]
    public string MediaAssetId { get; set; } = default!;

    public MediaAsset? MediaAsset { get; set; }

    public SpeakingRecordingKind Kind { get; set; } = SpeakingRecordingKind.Audio;

    public SpeakingRecordingSource Source { get; set; } = SpeakingRecordingSource.ClientMediaRecorder;

    public int DurationSeconds { get; set; }

    public long SizeBytes { get; set; }

    [MaxLength(64)]
    public string Sha256 { get; set; } = default!;

    [MaxLength(96)]
    public string MimeType { get; set; } = "audio/webm";

    [MaxLength(32)]
    public string ConsentVersion { get; set; } = "recording.v1";

    /// <summary>True once the row has been swept by
    /// `SpeakingAudioRetentionWorker`. The underlying blob is removed
    /// from storage; this row is kept for audit purposes.</summary>
    public bool IsArchived { get; set; }

    /// <summary>Date after which the retention worker is allowed to
    /// delete the underlying blob. Null = follow the
    /// `SpeakingComplianceOptions` default for the session's mode.</summary>
    public DateTimeOffset? RetentionExpiresAt { get; set; }

    /// <summary>LiveKit egress track id (only meaningful when
    /// <see cref="Source"/> = <c>LiveKitEgress</c>). Useful for matching
    /// follow-up webhooks to the recording row.</summary>
    [MaxLength(64)]
    public string? EgressTrackId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

[Index(nameof(SpeakingSessionId), nameof(IsLatest))]
public class SpeakingTranscript
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SpeakingSessionId { get; set; } = default!;

    public SpeakingSession? SpeakingSession { get; set; }

    /// <summary>Which STT provider produced this transcript. Multiple
    /// transcripts may co-exist for the same session if re-transcribed
    /// against a higher-confidence engine; only one is marked
    /// <see cref="IsLatest"/>.</summary>
    [MaxLength(32)]
    public string Provider { get; set; } = "whisper";

    [MaxLength(8)]
    public string Language { get; set; } = "en";

    /// <summary>JSON array of segments with shape
    /// `{speaker, startMs, endMs, text, confidence, words[]}`. Validated
    /// at write time so analytics queries can rely on the schema.</summary>
    public string SegmentsJson { get; set; } = "[]";

    public bool IsLatest { get; set; } = true;

    public int WordCount { get; set; }

    /// <summary>Average per-segment confidence over the transcript. Used
    /// by the analytics layer to flag low-confidence sessions for human
    /// review.</summary>
    public double MeanConfidence { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }
}

public static class SpeakingSessionModes
{
    public const string AiSelfPractice = "ai_self_practice";
    public const string AiExam = "ai_exam";
    public const string LiveTutor = "live_tutor";

    public static SpeakingSessionMode Parse(string? value) => value switch
    {
        AiExam => SpeakingSessionMode.AiExam,
        LiveTutor => SpeakingSessionMode.LiveTutor,
        _ => SpeakingSessionMode.AiSelfPractice,
    };

    public static string ToCode(SpeakingSessionMode mode) => mode switch
    {
        SpeakingSessionMode.AiExam => AiExam,
        SpeakingSessionMode.LiveTutor => LiveTutor,
        _ => AiSelfPractice,
    };
}

public static class SpeakingSessionStates
{
    public const string Prep = "prep";
    public const string Active = "active";
    public const string Finished = "finished";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";

    public static string ToCode(SpeakingSessionState state) => state switch
    {
        SpeakingSessionState.Active => Active,
        SpeakingSessionState.Finished => Finished,
        SpeakingSessionState.Cancelled => Cancelled,
        SpeakingSessionState.Expired => Expired,
        _ => Prep,
    };
}
