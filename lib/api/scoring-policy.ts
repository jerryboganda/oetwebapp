import { ensureFreshAccessToken } from '../auth-client';
import { ApiError, apiRequest, resolveApiUrl } from './client';
import { fetchWithTimeout } from '../network/fetch-with-timeout';

/**
 * Admin scoring policy CRUD + rulebook reference PDFs.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
// -----------------------------------------------------------------------------
// Scoring Policy (admin singleton document + learner read)
// -----------------------------------------------------------------------------

export interface ScoringPolicyDto {
  id: string;
  bodyMarkdown: string;
  policyJson: string;
  isActive: boolean;
  updatedByUserId: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ScoringPolicyLearnerDto {
  id: string;
  bodyMarkdown: string;
  policyJson: string;
  updatedAt: string;
}

export async function adminGetScoringPolicy(): Promise<ScoringPolicyDto | null> {
  return apiRequest<ScoringPolicyDto | null>('/v1/admin/scoring-policy');
}

export async function adminUpdateScoringPolicy(payload: {
  bodyMarkdown: string;
  policyJson: string;
}): Promise<ScoringPolicyDto> {
  return apiRequest<ScoringPolicyDto>('/v1/admin/scoring-policy', {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function adminListScoringPolicyHistory(): Promise<ScoringPolicyDto[]> {
  return apiRequest<ScoringPolicyDto[]>('/v1/admin/scoring-policy/history');
}

export async function adminActivateScoringPolicy(id: string): Promise<ScoringPolicyDto> {
  return apiRequest<ScoringPolicyDto>(`/v1/admin/scoring-policy/${encodeURIComponent(id)}/activate`, {
    method: 'POST',
    body: '{}',
  });
}

export async function learnerGetScoringPolicy(): Promise<ScoringPolicyLearnerDto | null> {
  return apiRequest<ScoringPolicyLearnerDto | null>('/v1/scoring-policy');
}

// -----------------------------------------------------------------------------
// Rulebook reference PDF (human-readable companion to the JSON rulebook)
// -----------------------------------------------------------------------------

export interface RulebookReferencePdfDto {
  id: string;
  referencePdfAssetId: string;
  originalFilename: string;
  sizeBytes: number;
}

export async function adminUploadRulebookReferencePdf(rulebookId: string, file: File): Promise<RulebookReferencePdfDto> {
  const form = new FormData();
  form.append('file', file);
  const token = await ensureFreshAccessToken();
  const headers: Record<string, string> = {};
  if (token) headers['Authorization'] = `Bearer ${token}`;
  const response = await fetchWithTimeout(
    resolveApiUrl(`/v1/admin/rulebooks/${encodeURIComponent(rulebookId)}/reference-pdf`),
    { method: 'POST', headers, body: form },
    120_000,
  );
  if (!response.ok) {
    const text = await response.text().catch(() => '');
    throw new Error(`Upload failed: ${response.status} ${text}`);
  }
  return response.json() as Promise<RulebookReferencePdfDto>;
}

export async function adminDeleteRulebookReferencePdf(rulebookId: string): Promise<void> {
  await apiRequest<void>(
    `/v1/admin/rulebooks/${encodeURIComponent(rulebookId)}/reference-pdf`,
    { method: 'DELETE' },
    { acceptedStatuses: [204] },
  );
}

export async function learnerGetRulebookReferencePdf(kind: string, profession: string): Promise<{ rulebookId: string; referencePdfAssetId: string } | null> {
  try {
    return await apiRequest<{ rulebookId: string; referencePdfAssetId: string }>(`/v1/rulebooks/${encodeURIComponent(kind)}/${encodeURIComponent(profession)}/reference-pdf`);
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) return null;
    throw e;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Recall set tags (practice-collection labels) — admin CRUD
// ─────────────────────────────────────────────────────────────────────────────

