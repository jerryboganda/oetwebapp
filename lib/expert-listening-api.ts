import { apiClient } from './api';
import type {
  ListeningExpertAttemptSummary,
  ListeningExpertBundle,
  ListeningExpertMyReviewSummary,
  SubmitListeningFeedbackRequest,
} from './types/expert';

// WORK-STREAM 7a — re-export the per-answer review types (incl. the new
// distractor-category / speaker-attitude / option-analysis fields) so callers
// of this client module can import them from a single surface.
export type {
  ListeningExpertAnswerItem,
  ListeningExpertMyReviewSummary,
  ListeningExpertOptionAnalysisItem,
} from './types/expert';

export async function getListeningExpertAttempts(params?: {
  page?: number;
  /** Name-or-id match applied server-side to learner display name and id. */
  search?: string;
  paperId?: string;
}): Promise<{ items: ListeningExpertAttemptSummary[]; total: number }> {
  const qs = new URLSearchParams();
  if (params?.page != null) qs.set('page', String(params.page));
  if (params?.search) qs.set('search', params.search);
  if (params?.paperId) qs.set('paperId', params.paperId);
  const query = qs.toString() ? `?${qs.toString()}` : '';
  return apiClient.get<{ items: ListeningExpertAttemptSummary[]; total: number }>(
    `/v1/expert/listening/attempts${query}`,
  );
}

export async function getListeningExpertMyReviews(params?: {
  page?: number;
  pageSize?: number;
}): Promise<{ items: ListeningExpertMyReviewSummary[]; total: number; page: number; pageSize: number }> {
  const qs = new URLSearchParams();
  if (params?.page != null) qs.set('page', String(params.page));
  if (params?.pageSize != null) qs.set('pageSize', String(params.pageSize));
  const query = qs.toString() ? `?${qs.toString()}` : '';
  return apiClient.get<{ items: ListeningExpertMyReviewSummary[]; total: number; page: number; pageSize: number }>(
    `/v1/expert/listening/my-reviews${query}`,
  );
}

export async function getListeningExpertBundle(
  attemptId: string,
): Promise<ListeningExpertBundle> {
  return apiClient.get<ListeningExpertBundle>(
    `/v1/expert/listening/attempts/${encodeURIComponent(attemptId)}/bundle`,
  );
}

export async function getListeningExpertFeedback(
  attemptId: string,
): Promise<ListeningExpertBundle['existingFeedback'] | null> {
  try {
    return await apiClient.get<ListeningExpertBundle['existingFeedback']>(
      `/v1/expert/listening/attempts/${encodeURIComponent(attemptId)}/feedback`,
    );
  } catch {
    return null;
  }
}

export async function submitListeningExpertFeedback(
  attemptId: string,
  req: SubmitListeningFeedbackRequest,
): Promise<void> {
  return apiClient.post<void>(
    `/v1/expert/listening/attempts/${encodeURIComponent(attemptId)}/feedback`,
    req,
  );
}
