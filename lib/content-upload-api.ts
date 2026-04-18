/**
 * Admin Content Upload API client. Matches backend endpoints in
 * `ContentPapersAdminEndpoints.cs`. See `docs/CONTENT-UPLOAD-PLAN.md`.
 */

import { env } from './env';
import { ensureFreshAccessToken } from './auth-client';
import { fetchWithTimeout } from './network/fetch-with-timeout';

// ── Types mirror the .NET contracts 1:1 ─────────────────────────────────

export type PaperAssetRole =
  | 'Audio'
  | 'QuestionPaper'
  | 'AudioScript'
  | 'AnswerKey'
  | 'CaseNotes'
  | 'ModelAnswer'
  | 'RoleCard'
  | 'AssessmentCriteria'
  | 'WarmUpQuestions'
  | 'Supplementary';

export type ContentStatus = 'Draft' | 'InReview' | 'Published' | 'Archived';

export interface ContentPaperDto {
  id: string;
  subtestCode: string;
  title: string;
  slug: string;
  professionId: string | null;
  appliesToAllProfessions: boolean;
  difficulty: string;
  estimatedDurationMinutes: number;
  status: ContentStatus;
  publishedRevisionId: string | null;
  cardType: string | null;
  letterType: string | null;
  priority: number;
  tagsCsv: string;
  sourceProvenance: string | null;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
  archivedAt: string | null;
  assets?: ContentPaperAssetDto[];
}

export interface ContentPaperAssetDto {
  id: string;
  role: PaperAssetRole;
  part: string | null;
  mediaAssetId: string;
  title: string | null;
  displayOrder: number;
  isPrimary: boolean;
  createdAt: string;
  media: {
    id: string;
    originalFilename: string;
    mimeType: string;
    format: string;
    sizeBytes: number;
    durationSeconds: number | null;
    sha256: string | null;
    mediaKind: string | null;
    uploadedAt: string;
  } | null;
}

export interface ContentPaperCreateDto {
  subtestCode: string;
  title: string;
  slug?: string | null;
  professionId?: string | null;
  appliesToAllProfessions: boolean;
  difficulty?: string | null;
  estimatedDurationMinutes: number;
  cardType?: string | null;
  letterType?: string | null;
  priority: number;
  tagsCsv?: string | null;
  sourceProvenance?: string | null;
}

export interface ContentPaperUpdateDto {
  title?: string;
  professionId?: string | null;
  appliesToAllProfessions?: boolean;
  difficulty?: string | null;
  estimatedDurationMinutes?: number;
  cardType?: string | null;
  letterType?: string | null;
  priority?: number;
  tagsCsv?: string | null;
  sourceProvenance?: string | null;
}

export interface ContentPaperAssetAttachDto {
  role: PaperAssetRole;
  mediaAssetId: string;
  part?: string | null;
  title?: string | null;
  displayOrder: number;
  makePrimary: boolean;
}

export interface ChunkedUploadStartResponse {
  uploadId: string;
  chunkSizeBytes: number;
  expiresAt: string;
}

export interface ChunkedUploadCommitResult {
  mediaAssetId: string;
  sha256: string;
  sizeBytes: number;
  deduplicated: boolean;
}

// ── Transport ───────────────────────────────────────────────────────────

