/**
 * Typed API client for the OET Speaking compliance surface (Phase 7).
 *
 * Hits the five routes registered in
 * `backend/.../Endpoints/SpeakingComplianceEndpoints.cs`:
 *
 *   POST   /v1/speaking/consents
 *   GET    /v1/speaking/consents/me
 *   POST   /v1/speaking/consents/{type}/revoke
 *   DELETE /v1/speaking/recordings/{id}
 *   POST   /v1/admin/speaking/recordings/{id}/access   (admin/tutor)
 *
 * Uses the shared `apiClient` from `lib/api.ts` so retry, CSRF, and
 * Authorization headers are consistent with the rest of the app.
 */
import { apiClient } from '@/lib/api';

export type SpeakingConsentType =
  | 'recording'
  | 'ai_processing'
  | 'tutor_review'
  | 'retention'
  | 'live_video_with_tutor';

export interface ConsentRecord {
  consentType: SpeakingConsentType | string;
  consentVersion: string;
  acceptedAt: string;
  revokedAt: string | null;
}

export interface ConsentHistoryResponse {
  consents: ConsentRecord[];
}

export interface RecordingDeletionResponse {
  recordingId: string;
  blobDeleted: boolean;
  archivedAt: string;
}

export interface RecordConsentInput {
  consentType: SpeakingConsentType;
  /**
   * Optional explicit version code. Server uses the configured current
   * version when omitted.
   */
  consentVersion?: string;
}

export interface AdminRecordingAccessInput {
  /**
   * Short human-readable reason — surfaced inside the AuditEvent row
   * and visible to admins later. Required.
   */
  purpose: string;
}

export async function recordSpeakingConsent(input: RecordConsentInput): Promise<ConsentRecord> {
  return apiClient.post<ConsentRecord>('/v1/speaking/consents', input);
}

export async function fetchMyConsents(): Promise<ConsentHistoryResponse> {
  return apiClient.get<ConsentHistoryResponse>('/v1/speaking/consents/me');
}

export async function revokeSpeakingConsent(consentType: SpeakingConsentType): Promise<{ revoked: number }> {
  return apiClient.post<{ revoked: number }>(
    `/v1/speaking/consents/${encodeURIComponent(consentType)}/revoke`,
    {},
  );
}

export async function deleteSpeakingRecording(recordingId: string): Promise<RecordingDeletionResponse> {
  return apiClient.delete<RecordingDeletionResponse>(
    `/v1/speaking/recordings/${encodeURIComponent(recordingId)}`,
  );
}

export async function adminAccessSpeakingRecording(
  recordingId: string,
  input: AdminRecordingAccessInput,
): Promise<{ recordingId: string; sessionId: string; isArchived: boolean }> {
  return apiClient.post(
    `/v1/admin/speaking/recordings/${encodeURIComponent(recordingId)}/access`,
    input,
  );
}

/** Localised labels used in consent banner + GDPR settings UI. */
export const CONSENT_TYPE_LABELS: Record<SpeakingConsentType, string> = {
  recording: 'Record my speaking session',
  ai_processing: 'Process recordings with AI for scoring',
  tutor_review: 'Allow human tutors to review recordings',
  retention: 'Retain recordings for the configured period',
  live_video_with_tutor: 'Share video with my tutor during live sessions',
};
