/**
 * Typed API client for the admin Video Library surface (Bunny Stream backed).
 *
 * JSON endpoints delegate to the shared `apiClient` (lib/api.ts) so they
 * inherit auth (Bearer), CSRF, retry-on-5xx/408/429 and the normalized
 * `ApiError` (status + code + fieldErrors). The paged list endpoints go
 * through a raw `fetchWithTimeout` path (copied from
 * `listContentPapersPaged` in lib/content-upload-api.ts) because the shared
 * client only surfaces the parsed JSON body, not the `X-Total-Count` header.
 *
 * Backend endpoints targeted (base `/v1/admin/video-library`):
 *   GET    /videos?q&status&accessTier&encodeStatus&categoryId&page&pageSize
 *   POST   /videos                          { title }        → AdminVideoDetail (Draft)
 *   GET    /videos/{videoId}                                 → AdminVideoDetail
 *   PATCH  /videos/{videoId}                partial          → AdminVideoDetail
 *   POST   /videos/{videoId}/upload-authorization            → VideoUploadAuthorization
 *   POST   /videos/{videoId}/reset-upload                    → VideoUploadAuthorization
 *   POST   /videos/{videoId}/refresh-status                  → AdminVideoDetail
 *   PUT    /videos/{videoId}/chapters       { chapters }
 *   POST   /videos/{videoId}/captions       { mediaAssetId, languageCode, label }
 *   DELETE /videos/{videoId}/captions/{captionId}
 *   POST   /videos/{videoId}/attachments    { mediaAssetId, title }
 *   DELETE /videos/{videoId}/attachments/{attachmentId}
 *   PUT    /videos/{videoId}/attachments/order { orderedIds }
 *   POST   /videos/{videoId}/thumbnail      { mediaAssetId }
 *   DELETE /videos/{videoId}/thumbnail
 *   GET    /videos/{videoId}/publish-gate                    → VideoPublishGate
 *   POST   /videos/{videoId}/publish        { publishAt? }   (422 → ApiError.fieldErrors)
 *   POST   /videos/{videoId}/unpublish | /archive | /restore
 *   POST   /videos/{videoId}/force-delete   { force: true, reason }
 *   POST   /videos/bulk-lifecycle           { action, videoIds }
 *   GET    /categories?includeInactive · POST · PATCH /{id} · DELETE /{id} · PUT /order
 *   GET    /analytics/summary?days=N
 *   GET    /videos/{videoId}/analytics/summary?days=N
 *   GET    /videos/{videoId}/analytics/viewers?page&pageSize (X-Total-Count)
 */

import { apiClient } from '@/lib/api';
import { env } from '@/lib/env';
import { ensureFreshAccessToken } from '@/lib/auth-client';
import { fetchWithTimeout } from '@/lib/network/fetch-with-timeout';

const BASE = '/v1/admin/video-library';

// ─────────────────────────────────────────────────────────────────────────────
// DTO types — mirror the backend contract 1:1
// ─────────────────────────────────────────────────────────────────────────────

export type VideoLifecycleStatus = 'Draft' | 'InReview' | 'Published' | 'Rejected' | 'Archived';
export type VideoAccessTier = 'free' | 'premium';
export type VideoDifficulty = 'foundation' | 'core' | 'advanced';
export type VideoEncodeStatus =
  | 'not_uploaded'
  | 'uploading'
  | 'queued'
  | 'processing'
  | 'encoding'
  | 'ready'
  | 'failed';
export type VideoThumbnailMode = 'auto' | 'custom';
/** Instruction language of a video ("en" | "ar"); null = unspecified. */
export type VideoLanguage = 'en' | 'ar';

export interface AdminVideoSummary {
  videoId: string;
  title: string;
  status: VideoLifecycleStatus;
  encodeStatus: VideoEncodeStatus;
  accessTier: VideoAccessTier;
  categoryNames: string[];
  durationSeconds: number | null;
  thumbnailUrl: string | null;
  isFeatured: boolean;
  viewCount: number;
  updatedAt: string;
  publishAt: string | null;
}

export interface AdminVideoCaption {
  id: string;
  languageCode: string;
  label: string;
  syncedToBunny: boolean;
}

export interface AdminVideoChapter {
  timeSeconds: number;
  title: string;
}

export interface AdminVideoAttachment {
  id: string;
  title: string;
  mediaAssetId: string;
  sortOrder: number;
}

