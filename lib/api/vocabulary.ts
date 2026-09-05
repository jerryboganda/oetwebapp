/**
 * Vocabulary terms, categories, recall sets, my-list, flashcards —
 * extracted from `lib/api.ts`. Re-exported there, so `@/lib/api`
 * imports keep working.
 */
import { apiRequest } from './client';
import type { LearnerVocabulary, MyVocabularyPageResponse } from '../types/vocabulary';

export async function fetchVocabularyTerms(params?: { examTypeCode?: string; category?: string; profession?: string; search?: string; recallSet?: string; freePreviewOnly?: boolean; page?: number; pageSize?: number }) {
  const p = new URLSearchParams();
  if (params?.examTypeCode) p.set('examTypeCode', params.examTypeCode);
  if (params?.category) p.set('category', params.category);
  if (params?.profession) p.set('profession', params.profession);
  if (params?.search) p.set('search', params.search);
  if (params?.recallSet) p.set('recallSet', params.recallSet);
  if (params?.freePreviewOnly) p.set('freePreviewOnly', 'true');
  if (params?.page) p.set('page', String(params.page));
  if (params?.pageSize) p.set('pageSize', String(params.pageSize));
  return apiRequest(`/v1/vocabulary/terms?${p}`);
}

export async function fetchVocabularyTerm(termId: string) {
  return apiRequest(`/v1/vocabulary/terms/${encodeURIComponent(termId)}`);
}

export async function lookupVocabularyTerm(query: string, examTypeCode = 'oet') {
  const p = new URLSearchParams({ q: query, examTypeCode });
  return apiRequest(`/v1/vocabulary/terms/lookup?${p}`);
}

export async function fetchVocabularyCategories(params?: { examTypeCode?: string; profession?: string }) {
  const p = new URLSearchParams();
  if (params?.examTypeCode) p.set('examTypeCode', params.examTypeCode);
  if (params?.profession) p.set('profession', params.profession);
  const qs = p.toString();
  return apiRequest(`/v1/vocabulary/categories${qs ? `?${qs}` : ''}`);
}

/**
 * Recall-set registry (year/source dimension). Returns the canonical 3-set
 * list (`old`, `2023-2025`, `2026`) with live term counts. Stable even when
 * no terms are tagged yet — admin/learner UIs can render the chips eagerly.
 * See `backend/src/OetLearner.Api/Domain/RecallSetCodes.cs`.
 */
export interface RecallSetSummary {
  code: string;
  displayName: string;
  shortLabel: string;
  description: string;
  sortOrder: number;
  termCount: number;
}
export interface RecallSetsResponse {
  examTypeCode: string;
  professionId: string | null;
  sets: RecallSetSummary[];
  /** Count of admin-flagged free-preview terms — powers the "Free Preview Recalls" chip badge. */
  freePreviewCount: number;
}
export async function fetchVocabularyRecallSets(params?: { examTypeCode?: string; profession?: string }): Promise<RecallSetsResponse> {
  const p = new URLSearchParams();
  if (params?.examTypeCode) p.set('examTypeCode', params.examTypeCode);
  if (params?.profession) p.set('profession', params.profession);
  const qs = p.toString();
  return apiRequest(`/v1/vocabulary/recall-sets${qs ? `?${qs}` : ''}`) as Promise<RecallSetsResponse>;
}

export async function fetchVocabularyStats() {
  return apiRequest('/v1/vocabulary/stats');
}

export async function fetchVocabularyDailySet(count = 10) {
  return apiRequest(`/v1/vocabulary/daily-set?count=${count}`);
}

export async function fetchVocabularyQuizHistory(params?: { page?: number; pageSize?: number }) {
  const p = new URLSearchParams();
  p.set('page', String(params?.page ?? 1));
  p.set('pageSize', String(params?.pageSize ?? 20));
  return apiRequest(`/v1/vocabulary/quiz/history?${p}`);
}

export interface MyVocabularyPageRequest {
  page: number;
  pageSize: number;
  termId?: string;
}

export function fetchMyVocabulary(mastery?: string): Promise<LearnerVocabulary[]>;
export function fetchMyVocabulary(
  mastery: string | undefined,
  pagination: MyVocabularyPageRequest,
): Promise<MyVocabularyPageResponse>;
export async function fetchMyVocabulary(
  mastery?: string,
  pagination?: MyVocabularyPageRequest,
): Promise<LearnerVocabulary[] | MyVocabularyPageResponse> {
  const p = new URLSearchParams();
  if (mastery) p.set('mastery', mastery);
  if (pagination?.termId) p.set('termId', pagination.termId);
  if (pagination) {
    p.set('page', String(pagination.page));
    p.set('pageSize', String(pagination.pageSize));
  }
  const qs = p.toString();
  return apiRequest<LearnerVocabulary[] | MyVocabularyPageResponse>(
    `/v1/vocabulary/my-list${qs ? `?${qs}` : ''}`,
  );
}

export async function addToMyVocabulary(termId: string, opts?: { sourceRef?: string; context?: string }) {
  return apiRequest(`/v1/vocabulary/my-list/${encodeURIComponent(termId)}`, {
    method: 'POST',
    body: JSON.stringify({ sourceRef: opts?.sourceRef, context: opts?.context }),
  });
}

export async function removeFromMyVocabulary(termId: string) {
  return apiRequest(`/v1/vocabulary/my-list/${encodeURIComponent(termId)}`, { method: 'DELETE' });
}

export async function fetchDueFlashcards(limit = 20) {
  return apiRequest(`/v1/vocabulary/flashcards/due?limit=${limit}`);
}

export async function submitFlashcardReview(lvId: string, quality: number) {
  return apiRequest(`/v1/vocabulary/flashcards/${encodeURIComponent(lvId)}/review`, {
    method: 'POST',
    body: JSON.stringify({ quality }),
  });
}

export async function fetchVocabQuiz(count = 10, format: string = 'definition_match') {
  return apiRequest(`/v1/vocabulary/quiz?count=${count}&format=${encodeURIComponent(format)}`);
}

export async function submitVocabQuiz(payload: { answers: Array<{ termId: string; correct: boolean; userAnswer?: string }>; durationSeconds: number; format?: string }) {
  return apiRequest('/v1/vocabulary/quiz/submit', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}
