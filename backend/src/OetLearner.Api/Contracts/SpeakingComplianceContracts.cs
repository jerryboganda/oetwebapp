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
