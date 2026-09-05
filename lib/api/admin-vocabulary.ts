/**
 * Admin vocabulary management (items, recall sets, bulk ops, audio,
 * import batches, AI drafts) — extracted from `lib/api.ts`.
 * Re-exported there, so `@/lib/api` imports keep working.
 */
import { ApiError, apiRequest, getHeaders, resolveApiUrl } from './client';
import { fetchWithTimeout } from '../network/fetch-with-timeout';

export async function fetchAdminVocabularyItems(params?: {
  profession?: string; category?: string; status?: string; search?: string;
  recallSet?: string;
  page?: number; pageSize?: number;
}) {
  const p = new URLSearchParams();
  if (params?.profession) p.set('profession', params.profession);
  if (params?.category) p.set('category', params.category);
  if (params?.status) p.set('status', params.status);
  if (params?.search) p.set('search', params.search);
  if (params?.recallSet) p.set('recallSet', params.recallSet);
  if (params?.page) p.set('page', String(params.page));
  if (params?.pageSize) p.set('pageSize', String(params.pageSize));
  const qs = p.toString();
  return apiRequest(`/v1/admin/vocabulary/items${qs ? `?${qs}` : ''}`);
}

/**
 * Admin recall-set registry: canonical 3-set list with per-status counts
 * (active / draft / archived / total). See `RecallSetCodes` on the backend.
 */
export interface AdminRecallSetSummary {
  code: string;
  displayName: string;
  shortLabel: string;
  description: string;
  sortOrder: number;
  active: number;
  draft: number;
  archived: number;
  total: number;
}
export async function fetchAdminVocabularyRecallSets(params?: { examTypeCode?: string; professionId?: string }) {
  const p = new URLSearchParams();
  if (params?.examTypeCode) p.set('examTypeCode', params.examTypeCode);
  if (params?.professionId) p.set('professionId', params.professionId);
  const qs = p.toString();
  return apiRequest(`/v1/admin/vocabulary/recall-sets${qs ? `?${qs}` : ''}`) as Promise<{
    examTypeCode: string | null;
    professionId: string | null;
    sets: AdminRecallSetSummary[];
  }>;
}

export async function fetchAdminVocabularyItem(itemId: string) {
  return apiRequest(`/v1/admin/vocabulary/items/${encodeURIComponent(itemId)}`);
}

