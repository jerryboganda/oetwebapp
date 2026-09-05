/**
 * Admin content hierarchy management (programs/tracks/modules/lessons,
 * inventory, bulk import, dedup, media assets, mock assembly) — extracted
 * from `lib/api.ts`. Re-exported there, so `@/lib/api` imports keep working.
 */
import { apiRequest } from './client';

export async function fetchAdminContentPrograms(params?: { type?: string; language?: string; status?: string; page?: number; pageSize?: number }) {
  const p = new URLSearchParams();
  if (params?.type) p.set('type', params.type);
  if (params?.language) p.set('language', params.language);
  if (params?.status) p.set('status', params.status);
  if (params?.page) p.set('page', String(params.page));
  if (params?.pageSize) p.set('pageSize', String(params.pageSize));
  const qs = p.toString();
  return apiRequest(`/v1/admin/programs${qs ? `?${qs}` : ''}`);
}

export async function fetchAdminContentPackages(params?: { type?: string; status?: string; page?: number; pageSize?: number }) {
  const p = new URLSearchParams();
  if (params?.type) p.set('type', params.type);
  if (params?.status) p.set('status', params.status);
  if (params?.page) p.set('page', String(params.page));
  if (params?.pageSize) p.set('pageSize', String(params.pageSize));
  const qs = p.toString();
  return apiRequest(`/v1/admin/packages${qs ? `?${qs}` : ''}`);
}

export async function fetchAdminProgram(programId: string) {
  return apiRequest(`/v1/admin/programs/${encodeURIComponent(programId)}`);
}

export async function createAdminProgram(payload: Record<string, unknown>) {
  return apiRequest('/v1/admin/programs', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function updateAdminProgram(programId: string, payload: Record<string, unknown>) {
  return apiRequest(`/v1/admin/programs/${encodeURIComponent(programId)}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function fetchAdminTracks(programId: string) {
  return apiRequest(`/v1/admin/programs/${encodeURIComponent(programId)}/tracks`);
}

export async function createAdminTrack(payload: Record<string, unknown>) {
  return apiRequest('/v1/admin/tracks', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function updateAdminTrack(trackId: string, payload: Record<string, unknown>) {
  return apiRequest(`/v1/admin/tracks/${encodeURIComponent(trackId)}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function fetchAdminModules(trackId: string) {
  return apiRequest(`/v1/admin/tracks/${encodeURIComponent(trackId)}/modules`);
}

export async function createAdminModule(payload: Record<string, unknown>) {
  return apiRequest('/v1/admin/modules', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function updateAdminModule(moduleId: string, payload: Record<string, unknown>) {
  return apiRequest(`/v1/admin/modules/${encodeURIComponent(moduleId)}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function fetchAdminLessons(moduleId: string) {
  return apiRequest(`/v1/admin/modules/${encodeURIComponent(moduleId)}/lessons`);
}

export async function createAdminLesson(payload: Record<string, unknown>) {
  return apiRequest('/v1/admin/lessons', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function updateAdminLesson(lessonId: string, payload: Record<string, unknown>) {
  return apiRequest(`/v1/admin/lessons/${encodeURIComponent(lessonId)}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function fetchAdminPackage(packageId: string) {
  return apiRequest(`/v1/admin/packages/${encodeURIComponent(packageId)}`);
}

export async function createAdminPackage(payload: Record<string, unknown>) {
  return apiRequest('/v1/admin/packages', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function updateAdminPackage(packageId: string, payload: Record<string, unknown>) {
  return apiRequest(`/v1/admin/packages/${encodeURIComponent(packageId)}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function fetchAdminContentInventory(params?: {
  subtest?: string; profession?: string; language?: string; provenance?: string;
  freshness?: string; qaStatus?: string; status?: string; packageId?: string;
  importBatchId?: string; search?: string; page?: number; pageSize?: number;
}) {
  const p = new URLSearchParams();
  if (params?.subtest) p.set('subtest', params.subtest);
  if (params?.profession) p.set('profession', params.profession);
  if (params?.language) p.set('language', params.language);
  if (params?.provenance) p.set('provenance', params.provenance);
  if (params?.freshness) p.set('freshness', params.freshness);
  if (params?.qaStatus) p.set('qaStatus', params.qaStatus);
  if (params?.status) p.set('status', params.status);
  if (params?.packageId) p.set('packageId', params.packageId);
  if (params?.importBatchId) p.set('importBatchId', params.importBatchId);
  if (params?.search) p.set('search', params.search);
  if (params?.page) p.set('page', String(params.page));
  if (params?.pageSize) p.set('pageSize', String(params.pageSize));
  const qs = p.toString();
  return apiRequest(`/v1/admin/content/inventory${qs ? `?${qs}` : ''}`);
}

export async function adminBulkImportContent(batchTitle: string, rows: unknown[]) {
  return apiRequest('/v1/admin/content/bulk-import', {
    method: 'POST',
    body: JSON.stringify({ batchTitle, rows }),
  });
}

export async function fetchAdminDedupGroups(page?: number, pageSize?: number) {
  const p = new URLSearchParams();
  if (page) p.set('page', String(page));
  if (pageSize) p.set('pageSize', String(pageSize));
  const qs = p.toString();
  return apiRequest(`/v1/admin/dedup/groups${qs ? `?${qs}` : ''}`);
}

export async function adminDedupScan() {
  return apiRequest('/v1/admin/dedup/scan', { method: 'POST' });
}

export async function adminDesignateCanonical(groupId: string, canonicalItemId: string) {
  return apiRequest(`/v1/admin/dedup/groups/${encodeURIComponent(groupId)}/designate-canonical`, {
    method: 'POST',
    body: JSON.stringify({ canonicalItemId }),
  });
}

export async function fetchAdminMediaAssets(params?: { mimeType?: string; status?: string; page?: number; pageSize?: number }) {
  const p = new URLSearchParams();
  if (params?.mimeType) p.set('mimeType', params.mimeType);
  if (params?.status) p.set('status', params.status);
  if (params?.page) p.set('page', String(params.page));
  if (params?.pageSize) p.set('pageSize', String(params.pageSize));
  const qs = p.toString();
  return apiRequest(`/v1/admin/media-assets${qs ? `?${qs}` : ''}`);
}

export async function adminProcessMediaAsset(assetId: string) {
  return apiRequest(`/v1/admin/media/${encodeURIComponent(assetId)}/process`, { method: 'POST' });
}

export async function fetchAdminMediaAudit() {
  return apiRequest('/v1/admin/media/audit');
}

export async function adminAssembleMockExam(professionId?: string, language?: string) {
  return apiRequest('/v1/admin/mock/assemble', {
    method: 'POST',
    body: JSON.stringify({ professionId, language }),
  });
}

export async function adminGenerateDiagnostic(professionId?: string) {
  return apiRequest('/v1/admin/diagnostic/generate', {
    method: 'POST',
    body: JSON.stringify({ professionId }),
  });
}

export async function adminUpdateContentEligibility(contentId: string, eligibility: { isMockEligible?: boolean; isDiagnosticEligible?: boolean }) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/eligibility`, {
    method: 'PATCH',
    body: JSON.stringify(eligibility),
  });
}
