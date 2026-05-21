/**
 * Typed API client for the OET Speaking Drills bank (Phase 5).
 *
 * Backed by `SpeakingDrillEndpoints.cs`. The endpoints surfaced here:
 *
 *   GET    /v1/speaking/drills?kind=&professionId=&recommendedForSessionId=
 *   POST   /v1/speaking/drills/{id}/attempts                { source }
 *   POST   /v1/speaking/drills/attempts/{aid}/recordings    multipart audio
 *   POST   /v1/speaking/drills/attempts/{aid}/score
 *
 *   POST   /v1/admin/speaking/drills
 *   GET    /v1/admin/speaking/drills
 *   GET    /v1/admin/speaking/drills/{id}
 *   PATCH  /v1/admin/speaking/drills/{id}
 *   POST   /v1/admin/speaking/drills/{id}/publish
 *   POST   /v1/admin/speaking/drills/{id}/archive
 *   DELETE /v1/admin/speaking/drills/{id}
 *
 * Wraps the shared `apiClient` from `lib/api.ts` so retry, CSRF, and
 * Authorization headers stay consistent across the codebase.
 */
import { apiClient } from '@/lib/api';
import { ensureFreshAccessToken } from '@/lib/auth-client';
import { env } from '@/lib/env';

// ─────────────────────────────────────────────────────────────────────────────
// Enums
// ─────────────────────────────────────────────────────────────────────────────

export type SpeakingDrillKind =
  | 'Opening'
  | 'Empathy'
  | 'Ice'
  | 'OpenQuestion'
  | 'LayLanguage'
  | 'Signposting'
  | 'CheckingUnderstanding'
  | 'Reassurance'
  | 'Closing'
  | 'Pronunciation'
  | 'Fluency'
  | 'Grammar';

export const SPEAKING_DRILL_KINDS: SpeakingDrillKind[] = [
  'Opening',
  'Empathy',
  'Ice',
  'OpenQuestion',
  'LayLanguage',
  'Signposting',
  'CheckingUnderstanding',
  'Reassurance',
  'Closing',
  'Pronunciation',
  'Fluency',
  'Grammar',
];

export type SpeakingDrillAttemptSource =
  | 'RecommendedPostAssessment'
  | 'ManualBrowse'
  | 'LearningPathStage';

export type SpeakingDrillStatus = 'draft' | 'inreview' | 'published' | 'archived' | string;

// ─────────────────────────────────────────────────────────────────────────────
// Learner-facing DTOs
// ─────────────────────────────────────────────────────────────────────────────

export interface DrillSummary {
  drillId: string;
  drillKind: SpeakingDrillKind | string;
  title: string;
  instructionText: string;
  targetCriteria: string[];
  hasAttempted: boolean;
  bestScore: number | null;
}

export interface DrillAttemptDetail {
  attemptId: string;
  drillId: string;
  startedAt: string;
  completedAt: string | null;
  score: number | null;
  feedbackSummary: string | null;
}

export interface DrillScoringResponse {
  attemptId: string;
  score: number;
  summary: string;
  specificComments: string[];
  nextRecommendations: string[];
}

/**
 * Canonical learner-facing list shape returned by
 * `GET /v1/speaking/drills`. Backend implementation:
 * `LearnerService.ListSpeakingDrillsAsync(...)` →
 * `{ kinds, totalCount, completedCount, items }`.
 */
export interface ListDrillsResponse {
  kinds: readonly string[];
  totalCount: number;
  completedCount: number;
  items: DrillSummary[];
}

