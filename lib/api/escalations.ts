/**
 * Disputes + score guarantee: review/learner escalations, score guarantee,
 * equivalences, study commitment — extracted from `lib/api.ts`.
 * Re-exported there, so `@/lib/api` imports keep working.
 */
import { apiRequest } from './client';

export async function fetchReviewEscalations(params?: { status?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 20));
  return apiRequest(`/v1/admin/escalations?${qs}`);
}

export async function assignEscalationReviewer(escalationId: string, secondReviewerId: string) {
  return apiRequest(`/v1/admin/escalations/${encodeURIComponent(escalationId)}/assign`, {
    method: 'POST',
    body: JSON.stringify({ secondReviewerId }),
  });
}

export async function resolveEscalation(escalationId: string, finalScore: number, resolutionNote?: string) {
  return apiRequest(`/v1/admin/escalations/${encodeURIComponent(escalationId)}/resolve`, {
    method: 'POST',
    body: JSON.stringify({ finalScore, resolutionNote }),
  });
}

export async function submitEscalation(submissionId: string, reason: string, details: string) {
  return apiRequest('/v1/learner/escalations', {
    method: 'POST',
    body: JSON.stringify({ submissionId, reason, details }),
  });
}

export async function fetchMyEscalations() {
  const res = await apiRequest('/v1/learner/escalations');
  return res?.items ?? res;
}

export async function fetchEscalationDetails(id: string) {
  return apiRequest(`/v1/learner/escalations/${encodeURIComponent(id)}`);
}

export async function fetchScoreGuarantee() {
  return apiRequest('/v1/learner/score-guarantee');
}

export async function activateScoreGuarantee(baselineScore: number) {
  return apiRequest('/v1/learner/score-guarantee/activate', {
    method: 'POST',
    body: JSON.stringify({ baselineScore }),
  });
}

export async function submitScoreGuaranteeClaim(actualScore: number, proofDocumentUrl?: string, note?: string) {
  return apiRequest('/v1/learner/score-guarantee/claim', {
    method: 'POST',
    body: JSON.stringify({ actualScore, proofDocumentUrl, note }),
  });
}

export async function fetchScoreEquivalences() {
  return apiRequest('/v1/reference/score-equivalences');
}

export async function fetchStudyCommitment() {
  return apiRequest('/v1/learner/study-commitment');
}

export async function setStudyCommitment(dailyMinutes: number) {
  return apiRequest('/v1/learner/study-commitment', {
    method: 'POST',
    body: JSON.stringify({ dailyMinutes }),
  });
}