export interface AdminVideoDetail {
  videoId: string;
  title: string;
  description: string;
  subtestCode: string | null;
  tagsCsv: string;
  difficulty: VideoDifficulty;
  categoryIds: string[];
  categoryNames: string[];
  accessTier: VideoAccessTier;
  targetProfessionIds: string[];
  language: VideoLanguage | null;
  bunnyVideoId: string | null;
  bunnyCollectionId: string | null;
  encodeStatus: VideoEncodeStatus;
  encodeProgress: number | null;
  encodeError: string | null;
  durationSeconds: number | null;
  width: number | null;
  height: number | null;
  thumbnailUrl: string | null;
  thumbnailMode: VideoThumbnailMode;
  customThumbnailAssetId: string | null;
  captions: AdminVideoCaption[];
  chapters: AdminVideoChapter[];
  attachments: AdminVideoAttachment[];
  isFeatured: boolean;
  sortOrder: number;
  status: VideoLifecycleStatus;
  publishAt: string | null;
  publishedAt: string | null;
  archivedAt: string | null;
  viewCount: number;
  createdAt: string;
  updatedAt: string;
}

/** Partial PATCH payload for `/videos/{videoId}`. */
export interface AdminVideoPatch {
  title?: string;
  description?: string;
  tagsCsv?: string;
  difficulty?: VideoDifficulty;
  categoryIds?: string[];
  accessTier?: VideoAccessTier;
  targetProfessionIds?: string[];
  /** Instruction language: 'en' | 'ar'. Omitted = unchanged; '' = clear. */
  language?: VideoLanguage | '' | null;
  isFeatured?: boolean;
  sortOrder?: number;
  publishAt?: string | null;
  subtestCode?: string | null;
  /** Bunny collection membership mirror. Omitted = unchanged; '' = clear; guid = set. */
  bunnyCollectionId?: string | null;
}

/** Presigned Bunny TUS authorization from the backend. */
export interface VideoUploadAuthorization {
  bunnyVideoId: string;
  libraryId: string;
  tusEndpoint: string;
  authorizationSignature: string;
  authorizationExpire: number;
}

export interface VideoPublishGate {
  canPublish: boolean;
  errors: string[];
  warnings: string[];
}

export type VideoBulkLifecycleAction = 'publish' | 'unpublish' | 'archive';

export interface VideoBulkLifecycleResult {
  totalRequested: number;
  succeeded: number;
  skipped: number;
  failed: number;
  errors: string[];
}

export interface AdminVideoCategory {
  id: string;
  title: string;
  slug: string;
  description: string;
  displayOrder: number;
  status: string;
  videoCount: number;
}

export interface AdminVideoCategoryPatch {
  title?: string;
  description?: string;
  status?: string;
}

export interface VideoLibraryAnalyticsSummary {
  totals: {
    publishedVideos: number;
    views: number;
    watchHours: number;
    avgCompletionPercent: number;
  };
  viewsPerDay: Array<{ date: string; views: number }>;
  topVideos: Array<{
    videoId: string;
    title: string;
    views: number;
    watchHours: number;
    completionPercent: number;
  }>;
  viewsByCategory: Array<{ categoryId: string; title: string; views: number }>;
}

export interface AdminVideoAnalyticsSummary {
  views: number;
  uniqueViewers: number;
  watchHours: number;
  avgCompletionPercent: number;
  viewsPerDay: Array<{ date: string; views: number }>;
  /** 10 buckets (0–10%, …, 90–100%) of viewers still watching. */
  retentionBuckets: number[];
}

