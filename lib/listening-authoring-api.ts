/**
 * Listening authoring API client (admin-side).
 *
 * Mirrors lib/reading-authoring-api.ts for the Listening 42-item question map.
 * Until the relational ListeningPart/ListeningQuestion entities ship (Phase 2),
 * the authored structure is persisted as a JSON document at
 * `ContentPaper.ExtractedTextJson["listeningQuestions"]` — exactly what the
 * learner runtime (`ListeningLearnerService.ExtractQuestions`) reads to drive
 * the player + grader. This client is the only path admins should use to write
 * that document.
 */

import { ensureFreshAccessToken } from './auth-client';
import { fetchWithTimeout } from './network/fetch-with-timeout';
import { env } from './env';

export type ListeningPartCode = 'A1' | 'A2' | 'B' | 'C1' | 'C2';
export type ListeningQuestionType = 'short_answer' | 'multiple_choice_3';

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
  });
  for (let i = 1; i <= 12; i++) items.push(blank(i, 'A1', 'short_answer'));
  for (let i = 13; i <= 24; i++) items.push(blank(i, 'A2', 'short_answer'));
  for (let i = 25; i <= 30; i++) items.push(blank(i, 'B', 'multiple_choice_3'));
  for (let i = 31; i <= 36; i++) items.push(blank(i, 'C1', 'multiple_choice_3'));
  for (let i = 37; i <= 42; i++) items.push(blank(i, 'C2', 'multiple_choice_3'));
  return items;
}
