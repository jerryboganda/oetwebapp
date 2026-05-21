/**
 * Typed API client for the Speaking dual-scoring (AI + Tutor) surface.
 *
 * Mirrors the self-contained pattern from `lib/api/speaking-role-play-cards.ts`:
 * Bearer-token auth via `ensureFreshAccessToken`, CSRF token via the
 * `oet_csrf` cookie, retry on 5xx/408/429.
 *
 * Backend endpoints targeted (see plan section E):
 *   GET    /v1/speaking/sessions/{id}/assessments
 *   POST   /v1/expert/speaking/sessions/{id}/tutor-assessment              (create draft)
 *   PATCH  /v1/expert/speaking/sessions/{id}/tutor-assessments/{aid}
 *   POST   /v1/expert/speaking/sessions/{id}/tutor-assessments/{aid}/submit
 *   POST   /v1/expert/speaking/sessions/{id}/comments
 *   GET    /v1/expert/speaking/queue?professionId=
 *   POST   /v1/expert/speaking/queue/{sessionId}/claim
 *   POST   /v1/expert/speaking/queue/{sessionId}/release
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
    // Silent: caller handles 401 redirects.
  }

  return headers;
}

const RETRYABLE_STATUS = (status: number) => status >= 500 || status === 408 || status === 429;
const MAX_RETRIES = 2;
const RETRY_DELAYS_MS = [1000, 3000];

export class SpeakingAssessmentApiError extends Error {
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
    this.name = 'SpeakingAssessmentApiError';
    this.status = status;
    this.code = code;
    this.retryable = retryable;
    this.fieldErrors = fieldErrors;
  }
}

async function request<T>(path: string, init?: RequestInit, opts?: { acceptedStatuses?: number[] }): Promise<T> {
  let lastError: Error | null = null;

  for (let attempt = 0; attempt <= MAX_RETRIES; attempt++) {
    try {
      const response = await fetchWithTimeout(resolveApiUrl(path), {
        ...init,
        headers: await buildHeaders(init),
      });

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

        const apiError = new SpeakingAssessmentApiError(response.status, code, message, retryable, fieldErrors);
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
      if (err instanceof SpeakingAssessmentApiError) throw err;
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
// Types — public surface
// ─────────────────────────────────────────────────────────────────────────────

/**
 * The 9 OET Speaking criteria. The first four are scored 0-6 (linguistic);
 * the remaining five are scored 0-3 (clinical communication).
 */
export type SpeakingCriterionCode =
  | 'intelligibility'
  | 'fluency'
  | 'appropriateness'
  | 'grammarExpression'
  | 'relationshipBuilding'
  | 'patientPerspective'
  | 'structure'
  | 'informationGathering'
  | 'informationGiving';

export const LINGUISTIC_CRITERIA: SpeakingCriterionCode[] = [
  'intelligibility',
  'fluency',
  'appropriateness',
  'grammarExpression',
];

export const CLINICAL_CRITERIA: SpeakingCriterionCode[] = [
  'relationshipBuilding',
  'patientPerspective',
  'structure',
  'informationGathering',
  'informationGiving',
];

export const CRITERION_MAX: Record<SpeakingCriterionCode, number> = {
  intelligibility: 6,
  fluency: 6,
  appropriateness: 6,
  grammarExpression: 6,
  relationshipBuilding: 3,
  patientPerspective: 3,
  structure: 3,
  informationGathering: 3,
  informationGiving: 3,
};

export const CRITERION_LABEL: Record<SpeakingCriterionCode, string> = {
  intelligibility: 'Intelligibility',
  fluency: 'Fluency',
  appropriateness: 'Appropriateness of language',
  grammarExpression: 'Resources of grammar & expression',
  relationshipBuilding: 'Relationship building',
  patientPerspective: 'Understanding patient perspective',
  structure: 'Providing structure',
  informationGathering: 'Information gathering',
  informationGiving: 'Information giving',
};