export interface AdminVideoViewerRow {
  userId: string;
  email: string;
  name: string;
  positionSeconds: number;
  percentComplete: number;
  completed: boolean;
  lastWatchedAt: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Raw-fetch transport for X-Total-Count paged lists
// (copied from listContentPapersPaged in lib/content-upload-api.ts)
// ─────────────────────────────────────────────────────────────────────────────

function resolveUrl(path: string): string {
  if (path.startsWith('http')) return path;
  const base = env.apiBaseUrl || '';
  return base ? `${base.replace(/\/$/, '')}${path}` : path;
}

function readCsrfCookie(): string | null {
  if (typeof document === 'undefined') return null;
  const m = document.cookie.match(/(?:^|;\s*)oet_csrf=([^;]+)/);
  return m ? decodeURIComponent(m[1]) : null;
}

async function pagedGet<T>(path: string, fallbackPrefix: string): Promise<{ items: T[]; total: number }> {
  const token = await ensureFreshAccessToken();
  const csrf = readCsrfCookie();
  const res = await fetchWithTimeout(resolveUrl(path), {
    method: 'GET',
    credentials: 'include',
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(csrf ? { 'x-csrf-token': csrf } : {}),
    },
  });
  if (!res.ok) {
    let message = `${fallbackPrefix} HTTP ${res.status}`;
    try {
      const payload = (await res.json()) as { message?: string; title?: string };
      const prose = [payload?.message, payload?.title]
        .map((s) => (typeof s === 'string' ? s.trim() : ''))
        .find((s) => s.length > 0);
      if (prose) message = `${fallbackPrefix} ${prose}`;
    } catch {
      /* non-JSON error body — keep the status-line message */
    }
    const err = new Error(message) as Error & { status?: number };
    err.status = res.status;
    throw err;
  }
  const items = (await res.json()) as T[];
  // `Number(null)` is 0, so guard header presence before coercing — a missing
  // header must fall back to the page's item count, not report zero rows.
  const rawTotal = res.headers.get('X-Total-Count');
  const headerTotal = rawTotal === null ? Number.NaN : Number(rawTotal);
  const total = Number.isFinite(headerTotal) && headerTotal >= 0 ? headerTotal : items.length;
  return { items, total };
}

// ─────────────────────────────────────────────────────────────────────────────
// Videos
// ─────────────────────────────────────────────────────────────────────────────

export interface ListAdminVideosQuery {
  q?: string;
  status?: string;
  accessTier?: string;
  encodeStatus?: string;
  categoryId?: string;
  page?: number;
  pageSize?: number;
}

export async function adminListVideos(
  query: ListAdminVideosQuery = {},
): Promise<{ items: AdminVideoSummary[]; total: number }> {
  const qs = new URLSearchParams();
  Object.entries(query).forEach(([k, v]) => {
    if (v !== undefined && v !== null && v !== '') qs.append(k, String(v));
  });
  const suffix = qs.toString();
  return pagedGet<AdminVideoSummary>(
    `${BASE}/videos${suffix ? `?${suffix}` : ''}`,
    'Failed to list videos:',
  );
}

export function adminCreateVideo(input: { title: string }): Promise<AdminVideoDetail> {
  return apiClient.post<AdminVideoDetail>(`${BASE}/videos`, input);
}

export function adminGetVideo(videoId: string): Promise<AdminVideoDetail> {
  return apiClient.get<AdminVideoDetail>(`${BASE}/videos/${encodeURIComponent(videoId)}`);
}

export function adminPatchVideo(videoId: string, patch: AdminVideoPatch): Promise<AdminVideoDetail> {
  return apiClient.patch<AdminVideoDetail>(`${BASE}/videos/${encodeURIComponent(videoId)}`, patch);
}

// ─────────────────────────────────────────────────────────────────────────────
// Upload / encode lifecycle
// ─────────────────────────────────────────────────────────────────────────────

export function adminRequestUploadAuthorization(videoId: string): Promise<VideoUploadAuthorization> {
  return apiClient.post<VideoUploadAuthorization>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/upload-authorization`,
    {},
  );
}

export function adminResetVideoUpload(videoId: string): Promise<VideoUploadAuthorization> {
  return apiClient.post<VideoUploadAuthorization>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/reset-upload`,
    {},
  );
}

export function adminRefreshVideoStatus(videoId: string): Promise<AdminVideoDetail> {
  return apiClient.post<AdminVideoDetail>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/refresh-status`,
    {},
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Chapters / captions / attachments / thumbnail
// ─────────────────────────────────────────────────────────────────────────────

export function adminSetVideoChapters(
  videoId: string,
  chapters: AdminVideoChapter[],
): Promise<AdminVideoDetail> {
  return apiClient.put<AdminVideoDetail>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/chapters`,
    { chapters },
  );
}

export function adminAddVideoCaption(
  videoId: string,
  input: { mediaAssetId: string; languageCode: string; label: string },
): Promise<AdminVideoDetail> {
  return apiClient.post<AdminVideoDetail>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/captions`,
    input,
  );
}

export function adminDeleteVideoCaption(videoId: string, captionId: string): Promise<void> {
  return apiClient.delete<void>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/captions/${encodeURIComponent(captionId)}`,
  );
}

export function adminAddVideoAttachment(
  videoId: string,
  input: { mediaAssetId: string; title: string },
): Promise<AdminVideoDetail> {
  return apiClient.post<AdminVideoDetail>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/attachments`,
    input,
  );
}

export function adminDeleteVideoAttachment(videoId: string, attachmentId: string): Promise<void> {
  return apiClient.delete<void>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/attachments/${encodeURIComponent(attachmentId)}`,
  );
}

