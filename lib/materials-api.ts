/**
 * Materials library API — admin CRUD (folders, files, audience) and
 * learner read (visible tree).
 *
 * Uses the same auth/CSRF/fetch infrastructure as lib/api.ts but lives in
 * its own module to keep lib/api.ts from growing further.
 */

import { apiClient } from './api';

// ── Types ─────────────────────────────────────────────────────────────────────

export type MaterialAudienceMode = 'Inherit' | 'Everyone' | 'Restricted';
export type MaterialStatus = 'Draft' | 'Published' | 'Archived';
export type MaterialScopeKind = 'shared' | 'profession' | 'general_english';

export interface AudienceRow {
  id?: string;
  targetType: 'plan' | 'cohort' | 'institution';
  targetId: string;
}

export interface MaterialFolderDto {
  id: string;
  parentFolderId: string | null;
  name: string;
  description?: string | null;
  subtestCode?: string | null;
  scopeKind?: MaterialScopeKind | null;
  professionId?: string | null;
  audienceMode: MaterialAudienceMode;
  sortOrder: number;
  status: MaterialStatus;
  createdBy?: string | null;
  createdAt: string;
  updatedAt: string;
  audiences?: AudienceRow[];
  folders?: MaterialFolderDto[];
  files?: MaterialFileDto[];
}

export interface MaterialFileDto {
  id: string;
  folderId?: string | null;
  mediaAssetId: string;
  subtestCode: string;
  kind: 'pdf' | 'audio' | 'video' | 'image' | 'document';
  title: string;
  description?: string | null;
  sortOrder: number;
  status: MaterialStatus;
  createdBy?: string | null;
  createdAt: string;
  updatedAt: string;
  media?: {
    id: string;
    originalFilename: string;
    mimeType: string;
    sizeBytes: number;
    format: string;
  } | null;
}

export interface AudienceOptions {
  plans: { id: string; code: string; name: string }[];
  cohorts: { id: string; name: string; sponsorId: string }[];
  institutions: { id: string; name: string }[];
}

export interface LearnerMaterialFileDto {
  id: string;
  title: string;
  description?: string | null;
  subtestCode: string;
  kind: 'pdf' | 'audio' | 'video' | 'image' | 'document';
  sortOrder: number;
  mediaAssetId: string;
  downloadUrl: string;
  sizeBytes?: number | null;
  originalFilename?: string | null;
}

export interface LearnerMaterialFolderDto {
  id: string;
  name: string;
  description?: string | null;
  subtestCode?: string | null;
  sortOrder: number;
  folders: LearnerMaterialFolderDto[];
  files: LearnerMaterialFileDto[];
}

export interface LearnerMaterialsTreeDto {
  folders: LearnerMaterialFolderDto[];
}

export interface MaterialCourseMapItem { canonicalFileId: string; folderId: string; title: string; kind: string; status: MaterialStatus }
export interface MaterialCourseMapFolder { canonicalFolderId: string; name: string; status: MaterialStatus; parentFolderId?: string | null }
export interface MaterialCourseMapSection {
  subtestCode: string; sharing: 'shared' | 'profession'; folderCount: number; fileCount: number;
  folders: MaterialCourseMapFolder[]; files: MaterialCourseMapItem[];
}
export interface MaterialCourseMap {
  professions: { id: string; label: string; sections: MaterialCourseMapSection[] }[];
  generalEnglish: { id: string; label: string; folderCount: number; fileCount: number; folders: MaterialCourseMapFolder[]; files: MaterialCourseMapItem[] };
}

// ── Fetch helper ────────────────────────────────────────────────────────────
//
// Delegates to the shared API client (lib/api.ts) so every call inherits its
// auth (Bearer), CSRF header, credentials, timeout, retry-on-5xx/408/429, and
// normalized `ApiError` (carries status + code + retryable, detectable via
// `isApiError`). The local call sites keep passing `JSON.stringify(...)` string
// bodies, which the shared client forwards verbatim with a JSON Content-Type.

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  return apiClient.request<T>(path, init);
}

// ── Learner API ───────────────────────────────────────────────────────────────

export function fetchMaterialsTree(): Promise<LearnerMaterialsTreeDto> {
  return request<LearnerMaterialsTreeDto>('/v1/materials');
}