export interface ListDrillsParams {
  kind?: SpeakingDrillKind | string;
  professionId?: string;
  recommendedForSessionId?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Admin-facing DTOs
// ─────────────────────────────────────────────────────────────────────────────

export interface AdminDrillSummary {
  drillId: string;
  contentItemId: string;
  drillKind: SpeakingDrillKind | string;
  professionId: string | null;
  title: string;
  instructionText: string;
  targetCriteria: string[];
  recommendedAfterSessionScoreBelow: number | null;
  status: SpeakingDrillStatus;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
  archivedAt: string | null;
}

export type AdminDrillDetail = AdminDrillSummary;

export interface ListAdminDrillsResponse {
  drills: AdminDrillSummary[];
}

export interface AdminDrillCreateInput {
  drillKind: SpeakingDrillKind | string;
  professionId?: string | null;
  title: string;
  instructionText: string;
  targetCriteria: string[];
  recommendedAfterSessionScoreBelow?: number | null;
}

export type AdminDrillUpdateInput = Partial<AdminDrillCreateInput>;

// ─────────────────────────────────────────────────────────────────────────────
// Learner-facing API
// ─────────────────────────────────────────────────────────────────────────────

function toQuery(params: Record<string, string | undefined>): string {
  const entries = Object.entries(params).filter(([, v]) => v !== undefined && v !== '');
  if (entries.length === 0) return '';
  const search = new URLSearchParams();
  for (const [k, v] of entries) search.set(k, String(v));
  return `?${search.toString()}`;
}

export async function listSpeakingDrills(params: ListDrillsParams = {}): Promise<ListDrillsResponse> {
  return apiClient.get<ListDrillsResponse>(
    `/v1/speaking/drills${toQuery({
      kind: params.kind,
      professionId: params.professionId,
      recommendedForSessionId: params.recommendedForSessionId,
    })}`,
  );
}

export async function startDrillAttempt(
  drillId: string,
  source: SpeakingDrillAttemptSource = 'ManualBrowse',
): Promise<DrillAttemptDetail> {
  return apiClient.post<DrillAttemptDetail>(
    `/v1/speaking/drills/${encodeURIComponent(drillId)}/attempts`,
    { source },
  );
}

/**
 * Multipart upload of a short MediaRecorder blob (≤5MB) tied to a drill
 * attempt. Goes through the shared `fetch` instead of `apiClient.post`
 * because the latter assumes JSON bodies — we need FormData here.
 */
export async function uploadDrillRecording(
  attemptId: string,
  audio: Blob,
  mimeType?: string,
): Promise<{ uploaded: boolean }> {
  const form = new FormData();
  const filename = `drill-${attemptId}.${guessExtension(mimeType ?? audio.type)}`;
  form.append('audio', audio, filename);

  const headers: HeadersInit = {};
  if (typeof document !== 'undefined') {
    const csrf = document.cookie.match(/(?:^|;\s*)oet_csrf=([^;]+)/);
    if (csrf) headers['x-csrf-token'] = csrf[1];
  }
  try {
    const token = await ensureFreshAccessToken();
    if (token) headers['Authorization'] = `Bearer ${token}`;
  } catch {
    // Falls through to a 401 from the backend which the caller handles.
  }

  const baseUrl = env.apiBaseUrl;
  const url = `${baseUrl}/v1/speaking/drills/attempts/${encodeURIComponent(attemptId)}/recordings`;
  const response = await fetch(url, {
    method: 'POST',
    body: form,
    headers,
    credentials: 'include',
  });
  if (!response.ok) {
    throw new Error(`Drill recording upload failed: ${response.status}`);
  }
  return (await response.json()) as { uploaded: boolean };
}

export async function scoreDrillAttempt(attemptId: string): Promise<DrillScoringResponse> {
  return apiClient.post<DrillScoringResponse>(
    `/v1/speaking/drills/attempts/${encodeURIComponent(attemptId)}/score`,
    {},
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Admin-facing API
// ─────────────────────────────────────────────────────────────────────────────

export interface ListAdminDrillsParams {
  drillKind?: SpeakingDrillKind | string;
  professionId?: string;
  status?: SpeakingDrillStatus;
}

export async function listAdminDrills(
  params: ListAdminDrillsParams = {},
): Promise<ListAdminDrillsResponse> {
  return apiClient.get<ListAdminDrillsResponse>(
    `/v1/admin/speaking/drills${toQuery({
      drillKind: params.drillKind,
      professionId: params.professionId,
      status: params.status,
    })}`,
  );
}

export async function getAdminDrill(drillId: string): Promise<AdminDrillDetail> {
  return apiClient.get<AdminDrillDetail>(
    `/v1/admin/speaking/drills/${encodeURIComponent(drillId)}`,
  );
}

export async function createAdminDrill(input: AdminDrillCreateInput): Promise<AdminDrillDetail> {
  return apiClient.post<AdminDrillDetail>('/v1/admin/speaking/drills', input);
}

export async function updateAdminDrill(
  drillId: string,
  input: AdminDrillUpdateInput,
): Promise<AdminDrillDetail> {
  return apiClient.request<AdminDrillDetail>(
    `/v1/admin/speaking/drills/${encodeURIComponent(drillId)}`,
    {
      method: 'PATCH',
      body: JSON.stringify(input),
      headers: { 'Content-Type': 'application/json' },
    },
  );
}

export async function publishAdminDrill(drillId: string): Promise<AdminDrillDetail> {
  return apiClient.post<AdminDrillDetail>(
    `/v1/admin/speaking/drills/${encodeURIComponent(drillId)}/publish`,
    {},
  );
}

export async function archiveAdminDrill(drillId: string): Promise<AdminDrillDetail> {
  return apiClient.post<AdminDrillDetail>(
    `/v1/admin/speaking/drills/${encodeURIComponent(drillId)}/archive`,
    {},
  );
}

export async function deleteAdminDrill(drillId: string): Promise<AdminDrillDetail> {
  return apiClient.request<AdminDrillDetail>(
    `/v1/admin/speaking/drills/${encodeURIComponent(drillId)}`,
    { method: 'DELETE' },
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 11 (G.11) — AI-assisted draft
// ─────────────────────────────────────────────────────────────────────────────

/** Seed for `POST /v1/admin/speaking/drills/ai-draft`. */
export interface AdminSpeakingDrillAiDraftInput {
  drillKind: string;
  professionId?: string | null;
  topic?: string | null;
  criterionFocus?: string | null;
  difficulty?: string | null;
}

/** Response from the grounded-gateway-backed drill draft endpoint. */
export interface AdminSpeakingDrillAiDraftResponse {
  drillId: string;
  drillKind: string;
  professionId: string | null;
  title: string;
  instructionText: string;
  targetCriteria: string[];
  recommendedAfterSessionScoreBelow: number | null;
  status: string;
  createdAt: string;
  warning?: string | null;
}

/**
 * Calls the grounded gateway via the backend to persist a draft drill.
 * The server enforces grounding (rulebook + scoring), persists a
 * Draft `SpeakingDrillItem` + paired `ContentItem`, and returns a
 * flat projection plus an optional `warning` when the AI reply could
 * not be parsed and a deterministic fallback was used.
 */
export async function draftSpeakingDrill(
  input: AdminSpeakingDrillAiDraftInput,
): Promise<AdminSpeakingDrillAiDraftResponse> {
  return apiClient.post<AdminSpeakingDrillAiDraftResponse>(
    '/v1/admin/speaking/drills/ai-draft',
    input,
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// helpers
// ─────────────────────────────────────────────────────────────────────────────

function guessExtension(mime: string): string {
  const lower = (mime || '').toLowerCase();
  if (lower.includes('webm')) return 'webm';
  if (lower.includes('mp4') || lower.includes('m4a')) return 'm4a';
  if (lower.includes('mpeg') || lower.includes('mp3')) return 'mp3';
  if (lower.includes('wav')) return 'wav';
  if (lower.includes('ogg')) return 'ogg';
  return 'bin';
}
