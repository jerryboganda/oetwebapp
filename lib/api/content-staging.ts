import { ensureFreshAccessToken } from '../auth-client';
import type { RealContentTarget } from './speaking-shared-resources';
import { fetchWithTimeout } from '../network/fetch-with-timeout';
import { ApiError, apiRequest, getHeaders, isRetryable, resolveApiUrl } from './client';

/**
 * Real-content folder staging/commit + rulebook PDF + media asset downloads.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export interface RealContentProposalDto {
  target: RealContentTarget;
  title: string;
  subtest: string | null;
  professionId: string | null;
  cardType: string | null;
  letterType: string | null;
  periodLabel: string | null;
  templateKey: string | null;
  sharedResourceKind: string | null;
  rulebookKind: string | null;
  rulebookProfession: string | null;
  sourcePath: string;
  assets: Array<{ role: string; part: string | null; sourcePath: string; originalFilename: string | null }>;
}

export interface RealContentStageResultDto {
  sessionId: string;
  uploadedFilename: string;
  stagedAt: string;
  proposals: RealContentProposalDto[];
  issues: string[];
}

export interface RealContentCommitResultDto {
  created: Array<{ target: RealContentTarget; id: string; title: string }>;
  errors: string[];
}

export async function adminStageRealContentFolder(file: File): Promise<RealContentStageResultDto> {
  const form = new FormData();
  form.append('file', file);
  const token = await ensureFreshAccessToken();
  const headers: Record<string, string> = {};
  if (token) headers['Authorization'] = `Bearer ${token}`;
  const response = await fetchWithTimeout(
    resolveApiUrl('/v1/admin/imports/real-content-folder/stage'),
    { method: 'POST', headers, body: form },
    600_000, // 10 min for large ZIPs
  );
  if (!response.ok) {
    const text = await response.text().catch(() => '');
    throw new Error(`Stage failed: ${response.status} ${text}`);
  }
  return response.json() as Promise<RealContentStageResultDto>;
}

export async function adminCommitRealContentFolder(
  sessionId: string,
  approvedSourcePaths?: string[],
): Promise<RealContentCommitResultDto> {
  return apiRequest<RealContentCommitResultDto>(
    `/v1/admin/imports/real-content-folder/${encodeURIComponent(sessionId)}/commit`,
    {
      method: 'POST',
      body: JSON.stringify({ approvedSourcePaths: approvedSourcePaths ?? null }),
    },
  );
}

export async function downloadRulebookReferencePdfMedia(assetId: string): Promise<Blob> {
  const path = `/v1/media/${encodeURIComponent(assetId)}/content`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    method: 'GET',
    headers: await getHeaders(path, undefined, { json: false }),
  }, 120_000);
  if (!response.ok) {
    throw new ApiError(response.status, 'rulebook_reference_pdf_download_failed', `Rulebook reference PDF download failed: ${response.status}`, isRetryable(response.status));
  }
  return response.blob();
}

/** Generic authenticated fetch of a MediaAsset's bytes (PDF/audio/image) for
 * in-app preview. Caller is responsible for `URL.createObjectURL` lifecycle. */
export async function downloadMediaAssetContent(assetId: string): Promise<Blob> {
  const path = `/v1/media/${encodeURIComponent(assetId)}/content`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    method: 'GET',
    headers: await getHeaders(path, undefined, { json: false }),
  }, 120_000);
  if (!response.ok) {
    throw new ApiError(response.status, 'media_asset_download_failed', `Media download failed: ${response.status}`, isRetryable(response.status));
  }
  return response.blob();
}