/** Per-criterion descriptors used by both AI and Tutor rubric UIs. */
export const CRITERION_LEVEL_DESCRIPTORS: Record<SpeakingCriterionCode, Record<number, string>> = {
  intelligibility: {
    0: 'Almost unintelligible; communication breaks down.',
    1: 'Frequent strain; many features impede understanding.',
    2: 'Some strain; several pronunciation features impede understanding.',
    3: 'Generally intelligible; occasional strain on listener.',
    4: 'Easily understood; some pronunciation slips that do not impede communication.',
    5: 'Easily understood; minor mispronunciation that never impedes communication.',
    6: 'Highly intelligible; pronunciation does not impede communication at any point.',
  },
  fluency: {
    0: 'Speech breaks down repeatedly; lengthy pauses.',
    1: 'Frequent hesitation; speech is fragmented.',
    2: 'Noticeable hesitation; uneven pace impedes listener.',
    3: 'Generally fluent with some hesitation that rarely impedes.',
    4: 'Fluent with occasional hesitation searching for words.',
    5: 'Fluent with only natural pauses for thought.',
    6: 'Highly fluent; natural rate, smooth flow, no strain.',
  },
  appropriateness: {
    0: 'Inappropriate register throughout; jargon or insensitive tone.',
    1: 'Frequently inappropriate; little adaptation to patient.',
    2: 'Some inappropriate moments; limited register awareness.',
    3: 'Generally appropriate; some lapses in tone or jargon use.',
    4: 'Appropriate register with minor lapses; mostly patient-centred.',
    5: 'Consistently appropriate; clear sensitivity to patient context.',
    6: 'Highly appropriate; nuanced adaptation across the encounter.',
  },
  grammarExpression: {
    0: 'Pervasive errors; meaning is frequently obscured.',
    1: 'Many basic errors; limited range.',
    2: 'Frequent errors; restricted range of structures.',
    3: 'Generally accurate; some errors when attempting more complex forms.',
    4: 'Mostly accurate with a good range; minor slips.',
    5: 'Accurate with a wide range of structures; rare slips.',
    6: 'Highly accurate; very wide range of structures used naturally.',
  },
  relationshipBuilding: {
    0: 'No rapport; tone may alienate the patient.',
    1: 'Limited rapport; minimal acknowledgement of patient.',
    2: 'Some rapport-building; partial acknowledgement of feelings.',
    3: 'Clear rapport; warm tone, active acknowledgement of patient throughout.',
  },
  patientPerspective: {
    0: 'No attempt to elicit or acknowledge patient perspective.',
    1: 'Minimal exploration of perspective; mostly clinician-led.',
    2: 'Some exploration of patient ideas, concerns and expectations.',
    3: 'Thorough exploration of patient perspective; responses tailored accordingly.',
  },
  structure: {
    0: 'Disorganised; patient is left confused about flow.',
    1: 'Poorly signposted; gaps between phases.',
    2: 'Generally structured; some signposting.',
    3: 'Clearly structured; signposted phases and clear transitions throughout.',
  },
  informationGathering: {
    0: 'Information gathered is inadequate or irrelevant.',
    1: 'Some gaps; limited use of open/closed questions.',
    2: 'Adequate gathering; reasonable use of question types.',
    3: 'Thorough, focused gathering with effective use of open/closed questions.',
  },
  informationGiving: {
    0: 'Information given is unclear or misleading.',
    1: 'Some clarity issues; minimal chunking.',
    2: 'Generally clear; some chunking and checking of understanding.',
    3: 'Clear, chunked information with regular checking of understanding.',
  },
};

export type ReadinessBand = 'not_ready' | 'developing' | 'on_track' | 'exam_ready' | 'exceeds';

export interface AiCriterionScore {
  score: number;
  maxScore: number;
  rationale: string;
  evidenceQuotes: string[];
}

