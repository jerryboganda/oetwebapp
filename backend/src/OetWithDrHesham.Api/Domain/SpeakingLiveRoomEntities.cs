using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain;

// Phase 3 of the OET Speaking module roadmap.
//
// `SpeakingLiveRoom` is the LiveKit (or pluggable provider) realtime room
// that hosts a `SpeakingSession` when its `Mode` is `LiveTutor`. The room
// is provisioned just-in-time (on tutor "Start session"), recorded via
// egress, and torn down when the session ends.
//
// `SpeakingLiveRoomToken` is the short-lived per-participant JWT issued by
// the backend. Tokens are revoked when participants leave or when the
// room state transitions to `Ended` / `Failed`.

public enum SpeakingLiveRoomState
{
    Scheduled = 0,
    Provisioning = 1,
    Active = 2,
    Ended = 3,
    Failed = 4,
}

public enum SpeakingLiveRoomTokenRole
{
    Learner = 0,
    Tutor = 1,
    Observer = 2,
}

[Index(nameof(SpeakingSessionId), IsUnique = true)]
[Index(nameof(RoomName), IsUnique = true)]
public class SpeakingLiveRoom
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SpeakingSessionId { get; set; } = default!;

    public SpeakingSession? SpeakingSession { get; set; }

    [MaxLength(32)]
    public string Provider { get; set; } = "livekit";

    /// <summary>Globally-unique LiveKit room name. Used as both the
    /// connection key and the natural identifier inside webhooks.</summary>
    [MaxLength(128)]
    public string RoomName { get; set; } = default!;

    [MaxLength(96)]
    public string LearnerIdentity { get; set; } = default!;

    [MaxLength(96)]
    public string TutorIdentity { get; set; } = default!;

    /// <summary>LiveKit's internal SID (set after room is created with the
    /// provider). Null while still `Scheduled`.</summary>
    [MaxLength(96)]
    public string? LiveKitRoomSid { get; set; }

    public DateTimeOffset ScheduledStartUtc { get; set; }

    public DateTimeOffset? ActualStartUtc { get; set; }

    public DateTimeOffset? ActualEndUtc { get; set; }

    public SpeakingLiveRoomState State { get; set; } = SpeakingLiveRoomState.Scheduled;

    /// <summary>LiveKit egress id when recording is active. Used to
    /// reconcile follow-up egress webhooks back to this room.</summary>
    [MaxLength(96)]
    public string? EgressId { get; set; }

    [MaxLength(500)]
    public string? EgressOutputUrl { get; set; }

    /// <summary>Hard cap on how long the room may stay `Active`. The
    /// backend auto-ends rooms that overrun this.</summary>
    public int MaxDurationSeconds { get; set; } = 1800;

    public bool RecordingEnabled { get; set; } = true;

    [MaxLength(32)]
    public string RecordingConsentVersion { get; set; } = default!;

    /// <summary>JSON array of raw webhook events received from the
    /// provider, append-only for debugging.</summary>
    public string WebhookEventsJson { get; set; } = "[]";

    /// <summary>Optional FK to `PrivateSpeakingBooking` when this room is
    /// being used to fulfil a paid live-tutor booking.</summary>
    [MaxLength(64)]
    public string? BookingId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

[Index(nameof(LiveRoomId))]
public class SpeakingLiveRoomToken
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string LiveRoomId { get; set; } = default!;

    public SpeakingLiveRoom? LiveRoom { get; set; }

    [MaxLength(96)]
    public string Identity { get; set; } = default!;

    public DateTimeOffset IssuedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public SpeakingLiveRoomTokenRole Role { get; set; } = SpeakingLiveRoomTokenRole.Learner;

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Comma-separated provider capability list (e.g.
    /// `publish,subscribe,update_metadata`). Stored for audit and for
    /// re-issue parity if the same identity re-joins.</summary>
    [MaxLength(256)]
    public string Capabilities { get; set; } = string.Empty;
}