function resolveUrl(path: string): string {
  if (path.startsWith('http')) return path;
  const base = env.apiBaseUrl || '';
  return base ? `${base.replace(/\/$/, '')}${path}` : path;
}

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const token = await ensureFreshAccessToken();
  const headers = new Headers(init?.headers);
  headers.set('Accept', 'application/json');
  if (token) headers.set('Authorization', `Bearer ${token}`);
  if (init?.body && typeof init.body === 'string' && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  const res = await fetchWithTimeout(resolveUrl(path), { ...init, headers });
  if (!res.ok) {
    let detail: unknown = null;
    try { detail = await res.json(); } catch { /* ignore */ }
    const err = new Error(`HTTP ${res.status}`) as Error & { status?: number; detail?: unknown };
    err.status = res.status;
    err.detail = detail;
    throw err;
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

// ── Papers ──────────────────────────────────────────────────────────────

export const listContentPapers = (query: Partial<{
  subtest: string; profession: string; status: string;
  cardType: string; letterType: string; search: string;
  page: number; pageSize: number;
}>) => {
  const qs = new URLSearchParams();
  Object.entries(query).forEach(([k, v]) => {
    if (v !== undefined && v !== null && v !== '') qs.append(k, String(v));
  });
  return api<ContentPaperDto[]>(`/v1/admin/papers?${qs.toString()}`);
};

export const getContentPaper = (id: string) => api<ContentPaperDto>(`/v1/admin/papers/${id}`);
export const createContentPaper = (body: ContentPaperCreateDto) =>
  api<ContentPaperDto>('/v1/admin/papers', { method: 'POST', body: JSON.stringify(body) });
export const updateContentPaper = (id: string, body: ContentPaperUpdateDto) =>
  api<ContentPaperDto>(`/v1/admin/papers/${id}`, { method: 'PUT', body: JSON.stringify(body) });
export const archiveContentPaper = (id: string) =>
  api<void>(`/v1/admin/papers/${id}`, { method: 'DELETE' });
export const publishContentPaper = (id: string) =>
  api<void>(`/v1/admin/papers/${id}/publish`, { method: 'POST' });

export const attachPaperAsset = (paperId: string, body: ContentPaperAssetAttachDto) =>
  api<ContentPaperAssetDto>(`/v1/admin/papers/${paperId}/assets`, {
    method: 'POST', body: JSON.stringify(body),
  });
export const removePaperAsset = (paperId: string, assetId: string) =>
  api<void>(`/v1/admin/papers/${paperId}/assets/${assetId}`, { method: 'DELETE' });

export const getRequiredRoles = (subtest: string) =>
  api<{ subtest: string; required: PaperAssetRole[] }>(
    `/v1/admin/papers/required-roles/${subtest}`,
  );

// ── Chunked uploads ─────────────────────────────────────────────────────

export const startUpload = (body: {
  originalFilename: string;
  declaredMimeType: string;
  declaredSizeBytes: number;
  intendedRole: PaperAssetRole;
}) => api<ChunkedUploadStartResponse>('/v1/admin/uploads', {
  method: 'POST', body: JSON.stringify(body),
});

export async function uploadPart(uploadId: string, partNumber: number, body: Blob): Promise<void> {
  const token = await ensureFreshAccessToken();
  const res = await fetchWithTimeout(
    resolveUrl(`/v1/admin/uploads/${uploadId}/parts/${partNumber}`),
    {
      method: 'PUT',
      body,
      headers: {
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        'Content-Type': 'application/octet-stream',
      },
    },
  );
  if (!res.ok) throw new Error(`Upload part failed: HTTP ${res.status}`);
}

export const completeUpload = (uploadId: string) =>
  api<ChunkedUploadCommitResult>(`/v1/admin/uploads/${uploadId}/complete`, { method: 'POST' });

export const abortUpload = (uploadId: string) =>
  api<void>(`/v1/admin/uploads/${uploadId}`, { method: 'DELETE' });

/**
 * High-level helper: take a File, run the whole chunked upload protocol,
 * return the resulting MediaAsset id. Reports progress 0..1 via callback.
 */
export async function uploadFileChunked(
  file: File,
  intendedRole: PaperAssetRole,
  onProgress?: (pct: number) => void,
): Promise<ChunkedUploadCommitResult> {
  const start = await startUpload({
    originalFilename: file.name,
    declaredMimeType: file.type || 'application/octet-stream',
    declaredSizeBytes: file.size,
    intendedRole,
  });
  const chunkSize = start.chunkSizeBytes;
  const totalParts = Math.max(1, Math.ceil(file.size / chunkSize));
  for (let i = 0; i < totalParts; i++) {
    const from = i * chunkSize;
    const to = Math.min(file.size, from + chunkSize);
    const chunk = file.slice(from, to);
    await uploadPart(start.uploadId, i + 1, chunk);
    onProgress?.((i + 1) / totalParts * 0.95); // leave 5% for complete
  }
  const result = await completeUpload(start.uploadId);
  onProgress?.(1);
  return result;
}
