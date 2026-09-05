/**
 * Expert operations: annotation templates, amend review, rework chain,
 * bulk queue ops, messaging, compensation, score-guarantee claims —
 * extracted from `lib/api.ts`. Re-exported there, so `@/lib/api`
 * imports keep working.
 */
import { apiRequest } from './client';

export async function fetchAnnotationTemplates(params?: { subtestCode?: string; criterionCode?: string; search?: string }) {
  const qs = new URLSearchParams();
  if (params?.subtestCode) qs.set('subtestCode', params.subtestCode);
  if (params?.criterionCode) qs.set('criterionCode', params.criterionCode);
  if (params?.search) qs.set('search', params.search);
  const q = qs.toString();
  return apiRequest(`/v1/expert/annotation-templates${q ? `?${q}` : ''}`);
}

export async function createAnnotationTemplate(payload: {
  subtestCode: string; criterionCode: string; label: string; templateText: string; isShared: boolean;
}) {
  return apiRequest('/v1/expert/annotation-templates', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function updateAnnotationTemplate(templateId: string, payload: {
  subtestCode: string; criterionCode: string; label: string; templateText: string; isShared: boolean;
}) {
  return apiRequest(`/v1/expert/annotation-templates/${encodeURIComponent(templateId)}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function deleteAnnotationTemplate(templateId: string) {
  return apiRequest(`/v1/expert/annotation-templates/${encodeURIComponent(templateId)}`, {
    method: 'DELETE',
  });
}

export async function fetchAmendEligibility(reviewRequestId: string) {
  return apiRequest(`/v1/expert/reviews/${encodeURIComponent(reviewRequestId)}/amend-eligibility`);
}

export async function amendReview(reviewRequestId: string, payload: {
  scores: Record<string, number>;
  criterionComments: Record<string, string>;
  finalComment: string;
}) {
  return apiRequest(`/v1/expert/reviews/${encodeURIComponent(reviewRequestId)}/amend`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function fetchReworkChain(reviewRequestId: string) {
  return apiRequest(`/v1/expert/reviews/${encodeURIComponent(reviewRequestId)}/rework-chain`);
}

export async function bulkClaimReviews(reviewRequestIds: string[]) {
  return apiRequest('/v1/expert/queue/bulk-claim', {
    method: 'POST',
    body: JSON.stringify({ reviewRequestIds }),
  });
}

export async function bulkReleaseReviews(reviewRequestIds: string[]) {
  return apiRequest('/v1/expert/queue/bulk-release', {
    method: 'POST',
    body: JSON.stringify({ reviewRequestIds }),
  });
}

export async function fetchExpertMessageThreads() {
  return apiRequest('/v1/expert/messages');
}

export async function createExpertMessageThread(payload: {
  title: string; body: string; linkedReviewRequestId?: string;
  linkedCalibrationCaseId?: string; linkedLearnerId?: string;
}) {
  return apiRequest('/v1/expert/messages', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function fetchExpertMessageThread(threadId: string) {
  return apiRequest(`/v1/expert/messages/${encodeURIComponent(threadId)}`);
}

export async function postExpertMessageReply(threadId: string, body: string) {
  return apiRequest(`/v1/expert/messages/${encodeURIComponent(threadId)}/replies`, {
    method: 'POST',
    body: JSON.stringify({ body }),
  });
}

export async function fetchExpertCompensationSummary() {
  return apiRequest('/v1/expert/compensation');
}

export async function fetchExpertEarningsHistory(page?: number, pageSize?: number) {
  const qs = new URLSearchParams();
  if (page) qs.set('page', String(page));
  if (pageSize) qs.set('pageSize', String(pageSize));
  const q = qs.toString();
  return apiRequest(`/v1/expert/compensation/earnings${q ? `?${q}` : ''}`);
}

export async function fetchExpertPayouts() {
  return apiRequest('/v1/expert/compensation/payouts');
}

export async function fetchAdminScoreGuaranteeClaims(params?: { status?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 20));
  return apiRequest(`/v1/admin/score-guarantee-claims?${qs}`);
}

export async function reviewScoreGuaranteeClaim(pledgeId: string, decision: 'approve' | 'reject', note?: string) {
  return apiRequest(`/v1/admin/score-guarantee-claims/${encodeURIComponent(pledgeId)}/review`, {
    method: 'POST',
    body: JSON.stringify({ decision, note }),
  });
}
