/**
 * Listening authoring workspace — extracted from `lib/api.ts`.
 * Re-exported there, so `@/lib/api` imports keep working.
 *
 * Typed wrappers for the Listening authoring workspace.
 * Mirrors backend routes in:
 *   - Endpoints/ListeningAuthoringAdminEndpoints.cs
 *   - Endpoints/ListeningAdminAnalyticsEndpoints.cs
 * Canonical paper shape: A1=12, A2=12, B=6, C1=6, C2=6 → 42 items.
 */
import { apiRequest } from './client';
import type {
  ListeningAuthoredExtract,
  ListeningAuthoredQuestion,
  ListeningAuthoredQuestionList,
  ListeningBackfillAllResponse,
  ListeningExtractPatch,
  ListeningExtractsResponse,
  ListeningQuestionPatch,
  ListeningValidationReport,
} from '../types/admin/listening-authoring';

const lap = (paperId: string) =>
  `/v1/admin/papers/${encodeURIComponent(paperId)}/listening`;

export async function adminListeningValidate(paperId: string) {
  return apiRequest<ListeningValidationReport>(`${lap(paperId)}/validate`);
}

export async function adminListeningGetStructure(paperId: string) {
  return apiRequest<ListeningAuthoredQuestionList>(`${lap(paperId)}/structure`);
}

export async function adminListeningReplaceStructure(
  paperId: string,
  questions: ListeningAuthoredQuestion[],
) {
  return apiRequest<ListeningAuthoredQuestionList>(`${lap(paperId)}/structure`, {
    method: 'PUT',
    body: JSON.stringify({ questions }),
  });
}

export async function adminListeningPatchQuestion(
  paperId: string,
  questionId: string,
  patch: ListeningQuestionPatch,
) {
  return apiRequest<ListeningAuthoredQuestionList>(
    `${lap(paperId)}/structure/${encodeURIComponent(questionId)}`,
    { method: 'PATCH', body: JSON.stringify(patch) },
  );
}

export async function adminListeningGetExtracts(paperId: string) {
  return apiRequest<ListeningExtractsResponse>(`${lap(paperId)}/extracts`);
}

export async function adminListeningReplaceExtracts(
  paperId: string,
  extracts: ListeningAuthoredExtract[],
) {
  return apiRequest<ListeningExtractsResponse>(`${lap(paperId)}/extracts`, {
    method: 'PUT',
    body: JSON.stringify({ extracts }),
  });
}

export async function adminListeningPatchExtract(
  paperId: string,
  extractCode: string,
  patch: ListeningExtractPatch,
) {
  return apiRequest<ListeningExtractsResponse>(
    `${lap(paperId)}/extracts/${encodeURIComponent(extractCode)}`,
    { method: 'PATCH', body: JSON.stringify(patch) },
  );
}

export async function adminListeningBackfillAll() {
  return apiRequest<ListeningBackfillAllResponse>(`/v1/admin/listening/backfill`, {
    method: 'POST',
  });
}

export async function adminListeningGetAnalytics(days?: number) {
  const qs = typeof days === 'number' ? `?days=${days}` : '';
  return apiRequest<unknown>(`/v1/admin/listening/analytics${qs}`);
}

export async function adminListeningExportAttempt(attemptId: string) {
  return apiRequest<unknown>(
    `/v1/admin/listening/attempts/${encodeURIComponent(attemptId)}/export`,
  );
}