// ── Admin API ─────────────────────────────────────────────────────────────────

export function adminGetAudienceOptions(): Promise<AudienceOptions> {
  return request<AudienceOptions>('/v1/admin/materials/audience-options');
}

export function adminListMaterialFolders(): Promise<MaterialFolderDto[]> {
  return request<MaterialFolderDto[]>('/v1/admin/materials/folders');
}

export function adminGetMaterialCourseMap(): Promise<MaterialCourseMap> {
  return request<MaterialCourseMap>('/v1/admin/materials/course-map');
}

export function adminCreateMaterialFolder(body: {
  parentFolderId?: string | null;
  name: string;
  description?: string | null;
  subtestCode?: string | null;
  scopeKind?: MaterialScopeKind | null;
  professionId?: string | null;
  audienceMode?: MaterialAudienceMode;
  sortOrder?: number;
}): Promise<MaterialFolderDto> {
  return request<MaterialFolderDto>('/v1/admin/materials/folders', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export function adminUpdateMaterialFolder(
  id: string,
  body: {
    name?: string;
    description?: string | null;
    subtestCode?: string | null;
    scopeKind?: MaterialScopeKind | null;
    professionId?: string | null;
    audienceMode?: MaterialAudienceMode;
    sortOrder?: number;
    status?: MaterialStatus;
  },
): Promise<MaterialFolderDto> {
  return request<MaterialFolderDto>(`/v1/admin/materials/folders/${encodeURIComponent(id)}`, {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

export function adminMoveMaterialFolder(
  id: string,
  body: { parentFolderId: string | null; sortOrder?: number },
): Promise<MaterialFolderDto> {
  return request<MaterialFolderDto>(`/v1/admin/materials/folders/${encodeURIComponent(id)}/move`, {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export async function adminDeleteMaterialFolder(id: string, recursive = false): Promise<void> {
  await request<unknown>(
    `/v1/admin/materials/folders/${encodeURIComponent(id)}?recursive=${recursive}`,
    { method: 'DELETE' },
  );
}

export async function adminSetFolderAudience(
  id: string,
  body: { audienceMode: MaterialAudienceMode; audiences?: { targetType: string; targetId: string }[] },
): Promise<void> {
  await request<unknown>(`/v1/admin/materials/folders/${encodeURIComponent(id)}/audience`, {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

export function adminListMaterialFiles(params?: {
  folderId?: string;
  subtest?: string;
  status?: string;
  page?: number;
  pageSize?: number;
}): Promise<{ items: MaterialFileDto[]; total: number; page: number; pageSize: number }> {
  const q = new URLSearchParams();
  if (params?.folderId) q.set('folderId', params.folderId);
  if (params?.subtest) q.set('subtest', params.subtest);
  if (params?.status) q.set('status', params.status);
  if (params?.page) q.set('page', String(params.page));
  if (params?.pageSize) q.set('pageSize', String(params.pageSize));
  const qs = q.toString();
  return request(`/v1/admin/materials/files${qs ? `?${qs}` : ''}`);
}

export function adminCreateMaterialFile(body: {
  folderId?: string | null;
  mediaAssetId: string;
  subtestCode: string;
  title: string;
  description?: string | null;
  sortOrder?: number;
}): Promise<MaterialFileDto> {
  return request<MaterialFileDto>('/v1/admin/materials/files', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export function adminUpdateMaterialFile(
  id: string,
  body: {
    folderId?: string | null;
    mediaAssetId?: string;
    subtestCode?: string;
    title?: string;
    description?: string | null;
    sortOrder?: number;
    status?: MaterialStatus;
  },
): Promise<MaterialFileDto> {
  return request<MaterialFileDto>(`/v1/admin/materials/files/${encodeURIComponent(id)}`, {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

export async function adminDeleteMaterialFile(id: string): Promise<void> {
  await request<unknown>(`/v1/admin/materials/files/${encodeURIComponent(id)}`, { method: 'DELETE' });
}

export async function adminReorderMaterialItems(
  type: 'folders' | 'files',
  items: { id: string; sortOrder: number }[],
): Promise<void> {
  await request<unknown>(`/v1/admin/materials/${type}/reorder`, {
    method: 'POST',
    body: JSON.stringify({ items }),
  });
}
