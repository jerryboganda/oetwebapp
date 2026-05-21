/**
 * Typed API client for the OET Speaking session lifecycle (Phase 2).
 *
 * Backed by `SpeakingSessionEndpoints.cs` (created in parallel by the
 * P2 agent — see plan section C.2). The endpoints surfaced here are:
 *
 *   POST   /v1/speaking/sessions
 *   GET    /v1/speaking/sessions/{id}
 *   POST   /v1/speaking/sessions/{id}/start-roleplay
 *   POST   /v1/speaking/sessions/{id}/end
 *   POST   /v1/speaking/sessions/{id}/consent
 *   POST   /v1/speaking/sessions/{id}/ai-assessment        (AI scoring trigger)
 *   GET    /v1/speaking/sessions/{id}/ai-assessment        (latest AI scoring)
 *   GET    /v1/speaking/sessions/{id}/transcript           (LearnerSpeakingTranscript)
 *
 * Wraps the shared `apiClient` from `lib/api.ts` so retry, CSRF, and
 * Authorization headers stay consistent across the codebase. Do NOT
 * fold these into `lib/api.ts` — that file is owned by the core team
 * and edits there would conflict with parallel work.
 */
import { apiClient } from '@/lib/api';
import type {
  RolePlayCardLearnerDetail,
  ResistanceLevelCode,
} from '@/lib/api/speaking-role-play-cards';

// ─────────────────────────────────────────────────────────────────────────────
// Enums
// ─────────────────────────────────────────────────────────────────────────────

export type SpeakingSessionMode = 'ai_self_practice' | 'ai_exam' | 'live_tutor';

export type SpeakingSessionState =
  | 'PrepInProgress'
  | 'RolePlayInProgress'
  | 'PendingAssessment'
  | 'Scored'
  | 'Reviewed'
  | 'Cancelled'
  | 'Failed';

export type SpeakingAiAssessmentStatus =
  | 'Pending'
  | 'Processing'
  | 'Completed'
  | 'Failed';

// ─────────────────────────────────────────────────────────────────────────────
// Request / response DTOs (mirror SpeakingSessionContracts.cs)
// ─────────────────────────────────────────────────────────────────────────────

export interface CreateSpeakingSessionInput {
  rolePlayCardId: string;
  mode: SpeakingSessionMode;
  /** Required when `mode === 'ai_exam'` and the session is part of a full mock. */
  mockSetId?: string | null;
  /** Required when `mode === 'live_tutor'` and the session is launched from a booking. */
  bookingId?: string | null;
  consentVersion: string;
}

export interface SpeakingSessionTimingDetail {
  sessionId: string;
  state: SpeakingSessionState | string;
  mode: SpeakingSessionMode | string;
  prepStartedAt: string;
  prepEndsAt: string;
  rolePlayEndsAt: string;
  rolePlayStartedAt: string | null;
  endedAt: string | null;
  consentVersion: string;
  card: RolePlayCardLearnerDetail;
}

export interface SpeakingSessionDetail extends SpeakingSessionTimingDetail {
  /** Available when AI scoring is complete; null otherwise. */
  aiAssessmentStatus: SpeakingAiAssessmentStatus | string | null;
  liveRoomId: string | null;
  mockSetId: string | null;
  bookingId: string | null;
}

export interface CriterionScore {
  /** Stable criterion code, e.g. `intelligibility`, `relationshipBuilding`. */
  code: string;
  /** Localised label for display. */
  label: string;
  /** Band score on the OET Speaking scale (typically 0-6 or 0-7). */
  score: number;
  /** Optional 1-2 sentence rationale from the AI. */
  rationale: string | null;
}

export interface AiAssessmentDetail {
  sessionId: string;
  status: SpeakingAiAssessmentStatus | string;
  overallBand: number | null;
  criteria: CriterionScore[];
  resistanceLevelInferred: ResistanceLevelCode | string | null;
  summary: string | null;
  failureReason: string | null;
  startedAt: string | null;
  completedAt: string | null;
  modelVersion: string | null;
  disclaimer: string;
}

