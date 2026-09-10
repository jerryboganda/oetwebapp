/**
 * Recalls (unified vocabulary + spaced-repetition) — extracted from `lib/api.ts`.
 * Re-exported there, so `@/lib/api` imports keep working.
 * See docs/RECALLS-MODULE-PLAN.md.
 */
import { ApiError, apiRequest, getHeaders, isRetryable, resolveApiUrl } from './client';
import { fetchWithTimeout } from '../network/fetch-with-timeout';

export interface RecallsTodayResponse {
  dueToday: number;
  mastered: number;
  total: number;
  starred: number;
  vocabDueToday: number;
  reviewDueToday: number;
  readinessScore: number;
  weakTopics: { topic: string; total: number; weakCount: number }[];
}

export interface RecallsQueueItem {
  kind: 'vocab' | 'review';
  id: string;
  termId: string | null;
  title: string;
  subtitle: string | null;
  dueDate: string | null;
  starred: boolean;
  starReason: string | null;
  mastery: string;
  ipa: string | null;
  extraJson: string | null;
  /** How many times this vocab term has appeared across recall exams (the ×N badge). 0 for 'review' items. */
  examFrequencyCount?: number;
  /** Per-recall-set occurrence breakdown behind examFrequencyCount. */
  recallSetOccurrences?: Record<string, number> | null;
  /** When the underlying vocab term's content or ×N frequency was last touched. */
  updatedAt?: string | null;
}

export type RecallsStarReason = 'spelling' | 'pronunciation' | 'meaning' | 'hearing' | 'confused';

export interface RecallsLibraryItem {
  cardId: string;
  termId: string;
  term: string;
  definition: string;
  category: string;
  mastery: string;
  starred: boolean;
  starReason: string | null;
  lastErrorTypeCode: string | null;
  intervalDays: number;
  reviewCount: number;
  correctCount: number;
  /** How many times this term has appeared across recall exams (the ×N badge). */
  examFrequencyCount?: number;
  /** Per-recall-set occurrence breakdown behind examFrequencyCount. */
  recallSetOccurrences?: Record<string, number> | null;
  /** When this term's content or ×N frequency was last touched. */
  updatedAt?: string | null;
}

export async function fetchRecallsToday() {
  return apiRequest<RecallsTodayResponse>('/v1/recalls/today');
}

export async function fetchRecallsQueue(limit = 20) {
  return apiRequest<RecallsQueueItem[]>(`/v1/recalls/queue?limit=${limit}`);
}

export async function starRecall(kind: 'vocab' | 'term' | 'review', id: string, starred: boolean, reason?: RecallsStarReason) {
  return apiRequest('/v1/recalls/star', {
    method: 'POST',
    body: JSON.stringify({ kind, id, starred, reason }),
  });
}

/**
 * §3D — reuse the stored audio instead of paying for it twice.
 *
 * The server already serves the existing ElevenLabs asset without regenerating it
 * (`RecallsService.EnsureAudioAsync` returns the stored `MediaAsset`), but every
 * play used to re-download the blob. Practice Spelling, the mini Spelling Test and
 * Replay all hit the same word repeatedly, so the bytes are cached here.
 *
 * The cache holds the **Blob**, not the object URL: `playTransientAudio` revokes
 * object URLs when playback ends, so a cached URL would be dead on the second
 * play. A fresh URL is minted per play from the cached bytes.
 *
 * Bounded so an "All Words" test cannot pin the whole bank in memory; the Map's
 * insertion order gives cheap FIFO eviction.
 */
const AUDIO_CACHE_LIMIT = 64;
const audioCache = new Map<string, { blob: Blob; provider: string }>();

/** Test/teardown hook — the cache is module-level and would otherwise leak between cases. */
export function clearRecallsAudioCache() {
  audioCache.clear();
}

export async function fetchRecallsAudio(termId: string, speed: 'normal' | 'slow' | 'sentence' = 'normal') {
  const cacheKey = `${termId}:${speed}`;
  const cached = audioCache.get(cacheKey);
  if (cached) {
    // Re-insert so this entry becomes the most recently used.
    audioCache.delete(cacheKey);
    audioCache.set(cacheKey, cached);
    return { url: URL.createObjectURL(cached.blob), provider: cached.provider };
  }

  const path = `/v1/recalls/audio/${encodeURIComponent(termId)}?speed=${speed}`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    headers: await getHeaders(path, undefined, { json: false }),
  });

  if (!response.ok) {
    let code = response.status === 401 ? 'not_authenticated' : response.status === 403 ? 'forbidden' : 'unknown_error';
    let message = `Request failed: ${response.status}`;
    try {
      const error = await response.json();
      code = error.code ?? code;
      message = error.message ?? error.title ?? message;
    } catch {
      // Non-JSON error bodies are mapped through the status code above.
    }
    throw new ApiError(response.status, code, message, isRetryable(response.status));
  }

  const blob = await response.blob();
  const provider = response.headers.get('x-recalls-tts-provider') ?? 'stream';

  audioCache.set(cacheKey, { blob, provider });
  if (audioCache.size > AUDIO_CACHE_LIMIT) {
    const oldest = audioCache.keys().next().value;
    if (oldest !== undefined) audioCache.delete(oldest);
  }

  return { url: URL.createObjectURL(blob), provider };
}