export function adminOrderVideoAttachments(
  videoId: string,
  orderedIds: string[],
): Promise<AdminVideoDetail> {
  return apiClient.put<AdminVideoDetail>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/attachments/order`,
    { orderedIds },
  );
}

export function adminSetVideoThumbnail(videoId: string, mediaAssetId: string): Promise<AdminVideoDetail> {
  return apiClient.post<AdminVideoDetail>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/thumbnail`,
    { mediaAssetId },
  );
}

export function adminClearVideoThumbnail(videoId: string): Promise<AdminVideoDetail> {
  return apiClient.delete<AdminVideoDetail>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/thumbnail`,
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Lifecycle
// ─────────────────────────────────────────────────────────────────────────────

export function adminGetVideoPublishGate(videoId: string): Promise<VideoPublishGate> {
  return apiClient.get<VideoPublishGate>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/publish-gate`,
  );
}

/** 422 responses throw the shared `ApiError` carrying `fieldErrors` / `errors`. */
export function adminPublishVideo(videoId: string, publishAt?: string): Promise<AdminVideoDetail> {
  return apiClient.post<AdminVideoDetail>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/publish`,
    publishAt ? { publishAt } : {},
  );
}

export function adminUnpublishVideo(videoId: string): Promise<AdminVideoDetail> {
  return apiClient.post<AdminVideoDetail>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/unpublish`,
    {},
  );
}

export function adminArchiveVideo(videoId: string): Promise<AdminVideoDetail> {
  return apiClient.post<AdminVideoDetail>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/archive`,
    {},
  );
}

export function adminRestoreVideo(videoId: string): Promise<AdminVideoDetail> {
  return apiClient.post<AdminVideoDetail>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/restore`,
    {},
  );
}

export function adminForceDeleteVideo(videoId: string, reason: string): Promise<void> {
  return apiClient.post<void>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/force-delete`,
    { force: true, reason },
  );
}

export function adminBulkVideoLifecycle(
  action: VideoBulkLifecycleAction,
  videoIds: string[],
): Promise<VideoBulkLifecycleResult> {
  return apiClient.post<VideoBulkLifecycleResult>(`${BASE}/videos/bulk-lifecycle`, {
    action,
    videoIds,
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// Categories
// ─────────────────────────────────────────────────────────────────────────────

export function adminListVideoCategories(includeInactive = true): Promise<AdminVideoCategory[]> {
  return apiClient.get<AdminVideoCategory[]>(
    `${BASE}/categories?includeInactive=${includeInactive ? 'true' : 'false'}`,
  );
}

export function adminCreateVideoCategory(input: {
  title: string;
  description?: string;
}): Promise<AdminVideoCategory> {
  return apiClient.post<AdminVideoCategory>(`${BASE}/categories`, input);
}

export function adminPatchVideoCategory(
  id: string,
  patch: AdminVideoCategoryPatch,
): Promise<AdminVideoCategory> {
  return apiClient.patch<AdminVideoCategory>(
    `${BASE}/categories/${encodeURIComponent(id)}`,
    patch,
  );
}

export function adminDeleteVideoCategory(id: string): Promise<void> {
  return apiClient.delete<void>(`${BASE}/categories/${encodeURIComponent(id)}`);
}

export function adminOrderVideoCategories(orderedIds: string[]): Promise<AdminVideoCategory[]> {
  return apiClient.put<AdminVideoCategory[]>(`${BASE}/categories/order`, { orderedIds });
}

export function adminMergeVideoCategory(
  id: string,
  targetCategoryId: string,
): Promise<{ mergedVideoCount: number; targetCategoryId: string }> {
  return apiClient.post(`${BASE}/categories/${encodeURIComponent(id)}/merge`, { targetCategoryId });
}

// ─────────────────────────────────────────────────────────────────────────────
// Analytics
// ─────────────────────────────────────────────────────────────────────────────

export function adminGetVideoLibraryAnalytics(days = 30): Promise<VideoLibraryAnalyticsSummary> {
  return apiClient.get<VideoLibraryAnalyticsSummary>(`${BASE}/analytics/summary?days=${days}`);
}

export function adminGetVideoAnalytics(
  videoId: string,
  days = 30,
): Promise<AdminVideoAnalyticsSummary> {
  return apiClient.get<AdminVideoAnalyticsSummary>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/analytics/summary?days=${days}`,
  );
}

