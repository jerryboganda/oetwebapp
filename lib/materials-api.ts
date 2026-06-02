/**
 * Materials library API — admin CRUD (folders, files, audience) and
 * learner read (visible tree).
 *
 * Uses the same auth/CSRF/fetch infrastructure as lib/api.ts but lives in
 * its own module to keep lib/api.ts from growing further.
 */

import { env } from './env';
import { ensureFreshAccessToken } from './auth-client';
import { fetchWithTimeout } from './network/fetch-with-timeout';

// ── Types ─────────────────────────────────────────────────────────────────────

export type MaterialAudienceMode = 'Inherit' | 'Everyone' | 'Restricted';
export type MaterialStatus = 'Draft' | 'Published' | 'Archived';

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
  kind: 'pdf' | 'audio';
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
  kind: 'pdf' | 'audio';
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

// ── Fetch helpers ─────────────────────────────────────────────────────────────

const API_BASE = env.apiBaseUrl;

function resolveUrl(path: string): string {
  if (/^https?:\/\//i.test(path)) return path;
  const base = API_BASE.endsWith('/') ? API_BASE.slice(0, -1) : API_BASE;
  return `${base}${path}`;
}

function readCsrf(): string | null {
  if (typeof document === 'undefined') return null;
  const m = document.cookie.match(/(?:^|;\s*)oet_csrf=([^;]+)/);
  return m ? m[1] : null;
}

const SAFE = new Set(['GET', 'HEAD', 'OPTIONS', 'TRACE']);

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const token = await ensureFreshAccessToken();
  const headers = new Headers(init?.headers);
  headers.set('Accept', 'application/json');
  if (token) headers.set('Authorization', `Bearer ${token}`);
  if (init?.body && typeof init.body === 'string' && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  const method = (init?.method ?? 'GET').toUpperCase();
  if (!SAFE.has(method) && !headers.has('x-csrf-token')) {
    const csrf = readCsrf();
    if (csrf) headers.set('x-csrf-token', csrf);
  }
  const res = await fetchWithTimeout(resolveUrl(path), { ...init, headers, credentials: 'include' });
  if (!res.ok) {
    let message = `${method} ${path} failed: ${res.status}`;
    try {
      const body = await res.json() as { message?: string; error?: string };
      message = body.message ?? body.error ?? message;
    } catch { /* non-JSON error body */ }
    throw new Error(message);
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
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

export function adminCreateMaterialFolder(body: {
  parentFolderId?: string | null;
  name: string;
  description?: string | null;
  subtestCode?: string | null;
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