export interface TranscriptSegment {
  /** Stable ordering index. */
  index: number;
  /** Either `candidate` or `interlocutor` (AI partner OR tutor). */
  speaker: 'candidate' | 'interlocutor' | string;
  startSeconds: number;
  endSeconds: number;
  text: string;
}

export interface TranscriptDetail {
  sessionId: string;
  transcriptVersion: string;
  generatedAt: string;
  segments: TranscriptSegment[];
}

export interface ConsentAckResponse {
  consentVersion: string;
  acceptedAt: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// API functions
// ─────────────────────────────────────────────────────────────────────────────

export async function createSpeakingSession(
  body: CreateSpeakingSessionInput,
): Promise<SpeakingSessionTimingDetail> {
  return apiClient.post<SpeakingSessionTimingDetail>('/v1/speaking/sessions', body);
}

export async function getSpeakingSession(sessionId: string): Promise<SpeakingSessionDetail> {
  return apiClient.get<SpeakingSessionDetail>(
    `/v1/speaking/sessions/${encodeURIComponent(sessionId)}`,
  );
}

export async function startRolePlay(sessionId: string): Promise<SpeakingSessionDetail> {
  return apiClient.post<SpeakingSessionDetail>(
    `/v1/speaking/sessions/${encodeURIComponent(sessionId)}/start-roleplay`,
    {},
  );
}

/** Phase 3 P3.4 — mark warm-up as started. */
export async function startSpeakingWarmup(sessionId: string): Promise<SpeakingSessionDetail> {
  return apiClient.post<SpeakingSessionDetail>(
    `/v1/speaking/sessions/${encodeURIComponent(sessionId)}/start-warmup`,
    {},
  );
}

/** Phase 3 P3.4 — finish warm-up and transition to prep. */
export async function finishSpeakingWarmup(sessionId: string): Promise<SpeakingSessionDetail> {
  return apiClient.post<SpeakingSessionDetail>(
    `/v1/speaking/sessions/${encodeURIComponent(sessionId)}/finish-warmup`,
    {},
  );
}

export async function endSpeakingSession(sessionId: string): Promise<SpeakingSessionDetail> {
  return apiClient.post<SpeakingSessionDetail>(
    `/v1/speaking/sessions/${encodeURIComponent(sessionId)}/end`,
    {},
  );
}

export async function recordConsent(
  sessionId: string,
  consentVersion: string,
): Promise<ConsentAckResponse> {
  return apiClient.post<ConsentAckResponse>(
    `/v1/speaking/sessions/${encodeURIComponent(sessionId)}/consent`,
    { consentVersion },
  );
}

/**
 * Kicks off AI scoring for a completed session. Idempotent — calling
 * twice returns the existing assessment (or the in-flight job).
 */
export async function runAiAssessment(sessionId: string): Promise<AiAssessmentDetail> {
  return apiClient.post<AiAssessmentDetail>(
    `/v1/speaking/sessions/${encodeURIComponent(sessionId)}/ai-assessment`,
    {},
  );
}

export async function getAiAssessment(sessionId: string): Promise<AiAssessmentDetail | null> {
  return apiClient.postWithAcceptedStatuses<AiAssessmentDetail | null>(
    `/v1/speaking/sessions/${encodeURIComponent(sessionId)}/ai-assessment`,
    {},
    [404],
    // Re-use POST so the backend can return the same body shape for both
    // "create-or-get" and "get" semantics. If the backend exposes a GET
    // variant, swap to apiClient.get without changing the call sites.
  );
}

export async function getTranscript(sessionId: string): Promise<TranscriptDetail | null> {
  return apiClient.request<TranscriptDetail | null>(
    `/v1/speaking/sessions/${encodeURIComponent(sessionId)}/transcript`,
    { method: 'GET' },
    { acceptedStatuses: [404] },
  );
}
