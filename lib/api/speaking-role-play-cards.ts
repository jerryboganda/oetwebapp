/**
 * Typed API client for the OET Speaking role-play card surface.
 *
 * Mirrors `lib/api.ts`'s `apiRequest` pattern (Bearer-token auth via
 * `ensureFreshAccessToken`, CSRF token via the `oet_csrf` cookie, retry
 * on 5xx/408/429) without depending on the main file. This module is
 * intentionally self-contained so the Phase 1 admin builder can ship
 * independently of any refactors to `lib/api.ts`.
 *
 * Backend endpoints targeted (see plan section B.1):
 *   POST    /v1/admin/speaking/role-play-cards
 *   GET     /v1/admin/speaking/role-play-cards
 *   GET     /v1/admin/speaking/role-play-cards/{id}
 *   PATCH   /v1/admin/speaking/role-play-cards/{id}
 *   POST    /v1/admin/speaking/role-play-cards/{id}/publish
 *   POST    /v1/admin/speaking/role-play-cards/{id}/archive
 *   POST    /v1/admin/speaking/role-play-cards/{id}/duplicate
 *   GET     /v1/admin/speaking/role-play-cards/{id}/interlocutor-script
 *   PUT     /v1/admin/speaking/role-play-cards/{id}/interlocutor-script
 *   GET     /v1/speaking/role-play-cards/{id}        (learner — no interlocutor)
 */

import { ensureFreshAccessToken } from '@/lib/auth-client';
import { env } from '@/lib/env';
import { fetchWithTimeout } from '@/lib/network/fetch-with-timeout';

const API_BASE_URL = env.apiBaseUrl;

