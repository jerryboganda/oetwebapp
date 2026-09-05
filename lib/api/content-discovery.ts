/**
 * Content discovery: readiness, search, recommendations, signed media URLs,
 * media management — extracted from `lib/api.ts`. Re-exported there, so
 * `@/lib/api` imports keep working.
 */
import { ApiError, apiRequest, getHeaders, isRetryable, resolveApiUrl } from './client';
import { fetchWithTimeout } from '../network/fetch-with-timeout';

export async function fetchReadinessScore() {
  return apiRequest('/v1/readiness');
}

export async function fetchContentBySkill(subtest: string, skillTag?: string, page?: number, pageSize?: number) {
  const p = new URLSearchParams({ subtest });
  if (skillTag) p.set('skillTag', skillTag);
  if (page) p.set('page', String(page));
  if (pageSize) p.set('pageSize', String(pageSize));
  return apiRequest(`/v1/content/by-skill?${p}`);
}

export async function searchContent(params?: {
  q?: string; subtest?: string; profession?: string; difficulty?: string;
  language?: string; provenance?: string; contentType?: string;
  minQuality?: number; mockEligible?: boolean; previewEligible?: boolean;
  page?: number; pageSize?: number;
}) {
  const p = new URLSearchParams();
  if (params?.q) p.set('q', params.q);
  if (params?.subtest) p.set('subtest', params.subtest);
  if (params?.profession) p.set('profession', params.profession);
  if (params?.difficulty) p.set('difficulty', params.difficulty);
  if (params?.language) p.set('language', params.language);
  if (params?.provenance) p.set('provenance', params.provenance);
  if (params?.contentType) p.set('contentType', params.contentType);
  if (params?.minQuality) p.set('minQuality', String(params.minQuality));
  if (params?.mockEligible) p.set('mockEligible', 'true');
  if (params?.previewEligible) p.set('previewEligible', 'true');
  if (params?.page) p.set('page', String(params.page));
  if (params?.pageSize) p.set('pageSize', String(params.pageSize));
  const qs = p.toString();
  return apiRequest(`/v1/search${qs ? `?${qs}` : ''}`);
}

export async function fetchSearchFacets() {
  return apiRequest('/v1/search/facets');
}

export async function fetchRecommendations(count?: number) {
  return apiRequest(`/v1/recommendations${count ? `?count=${count}` : ''}`);
}

export async function fetchSignedMediaUrl(assetId: string) {
  return apiRequest(`/v1/media/${encodeURIComponent(assetId)}/url`);
}

export interface UploadedMediaAsset {
  id: string;
  originalFilename: string;
  mimeType: string;
  format: string;
  sizeBytes: number;
  status: string;
  uploadedBy: string;
  uploadedAt: string;
  url: string;
}

export async function uploadMedia(file: File): Promise<UploadedMediaAsset> {
  const formData = new FormData();
  formData.append('file', file);
  const response = await fetchWithTimeout(resolveApiUrl('/v1/media/upload'), {
    method: 'POST',
    headers: await getHeaders('/v1/media/upload', undefined, { json: false }),
    body: formData,
  }, 90_000);
  if (!response.ok) {
    let code = 'upload_failed';
    let message = `Upload failed: ${response.status}`;
    try {
      const error = await response.json();
      code = error.code ?? code;
      message = error.message ?? message;
    } catch { /* ignore */ }
    throw new ApiError(response.status, code, message, false);
  }
  return response.json();
}

export async function fetchMediaItem(id: string) {
  return apiRequest(`/v1/media/${encodeURIComponent(id)}`);
}

export async function deleteMedia(id: string) {
  return apiRequest(`/v1/media/${encodeURIComponent(id)}`, { method: 'DELETE' });
}

export async function fetchMyMedia(params?: { page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 20));
  return apiRequest(`/v1/media?${qs}`);
}