export async function createAdminVocabularyItem(payload: Record<string, unknown>) {
  return apiRequest('/v1/admin/vocabulary/items', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function updateAdminVocabularyItem(itemId: string, payload: Record<string, unknown>) {
  return apiRequest(`/v1/admin/vocabulary/items/${encodeURIComponent(itemId)}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function deleteAdminVocabularyItem(itemId: string) {
  return apiRequest(`/v1/admin/vocabulary/items/${encodeURIComponent(itemId)}`, {
    method: 'DELETE',
  });
}

export type AdminVocabularyBulkDeleteResponse = {
  totalRequested: number;
  deleted: number;
  archived: number;
  failed: number;
  errors: string[];
};

export async function deleteAdminVocabularyItems(itemIds: string[]): Promise<AdminVocabularyBulkDeleteResponse> {
  return apiRequest('/v1/admin/vocabulary/items/bulk-delete', {
    method: 'POST',
    body: JSON.stringify({ itemIds }),
  }) as Promise<AdminVocabularyBulkDeleteResponse>;
}

export type AdminVocabularyBulkActivateResponse = {
  totalRequested: number;
  activated: number;
  skipped: number;
  failed: number;
  errors: string[];
};

export async function bulkActivateAdminVocabularyItems(itemIds: string[]): Promise<AdminVocabularyBulkActivateResponse> {
  return apiRequest('/v1/admin/vocabulary/items/bulk-activate', {
    method: 'POST',
    body: JSON.stringify({ itemIds }),
  }) as Promise<AdminVocabularyBulkActivateResponse>;
}

export type AdminVocabularyBulkArchiveResponse = {
  totalRequested: number;
  archived: number;
  skipped: number;
};

export async function bulkArchiveAdminVocabularyItems(itemIds: string[]): Promise<AdminVocabularyBulkArchiveResponse> {
  return apiRequest('/v1/admin/vocabulary/items/bulk-archive', {
    method: 'POST',
    body: JSON.stringify({ itemIds }),
  }) as Promise<AdminVocabularyBulkArchiveResponse>;
}

export type AdminVocabularyBulkDraftResponse = {
  totalRequested: number;
  drafted: number;
  skipped: number;
};

export async function bulkDraftAdminVocabularyItems(itemIds: string[]): Promise<AdminVocabularyBulkDraftResponse> {
  return apiRequest('/v1/admin/vocabulary/items/bulk-draft', {
    method: 'POST',
    body: JSON.stringify({ itemIds }),
  }) as Promise<AdminVocabularyBulkDraftResponse>;
}

export type AdminVocabularyBulkPreviewResponse = {
  totalRequested: number;
  updated: number;
  failed: number;
  freePreviewTotal: number;
  errors: string[];
};

/**
 * Set or clear the free-preview flag on the given vocabulary terms. Free-preview
 * terms are the only Recall Vocabulary Bank terms a non-subscribed learner can
 * access. Admin-curated — no automatic cap.
 */
export async function setAdminVocabularyFreePreview(
  itemIds: string[],
  isFreePreview: boolean,
): Promise<AdminVocabularyBulkPreviewResponse> {
  return apiRequest('/v1/admin/vocabulary/items/bulk-free-preview', {
    method: 'POST',
    body: JSON.stringify({ itemIds, isFreePreview }),
  }) as Promise<AdminVocabularyBulkPreviewResponse>;
}

export type AdminVocabularyAudioProgress = {
  total: number;
  withAudio: number;
  pending: number;
  percentComplete: number;
};

export async function fetchAdminVocabularyAudioProgress(): Promise<AdminVocabularyAudioProgress> {
  // no-store: the progress endpoint must never be served from cache, otherwise
  // the admin panel can pin to a stale "N pending" snapshot forever.
  return apiRequest('/v1/admin/vocabulary/audio/progress', { cache: 'no-store' }) as Promise<AdminVocabularyAudioProgress>;
}

export type AdminVocabularyAudioGenerateResponse = {
  totalRequested: number;
  enqueued: number;
  skipped: number;
  notFound: number;
  dryRun: boolean;
  batchId: string;
};

/**
 * Enqueue ElevenLabs audio synthesis for a set of vocabulary terms.
 * - `forceRegenerate=false` (default): only terms missing/broken audio are queued;
 *   terms that already have working audio are skipped. Used by the bulk
 *   "Auto-generate missing audios" action.
 * - `forceRegenerate=true`: re-synthesise every term, overwriting existing audio.
 *   Used by the per-row "Regenerate audio" button.
 * - `dryRun=true`: return the counts without enqueuing anything (cost preview).
 *
 * The id list is chunked (≤1000) to stay under the server's per-call cap; the
 * per-chunk counts are summed. `batchId` from the first chunk is returned.
 */
export async function generateAdminVocabularyAudio(
  itemIds: string[],
  opts?: { forceRegenerate?: boolean; dryRun?: boolean },
): Promise<AdminVocabularyAudioGenerateResponse> {
  const forceRegenerate = opts?.forceRegenerate ?? false;
  const dryRun = opts?.dryRun ?? false;
  const CHUNK = 1000;
  const agg: AdminVocabularyAudioGenerateResponse = {
    totalRequested: 0, enqueued: 0, skipped: 0, notFound: 0, dryRun, batchId: '',
  };
  for (let i = 0; i < itemIds.length; i += CHUNK) {
    const chunk = itemIds.slice(i, i + CHUNK);
    const res = (await apiRequest('/v1/admin/vocabulary/items/audio/generate', {
      method: 'POST',
      body: JSON.stringify({ itemIds: chunk, forceRegenerate, dryRun }),
    })) as AdminVocabularyAudioGenerateResponse;
    agg.totalRequested += res.totalRequested;
    agg.enqueued += res.enqueued;
    agg.skipped += res.skipped;
    agg.notFound += res.notFound;
    if (!agg.batchId) agg.batchId = res.batchId;
  }
  return agg;
}

export async function fetchAdminVocabularyCategories(params?: { examTypeCode?: string; professionId?: string }) {
  const p = new URLSearchParams();
  if (params?.examTypeCode) p.set('examTypeCode', params.examTypeCode);
  if (params?.professionId) p.set('professionId', params.professionId);
  const qs = p.toString();
  return apiRequest(`/v1/admin/vocabulary/categories${qs ? `?${qs}` : ''}`);
}

export async function previewAdminVocabularyImport(file: File, importBatchId?: string, recallSetCode?: string) {
  const formData = new FormData();
  formData.append('file', file);
  const params = new URLSearchParams();
  if (importBatchId) params.set('importBatchId', importBatchId);
  if (recallSetCode) params.set('recallSetCode', recallSetCode);
  const qs = params.toString();
  const response = await fetchWithTimeout(resolveApiUrl(`/v1/admin/vocabulary/import/preview${qs ? `?${qs}` : ''}`), {
    method: 'POST',
    headers: await getHeaders('/v1/admin/vocabulary/import/preview', undefined, { json: false }),
    body: formData,
  }, 60_000);
  if (!response.ok) {
    let code = 'preview_failed';
    let message = `Preview failed: ${response.status}`;
    try {
      const err = await response.json();
      code = err.code ?? err.errorCode ?? code;
      message = err.message ?? err.error ?? message;
    } catch { /* ignore */ }
    throw new ApiError(response.status, code, message, false);
  }
  return response.json();
}

export async function bulkImportAdminVocabulary(file: File, dryRun = true, importBatchId?: string, recallSetCode?: string) {
  const formData = new FormData();
  formData.append('file', file);
  const params = new URLSearchParams({ dryRun: String(dryRun) });
  if (importBatchId) params.set('importBatchId', importBatchId);
  if (recallSetCode) params.set('recallSetCode', recallSetCode);
  const response = await fetchWithTimeout(resolveApiUrl(`/v1/admin/vocabulary/import?${params.toString()}`), {
    method: 'POST',
    headers: await getHeaders('/v1/admin/vocabulary/import', undefined, { json: false }),
    body: formData,
  }, 120_000);
  if (!response.ok) {
    let code = 'import_failed';
    let message = `Import failed: ${response.status}`;
    try {
      const err = await response.json();
      code = err.code ?? err.errorCode ?? code;
      message = err.message ?? err.error ?? message;
    } catch { /* ignore */ }
    throw new ApiError(response.status, code, message, false);
  }
  return response.json();
}

export async function fetchAdminVocabularyImportBatch(importBatchId: string) {
  return apiRequest(`/v1/admin/vocabulary/import/batches/${encodeURIComponent(importBatchId)}`);
}

export async function backfillAdminVocabularyAudio(batchId?: string) {
  const qs = batchId ? `?batchId=${encodeURIComponent(batchId)}` : '';
  return apiRequest(`/v1/admin/vocabulary/audio/backfill${qs}`, { method: 'POST' });
}

export async function cancelAdminVocabularyImportAudio(importBatchId: string) {
  return apiRequest(`/v1/admin/vocabulary/import/batches/${encodeURIComponent(importBatchId)}/audio/cancel`, {
    method: 'POST',
  });
}

export async function resumeAdminVocabularyAudio() {
  return apiRequest(`/v1/admin/vocabulary/audio/resume`, { method: 'POST' });
}

export async function exportAdminVocabularyImportBatchCsv(importBatchId: string) {
  const path = `/v1/admin/vocabulary/import/batches/${encodeURIComponent(importBatchId)}/export`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    method: 'GET',
    headers: await getHeaders(path),
  }, 60_000);
  if (!response.ok) {
    throw new ApiError(response.status, 'export_failed', `Export failed: ${response.status}`, false);
  }
  return response.blob();
}

export async function reconcileAdminVocabularyImportBatch(importBatchId: string, file: File) {
  const formData = new FormData();
  formData.append('file', file);
  const path = `/v1/admin/vocabulary/import/batches/${encodeURIComponent(importBatchId)}/reconcile`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    method: 'POST',
    headers: await getHeaders(path, undefined, { json: false }),
    body: formData,
  }, 120_000);
  if (!response.ok) {
    let code = 'reconcile_failed';
    let message = `Reconciliation failed: ${response.status}`;
    try {
      const err = await response.json();
      code = err.code ?? err.errorCode ?? code;
      message = err.message ?? err.error ?? message;
    } catch { /* ignore */ }
    throw new ApiError(response.status, code, message, false);
  }
  return response.json();
}

export async function rollbackAdminVocabularyImportBatch(importBatchId: string, deleteDraftRows = false) {
  return apiRequest(`/v1/admin/vocabulary/import/batches/${encodeURIComponent(importBatchId)}/rollback`, {
    method: 'POST',
    body: JSON.stringify({ deleteDraftRows }),
  });
}

export async function requestAdminVocabularyAiDraft(payload: {
  count: number;
  examTypeCode: string;
  professionId?: string | null;
  category: string;
  difficulty?: string;
  seedPrompt?: string;
}) {
  return apiRequest('/v1/admin/vocabulary/ai/draft', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function acceptAdminVocabularyAiDrafts(payload: {
  examTypeCode: string;
  professionId?: string | null;
  sourceProvenance: string;
  drafts: Array<Record<string, unknown>>;
}) {
  return apiRequest('/v1/admin/vocabulary/ai/draft/accept', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}
