/**
 * Admin alerts + content/taxonomy/signup/criteria/AI-config/flags/audit —
 * extracted from `lib/api.ts`. Re-exported there, so `@/lib/api`
 * imports keep working.
 */
import { ApiError, apiRequest, getHeaders, resolveApiUrl } from './client';
import { fetchWithTimeout } from '../network/fetch-with-timeout';

export async function fetchAdminAlerts() {
  return apiRequest('/v1/admin/alerts');
}

export async function fetchAdminContent(params?: { type?: string; subtest?: string; profession?: string; status?: string; search?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.type) qs.set('type', params.type);
  if (params?.subtest) qs.set('subtest', params.subtest);
  if (params?.profession) qs.set('profession', params.profession);
  if (params?.status) qs.set('status', params.status);
  if (params?.search) qs.set('search', params.search);
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  return apiRequest(`/v1/admin/content${q ? `?${q}` : ''}`);
}

export async function fetchAdminDashboard() {
  return apiRequest('/v1/admin/dashboard');
}

export async function fetchAdminContentDetail(contentId: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}`);
}

export async function createAdminContent(payload: { contentType: string; subtestCode: string; professionId: string; title: string; difficulty: string; estimatedDurationMinutes?: number; description?: string; caseNotes?: string; modelAnswer?: string; criteriaFocus?: string; sourceType?: string; qaStatus?: string }) {
  return apiRequest('/v1/admin/content', { method: 'POST', body: JSON.stringify(payload) });
}

export async function updateAdminContent(contentId: string, payload: Record<string, any>) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}`, { method: 'PUT', body: JSON.stringify(payload) });
}

export async function publishAdminContent(contentId: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/publish`, { method: 'POST' });
}

export async function archiveAdminContent(contentId: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/archive`, { method: 'POST' });
}

export async function fetchAdminContentRevisions(contentId: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/revisions`);
}

export async function restoreAdminContentRevision(contentId: string, revisionId: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/revisions/${encodeURIComponent(revisionId)}/restore`, { method: 'POST' });
}

export async function fetchAdminTaxonomy(params?: { type?: string; status?: string }) {
  const qs = new URLSearchParams();
  if (params?.type) qs.set('type', params.type);
  if (params?.status) qs.set('status', params.status);
  const q = qs.toString();
  return apiRequest(`/v1/admin/taxonomy${q ? `?${q}` : ''}`);
}

export async function createAdminTaxonomy(payload: { code: string; label: string }) {
  return apiRequest('/v1/admin/taxonomy', { method: 'POST', body: JSON.stringify(payload) });
}

export async function updateAdminTaxonomy(professionId: string, payload: { label?: string; code?: string; status?: string }) {
  return apiRequest(`/v1/admin/taxonomy/${encodeURIComponent(professionId)}`, { method: 'PUT', body: JSON.stringify(payload) });
}

export async function archiveAdminTaxonomy(professionId: string) {
  return apiRequest(`/v1/admin/taxonomy/${encodeURIComponent(professionId)}/archive`, { method: 'POST' });
}

/** Permanently deletes an archived profession/taxonomy node. system_admin only. */
export async function forceDeleteAdminTaxonomy(professionId: string) {
  return apiRequest(`/v1/admin/taxonomy/${encodeURIComponent(professionId)}/force-delete`, { method: 'POST' });
}

export interface AdminSignupExamTypePayload {
  id: string;
  code: string;
  label: string;
  description?: string;
  sortOrder?: number;
  isActive?: boolean;
}

export interface AdminSignupProfessionPayload {
  id: string;
  label: string;
  description?: string;
  examTypeIds?: string[];
  countryTargets?: string[];
  sortOrder?: number;
  isActive?: boolean;
}

export async function fetchAdminSignupCatalog() {
  return apiRequest('/v1/admin/signup-catalog');
}

export async function createAdminSignupExamType(payload: AdminSignupExamTypePayload) {
  return apiRequest('/v1/admin/signup-catalog/exam-types', { method: 'POST', body: JSON.stringify(payload) });
}

export async function updateAdminSignupExamType(id: string, payload: AdminSignupExamTypePayload) {
  return apiRequest(`/v1/admin/signup-catalog/exam-types/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(payload) });
}

export async function archiveAdminSignupExamType(id: string) {
  return apiRequest(`/v1/admin/signup-catalog/exam-types/${encodeURIComponent(id)}/archive`, { method: 'POST' });
}

/** Permanently deletes a signup exam-type catalog row. system_admin only. */
export async function forceDeleteAdminSignupExamType(id: string) {
  return apiRequest(`/v1/admin/signup-catalog/exam-types/${encodeURIComponent(id)}/force-delete`, { method: 'POST' });
}

export async function activateAdminSignupExamType(id: string) {
  return apiRequest(`/v1/admin/signup-catalog/exam-types/${encodeURIComponent(id)}/activate`, { method: 'POST' });
}

export async function createAdminSignupProfession(payload: AdminSignupProfessionPayload) {
  return apiRequest('/v1/admin/signup-catalog/professions', { method: 'POST', body: JSON.stringify(payload) });
}

export async function updateAdminSignupProfession(id: string, payload: AdminSignupProfessionPayload) {
  return apiRequest(`/v1/admin/signup-catalog/professions/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(payload) });
}

