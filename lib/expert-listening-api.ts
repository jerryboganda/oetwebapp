import { apiClient } from './api';
import type {
  ListeningExpertAttemptSummary,
  ListeningExpertBundle,
  SubmitListeningFeedbackRequest,
} from './types/expert';

// WORK-STREAM 7a — re-export the per-answer review types (incl. the new
// distractor-category / speaker-attitude / option-analysis fields) so callers
// of this client module can import them from a single surface.
export type {
  ListeningExpertAnswerItem,
  ListeningExpertOptionAnalysisItem,
} from './types/expert';

export async function getListeningExpertAttempts(params?: {
  page?: number;
  learnerId?: string;
  paperId?: string;
}): Promise<{ items: ListeningExpertAttemptSummary[]; total: number }> {
  const qs = new URLSearchParams();
  if (params?.page != null) qs.set('page', String(params.page));
  if (params?.learnerId) qs.set('learnerId', params.learnerId);
  if (params?.paperId) qs.set('paperId', params.paperId);
  const query = qs.toString() ? `?${qs.toString()}` : '';
  return apiClient.get<{ items: ListeningExpertAttemptSummary[]; total: number }>(
    `/v1/expert/listening/attempts${query}`,
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
