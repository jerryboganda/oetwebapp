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


// ─────────────────────────────────────────────────────────────────────────────
// Phase 10 P10.1 — learner self-management
// ─────────────────────────────────────────────────────────────────────────────

export interface MyRecordingRow {
  recordingId: string;
  sessionId: string;
  createdAt: string;
  mode: string;
  professionId: string;
  scenarioTitle: string;
  durationSeconds: number;
  mimeType: string | null;
  isArchived: boolean;
  retentionExpiresAt: string | null;
}

export interface MyRecordingsResponse {
  recordings: MyRecordingRow[];
}

export async function fetchMySpeakingRecordings(): Promise<MyRecordingsResponse> {
  return apiClient.get<MyRecordingsResponse>('/v1/speaking/recordings/mine');
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 10 P10.2 — admin recording-access audit
// ─────────────────────────────────────────────────────────────────────────────

export interface SpeakingAccessAuditFilter {
  recordingId?: string;
  learnerEmailOrId?: string;
  tutorEmailOrId?: string;
  from?: string;
  to?: string;
  limit?: number;
}

export interface SpeakingAccessAuditRow {
  auditEventId: string;
  occurredAt: string;
  action: string;
  recordingId: string | null;
  sessionId: string | null;
  learnerUserId: string | null;
  actorId: string;
  actorName: string;
  actorRole: string | null;
  purpose: string | null;
  reason: string | null;
  detailsJson: string | null;
}

export interface SpeakingAccessAuditResponse {
  rows: SpeakingAccessAuditRow[];
}

export async function fetchSpeakingAccessAudit(
  filter: SpeakingAccessAuditFilter = {},
): Promise<SpeakingAccessAuditResponse> {
  const qs = new URLSearchParams();
  if (filter.recordingId) qs.set('recordingId', filter.recordingId);
  if (filter.learnerEmailOrId) qs.set('learnerEmailOrId', filter.learnerEmailOrId);
  if (filter.tutorEmailOrId) qs.set('tutorEmailOrId', filter.tutorEmailOrId);
  if (filter.from) qs.set('from', filter.from);
  if (filter.to) qs.set('to', filter.to);
  if (filter.limit) qs.set('limit', String(filter.limit));
  const query = qs.toString();
  return apiClient.get<SpeakingAccessAuditResponse>(
    `/v1/admin/speaking/recordings/audit${query ? `?${query}` : ''}`,
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 10 P10.3 — erasure preflight
// ─────────────────────────────────────────────────────────────────────────────

export interface ErasurePreflightRecording {
  recordingId: string;
  sessionId: string;
  createdAt: string;
  durationSeconds: number;
  isArchived: boolean;
}

export interface ErasurePreflightAssessment {
  assessmentId: string;
  sessionId: string;
  kind: string;
  generatedAt: string;
}

export interface ErasurePreflightResponse {
  consents: ConsentRecord[];
  recordings: ErasurePreflightRecording[];
  assessments: ErasurePreflightAssessment[];
}

export async function fetchSpeakingErasurePreflight(): Promise<ErasurePreflightResponse> {
  return apiClient.get<ErasurePreflightResponse>('/v1/speaking/recordings/erasure-preflight');
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 8 — pathway + Phase 9 — analytics (typed as `unknown` until contracts ship)
// ─────────────────────────────────────────────────────────────────────────────

export interface SpeakingPathwayStage {
  code: string;
  orderIndex: number;
  title: string;
  description: string;
  activityKind: string;
  state: 'completed' | 'in_progress' | 'locked';
  actionHref?: string;
  actionLabel?: string;
}

export interface SpeakingPathwayResponse {
  pathwayCode: string;
  title: string;
  totalStages: number;
  completedStageCount: number;
  progressPercent: number;
  stages: SpeakingPathwayStage[];
  nextStage: SpeakingPathwayStage | null;
}

export async function fetchSpeakingPathway(): Promise<SpeakingPathwayResponse> {
  return apiClient.get<SpeakingPathwayResponse>('/v1/speaking/course-pathway');
}

export interface SpeakingLearnerAnalytics {
  estimatedBand: string;
  currentScaled: number;
  sessionCount: number;
  criterionTrends: Array<{ date: string; criterion: string; score: number }>;
  avgRolePlayLengthSeconds: number;
  speakingSpeedWpm: number;
  recurringIssues: string[];
  readinessStatus: string;
  weakestCriterion: string;
  strongestCriterion: string;
}

export async function fetchSpeakingAnalyticsMe(): Promise<SpeakingLearnerAnalytics> {
  return apiClient.get<SpeakingLearnerAnalytics>('/v1/speaking/analytics/me');
}

export async function fetchSpeakingAnalyticsClass(
  cohortId?: string,
  professionId?: string,
): Promise<unknown> {
  const qs = new URLSearchParams();
  if (cohortId) qs.set('cohortId', cohortId);
  if (professionId) qs.set('professionId', professionId);
  const query = qs.toString();
  return apiClient.get<unknown>(
    `/v1/expert/speaking/analytics/class${query ? `?${query}` : ''}`,
  );
}

export async function fetchSpeakingTutorConsistency(tutorId?: string): Promise<unknown> {
  const qs = tutorId ? `?tutorId=${encodeURIComponent(tutorId)}` : '';
  return apiClient.get<unknown>(`/v1/expert/speaking/analytics/tutor-consistency${qs}`);
}

export async function fetchSpeakingContentDifficulty(professionId?: string): Promise<unknown> {
  const qs = professionId ? `?professionId=${encodeURIComponent(professionId)}` : '';
  return apiClient.get<unknown>(`/v1/admin/speaking/analytics/content-difficulty${qs}`);
}
