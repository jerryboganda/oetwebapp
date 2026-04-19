using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// Per-learner Google Calendar sync credentials & state. Stored encrypted
/// (OAuth refresh token is protected via DataProtection, same as BYOK keys
/// in <c>UserAiCredential</c>). One row per learner per provider.
///
/// <para>
/// Sync model: one-way push of pending <see cref="StudyPlanItem"/> rows to a
/// dedicated calendar the learner grants access to. On plan regeneration we
/// delete + recreate events rather than tracking individual event ids, to
/// keep the sync state-machine tiny.
/// </para>
/// </summary>
public class LearnerCalendarLink
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(32)]
    public string Provider { get; set; } = "google";

    /// <summary>Target calendar ID (primary or a dedicated one).</summary>
    [MaxLength(256)]
    public string CalendarId { get; set; } = "primary";

    /// <summary>Protected refresh token (DataProtection).</summary>
    public string RefreshTokenEncrypted { get; set; } = default!;

    /// <summary>Short hint for UI (e.g. last 4 chars). Never reveals the secret.</summary>
    [MaxLength(16)]
    public string TokenHint { get; set; } = "";

    /// <summary>Expiry of the access token we cached last; may be null.</summary>
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }

    /// <summary>True = we'll push events on plan regen. False = suspended.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Last successful sync.</summary>
    public DateTimeOffset? LastSyncedAt { get; set; }

    [MaxLength(256)]
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