function resolveApiUrl(pathOrUrl: string): string {
  if (/^https?:\/\//i.test(pathOrUrl)) return pathOrUrl;
  return `${API_BASE_URL}${pathOrUrl.startsWith('/') ? pathOrUrl : `/${pathOrUrl}`}`;
}

async function buildHeaders(init?: RequestInit): Promise<HeadersInit> {
  const headers = new Headers(init?.headers);
  if (!headers.has('Content-Type') && init?.body && typeof init.body === 'string') {
    headers.set('Content-Type', 'application/json');
  }

  if (typeof document !== 'undefined') {
    const csrf = document.cookie.match(/(?:^|;\s*)oet_csrf=([^;]+)/);
    if (csrf) headers.set('x-csrf-token', csrf[1]);
  }

  try {
    const token = await ensureFreshAccessToken();
    if (token) headers.set('Authorization', `Bearer ${token}`);
  } catch {
    // Silent failure — the request will 401 and the caller will redirect.
  }

  return headers;
}

const RETRYABLE_STATUS = (status: number) => status >= 500 || status === 408 || status === 429;
const MAX_RETRIES = 2;
const RETRY_DELAYS_MS = [1000, 3000];

export class RolePlayCardApiError extends Error {
  status: number;
  code: string;
  retryable: boolean;
  fieldErrors: Array<{ field: string; code: string; message: string }>;

  constructor(
    status: number,
    code: string,
    message: string,
    retryable: boolean,
    fieldErrors: Array<{ field: string; code: string; message: string }> = [],
  ) {
    super(message);
    this.name = 'RolePlayCardApiError';
    this.status = status;
    this.code = code;
    this.retryable = retryable;
    this.fieldErrors = fieldErrors;
  }
}

type RequestOptions = {
  acceptedStatuses?: number[];
  timeoutMs?: number;
};

async function request<T>(path: string, init?: RequestInit, opts?: RequestOptions): Promise<T> {
  let lastError: Error | null = null;

  for (let attempt = 0; attempt <= MAX_RETRIES; attempt++) {
    try {
      const response = await fetchWithTimeout(resolveApiUrl(path), {
        ...init,
        headers: await buildHeaders(init),
      }, opts?.timeoutMs);

      const accepted = opts?.acceptedStatuses ?? [];
      if (!response.ok && accepted.includes(response.status)) {
        if (response.status === 204) return undefined as T;
        return (await response.json()) as T;
      }

      if (!response.ok) {
        let code = 'unknown_error';
        let message = `Request failed: ${response.status}`;
        let retryable = false;
        let fieldErrors: Array<{ field: string; code: string; message: string }> = [];
        try {
          const err = await response.json();
          code = err.code ?? (response.status === 401 ? 'not_authenticated' : response.status === 403 ? 'forbidden' : code);
          message = err.message ?? err.title ?? message;
          retryable = err.retryable ?? RETRYABLE_STATUS(response.status);
          fieldErrors = Array.isArray(err.fieldErrors) ? err.fieldErrors : [];
        } catch {
          retryable = RETRYABLE_STATUS(response.status);
          if (response.status === 401) code = 'not_authenticated';
          else if (response.status === 403) code = 'forbidden';
        }

        const apiError = new RolePlayCardApiError(response.status, code, message, retryable, fieldErrors);
        if (retryable && attempt < MAX_RETRIES) {
          lastError = apiError;
          await new Promise(resolve => setTimeout(resolve, RETRY_DELAYS_MS[attempt]));
          continue;
        }
        throw apiError;
      }

      if (response.status === 204) return undefined as T;
      return (await response.json()) as T;
    } catch (err) {
      if (err instanceof RolePlayCardApiError) throw err;
      if (attempt < MAX_RETRIES) {
        lastError = err as Error;
        await new Promise(resolve => setTimeout(resolve, RETRY_DELAYS_MS[attempt]));
        continue;
      }
      throw err;
    }
  }

  throw lastError ?? new Error('Request failed after retries');
}

// ─────────────────────────────────────────────────────────────────────────────
// Types — mirrors backend DTOs returned from AdminSpeakingContentEndpoints
// ─────────────────────────────────────────────────────────────────────────────

export type RolePlayCardStatus = 'Draft' | 'InReview' | 'Published' | 'Archived';
export type ResistanceLevelCode = 'low' | 'medium' | 'high';
export type RolePlayCardDifficulty = 'core' | 'extension' | 'exam';

export interface RolePlayCardSummary {
  cardId: string;
  contentItemId: string;
  title: string;
  professionId: string;
  setting: string;
  clinicalTopic: string;
  difficulty: RolePlayCardDifficulty | string;
  status: RolePlayCardStatus | string;
  isLiveTutorEligible: boolean;
  hasInterlocutorScript: boolean;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
}

export interface RolePlayCardDetail {
  cardId: string;
  contentItemId: string;
  professionId: string;
  scenarioTitle: string;
  setting: string;
  candidateRole: string;
  interlocutorRole: string;
  patientName: string | null;
  patientAge: string | null;
  background: string;
  /** Up to 5 task bullets. Filtered to non-empty entries. */
  tasks: string[];
  allowedNotes: boolean;
  prepTimeSeconds: number;
  rolePlayTimeSeconds: number;
  patientEmotion: string;
  communicationGoal: string;
  clinicalTopic: string;
  difficulty: RolePlayCardDifficulty | string;
  criteriaFocus: string[];
  disclaimer: string;
  isLiveTutorEligible: boolean;
  status: RolePlayCardStatus | string;
  hasInterlocutorScript: boolean;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
  archivedAt: string | null;
}

export interface RolePlayCardLearnerDetail {
  cardId: string;
  professionId: string;
  scenarioTitle: string;
  setting: string;
  candidateRole: string;
  interlocutorRole: string;
  patientName: string | null;
  patientAge: string | null;
  background: string;
  tasks: string[];
  allowedNotes: boolean;
  prepTimeSeconds: number;
  rolePlayTimeSeconds: number;
  patientEmotion: string;
  communicationGoal: string;
  clinicalTopic: string;
  difficulty: RolePlayCardDifficulty | string;
  criteriaFocus: string[];
  disclaimer: string;
}

export interface InterlocutorScriptDetail {
  cardId: string;
  openingResponse: string;
  prompt1: string | null;
  prompt2: string | null;
  prompt3: string | null;
  hiddenInformation: string;
  resistanceLevel: ResistanceLevelCode;
  closingCue: string;
  emotionalState: string;
  professionRoleNotes: string | null;
  layLanguageTriggers: string[];
  updatedAt: string;
}

export interface CreateRolePlayCardInput {
  professionId: string;
  scenarioTitle: string;
  setting: string;
  candidateRole: string;
  interlocutorRole?: string;
  patientName?: string | null;
  patientAge?: string | null;
  background: string;
  task1?: string | null;
  task2?: string | null;
  task3?: string | null;
  task4?: string | null;
  task5?: string | null;
  allowedNotes?: boolean;
  prepTimeSeconds?: number;
  rolePlayTimeSeconds?: number;
  patientEmotion: string;
  communicationGoal: string;
  clinicalTopic: string;
  difficulty?: RolePlayCardDifficulty | string;
  criteriaFocus: string[];
  disclaimer?: string;
  isLiveTutorEligible?: boolean;
}

export type PatchRolePlayCardInput = Partial<CreateRolePlayCardInput>;

export interface UpsertInterlocutorScriptInput {
  openingResponse: string;
  prompt1?: string | null;
  prompt2?: string | null;
  prompt3?: string | null;
  hiddenInformation?: string;
  resistanceLevel?: ResistanceLevelCode;
  closingCue?: string;
  emotionalState?: string;
  professionRoleNotes?: string | null;
  layLanguageTriggers?: string[];
}

export interface ListRolePlayCardsFilters {
  professionId?: string;
  difficulty?: string;
  status?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Admin functions
// ─────────────────────────────────────────────────────────────────────────────

export async function adminListRolePlayCards(filters: ListRolePlayCardsFilters = {}): Promise<RolePlayCardSummary[]> {
  const qs = new URLSearchParams();
  if (filters.professionId) qs.set('professionId', filters.professionId);
  if (filters.difficulty) qs.set('difficulty', filters.difficulty);
  if (filters.status) qs.set('status', filters.status);
  const query = qs.toString();
  return request<RolePlayCardSummary[]>(`/v1/admin/speaking/role-play-cards${query ? `?${query}` : ''}`);
}

export async function adminCreateRolePlayCard(input: CreateRolePlayCardInput): Promise<RolePlayCardDetail> {
  return request<RolePlayCardDetail>(`/v1/admin/speaking/role-play-cards`, {
    method: 'POST',
    body: JSON.stringify(input),
  });
}

export async function adminGetRolePlayCard(cardId: string): Promise<RolePlayCardDetail> {
  return request<RolePlayCardDetail>(`/v1/admin/speaking/role-play-cards/${encodeURIComponent(cardId)}`);
}

export async function adminPatchRolePlayCard(cardId: string, input: PatchRolePlayCardInput): Promise<RolePlayCardDetail> {
  return request<RolePlayCardDetail>(`/v1/admin/speaking/role-play-cards/${encodeURIComponent(cardId)}`, {
    method: 'PATCH',
    body: JSON.stringify(input),
  });
}

export async function adminPublishRolePlayCard(cardId: string): Promise<RolePlayCardDetail> {
  return request<RolePlayCardDetail>(`/v1/admin/speaking/role-play-cards/${encodeURIComponent(cardId)}/publish`, {
    method: 'POST',
    body: '{}',
  });
}

export async function adminArchiveRolePlayCard(cardId: string): Promise<RolePlayCardDetail> {
  return request<RolePlayCardDetail>(`/v1/admin/speaking/role-play-cards/${encodeURIComponent(cardId)}/archive`, {
    method: 'POST',
    body: '{}',
  });
}

export async function adminDuplicateRolePlayCard(cardId: string): Promise<RolePlayCardDetail> {
  return request<RolePlayCardDetail>(`/v1/admin/speaking/role-play-cards/${encodeURIComponent(cardId)}/duplicate`, {
    method: 'POST',
    body: '{}',
  });
}

export async function adminGetInterlocutorScript(cardId: string): Promise<InterlocutorScriptDetail | null> {
  return request<InterlocutorScriptDetail | null>(
    `/v1/admin/speaking/role-play-cards/${encodeURIComponent(cardId)}/interlocutor-script`,
    undefined,
    { acceptedStatuses: [404] },
  );
}

export async function adminUpsertInterlocutorScript(
  cardId: string,
  input: UpsertInterlocutorScriptInput,
): Promise<InterlocutorScriptDetail> {
  return request<InterlocutorScriptDetail>(
    `/v1/admin/speaking/role-play-cards/${encodeURIComponent(cardId)}/interlocutor-script`,
    { method: 'PUT', body: JSON.stringify(input) },
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 11 (G.11) — AI-assisted draft
// ─────────────────────────────────────────────────────────────────────────────

/** Seed for `POST /v1/admin/speaking/role-play-cards/ai-draft`. */
export interface AdminRolePlayCardAiDraftInput {
  professionId: string;
  topic?: string | null;
  emotion?: string | null;
  difficulty?: string | null;
  setting?: string | null;
  candidateRole?: string | null;
  interlocutorRole?: string | null;
  communicationGoal?: string | null;
}

/** Response from the grounded-gateway-backed role-play card draft endpoint. */
export interface AdminRolePlayCardAiDraftResponse {
  cardId: string;
  card: RolePlayCardDetail;
  warning?: string | null;
}

/**
 * Calls the grounded gateway via the backend to persist a Draft
 * candidate role-play card + paired hidden interlocutor script.
 * The server enforces grounding (rulebook + scoring) and returns the
 * persisted card + script as `RolePlayCardDetail` for an inline
 * preview, plus an optional `warning` when the AI reply could not be
 * parsed and a deterministic fallback was used.
 */
export async function draftSpeakingRolePlayCard(
  input: AdminRolePlayCardAiDraftInput,
): Promise<AdminRolePlayCardAiDraftResponse> {
  return request<AdminRolePlayCardAiDraftResponse>(
    '/v1/admin/speaking/role-play-cards/ai-draft',
    { method: 'POST', body: JSON.stringify(input) },
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// WS9 (SPK-007) — scanned/text PDF import → structured draft
// ─────────────────────────────────────────────────────────────────────────────

/** One field-presence check from the import builder-validation report. */
export interface SpeakingImportFieldCheck {
  field: string;
  detected: boolean;
  required: boolean;
  note?: string | null;
}

/** Builder-validation report for an imported source PDF. */
export interface SpeakingImportValidationReport {
  isPublishable: boolean;
  checks: SpeakingImportFieldCheck[];
  blockers: string[];
}

/** Result of `POST /v1/admin/speaking/role-play-cards/import`. */
export interface SpeakingContentImportResult {
  sourceAssetKey: string;
  sourceBytes: number;
  extractedChars: number;
  likelyScanned: boolean;
  validation: SpeakingImportValidationReport;
  draftCardId?: string | null;
  draft?: RolePlayCardDetail | null;
  warning?: string | null;
  /** Viewable MediaAsset id for the persisted source PDF (provenance). Lets the
   * admin render the source beside the form when structuring a scanned card by
   * hand. Served (authenticated) at `/v1/media/{id}/content`. */
  sourceMediaId?: string | null;
}

/**
 * Imports a source paper (scanned or text PDF). The source asset is always
 * persisted server-side for provenance; when `autoDraft` is true and usable
 * text is extracted, the grounded AI-draft path produces a reviewable Draft
 * card. A scanned PDF with no OCR provider returns the validation report and
 * the saved source asset for manual structuring.
 */
export async function importSpeakingRolePlayCard(input: {
  file: File;
  professionId: string;
  topic?: string | null;
  autoDraft?: boolean;
}): Promise<SpeakingContentImportResult> {
  const form = new FormData();
  form.append('file', input.file);
  form.append('professionId', input.professionId);
  if (input.topic) form.append('topic', input.topic);
  form.append('autoDraft', String(input.autoDraft ?? false));
  return request<SpeakingContentImportResult>(
    '/v1/admin/speaking/role-play-cards/import',
    { method: 'POST', body: form },
    { timeoutMs: 180_000 },
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Learner function (no interlocutor data)
// ─────────────────────────────────────────────────────────────────────────────

export async function learnerGetRolePlayCard(cardId: string): Promise<RolePlayCardLearnerDetail> {
  return request<RolePlayCardLearnerDetail>(`/v1/speaking/role-play-cards/${encodeURIComponent(cardId)}`);
}

// ─────────────────────────────────────────────────────────────────────────────
// Constants for forms
// ─────────────────────────────────────────────────────────────────────────────

export const PROFESSION_OPTIONS: { value: string; label: string }[] = [
  { value: 'nursing', label: 'Nursing' },
  { value: 'medicine', label: 'Medicine' },
  { value: 'pharmacy', label: 'Pharmacy' },
  { value: 'physiotherapy', label: 'Physiotherapy' },
  { value: 'dentistry', label: 'Dentistry' },
  { value: 'occupational_therapy', label: 'Occupational therapy' },
  { value: 'radiography', label: 'Radiography' },
  { value: 'optometry', label: 'Optometry' },
  { value: 'podiatry', label: 'Podiatry' },
  { value: 'speech_pathology', label: 'Speech pathology' },
  { value: 'dietetics', label: 'Dietetics' },
  { value: 'veterinary_science', label: 'Veterinary science' },
];

export const DIFFICULTY_OPTIONS: { value: RolePlayCardDifficulty; label: string }[] = [
  { value: 'core', label: 'Core' },
  { value: 'extension', label: 'Extension' },
  { value: 'exam', label: 'Exam' },
];

/** The 9 OET Speaking criteria (4 linguistic + 5 clinical communication). */
export const SPEAKING_CRITERIA_OPTIONS: { value: string; label: string; band: 'linguistic' | 'clinical' }[] = [
  { value: 'intelligibility', label: 'Intelligibility', band: 'linguistic' },
  { value: 'fluency', label: 'Fluency', band: 'linguistic' },
  { value: 'appropriateness', label: 'Appropriateness of language', band: 'linguistic' },
  { value: 'grammarExpression', label: 'Resources of grammar & expression', band: 'linguistic' },
  { value: 'relationshipBuilding', label: 'Relationship building', band: 'clinical' },
  { value: 'patientPerspective', label: 'Understanding patient perspective', band: 'clinical' },
  { value: 'structure', label: 'Providing structure', band: 'clinical' },
  { value: 'informationGathering', label: 'Information gathering', band: 'clinical' },
  { value: 'informationGiving', label: 'Information giving', band: 'clinical' },
];

export const RESISTANCE_LEVEL_OPTIONS: { value: ResistanceLevelCode; label: string; description: string }[] = [
  { value: 'low', label: 'Low', description: 'Patient cooperative; accepts advice with minimal probing.' },
  { value: 'medium', label: 'Medium', description: 'Patient questions advice; needs reassurance before accepting.' },
  { value: 'high', label: 'High', description: 'Patient strongly resists; requires sustained empathy and rationale.' },
];

export const DEFAULT_DISCLAIMER =
  'Practice estimate only. This is not an official OET score or result.';