export interface AiAssessment {
  assessmentId: string;
  provider: string;
  modelId: string;
  promptTemplateId: string;
  criterionScores: Record<string, AiCriterionScore>;
  estimatedScaledScore: number;
  readinessBand: string;
  overallSummary: string;
  confidenceBand: string;
  generatedAt: string;
  isAdvisory: boolean;
  /** Optional: recommended remedial drills (slugs/ids); the learner page renders them on a tab. */
  recommendedDrills?: string[];
}

export interface TutorAssessment {
  assessmentId: string;
  tutorId: string;
  tutorName?: string;
  tutorPhotoUrl?: string;
  intelligibility: number;
  fluency: number;
  appropriateness: number;
  grammarExpression: number;
  relationshipBuilding: number;
  patientPerspective: number;
  structure: number;
  informationGathering: number;
  informationGiving: number;
  estimatedScaledScore: number;
  readinessBand: string;
  overallFeedbackMarkdown?: string;
  strengths: string[];
  improvements: string[];
  recommendedDrills: string[];
  recommendedRulebookEntries?: string[];
  isFinal: boolean;
  submittedAt?: string;
  createdAt?: string;
  updatedAt?: string;
}

export type AgreementBand = 'close' | 'moderate' | 'wide';

export interface DivergenceSummary {
  /** Per-criterion delta = tutor - ai. Positive = tutor scored higher. */
  perCriterion: Record<string, number>;
  /** Scaled-score delta (tutor.estimatedScaledScore - ai.estimatedScaledScore). */
  scaledDelta: number;
  agreementBand: AgreementBand;
}

export interface DualAssessmentResponse {
  sessionId: string;
  ai: AiAssessment | null;
  tutor: TutorAssessment | null;
  tutorHistory: TutorAssessment[];
  divergence: DivergenceSummary | null;
}

export interface TutorQueueItem {
  sessionId: string;
  learnerDisplayName: string;
  professionId: string;
  cardId: string;
  cardTitle: string;
  endedAt: string;
  durationSeconds: number;
  aiReadinessBand?: string;
  aiScaledScore?: number;
  hasDraft: boolean;
  claimedByMe: boolean;
  claimedBySomeoneElse: boolean;
  claimExpiresAt?: string | null;
}

export interface TutorQueueResponse {
  items: TutorQueueItem[];
  totalCount: number;
}

export interface TutorQueueFilters {
  professionId?: string;
  agePreset?: string;
}

export interface CreateTutorDraftInput {
  /** Optional initial draft body (all fields optional). */
  intelligibility?: number | null;
  fluency?: number | null;
  appropriateness?: number | null;
  grammarExpression?: number | null;
  relationshipBuilding?: number | null;
  patientPerspective?: number | null;
  structure?: number | null;
  informationGathering?: number | null;
  informationGiving?: number | null;
  overallFeedbackMarkdown?: string | null;
  strengths?: string[];
  improvements?: string[];
  recommendedDrills?: string[];
  recommendedRulebookEntries?: string[];
}

export type UpdateTutorDraftInput = CreateTutorDraftInput;

export interface SubmitTutorAssessmentInput {
  intelligibility: number;
  fluency: number;
  appropriateness: number;
  grammarExpression: number;
  relationshipBuilding: number;
  patientPerspective: number;
  structure: number;
  informationGathering: number;
  informationGiving: number;
  overallFeedbackMarkdown?: string;
  strengths: string[];
  improvements: string[];
  recommendedDrills: string[];
  recommendedRulebookEntries?: string[];
}

export interface TimestampedCommentInput {
  segmentStartMs: number;
  segmentEndMs: number;
  criterion?: SpeakingCriterionCode;
  severity?: 'note' | 'minor' | 'major';
  body: string;
}

