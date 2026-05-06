/**
 * Listening authoring API client (admin-side).
 *
 * Mirrors lib/reading-authoring-api.ts for the Listening 42-item question map.
 * Structure and extract metadata are authored through JSON-compatible admin
 * endpoints, then projected into the relational Listening tables by backfill.
 * The learner runtime prefers relational rows when present and keeps JSON as a
 * migration fallback.
 */

import { ensureFreshAccessToken } from './auth-client';
import { fetchWithTimeout } from './network/fetch-with-timeout';
import { env } from './env';

export type ListeningPartCode = 'A1' | 'A2' | 'B' | 'C1' | 'C2';
export type ListeningQuestionType = 'short_answer' | 'multiple_choice_3';
export type ListeningExtractKind = 'consultation' | 'workplace' | 'presentation';

/** Phase 4: per-option distractor category for Part B/C MCQ analysis. */
export type ListeningDistractorCategory =
  | 'too_strong'
  | 'too_weak'
  | 'wrong_speaker'
  | 'opposite_meaning'
  | 'reused_keyword';

/** Phase 4: speaker attitude tag for Part C extracts. */
export type ListeningSpeakerAttitude =
  | 'concerned'
  | 'optimistic'
  | 'doubtful'
  | 'critical'
  | 'neutral'
  | 'other';

export interface ListeningAuthoredQuestion {
  id: string;
  number: number;
  partCode: ListeningPartCode;
  type: ListeningQuestionType;
  stem: string;
  options: string[];
  correctAnswer: string;
  acceptedAnswers: string[];
  explanation: string | null;
  skillTag: string | null;
  transcriptExcerpt: string | null;
  distractorExplanation: string | null;
  points: number;
  // Phase 4: parallel arrays aligned with `options` (length matches options).
  // `null` means "not authored yet" for that option.
  optionDistractorWhy?: (string | null)[];
  optionDistractorCategory?: (ListeningDistractorCategory | null)[];
  // Phase 4: Part C only — the speaker's attitude in the extract.
  speakerAttitude?: ListeningSpeakerAttitude | null;
  // Phase 5: time-coded transcript evidence (start/end ms in section audio)
  // for jump-to-evidence in the post-attempt review player.
  transcriptEvidenceStartMs?: number | null;
  transcriptEvidenceEndMs?: number | null;
}

export interface ListeningAuthoredSpeaker {
  id: string;
  role: string;
  gender?: 'm' | 'f' | 'nb' | null;
  accent?: string | null;
}

export interface ListeningAuthoredExtract {
  partCode: ListeningPartCode;
  displayOrder: number;
  kind: ListeningExtractKind;
  title: string;
  accentCode: string | null;
  speakers: ListeningAuthoredSpeaker[];
  audioStartMs: number | null;
  audioEndMs: number | null;
}

export interface ListeningAuthoredExtractList {
  extracts: ListeningAuthoredExtract[];
}

export interface ListeningBackfillReport {
  success: boolean;
  paperId: string;
  reason: string;
  partsCreated: number;
  extractsCreated: number;
  questionsCreated: number;
  optionsCreated: number;
}

export interface ListeningValidationCounts {
  partACount: number;
  partBCount: number;
  partCCount: number;
  totalItems: number;
}

export interface ListeningAuthoredQuestionList {
  questions: ListeningAuthoredQuestion[];
  counts: ListeningValidationCounts;
}

export interface ListeningValidationIssue {
  code: string;
  severity: 'error' | 'warning';
  message: string;
}

export interface ListeningValidationReport {
  isPublishReady: boolean;
  issues: ListeningValidationIssue[];
  counts: ListeningValidationCounts;
}

const CSRF_SAFE = new Set(['GET', 'HEAD', 'OPTIONS']);

function readCsrfCookie(): string | null {
  if (typeof document === 'undefined') return null;
  const m = document.cookie.match(/(?:^|;\s*)oet_csrf=([^;]+)/);
  return m ? decodeURIComponent(m[1]) : null;
}

