using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Machine-generated security telemetry: auth lifecycle, session/device
/// changes, playback session lifecycle, risk signals, and admin security
/// actions. Distinct from <see cref="AuditEvent"/> (admin-action audit trail)
/// because the actor model, volume, and retention window differ — see
/// Course Platform Security Requirements §4.4. Range-partitioned monthly on
/// <see cref="OccurredAt"/> on Npgsql (composite PK declared in
/// LearnerDbContext.OnModelCreating, gated to Npgsql like <see cref="AuditEvent"/>);
/// paired migration: 20260812090000_AddSecurityEvents.
/// </summary>
[Index(nameof(AuthAccountId), nameof(OccurredAt))]
[Index(nameof(Kind), nameof(OccurredAt))]
[Index(nameof(SessionFamilyId))]
public class SecurityEvent
{
    [Key]
    public Guid Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Null when the event predates account resolution (e.g. a
    /// failed sign-in against an unrecognised email is not logged here at
    /// all — see <see cref="OetLearner.Api.Security.ISecurityEventLogger"/>).</summary>
    [MaxLength(64)]
    public string? AuthAccountId { get; set; }

    /// <summary>One of <see cref="SecurityEventKinds"/>.All.</summary>
    [MaxLength(64)]
    public string Kind { get; set; } = default!;

    /// <summary>One of <see cref="SecurityEventKinds"/>.Severities ("info" | "warning" | "critical").</summary>
    [MaxLength(16)]
    public string Severity { get; set; } = "info";

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    /// <summary>ISO 3166-1 alpha-2 country code from CF-IPCountry, when present.</summary>
    [MaxLength(8)]
    public string? CountryCode { get; set; }

    [MaxLength(256)]
    public string? UserAgent { get; set; }

    /// <summary>web | desktop | capacitor-android | capacitor-ios, from X-OET-Client-Platform.</summary>
    [MaxLength(16)]
    public string? Platform { get; set; }

    /// <summary>Refresh-token family id (the identity that survives rotation —
    /// see <c>RefreshTokenRecord.FamilyId</c>), when the event is session-scoped.</summary>
    public Guid? SessionFamilyId { get; set; }

    [MaxLength(128)]
    public string? DeviceId { get; set; }

    /// <summary>Free-form JSON detail payload. Mapped to Postgres <c>text</c>
    /// via fluent config in LearnerDbContext (same pattern as
    /// <see cref="AuditEvent.Details"/>) — payloads can exceed a short varchar cap.
    /// SQLite test provider stores it as TEXT regardless.</summary>
    public string? DetailsJson { get; set; }
}

/// <summary>
/// Canonical <see cref="SecurityEvent.Kind"/> whitelist. Backend-only today
/// (unlike <c>MockProctoringKinds</c>, no frontend caller submits these
/// directly — they are all server-emitted from known code paths), but kept
/// as a static whitelist for the same reason: a typo in a kind string is a
/// silent data-quality bug that only surfaces months later in a dashboard.
/// </summary>
public static class SecurityEventKinds
{
    public const string AuthSignInSucceeded = "auth.sign_in_succeeded";
    public const string AuthSignInFailed = "auth.sign_in_failed";
    public const string AuthSignOut = "auth.sign_out";
    public const string AuthMfaFailed = "auth.mfa_failed";
    public const string AuthRefreshReuseDetected = "auth.refresh_reuse_detected";
    public const string AuthRefreshDeviceMismatch = "auth.refresh_device_mismatch";
    public const string AuthTokenRejected = "auth.token_rejected";

    public const string SessionCreated = "session.created";
    public const string SessionRevoked = "session.revoked";
    public const string SessionRevokedAll = "session.revoked_all";

    public const string DeviceTrustRequested = "device.trust_requested";
    public const string DeviceTrusted = "device.trusted";
    public const string DeviceRevoked = "device.revoked";
    public const string DeviceChangeBlockedCooldown = "device.change_blocked_cooldown";
    public const string DeviceAdminReset = "device.admin_reset";

    public const string PlaybackSessionStarted = "playback.session_started";
    public const string PlaybackSessionsRevoked = "playback.sessions_revoked";

    public const string RiskCountryChanged = "risk.country_changed";
    public const string RiskImpossibleTravel = "risk.impossible_travel";
    public const string RiskStepUpRequired = "risk.step_up_required";
    public const string RiskSignInBlocked = "risk.sign_in_blocked";

    public const string AdminSessionRevoked = "admin.session_revoked";
    public const string AdminDeviceReset = "admin.device_reset";
    public const string AdminAccountSuspended = "admin.account_suspended";
    public const string AdminAccountReactivated = "admin.account_reactivated";
    public const string AdminPlaybackBlocked = "admin.playback_blocked";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        AuthSignInSucceeded, AuthSignInFailed, AuthSignOut, AuthMfaFailed,
        AuthRefreshReuseDetected, AuthRefreshDeviceMismatch, AuthTokenRejected,
        SessionCreated, SessionRevoked, SessionRevokedAll,
        DeviceTrustRequested, DeviceTrusted, DeviceRevoked, DeviceChangeBlockedCooldown, DeviceAdminReset,
        PlaybackSessionStarted, PlaybackSessionsRevoked,
        RiskCountryChanged, RiskImpossibleTravel, RiskStepUpRequired, RiskSignInBlocked,
        AdminSessionRevoked, AdminDeviceReset, AdminAccountSuspended, AdminAccountReactivated, AdminPlaybackBlocked,
    };

    public static readonly IReadOnlySet<string> Severities = new HashSet<string>(StringComparer.Ordinal)
    {
        "info", "warning", "critical",
    };

    /// <summary>Default severity per kind when the caller does not override one.</summary>
    public static string DefaultSeverity(string kind) => kind switch
    {
        AuthSignInFailed or AuthMfaFailed or DeviceChangeBlockedCooldown or RiskCountryChanged
            or RiskStepUpRequired => "warning",
        AuthRefreshReuseDetected or AuthRefreshDeviceMismatch or RiskImpossibleTravel or RiskSignInBlocked
            or AdminAccountSuspended or AdminPlaybackBlocked => "critical",
        _ => "info",
    };
}

/// <summary>
/// One trusted device per account (Course Platform Security Requirements
/// §3.2). Outlives sessions — a session (RefreshTokenRecord) rotates every
/// ~15 minutes, but the device it belongs to persists until the account
/// holder (or an admin) trusts a different one. Only one row per account
/// should have <see cref="RevokedAt"/> null at a time; enforced in
/// application code (<c>TrustedDeviceService</c>) rather than a DB
/// constraint, for portability with the SQLite test provider.
/// </summary>
[Index(nameof(ApplicationUserAccountId))]
public class TrustedDevice
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string ApplicationUserAccountId { get; set; } = default!;

    /// <summary>Client-generated opaque id (lib/device-id.ts), sent as the
    /// X-OET-Device-Id header on every auth request.</summary>
    [MaxLength(128)]
    public string DeviceId { get; set; } = default!;

    /// <summary>Human-readable label derived from the User-Agent at trust time
    /// (e.g. "Chrome on Windows"), for the account holder's sessions page.</summary>
    [MaxLength(256)]
    public string? DeviceName { get; set; }

    [MaxLength(32)]
    public string? Platform { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset TrustedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>"bootstrap" (first device, auto-trusted) | "otp_verified"
    /// (explicit email-OTP approval of a new device) | "admin_reset".</summary>
    [MaxLength(32)]
    public string TrustGrantedVia { get; set; } = "bootstrap";
}
