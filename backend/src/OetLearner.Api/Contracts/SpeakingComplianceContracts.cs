namespace OetLearner.Api.Contracts;

// Phase 7 (B.8) of the OET Speaking module plan.
//
// Request and response shapes for the durable consent + recording-deletion
// surface introduced in `SpeakingComplianceEndpoints` and consumed by the
// `SpeakingConsentBanner` frontend component plus `lib/api/speaking-compliance.ts`.

/// <summary>POST /v1/speaking/consents body. The learner accepts a
/// specific consent type. If <c>ConsentVersion</c> is omitted the server
/// uses the current version configured in
/// <see cref="OetLearner.Api.Configuration.SpeakingComplianceOptions"/>.</summary>
public record RecordConsentRequest(
    string ConsentType,
    string? ConsentVersion = null);

/// <summary>One consent row in <c>GET /v1/speaking/consents/me</c>. Includes
/// both active and revoked rows so the UI can show a full GDPR audit
/// trail. The latest non-revoked row for each <c>ConsentType</c> is the
/// active consent.</summary>
public record ConsentRecord(
    string ConsentType,
    string ConsentVersion,
    DateTimeOffset AcceptedAt,
    DateTimeOffset? RevokedAt);

/// <summary>Response from <c>GET /v1/speaking/consents/me</c>.</summary>
public record ConsentHistoryResponse(ConsentRecord[] Consents);

/// <summary>POST /v1/admin/speaking/recordings/{id}/access body. Carries
/// the human-readable purpose recorded in the audit row alongside the
/// admin/tutor's identity.</summary>
public record AdminRecordingAccessRequest(string Purpose);

/// <summary>Response shape after a successful learner-initiated recording
/// deletion (DELETE /v1/speaking/recordings/{id}).</summary>
public record RecordingDeletionResponse(
    string RecordingId,
    bool BlobDeleted,
    DateTimeOffset ArchivedAt);

// ── Phase 10 (P10) — recording self-management + admin audit + erasure preflight ──

/// <summary>One row of <c>GET /v1/speaking/recordings/mine</c>. Joins the
/// recording row with the owning session + role-play card so the learner
/// "My recordings" UI can render profession + mode + duration + retention
/// expiry without a separate lookup.</summary>
public record MyRecordingRow(
    string RecordingId,
    string SessionId,
    DateTimeOffset CreatedAt,
    string Mode,
    string ProfessionId,
    string ScenarioTitle,
    int DurationSeconds,
    string MimeType,
    bool IsArchived,
    DateTimeOffset? RetentionExpiresAt);

/// <summary>Response shape for <c>GET /v1/speaking/recordings/mine</c>.</summary>
public record MyRecordingsResponse(MyRecordingRow[] Recordings);

/// <summary>Filter inputs for the admin audit viewer.</summary>
public record SpeakingAccessAuditFilter(
    string? RecordingId,
    string? LearnerEmailOrId,
    string? TutorEmailOrId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Limit = 100);

/// <summary>One row of <c>GET /v1/admin/speaking/recordings/audit</c>.
/// Each row is an <see cref="AuditEvent"/> with action starting with
/// <c>speaking.recording.</c> projected to a learner-friendly shape.</summary>
public record SpeakingAccessAuditRow(
    string AuditEventId,
    DateTimeOffset OccurredAt,
    string Action,
    string? RecordingId,
    string? SessionId,
    string? LearnerUserId,
    string ActorId,
    string ActorName,
    string? ActorRole,
    string? Purpose,
    string? Reason,
    string? DetailsJson);

/// <summary>Response shape for the admin audit viewer.</summary>
public record SpeakingAccessAuditResponse(SpeakingAccessAuditRow[] Events);

/// <summary>Item returned in the erasure-preflight inventory for the
/// learner's recordings.</summary>
public record ErasurePreflightRecording(
    string RecordingId,
    string SessionId,
    DateTimeOffset CreatedAt,
    int DurationSeconds,
    bool IsArchived);

/// <summary>Item returned in the erasure-preflight inventory for the
/// learner's AI / tutor assessments.</summary>
public record ErasurePreflightAssessment(
    string AssessmentId,
    string SessionId,
    string Track,
    DateTimeOffset GeneratedAt);

/// <summary>Response shape for <c>POST /v1/learner/account/erasure-preflight</c>.
/// Surfaces the inventory of consent, recording, and assessment records
/// the caller currently owns so a subsequent full-erasure flow can show
/// the user exactly what will be deleted. The preflight does NOT delete
/// anything itself.</summary>
public record ErasurePreflightResponse(
    ConsentRecord[] Consents,
    ErasurePreflightRecording[] Recordings,
    ErasurePreflightAssessment[] Assessments);
