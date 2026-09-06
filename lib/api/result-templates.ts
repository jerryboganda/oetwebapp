import { ensureFreshAccessToken } from '../auth-client';
import { ApiError, apiRequest, resolveApiUrl } from './client';
import { fetchWithTimeout } from '../network/fetch-with-timeout';

/**
 * Admin result templates CRUD + learner active template.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export interface ResultTemplateDto {
  id: string;
  templateKey: string;
  title: string;
  description: string | null;
  professionId: string | null;
  mediaAssetId: string;
  isActive: boolean;
  sortOrder: number;
  uploadedByUserId: string | null;
  createdAt: string;
  updatedAt: string;
  media: {
    id: string;
    originalFilename: string;
    mimeType: string;
    format: string;
    sizeBytes: number;
    sha256: string | null;
  } | null;
}

export interface LearnerResultTemplateDto {
  id: string;
  templateKey: string;
  title: string;
  description: string | null;
  professionId: string | null;
  mediaAssetId: string;
  sortOrder: number;
  updatedAt: string;
  media: {
    id: string;
    originalFilename: string;
    mimeType: string;
    sizeBytes: number;
  } | null;
}

export async function adminListResultTemplates(profession?: string): Promise<ResultTemplateDto[]> {
  const qs = profession ? `?profession=${encodeURIComponent(profession)}` : '';
  return apiRequest<ResultTemplateDto[]>(`/v1/admin/result-templates${qs}`);
}

export async function adminUploadResultTemplate(payload: {
  file: File;
  templateKey: string;
  title: string;
  description?: string | null;
  professionId?: string | null;
  sortOrder?: number;
}): Promise<ResultTemplateDto> {
  const form = new FormData();
  form.append('file', payload.file);
  form.append('templateKey', payload.templateKey);
  form.append('title', payload.title);
  if (payload.description != null) form.append('description', payload.description);
  if (payload.professionId) form.append('professionId', payload.professionId);
  if (payload.sortOrder != null) form.append('sortOrder', String(payload.sortOrder));
  const token = await ensureFreshAccessToken();
  const headers: Record<string, string> = {};
  if (token) headers['Authorization'] = `Bearer ${token}`;
  const response = await fetchWithTimeout(resolveApiUrl('/v1/admin/result-templates'), {
    method: 'POST', headers, body: form,
  }, 120_000);
  if (!response.ok) {
    const text = await response.text().catch(() => '');
    throw new Error(`Upload failed: ${response.status} ${text}`);
  }
  return response.json() as Promise<ResultTemplateDto>;
}

export async function adminUpdateResultTemplate(id: string, patch: {
  title?: string;
  description?: string | null;
  professionId?: string | null;
  sortOrder?: number;
}): Promise<ResultTemplateDto> {
  return apiRequest<ResultTemplateDto>(`/v1/admin/result-templates/${encodeURIComponent(id)}`, {
    method: 'PUT', body: JSON.stringify(patch),
  });
}

export async function adminActivateResultTemplate(id: string): Promise<{ id: string; isActive: boolean }> {
  return apiRequest(`/v1/admin/result-templates/${encodeURIComponent(id)}/activate`, { method: 'POST', body: '{}' });
}

export async function adminDeactivateResultTemplate(id: string): Promise<{ id: string; isActive: boolean }> {
  return apiRequest(`/v1/admin/result-templates/${encodeURIComponent(id)}/deactivate`, { method: 'POST', body: '{}' });
}

export async function adminDeleteResultTemplate(id: string): Promise<void> {
  await apiRequest<void>(`/v1/admin/result-templates/${encodeURIComponent(id)}`, {
    method: 'DELETE',
  }, { acceptedStatuses: [204] });
}

/** Permanently removes a result-template row (the MediaAsset it points at is kept). system_admin only. */
export async function adminForceDeleteResultTemplate(id: string): Promise<void> {
  await apiRequest<void>(`/v1/admin/result-templates/${encodeURIComponent(id)}/force-delete`, {
    method: 'POST',
  }, { acceptedStatuses: [204] });
}

export async function learnerGetActiveResultTemplate(): Promise<LearnerResultTemplateDto | null> {
  try {
    return await apiRequest<LearnerResultTemplateDto>('/v1/result-templates/active');
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) return null;
    throw e;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Speaking shared resources (Warm-up Questions + Assessment Criteria PDFs)
// ─────────────────────────────────────────────────────────────────────────────