function resolveUrl(path: string): string {
  if (path.startsWith('http')) return path;
  const base = env.apiBaseUrl || '';
  return base ? `${base.replace(/\/$/, '')}${path}` : path;
}

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const token = await ensureFreshAccessToken();
  const headers = new Headers(init?.headers);
  headers.set('Accept', 'application/json');
  if (token) headers.set('Authorization', `Bearer ${token}`);
  if (init?.body && typeof init.body === 'string' && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  const method = (init?.method ?? 'GET').toUpperCase();
  if (!CSRF_SAFE.has(method) && !headers.has('x-csrf-token')) {
    const csrf = readCsrfCookie();
    if (csrf) headers.set('x-csrf-token', csrf);
  }
  const res = await fetchWithTimeout(resolveUrl(path), {
    ...init,
    headers,
    credentials: init?.credentials ?? 'include',
  });
  if (!res.ok) {
    let detail: unknown = null;
    try { detail = await res.json(); } catch { /* ignore */ }
    const err = new Error(`HTTP ${res.status}`) as Error & { status?: number; detail?: unknown };
    err.status = res.status;
    err.detail = detail;
    throw err;
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

export const getListeningStructure = (paperId: string) =>
  api<ListeningAuthoredQuestionList>(`/v1/admin/papers/${paperId}/listening/structure`);

export const replaceListeningStructure = (
  paperId: string,
  questions: ListeningAuthoredQuestion[],
) =>
  api<ListeningAuthoredQuestionList>(`/v1/admin/papers/${paperId}/listening/structure`, {
    method: 'PUT',
    body: JSON.stringify({ questions }),
  });

export const validateListeningStructure = (paperId: string) =>
  api<ListeningValidationReport>(`/v1/admin/papers/${paperId}/listening/validate`);

export const getListeningExtracts = (paperId: string) =>
  api<ListeningAuthoredExtractList>(`/v1/admin/papers/${paperId}/listening/extracts`);

export const replaceListeningExtracts = (
  paperId: string,
  extracts: ListeningAuthoredExtract[],
) =>
  api<ListeningAuthoredExtractList>(`/v1/admin/papers/${paperId}/listening/extracts`, {
    method: 'PUT',
    body: JSON.stringify({ extracts }),
  });

export const backfillListeningPaper = (paperId: string) =>
  api<ListeningBackfillReport>(`/v1/admin/papers/${paperId}/listening/backfill`, {
    method: 'POST',
    body: JSON.stringify({}),
  });

// ── Phase 8: AI extraction (admin) ─────────────────────────────────────

export type ListeningExtractionStatus = 'Pending' | 'Approved' | 'Rejected';

/**
 * Snapshot returned by POST /v1/admin/papers/{paperId}/listening/extract.
 *
 * After Gap B7 the propose endpoint persists the AI-gateway result as a
 * Pending {@link ListeningExtractionDraftDto} and returns the draft id +
 * current status so the admin diff/approve UI can take over instead of
 * silently overwriting the authored structure.
 */
export interface ListeningExtractionProposal {
  draftId: string;
  status: ListeningExtractionStatus;
  summary: string;
  isStub: boolean;
  stubReason: string | null;
  questions: ListeningAuthoredQuestion[];
}

/**
 * Full extraction draft persisted on the backend. Mirrors the
 * `ListeningExtractionDraft` entity in `Domain/ListeningEntities.cs` plus
 * the `questions` projection helpers parse client-side from
 * `proposedQuestionsJson` when the server returns the raw entity shape.
 */
export interface ListeningExtractionDraftDto {
  id: string;
  paperId: string;
  status: ListeningExtractionStatus;
  proposedAt: string;
  proposedByUserId: string | null;
  isStub: boolean;
  stubReason: string | null;
  summary: string;
  questions: ListeningAuthoredQuestion[];
  rawAiResponseJson: string | null;
  decidedAt: string | null;
  decidedByUserId: string | null;
  decisionReason: string | null;
}

/**
 * Wire shape the backend currently emits for list/get draft endpoints — the
 * raw entity. We normalise into {@link ListeningExtractionDraftDto} on the
 * client by parsing `proposedQuestionsJson` if `questions` isn't already
 * supplied. This keeps the UI insulated from DTO projection changes on the
 * server.
 */
type ListeningExtractionDraftWire = Partial<ListeningExtractionDraftDto> & {
  id: string;
  paperId: string;
  status: ListeningExtractionStatus;
  proposedAt: string;
  isStub: boolean;
  summary?: string | null;
  proposedQuestionsJson?: string | null;
  questions?: ListeningAuthoredQuestion[] | null;
};

function parseQuestionsJson(json: string | null | undefined): ListeningAuthoredQuestion[] {
  if (!json) return [];
  try {
    const parsed = JSON.parse(json);
    return Array.isArray(parsed) ? (parsed as ListeningAuthoredQuestion[]) : [];
  } catch {
    return [];
  }
}

function normaliseDraft(wire: ListeningExtractionDraftWire): ListeningExtractionDraftDto {
  const questions = wire.questions && wire.questions.length > 0
    ? wire.questions
    : parseQuestionsJson(wire.proposedQuestionsJson);
  return {
    id: wire.id,
    paperId: wire.paperId,
    status: wire.status,
    proposedAt: wire.proposedAt,
    proposedByUserId: wire.proposedByUserId ?? null,
    isStub: wire.isStub,
    stubReason: wire.stubReason ?? null,
    summary: wire.summary ?? '',
    questions,
    rawAiResponseJson: wire.rawAiResponseJson ?? null,
    decidedAt: wire.decidedAt ?? null,
    decidedByUserId: wire.decidedByUserId ?? null,
    decisionReason: wire.decisionReason ?? null,
  };
}

export const proposeListeningStructure = (paperId: string) =>
  api<ListeningExtractionProposal>(`/v1/admin/papers/${paperId}/listening/extract`, {
    method: 'POST',
    body: JSON.stringify({}),
  });

export async function listListeningExtractionDrafts(
  paperId: string,
  status?: ListeningExtractionStatus,
): Promise<ListeningExtractionDraftDto[]> {
  const qs = status ? `?status=${encodeURIComponent(status)}` : '';
  const wire = await api<ListeningExtractionDraftWire[]>(
    `/v1/admin/papers/${paperId}/listening/extractions${qs}`,
  );
  return wire.map(normaliseDraft);
}

export async function getListeningExtractionDraft(
  paperId: string,
  draftId: string,
): Promise<ListeningExtractionDraftDto> {
  const wire = await api<ListeningExtractionDraftWire>(
    `/v1/admin/papers/${paperId}/listening/extractions/${draftId}`,
  );
  return normaliseDraft(wire);
}

export async function approveListeningExtractionDraft(
  paperId: string,
  draftId: string,
  reason?: string,
): Promise<ListeningExtractionDraftDto> {
  const wire = await api<ListeningExtractionDraftWire>(
    `/v1/admin/papers/${paperId}/listening/extractions/${draftId}/approve`,
    {
      method: 'POST',
      body: JSON.stringify({ reason: reason ?? '' }),
    },
  );
  return normaliseDraft(wire);
}

export async function rejectListeningExtractionDraft(
  paperId: string,
  draftId: string,
  reason: string,
): Promise<ListeningExtractionDraftDto> {
  const wire = await api<ListeningExtractionDraftWire>(
    `/v1/admin/papers/${paperId}/listening/extractions/${draftId}/reject`,
    {
      method: 'POST',
      body: JSON.stringify({ reason }),
    },
  );
  return normaliseDraft(wire);
}

// ── Learner: course pathway snapshot ───────────────────────────────────
//
// Mirrors `ListeningPathwaySnapshot` from
// `backend/src/OetLearner.Api/Services/Listening/ListeningPathwayService.cs`.
// Surfaced at GET /v1/listening-papers/me/pathway (auth-required) and joins
// completed-attempt, best-scaled, and mock signals into a single readiness
// stage with one structured next-action.

export type ListeningPathwayStage =
  | 'not_started'
  | 'diagnostic'
  | 'drilling'
  | 'mini_tests'
  | 'mock_ready'
  | 'exam_ready';

export type ListeningPathwayActionKind =
  | 'start_diagnostic'
  | 'start_drill'
  | 'start_mini_test'
  | 'start_mock'
  | 'review_results'
  | 'book_exam';

export interface ListeningPathwayAction {
  kind: ListeningPathwayActionKind;
  label: string;
  drillId: string | null;
  paperId: string | null;
  route: string | null;
}

export interface ListeningPathwayMilestone {
  code: string;
  label: string;
  achieved: boolean;
  progress: number | null;
  target: number | null;
}

export interface ListeningPathwaySnapshot {
  stage: ListeningPathwayStage;
  headline: string;
  bestScaledScore: number | null;
  submittedAttempts: number;
  submittedListeningMockAttempts: number;
  nextAction: ListeningPathwayAction;
  milestones: ListeningPathwayMilestone[];
}

export const getListeningPathway = () =>
  api<ListeningPathwaySnapshot>('/v1/listening-papers/me/pathway');

// ── Phase 6: per-learner Listening analytics ───────────────────────────
//
// Mirrors `ListeningStudentAnalyticsDto` in
// `backend/src/OetLearner.Api/Services/Listening/ListeningAnalyticsService.cs`.
// Surfaced at GET /v1/listening-papers/me/analytics.

export interface ListeningPartBreakdown {
  partCode: string;        // "A" | "B" | "C"
  earned: number;
  max: number;
  accuracyPercent: number;
}

export interface ListeningTopWeakness {
  errorType: string;
  label: string;
  count: number;
}

export interface ListeningActionPlanItem {
  headline: string;
  detail: string;
  route: string | null;
}

export interface ListeningStudentAnalytics {
  completedAttempts: number;
  bestScaledScore: number | null;
  averageScaledScore: number | null;
  likelyPassing: boolean;
  partBreakdown: ListeningPartBreakdown[];
  weaknesses: ListeningTopWeakness[];
  actionPlan: ListeningActionPlanItem[];
}

export const getListeningStudentAnalytics = () =>
  api<ListeningStudentAnalytics>('/v1/listening-papers/me/analytics');

// ── Phase 7: admin-side Listening analytics ────────────────────────────

export interface ListeningHardestQuestion {
  paperId: string;
  paperTitle: string;
  questionNumber: number;
  partCode: string;
  attemptCount: number;
  accuracyPercent: number;
}

export interface ListeningDistractorHeat {
  paperId: string;
  questionNumber: number;
  correctAnswer: string;
  wrongAnswerHistogram: Record<string, number>;
}

export interface ListeningCommonMisspelling {
  correctAnswer: string;
  wrongSpelling: string;
  count: number;
}

export interface ListeningAdminAnalytics {
  days: number;
  completedAttempts: number;
  averageScaledScore: number | null;
  percentLikelyPassing: number;
  classPartAverages: ListeningPartBreakdown[];
  hardestQuestions: ListeningHardestQuestion[];
  distractorHeat: ListeningDistractorHeat[];
  commonMisspellings: ListeningCommonMisspelling[];
}

export const getListeningAdminAnalytics = (days: number = 30) =>
  api<ListeningAdminAnalytics>(`/v1/admin/listening/analytics?days=${days}`);

// ── Canonical scaffold ─────────────────────────────────────────────────────

export const LISTENING_CANONICAL_TOTAL = 42;
export const LISTENING_PART_A_COUNT = 24; // 12 + 12 across two consultations
export const LISTENING_PART_B_COUNT = 6;
export const LISTENING_PART_C_COUNT = 12; // 6 + 6 across two presentations

/**
 * Generates the 42-item canonical OET Listening skeleton with empty stems and
 * answers, ready for an admin to fill in. Numbering matches the printed
 * Question-Paper PDFs (1–42), part codes follow the runtime's expected scheme:
 *   A1 = consultation 1 (Q1–12), A2 = consultation 2 (Q13–24)
 *   B  = workplace extracts (Q25–30)
 *   C1 = presentation 1 (Q31–36), C2 = presentation 2 (Q37–42)
 */
export function buildCanonicalListeningSkeleton(): ListeningAuthoredQuestion[] {
  const items: ListeningAuthoredQuestion[] = [];
  const blank = (n: number, partCode: ListeningPartCode, type: ListeningQuestionType): ListeningAuthoredQuestion => ({
    id: `lq-${n}`,
    number: n,
    partCode,
    type,
    stem: '',
    options: type === 'multiple_choice_3' ? ['', '', ''] : [],
    correctAnswer: '',
    acceptedAnswers: [],
    explanation: null,
    skillTag: null,
    transcriptExcerpt: null,
    distractorExplanation: null,
    points: 1,
    optionDistractorWhy: type === 'multiple_choice_3' ? [null, null, null] : [],
    optionDistractorCategory: type === 'multiple_choice_3' ? [null, null, null] : [],
    speakerAttitude: null,
    transcriptEvidenceStartMs: null,
    transcriptEvidenceEndMs: null,
  });
  for (let i = 1; i <= 12; i++) items.push(blank(i, 'A1', 'short_answer'));
  for (let i = 13; i <= 24; i++) items.push(blank(i, 'A2', 'short_answer'));
  for (let i = 25; i <= 30; i++) items.push(blank(i, 'B', 'multiple_choice_3'));
  for (let i = 31; i <= 36; i++) items.push(blank(i, 'C1', 'multiple_choice_3'));
  for (let i = 37; i <= 42; i++) items.push(blank(i, 'C2', 'multiple_choice_3'));
  return items;
}
