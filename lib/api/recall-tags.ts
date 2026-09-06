import { apiRequest } from './client';

/**
 * Admin recall set tags CRUD.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export interface RecallSetTagDto {
  code: string;
  displayName: string;
  shortLabel: string | null;
  description: string | null;
  sortOrder: number;
  isActive: boolean;
  examTypeCode: string | null;
  createdByUserId: string | null;
  createdAt: string;
  updatedAt: string;
  canonical: boolean;
}

export async function adminListRecallSetTags(params: { includeArchived?: boolean; examTypeCode?: string } = {}): Promise<RecallSetTagDto[]> {
  const qs = new URLSearchParams();
  if (params.includeArchived) qs.set('includeArchived', 'true');
  if (params.examTypeCode) qs.set('examTypeCode', params.examTypeCode);
  const q = qs.toString();
  return apiRequest<RecallSetTagDto[]>(`/v1/admin/recall-set-tags${q ? `?${q}` : ''}`);
}

export async function adminGetRecallSetTag(code: string): Promise<RecallSetTagDto> {
  return apiRequest<RecallSetTagDto>(`/v1/admin/recall-set-tags/${encodeURIComponent(code)}`);
}

export async function adminCreateRecallSetTag(payload: {
  code: string;
  displayName: string;
  shortLabel?: string | null;
  description?: string | null;
  sortOrder?: number;
  isActive?: boolean;
  examTypeCode?: string | null;
}): Promise<RecallSetTagDto> {
  return apiRequest<RecallSetTagDto>('/v1/admin/recall-set-tags', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function adminUpdateRecallSetTag(code: string, patch: {
  displayName?: string;
  shortLabel?: string | null;
  description?: string | null;
  sortOrder?: number;
  isActive?: boolean;
  examTypeCode?: string | null;
}): Promise<RecallSetTagDto> {
  return apiRequest<RecallSetTagDto>(`/v1/admin/recall-set-tags/${encodeURIComponent(code)}`, {
    method: 'PUT',
    body: JSON.stringify(patch),
  });
}

export async function adminArchiveRecallSetTag(code: string): Promise<RecallSetTagDto> {
  return apiRequest<RecallSetTagDto>(`/v1/admin/recall-set-tags/${encodeURIComponent(code)}/archive`, {
    method: 'POST', body: '{}',
  });
}

export async function adminUnarchiveRecallSetTag(code: string): Promise<RecallSetTagDto> {
  return apiRequest<RecallSetTagDto>(`/v1/admin/recall-set-tags/${encodeURIComponent(code)}/unarchive`, {
    method: 'POST', body: '{}',
  });
}

export async function adminDeleteRecallSetTag(code: string): Promise<{ archived: boolean; code: string; hardDelete: boolean; reason?: string }> {
  return apiRequest(`/v1/admin/recall-set-tags/${encodeURIComponent(code)}`, {
    method: 'DELETE',
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// Result-template gallery (images displayed on learner mock-result pages)
// ─────────────────────────────────────────────────────────────────────────────

