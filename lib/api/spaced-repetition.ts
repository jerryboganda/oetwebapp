/**
 * Spaced Repetition review items — extracted from `lib/api.ts`.
 * Re-exported there, so `@/lib/api` imports keep working.
 *
 * NOTE: `submitReview` here is the spaced-repetition item grader, not
 * `submitReviewRequest` (writing expert-review requests) which stays in
 * `lib/api.ts`.
 */
import { apiRequest } from './client';

export async function fetchReviewSummary() {
  return apiRequest('/v1/review/summary');
}

export async function fetchDueReviewItems(limit = 20) {
  return apiRequest(`/v1/review/due?limit=${limit}`);
}

export async function createReviewItem(payload: {
  examTypeCode: string;
  sourceType: string;
  sourceId: string;
  subtestCode?: string;
  criterionCode?: string;
  questionJson: string;
  answerJson: string;
}) {
  return apiRequest('/v1/review/items', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function submitReview(itemId: string, quality: number) {
  return apiRequest(`/v1/review/items/${encodeURIComponent(itemId)}/submit`, {
    method: 'POST',
    body: JSON.stringify({ quality }),
  });
}

export async function deleteReviewItem(itemId: string) {
  return apiRequest(`/v1/review/items/${encodeURIComponent(itemId)}`, { method: 'DELETE' });
}
