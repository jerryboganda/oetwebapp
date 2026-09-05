/**
 * Content studio: writing coach checks, AI content generation jobs,
 * contributor marketplace — extracted from `lib/api.ts`. Re-exported
 * there, so `@/lib/api` imports keep working.
 */
import { apiRequest } from './client';

export async function coachCheckText(attemptId: string, currentText: string, cursorPosition?: number) {
  return apiRequest(`/v1/writing/attempts/${encodeURIComponent(attemptId)}/coach-check`, {
    method: 'POST',
    body: JSON.stringify({ currentText, cursorPosition }),
  });
}

export async function resolveCoachSuggestion(suggestionId: string, resolution: 'accepted' | 'dismissed') {
  return apiRequest(`/v1/writing/coach-suggestions/${encodeURIComponent(suggestionId)}/resolve`, {
    method: 'POST',
    body: JSON.stringify({ resolution }),
  });
}

export async function fetchCoachStats(attemptId: string) {
  return apiRequest(`/v1/writing/attempts/${encodeURIComponent(attemptId)}/coach-stats`);
}

export async function queueContentGeneration(params: {
  examTypeCode: string;
  subtestCode: string;
  taskTypeId?: string;
  professionId?: string;
  difficulty?: string;
  count: number;
  customInstructions?: string;
}) {
  return apiRequest('/v1/admin/content/generate', {
    method: 'POST',
    body: JSON.stringify(params),
  });
}

export async function fetchContentGenerationJobs(page = 1, pageSize = 20) {
  return apiRequest(`/v1/admin/content/generation-jobs?page=${page}&pageSize=${pageSize}`);
}

export async function fetchContentGenerationJob(jobId: string) {
  return apiRequest(`/v1/admin/content/generation-jobs/${encodeURIComponent(jobId)}`);
}

export async function fetchMarketplaceProfile() {
  return apiRequest('/v1/marketplace/profile');
}

export async function updateMarketplaceProfile(data: { displayName?: string; bio?: string }) {
  // FE-026: backend registers PATCH /v1/marketplace/profile (not PUT) → PUT 405s.
  return apiRequest('/v1/marketplace/profile', {
    method: 'PATCH',
    body: JSON.stringify(data),
  });
}

export async function createMarketplaceSubmission(data: {
  examFamilyCode?: string;
  subtestCode: string;
  title: string;
  description?: string;
  contentPayloadJson?: string;
  contentType?: string;
  professionId?: string;
  difficulty?: string;
  tags?: string;
}) {
  return apiRequest('/v1/marketplace/submissions', {
    method: 'POST',
    body: JSON.stringify(data),
  });
}

export async function fetchMyMarketplaceSubmissions(page = 1, pageSize = 20) {
  return apiRequest(`/v1/marketplace/submissions?page=${page}&pageSize=${pageSize}`);
}

export async function fetchMarketplaceSubmission(submissionId: string) {
  return apiRequest(`/v1/marketplace/submissions/${encodeURIComponent(submissionId)}`);
}

export async function browseMarketplace(params?: { examTypeCode?: string; subtest?: string; search?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.examTypeCode) qs.set('examTypeCode', params.examTypeCode);
  if (params?.subtest) qs.set('subtest', params.subtest);
  if (params?.search) qs.set('search', params.search);
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 20));
  return apiRequest(`/v1/marketplace/browse?${qs}`);
}

export async function reviewMarketplaceSubmission(submissionId: string, data: { decision: 'approved' | 'rejected'; notes?: string; createContentItem?: boolean }) {
  return apiRequest(`/v1/admin/marketplace/submissions/${encodeURIComponent(submissionId)}/review`, {
    method: 'POST',
    body: JSON.stringify(data),
  });
}

export async function fetchPendingMarketplaceSubmissions(page = 1, pageSize = 20) {
  return apiRequest(`/v1/admin/marketplace/pending?page=${page}&pageSize=${pageSize}`);
}