// ── Spelling practice / test (§3B, §3C) ──────────────────────────────────────
// Grading is a direct comparison against the stored canonical spelling on the
// server — no LLM tokens, no TTS, no AI-credit deduction.

export interface RecallsSpellingCheckResponse {
  correct: boolean;
  /** The stored canonical spelling. Only meaningful after Check. */
  canonical: string;
  wrongAttemptCount: number;
  addedToMistakes: boolean;
  removedFromMistakes: boolean;
}

export interface RecallsSpellingMistakeItem {
  termId: string;
  term: string;
  category: string;
  definition: string;
  ipa: string | null;
  hasAudio: boolean;
  wrongAttemptCount: number;
  lastWrongAt: string;
}

export interface RecallsSpellingMistakesResponse {
  items: RecallsSpellingMistakeItem[];
  total: number;
}

/**
 * One word in a spelling test. Carries no canonical spelling on purpose — the
 * answer must not be in the payload before Check.
 */
export interface RecallsSpellingSetItem {
  termId: string;
  category: string;
  examFrequencyCount: number;
  fromMistakes: boolean;
}

export interface RecallsSpellingSetResponse {
  items: RecallsSpellingSetItem[];
  total: number;
  source: 'all' | 'favorites' | 'mistakes';
  size: string;
}

export type RecallsSpellingTestSize = '10' | '20' | '30' | 'all';
export type RecallsSpellingTestSource = 'all' | 'favorites' | 'mistakes';

/** Grade one typed answer. Incorrect answers are recorded against the learner. */
export async function checkRecallSpelling(termId: string, typed: string) {
  return apiRequest<RecallsSpellingCheckResponse>('/v1/recalls/spelling/check', {
    method: 'POST',
    body: JSON.stringify({ termId, typed }),
  });
}

/** The learner's persisted Review Mistakes list (survives logout / other devices). */
export async function fetchRecallSpellingMistakes() {
  return apiRequest<RecallsSpellingMistakesResponse>('/v1/recalls/spelling/mistakes');
}

/** Build a spelling-test word set. Answers are withheld until Check. */
export async function fetchRecallSpellingSet(
  size: RecallsSpellingTestSize = '10',
  source: RecallsSpellingTestSource = 'all',
) {
  const p = new URLSearchParams({ size, source });
  return apiRequest<RecallsSpellingSetResponse>(`/v1/recalls/spelling/set?${p.toString()}`);
}

export async function fetchRecallsLibrary(opts?: { bucket?: 'starred' | 'weak' | 'mastered' | 'new'; topic?: string }) {
  const p = new URLSearchParams();
  if (opts?.bucket) p.set('bucket', opts.bucket);
  if (opts?.topic) p.set('topic', opts.topic);
  const qs = p.toString();
  return apiRequest<{ items: RecallsLibraryItem[] }>(`/v1/recalls/library${qs ? `?${qs}` : ''}`);
}

export interface RecallsBulkUploadRow {
  term: string;
  definition: string;
  exampleSentence?: string;
  category?: string;
  difficulty?: string;
  ipa?: string;
  americanSpelling?: string;
  synonymsCsv?: string;
  examTypeCode?: string;
  professionId?: string;
}

export interface RecallsBulkUploadResult {
  inserted: number;
  updated: number;
  skipped: number;
  errors: string[];
}

export async function adminBulkUploadRecalls(rows: RecallsBulkUploadRow[]) {
  return apiRequest<RecallsBulkUploadResult>('/v1/admin/recalls/bulk-upload', {
    method: 'POST',
    body: JSON.stringify(rows),
  });
}

export interface RecallsWeeklyReport {
  practisedCount: number;
  masteredCount: number;
  spellingAccuracyPct: number;
  weakestTopic: string | null;
  mostCommonErrorCode: string | null;
  mostCommonErrorLabel: string | null;
  averageReviewsPerCard: number;
}

export async function fetchRecallsWeeklyReport() {
  return apiRequest<RecallsWeeklyReport>('/v1/recalls/report/week');
}

export interface RecallsRevisionPlanResponse {
  dueToday: number;
  mastered: number;
  readinessScore: number;
  headline: string;
  steps: string[];
  aiNarrative: string | null;
}

export async function fetchRecallsRevisionPlan() {
  return apiRequest<RecallsRevisionPlanResponse>('/v1/recalls/revision-plan');
}