export async function archiveAdminSignupProfession(id: string) {
  return apiRequest(`/v1/admin/signup-catalog/professions/${encodeURIComponent(id)}/archive`, { method: 'POST' });
}

/** Permanently deletes a signup profession catalog row. system_admin only. */
export async function forceDeleteAdminSignupProfession(id: string) {
  return apiRequest(`/v1/admin/signup-catalog/professions/${encodeURIComponent(id)}/force-delete`, { method: 'POST' });
}

export async function activateAdminSignupProfession(id: string) {
  return apiRequest(`/v1/admin/signup-catalog/professions/${encodeURIComponent(id)}/activate`, { method: 'POST' });
}

export async function fetchAdminCriteria(params?: { subtest?: string; status?: string }) {
  const qs = new URLSearchParams();
  if (params?.subtest) qs.set('subtest', params.subtest);
  if (params?.status) qs.set('status', params.status);
  const q = qs.toString();
  return apiRequest(`/v1/admin/criteria${q ? `?${q}` : ''}`);
}

export async function createAdminCriterion(payload: { name: string; subtestCode: string; description?: string; weight?: number }) {
  return apiRequest('/v1/admin/criteria', { method: 'POST', body: JSON.stringify(payload) });
}

export async function updateAdminCriterion(criterionId: string, payload: { name?: string; description?: string; weight?: number; status?: string }) {
  return apiRequest(`/v1/admin/criteria/${encodeURIComponent(criterionId)}`, { method: 'PUT', body: JSON.stringify(payload) });
}

export async function fetchAdminAIConfig(params?: { status?: string }) {
  const qs = params?.status ? `?status=${encodeURIComponent(params.status)}` : '';
  return apiRequest(`/v1/admin/ai-config${qs}`);
}

export async function createAdminAIConfig(payload: { model: string; provider: string; taskType: string; status?: string; accuracy: number; confidenceThreshold: number; routingRule?: string; experimentFlag?: string; promptLabel?: string }) {
  return apiRequest('/v1/admin/ai-config', { method: 'POST', body: JSON.stringify(payload) });
}

export async function updateAdminAIConfig(configId: string, payload: Record<string, any>) {
  return apiRequest(`/v1/admin/ai-config/${encodeURIComponent(configId)}`, { method: 'PUT', body: JSON.stringify(payload) });
}

export async function fetchAdminFlags(params?: { type?: string }) {
  const qs = params?.type ? `?type=${encodeURIComponent(params.type)}` : '';
  return apiRequest(`/v1/admin/flags${qs}`);
}

export async function createAdminFlag(payload: { name: string; key: string; enabled: boolean; flagType?: string; rolloutPercentage?: number; description?: string; owner?: string }) {
  return apiRequest('/v1/admin/flags', { method: 'POST', body: JSON.stringify(payload) });
}

export async function updateAdminFlag(flagId: string, payload: Record<string, any>) {
  return apiRequest(`/v1/admin/flags/${encodeURIComponent(flagId)}`, { method: 'PUT', body: JSON.stringify(payload) });
}

export async function fetchAdminAuditLogs(params?: { action?: string; actor?: string; search?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.action) qs.set('action', params.action);
  if (params?.actor) qs.set('actor', params.actor);
  if (params?.search) qs.set('search', params.search);
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  return apiRequest(`/v1/admin/audit-logs${q ? `?${q}` : ''}`);
}

export async function exportAdminAuditLogs(params?: { action?: string; actor?: string; search?: string }) {
  const qs = new URLSearchParams();
  if (params?.action) qs.set('action', params.action);
  if (params?.actor) qs.set('actor', params.actor);
  if (params?.search) qs.set('search', params.search);

  const path = `/v1/admin/audit-logs/export${qs.toString() ? `?${qs.toString()}` : ''}`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    method: 'GET',
    headers: await getHeaders(path, undefined, { json: false }),
  });

  if (!response.ok) {
    throw new ApiError(response.status, 'audit_export_failed', 'Failed to export audit logs.', response.status >= 500);
  }

  return {
    blob: await response.blob(),
    fileName: response.headers.get('content-disposition')?.match(/filename="?([^"]+)"?/)?.[1] ?? 'audit-logs.csv',
  };
}
