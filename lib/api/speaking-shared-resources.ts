import { ensureFreshAccessToken } from '../auth-client';
import { ApiError, apiRequest, getHeaders, isRetryable, resolveApiUrl } from './client';
import { fetchWithTimeout } from '../network/fetch-with-timeout';

/**
 * Admin speaking shared resources + learner list + media download.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export interface SpeakingSharedResourceDto {
  id: string;
  kind: SpeakingSharedResourceKind;
  title: string;
  professionId: string | null;
  mediaAssetId: string;
  status: 'Draft' | 'InReview' | 'EditorReview' | 'PublisherApproval' | 'Published' | 'Rejected' | 'Archived';
  publishedAt: string | null;
  effectiveFrom: string | null;
  uploadedByUserId: string | null;
  createdAt: string;
  updatedAt: string;
  media: {
    id: string;
    originalFilename: string;
    mimeType: string;
    sizeBytes: number;
    sha256: string | null;
  } | null;
}

export interface SpeakingSharedResourceLearnerDto {
  id: string;
  kind: SpeakingSharedResourceKind;
  title: string;
  professionId: string | null;
  publishedAt: string | null;
  media: { id: string; originalFilename: string; sizeBytes: number } | null;
}

export async function adminListSpeakingSharedResources(params: { kind?: SpeakingSharedResourceKind; profession?: string } = {}): Promise<SpeakingSharedResourceDto[]> {
  const qs = new URLSearchParams();
  if (params.kind) qs.set('kind', params.kind);
  if (params.profession) qs.set('profession', params.profession);
  const q = qs.toString();
  return apiRequest<SpeakingSharedResourceDto[]>(`/v1/admin/speaking/shared-resources${q ? `?${q}` : ''}`);
}

export async function adminUploadSpeakingSharedResource(payload: {
  file: File;
  kind: SpeakingSharedResourceKind;
  title: string;
  professionId?: string | null;
}): Promise<SpeakingSharedResourceDto> {
  const form = new FormData();
  form.append('file', payload.file);
  form.append('kind', payload.kind);
  form.append('title', payload.title);
  if (payload.professionId) form.append('professionId', payload.professionId);
  const token = await ensureFreshAccessToken();
  const headers: Record<string, string> = {};
  if (token) headers['Authorization'] = `Bearer ${token}`;
  const response = await fetchWithTimeout(resolveApiUrl('/v1/admin/speaking/shared-resources'), {
    method: 'POST', headers, body: form,
  }, 120_000);
  if (!response.ok) {
    const text = await response.text().catch(() => '');
    throw new Error(`Upload failed: ${response.status} ${text}`);
  }
  return response.json() as Promise<SpeakingSharedResourceDto>;
}

export async function adminPublishSpeakingSharedResource(id: string): Promise<{ id: string; status: string }> {
  return apiRequest(`/v1/admin/speaking/shared-resources/${encodeURIComponent(id)}/publish`, { method: 'POST', body: '{}' });
}

export async function adminArchiveSpeakingSharedResource(id: string): Promise<{ id: string; status: string }> {
  return apiRequest(`/v1/admin/speaking/shared-resources/${encodeURIComponent(id)}/archive`, { method: 'POST', body: '{}' });
}

export async function adminDeleteSpeakingSharedResource(id: string): Promise<void> {
  await apiRequest<void>(`/v1/admin/speaking/shared-resources/${encodeURIComponent(id)}`, {
    method: 'DELETE',
  }, { acceptedStatuses: [204] });
}

/** Permanently removes a speaking shared resource row (MediaAsset left intact). system_admin only. */
export async function adminForceDeleteSpeakingSharedResource(id: string): Promise<void> {
  await apiRequest<void>(`/v1/admin/speaking/shared-resources/${encodeURIComponent(id)}/force-delete`, {
    method: 'POST',
  }, { acceptedStatuses: [204] });
}

export async function learnerListSpeakingSharedResources(): Promise<SpeakingSharedResourceLearnerDto[]> {
  return apiRequest<SpeakingSharedResourceLearnerDto[]>('/v1/speaking/shared-resources');
}

export async function downloadSpeakingSharedResourceMedia(assetId: string): Promise<Blob> {
  const path = `/v1/media/${encodeURIComponent(assetId)}/content`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    method: 'GET',
    headers: await getHeaders(path, undefined, { json: false }),
  }, 120_000);
  if (!response.ok) {
    throw new ApiError(response.status, 'speaking_shared_resource_download_failed', `Speaking resource download failed: ${response.status}`, isRetryable(response.status));
  }
  return response.blob();
}

// ─────────────────────────────────────────────────────────────────────────────
// Real Content folder importer
// ─────────────────────────────────────────────────────────────────────────────

export type RealContentTarget =
  | 'ListeningPaper' | 'ReadingPaper' | 'WritingPaper' | 'SpeakingPaper'
  | 'RecallDocument' | 'ResultTemplate' | 'SpeakingSharedResource'
  | 'RulebookReferencePdf' | 'ScoringPolicyBody';


export type SpeakingSharedResourceKind = 'WarmUpQuestions' | 'AssessmentCriteria';