export async function adminListVideoViewers(
  videoId: string,
  query: { page?: number; pageSize?: number } = {},
): Promise<{ items: AdminVideoViewerRow[]; total: number }> {
  const qs = new URLSearchParams();
  if (query.page !== undefined) qs.set('page', String(query.page));
  if (query.pageSize !== undefined) qs.set('pageSize', String(query.pageSize));
  const suffix = qs.toString();
  return pagedGet<AdminVideoViewerRow>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/analytics/viewers${suffix ? `?${suffix}` : ''}`,
    'Failed to list viewers:',
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Collections (live Bunny Stream library management)
//
// These read straight from the Bunny library (source of truth for membership).
// The list responses carry their own `totalItems` in the body, so they use the
// shared `apiClient` (not the X-Total-Count `pagedGet` path). A dormant Bunny
// (not configured) answers 503 `bunny_not_configured` — callers branch on that.
// ─────────────────────────────────────────────────────────────────────────────

export interface AdminCollection {
  collectionId: string;
  name: string;
  videoCount: number;
  totalSizeBytes: number;
}

export interface AdminCollectionList {
  totalItems: number;
  page: number;
  itemsPerPage: number;
  items: AdminCollection[];
}

export interface AdminCollectionVideo {
  bunnyVideoId: string;
  title: string;
  encodeStatus: VideoEncodeStatus;
  encodeProgress: number;
  durationSeconds: number;
  storageSizeBytes: number;
  thumbnailUrl: string | null;
  width: number | null;
  height: number | null;
  isImported: boolean;
  localVideoId: string | null;
  localStatus: VideoLifecycleStatus | null;
}

export interface AdminCollectionVideoPage {
  totalItems: number;
  page: number;
  itemsPerPage: number;
  items: AdminCollectionVideo[];
}

export interface ListCollectionQuery {
  page?: number;
  itemsPerPage?: number;
  search?: string;
}

function collectionQueryString(query: ListCollectionQuery): string {
  const qs = new URLSearchParams();
  if (query.page !== undefined) qs.set('page', String(query.page));
  if (query.itemsPerPage !== undefined) qs.set('itemsPerPage', String(query.itemsPerPage));
  if (query.search) qs.set('search', query.search);
  const suffix = qs.toString();
  return suffix ? `?${suffix}` : '';
}

export function adminListCollections(query: ListCollectionQuery = {}): Promise<AdminCollectionList> {
  return apiClient.get<AdminCollectionList>(`${BASE}/collections${collectionQueryString(query)}`);
}

export function adminCreateCollection(name: string): Promise<AdminCollection> {
  return apiClient.post<AdminCollection>(`${BASE}/collections`, { name });
}

export function adminRenameCollection(collectionId: string, name: string): Promise<AdminCollection> {
  return apiClient.post<AdminCollection>(
    `${BASE}/collections/${encodeURIComponent(collectionId)}`,
    { name },
  );
}

export function adminDeleteCollection(collectionId: string): Promise<void> {
  return apiClient.delete<void>(`${BASE}/collections/${encodeURIComponent(collectionId)}`);
}

export function adminListCollectionVideos(
  collectionId: string,
  query: ListCollectionQuery = {},
): Promise<AdminCollectionVideoPage> {
  return apiClient.get<AdminCollectionVideoPage>(
    `${BASE}/collections/${encodeURIComponent(collectionId)}/videos${collectionQueryString(query)}`,
  );
}

export function adminMoveCollectionVideo(
  bunnyVideoId: string,
  collectionId: string | null,
): Promise<{ moved: boolean }> {
  return apiClient.post<{ moved: boolean }>(
    `${BASE}/collections/videos/${encodeURIComponent(bunnyVideoId)}/move`,
    { collectionId },
  );
}

export function adminImportCollectionVideo(
  bunnyVideoId: string,
  input: { title?: string; collectionId?: string | null } = {},
): Promise<AdminVideoDetail> {
  return apiClient.post<AdminVideoDetail>(
    `${BASE}/collections/videos/${encodeURIComponent(bunnyVideoId)}/import`,
    input,
  );
}

export function adminBunnyDeleteCollectionVideo(bunnyVideoId: string): Promise<{ deleted: boolean }> {
  return apiClient.post<{ deleted: boolean }>(
    `${BASE}/collections/videos/${encodeURIComponent(bunnyVideoId)}/bunny-delete`,
    { force: true },
  );
}
