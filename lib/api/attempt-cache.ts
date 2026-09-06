import { ApiError, apiRequest, type ApiRecord } from './client';

/**
 * Attempt/evaluation localStorage cache + ensureAttempt + review-target resolution.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
function isBrowser() {
  return typeof window !== 'undefined';
}

export function cacheGet(key: string): string | null {
  if (!isBrowser()) return null;
  return window.localStorage.getItem(key);
}

export function cacheSet(key: string, value: string) {
  if (!isBrowser()) return;
  window.localStorage.setItem(key, value);
}

export function cacheRemove(key: string) {
  if (!isBrowser()) return;
  window.localStorage.removeItem(key);
}

export function attemptCacheKey(subtest: string, contentId: string, mode = 'default') {
  return `oet.${subtest}.attempt.${mode}.${contentId}`;
}

export function evaluationCacheKey(subtest: string, contentId: string) {
  return `oet.${subtest}.evaluation.${contentId}`;
}

function isReusableAttemptState(state: unknown) {
  return state === 'not_started' || state === 'in_progress' || state === 'paused';
}

export async function ensureAttempt(subtest: 'writing' | 'speaking' | 'reading' | 'listening', contentId: string, mode: string) {
  const key = attemptCacheKey(subtest, contentId, mode);
  const cached = cacheGet(key);

  if (cached) {
    try {
      const existing = await apiRequest<ApiRecord>(`/v1/${subtest}/attempts/${cached}`);
      if (isReusableAttemptState(existing.state)) {
        return existing;
      }
      cacheRemove(key);
    } catch (err) {
      if (err instanceof ApiError && err.status >= 500) {
        console.error('[API] ensureAttempt: server error checking existing attempt:', err);
      } else {
        console.error('[API] ensureAttempt: failed to verify existing attempt:', err);
      }
      cacheRemove(key);
    }
  }

  const context = mode === 'diagnostic' ? 'diagnostic' : mode === 'exam' ? 'exam' : 'practice';
  const attemptMode = mode === 'diagnostic' ? 'exam' : mode;
  const created = await apiRequest<ApiRecord>(`/v1/${subtest}/attempts`, {
    method: 'POST',
    body: JSON.stringify({ contentId, context, mode: attemptMode, deviceType: 'web', parentAttemptId: null }),
  });
  cacheSet(key, created.attemptId);
  if (subtest === 'writing' || subtest === 'speaking') {
    // Graded activities consume credits at submit/card-reveal, not here.
  } else {
    void import('@/lib/credit-feedback').then((m) => m.announceCreditUsage(subtest));
  }
  return created;
}


export async function latestEvaluationIdForContent(contentId: string, subtest: string): Promise<string | null> {
  const cached = cacheGet(evaluationCacheKey(subtest, contentId));
  if (cached) return cached;

  const submissions = await apiRequest<{ items: ApiRecord[] }>('/v1/submissions');
  const match = submissions.items.find((item) => item.contentId === contentId && String(item.subtest).toLowerCase() === subtest.toLowerCase());
  if (match?.evaluationId) {
    cacheSet(evaluationCacheKey(subtest, contentId), match.evaluationId);
    return match.evaluationId;
  }
  return null;
}

export async function resolveReviewTarget(submissionId: string): Promise<{ attemptId: string; subtest: 'writing' | 'speaking' }> {
  if (submissionId.startsWith('we-')) {
    const summary = await apiRequest<ApiRecord>(`/v1/writing/evaluations/${submissionId}/summary`);
    return { attemptId: summary.attemptId, subtest: 'writing' };
  }

  if (submissionId.startsWith('se-')) {
    const summary = await apiRequest<ApiRecord>(`/v1/speaking/evaluations/${submissionId}/summary`);
    return { attemptId: summary.attemptId, subtest: 'speaking' };
  }

  if (submissionId.startsWith('wa-') || submissionId.startsWith('ws-')) {
    return { attemptId: submissionId.replace(/^ws-/, 'wa-'), subtest: 'writing' };
  }

  if (submissionId.startsWith('sa-') || submissionId.startsWith('sr-')) {
    return { attemptId: submissionId.replace(/^sr-/, 'sa-'), subtest: 'speaking' };
  }

  const submissions = await apiRequest<{ items: ApiRecord[] }>('/v1/submissions');
  const match = submissions.items.find((item) => item.submissionId === submissionId || item.evaluationId === submissionId);
  if (match) {
    return { attemptId: match.submissionId, subtest: String(match.subtest).toLowerCase() === 'speaking' ? 'speaking' : 'writing' };
  }

  throw new ApiError(404, 'not_found', 'Submission not found.', false);
}