export interface TimestampedComment {
  commentId: string;
  tutorId: string;
  tutorName?: string;
  segmentStartMs: number;
  segmentEndMs: number;
  criterion?: SpeakingCriterionCode;
  severity?: 'note' | 'minor' | 'major';
  body: string;
  createdAt: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Learner — dual assessment fetch
// ─────────────────────────────────────────────────────────────────────────────

export async function learnerGetDualAssessment(sessionId: string): Promise<DualAssessmentResponse> {
  return request<DualAssessmentResponse>(
    `/v1/speaking/sessions/${encodeURIComponent(sessionId)}/assessments`,
    undefined,
    { acceptedStatuses: [404] },
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Tutor — queue management
// ─────────────────────────────────────────────────────────────────────────────

export async function tutorListQueue(filters: TutorQueueFilters = {}): Promise<TutorQueueResponse> {
  const qs = new URLSearchParams();
  if (filters.professionId) qs.set('professionId', filters.professionId);
  if (filters.agePreset) qs.set('agePreset', filters.agePreset);
  const query = qs.toString();
  return request<TutorQueueResponse>(`/v1/expert/speaking/queue${query ? `?${query}` : ''}`);
}

export async function tutorClaimSession(sessionId: string): Promise<{ claimedUntil: string }> {
  return request<{ claimedUntil: string }>(
    `/v1/expert/speaking/queue/${encodeURIComponent(sessionId)}/claim`,
    { method: 'POST', body: '{}' },
  );
}

export async function tutorReleaseSession(sessionId: string): Promise<void> {
  await request<void>(
    `/v1/expert/speaking/queue/${encodeURIComponent(sessionId)}/release`,
    { method: 'POST', body: '{}' },
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Tutor — draft + submit
// ─────────────────────────────────────────────────────────────────────────────

export async function tutorCreateDraft(
  sessionId: string,
  body: CreateTutorDraftInput,
): Promise<TutorAssessment> {
  return request<TutorAssessment>(
    `/v1/expert/speaking/sessions/${encodeURIComponent(sessionId)}/tutor-assessment`,
    { method: 'POST', body: JSON.stringify(body) },
  );
}

export async function tutorUpdateDraft(
  sessionId: string,
  assessmentId: string,
  body: UpdateTutorDraftInput,
): Promise<TutorAssessment> {
  return request<TutorAssessment>(
    `/v1/expert/speaking/sessions/${encodeURIComponent(sessionId)}/tutor-assessments/${encodeURIComponent(assessmentId)}`,
    { method: 'PATCH', body: JSON.stringify(body) },
  );
}

export async function tutorSubmitAssessment(
  sessionId: string,
  assessmentId: string,
  body: SubmitTutorAssessmentInput,
): Promise<TutorAssessment> {
  return request<TutorAssessment>(
    `/v1/expert/speaking/sessions/${encodeURIComponent(sessionId)}/tutor-assessments/${encodeURIComponent(assessmentId)}/submit`,
    { method: 'POST', body: JSON.stringify(body) },
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Tutor — timestamped comments
// ─────────────────────────────────────────────────────────────────────────────

export async function tutorAddTimestampedComment(
  sessionId: string,
  body: TimestampedCommentInput,
): Promise<TimestampedComment> {
  return request<TimestampedComment>(
    `/v1/expert/speaking/sessions/${encodeURIComponent(sessionId)}/comments`,
    { method: 'POST', body: JSON.stringify(body) },
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Helpers — agreement band → tone for the divergence banner
// ─────────────────────────────────────────────────────────────────────────────

export function agreementBandTone(band: AgreementBand): 'success' | 'warning' | 'danger' {
  switch (band) {
    case 'close':
      return 'success';
    case 'moderate':
      return 'warning';
    case 'wide':
    default:
      return 'danger';
  }
}

export function readinessBandLabel(band: string): string {
  switch (band) {
    case 'not_ready':
      return 'Not yet ready';
    case 'developing':
      return 'Developing';
    case 'on_track':
      return 'On track';
    case 'exam_ready':
      return 'Exam ready';
    case 'exceeds':
      return 'Exceeds expectations';
    default:
      return band ? band.replace(/_/g, ' ') : 'Unknown';
  }
}
