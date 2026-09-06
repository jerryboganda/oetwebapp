/**
 * Bulk import (ZIP) + content-generation orchestration — extracted from
 * `lib/api.ts`. Re-exported there, so `@/lib/api` imports keep working.
 *
 * Typed wrappers for the bulk-import (ZIP), rulebook-import, and
 * content-generation orchestration endpoints used by the Wave-2 admin UI
 * (`/admin/content/papers/import-zip`, `/admin/rulebooks/import`,
 * `/admin/content/generation/jobs`).
 */
import { apiRequest } from './client';
import type {
  BulkImportApprovalInput,
  BulkImportCommitResult,
  BulkImportSessionResponse,
  GenerationJobListResponse,
  GenerationJobSummary,
  QueueGenerationInput,
} from '../types/admin/bulk-import';

/**
 * Stage a ZIP payload for bulk content import. The backend accepts a single
 * `multipart/form-data` field named `file`; we drop the JSON Content-Type so
 * the browser produces the correct boundary. Returns the parsed session +
 * manifest so the UI can render an approval table before commit.
 */
export async function adminStartZipImport(
  file: File,
): Promise<BulkImportSessionResponse> {
  const form = new FormData();
  form.append('file', file, file.name);
  return apiRequest<BulkImportSessionResponse>(
    '/v1/admin/imports/zip',
    { method: 'POST', body: form },
    { json: false },
  );
}

/**
 * Commit a previously-staged ZIP import session with the admin's per-paper
 * approval decisions. `approvals` MUST include one entry per proposalId
 * returned by `adminStartZipImport`; the backend treats unknown ids as
 * skipped and refuses unknown sessions / cross-admin commits.
 */
export async function adminCommitZipImport(
  sessionId: string,
  approvals: BulkImportApprovalInput[],
): Promise<BulkImportCommitResult> {
  return apiRequest<BulkImportCommitResult>(
    `/v1/admin/imports/zip/${encodeURIComponent(sessionId)}/commit`,
    { method: 'POST', body: JSON.stringify(approvals) },
  );
}

/**
 * Abort an in-flight chunked upload session. The dedicated ZIP-import flow
 * does not use chunked uploads (the `/imports/zip` endpoint takes a single
 * multipart payload directly), but this wrapper is exported so the UI can
 * discard orphaned upload sessions if a different flow staged them.
 */
export async function adminDiscardUpload(uploadId: string): Promise<void> {
  await apiRequest<void>(
    `/v1/admin/uploads/${encodeURIComponent(uploadId)}`,
    { method: 'DELETE' },
  );
}

/**
 * Convenience wrapper that returns the strongly-typed jobs list. The
 * underlying `fetchContentGenerationJobs(page, pageSize)` helper already
 * exists in ./api/content-studio and is kept unchanged; this wrapper just narrows the return
 * type so the new jobs page can avoid `unknown` casts.
 */
export async function adminListGenerationJobs(
  page = 1,
  pageSize = 20,
): Promise<GenerationJobListResponse> {
  return apiRequest<GenerationJobListResponse>(
    `/v1/admin/content/generation-jobs?page=${page}&pageSize=${pageSize}`,
  );
}

export async function adminGetGenerationJob(
  jobId: string,
): Promise<GenerationJobSummary> {
  return apiRequest<GenerationJobSummary>(
    `/v1/admin/content/generation-jobs/${encodeURIComponent(jobId)}`,
  );
}

/**
 * Strongly-typed wrapper around `POST /v1/admin/content/generate`. The
 * existing `queueContentGeneration` helper in ./api/content-studio accepts the same shape but
 * returns `unknown`; this wrapper documents the field set the new launcher
 * UI uses and narrows the return type.
 */
export async function adminQueueContentGeneration(
  payload: QueueGenerationInput,
): Promise<{ jobId?: string } & Record<string, unknown>> {
  return apiRequest<{ jobId?: string } & Record<string, unknown>>(
    '/v1/admin/content/generate',
    { method: 'POST', body: JSON.stringify(payload) },
  );
}
