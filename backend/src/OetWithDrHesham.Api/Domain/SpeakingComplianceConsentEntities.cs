using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain;

// Phase 7 of the OET Speaking module roadmap (compliance + integration).
//
// `SpeakingComplianceConsent` is a durable, versioned record of every
// consent a learner gave when interacting with the Speaking module. The
// existing per-attempt boolean flags (recording_acknowledged, etc.) are
// kept for backwards compatibility, but the GDPR audit trail and the
// `SpeakingConsentBanner` UI both source from this row.
//
// Each consent type can be accepted, revoked, then accepted again under a
// new ConsentVersion as the legal copy evolves. The latest non-revoked
// row for (UserId, ConsentType) is the current state.

public static class SpeakingComplianceConsentTypes
{
    /// <summary>General audio-recording consent (covers AI and storage).</summary>
    public const string Recording = "recording";

    /// <summary>Sends recordings + transcripts to LLM providers.</summary>
    public const string AiProcessing = "ai_processing";

    /// <summary>Allows human tutors to listen / read transcripts.</summary>
    public const string TutorReview = "tutor_review";

    /// <summary>Acknowledges the retention policy (auto-delete after N days).</summary>
    public const string Retention = "retention";

    /// <summary>Video-with-tutor consent — tutor sees the learner's face.</summary>
    public const string LiveVideoWithTutor = "live_video_with_tutor";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Recording,
        AiProcessing,
        TutorReview,
        Retention,
        LiveVideoWithTutor,
    };
}

[Index(nameof(UserId), nameof(ConsentType))]
[Index(nameof(ConsentType), nameof(ConsentVersion))]
public class SpeakingComplianceConsent
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>One of <see cref="SpeakingComplianceConsentTypes"/>.</summary>
    [MaxLength(32)]
    public string ConsentType { get; set; } = default!;

    /// <summary>Version code (e.g. <c>recording.v1</c>) of the consent
    /// copy the learner saw. Bumped when wording materially changes.</summary>
    [MaxLength(32)]
    public string ConsentVersion { get; set; } = default!;

    public DateTimeOffset AcceptedAt { get; set; }

    /// <summary>Client-reported IP (captured server-side from
    /// X-Forwarded-For or RemoteIpAddress). Stored for GDPR audit.</summary>
    [MaxLength(64)]
    public string? AcceptedFromIp { get; set; }

    [MaxLength(512)]
    public string? UserAgent { get; set; }

    /// <summary>When the learner revoked this consent. Null while
    /// the consent is still active. A revoked row is immutable —
    /// re-consenting creates a fresh row with a new AcceptedAt.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}
