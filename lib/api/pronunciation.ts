/**
 * Pronunciation drills + admin CMS — extracted from `lib/api.ts`.
 * Re-exported there, so `@/lib/api` imports keep working.
 *
 * All pronunciation endpoints are protected by the backend's LearnerOnly policy
 * and the `pronunciation_analysis` feature flag. The recording+scoring flow:
 *   1. pronunciationInitAttempt(drillId) → { attemptId, uploadUrl, ... }
 *   2. pronunciationUploadAudio(drillId, attemptId, blob, durationMs)
 *   3. fetchPronunciationAssessment(assessmentId) for the result detail page.
 */
import { ApiError, apiRequest, getHeaders, resolveApiUrl } from './client';
import { fetchWithTimeout } from '../network/fetch-with-timeout';

export type PronunciationDrillSummary = {
  id: string;
  targetPhoneme: string;
  label: string;
  profession: string;
  focus: string;
  primaryRuleId: string | null;
  exampleWordsJson: string;
  minimalPairsJson: string;
  sentencesJson: string;
  difficulty: string;
  audioModelUrl: string | null;
  audioModelAssetId: string | null;
  tipsHtml: string;
};

export type PronunciationProgressItem = {
  phonemeCode: string;
  averageScore: number;
  attemptCount: number;
  lastPracticedAt: string | null;
  nextDueAt?: string | null;
  intervalDays?: number;
};

export type PronunciationEntitlement = {
  allowed: boolean;
  tier: string;
  remaining: number;
  limitPerWindow: number;
  windowDays: number;
  resetAt: string | null;
  reason: string;
};

export type PronunciationAssessmentDetail = {
  id: string;
  drillId: string | null;
  attemptId: string | null;
  accuracy: number;
  fluency: number;
  completeness: number;
  prosody: number;
  overall: number;
  projectedSpeakingScaled: number;
  projectedSpeakingGrade: string;
  wordScoresJson: string;
  problematicPhonemesJson: string;
  fluencyMarkersJson: string;
  findingsJson: string;
  feedbackJson: string;
  provider: string;
  rulebookVersion: string;
  createdAt: string;
};

export async function fetchPronunciationDrills(params?: {
  profession?: string;
  difficulty?: string;
  focus?: string;
}) {
  const q = new URLSearchParams();
  if (params?.profession) q.set('profession', params.profession);
  if (params?.difficulty) q.set('difficulty', params.difficulty);
  if (params?.focus) q.set('focus', params.focus);
  const suffix = q.toString() ? `?${q.toString()}` : '';
  return apiRequest(`/v1/pronunciation/drills${suffix}`);
}

export async function fetchPronunciationDueDrills(limit = 6) {
  return apiRequest(`/v1/pronunciation/drills/due?limit=${limit}`);
}

export async function fetchPronunciationDrill(drillId: string) {
  return apiRequest(`/v1/pronunciation/drills/${encodeURIComponent(drillId)}`);
}

export async function fetchMyPronunciationProgress() {
  return apiRequest('/v1/pronunciation/my-progress');
}

export async function fetchPronunciationProfile() {
  return apiRequest('/v1/pronunciation/profile');
}

export async function fetchPronunciationEntitlement() {
  return apiRequest('/v1/pronunciation/entitlement');
}

/** Reserve an attempt id + upload slot. Returns entitlement info too. */
export async function pronunciationInitAttempt(drillId: string) {
  return apiRequest(`/v1/pronunciation/drills/${encodeURIComponent(drillId)}/attempt/init`, {
    method: 'POST',
    body: JSON.stringify({}),
  });
}

/**
 * Upload the recorded audio blob and synchronously receive the scored
 * assessment. The request uses raw-body POST with the audio Content-Type set
 * directly — no multipart wrapper needed. Bypasses the JSON-first apiRequest.
 */
export async function pronunciationUploadAudio(
  drillId: string,
  attemptId: string,
  blob: Blob,
  options?: { durationMs?: number; signal?: AbortSignal }
) {
  const path = `/v1/pronunciation/drills/${encodeURIComponent(drillId)}/attempt/${encodeURIComponent(attemptId)}/audio`;
  const headers = new Headers(
    await getHeaders(path, undefined, { json: false })
  );
  headers.set('Content-Type', blob.type || 'application/octet-stream');
  if (typeof options?.durationMs === 'number') {
    headers.set('X-Audio-Duration-Ms', String(Math.round(options.durationMs)));
  }
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    method: 'POST',
    headers,
    body: blob,
    signal: options?.signal,
  }, 120_000);
  if (!response.ok) {
    let message = `Upload failed: ${response.status}`;
    let code = 'upload_failed';
    try {
      const err = await response.json();
      message = err.message ?? err.title ?? message;
      code = err.code ?? code;
    } catch {
      /* non-JSON error */
    }
    throw new ApiError(response.status, code, message, false);
  }
  return response.json();
}

export async function fetchPronunciationAssessment(assessmentId: string) {
  return apiRequest(`/v1/pronunciation/assessment/${encodeURIComponent(assessmentId)}`);
}

export async function fetchPronunciationSpeakingLinked(limit = 20) {
  return apiRequest(`/v1/pronunciation/speaking-linked?limit=${limit}`);
}

export async function submitPronunciationDiscrimination(
  drillId: string,
  roundsTotal: number,
  roundsCorrect: number,
) {
  return apiRequest(
    `/v1/pronunciation/drills/${encodeURIComponent(drillId)}/discrimination`,
    {
      method: 'POST',
      body: JSON.stringify({ roundsTotal, roundsCorrect }),
    }
  );
}

export async function fetchAdminPronunciationDrills(params?: {
  profession?: string;
  difficulty?: string;
  status?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}) {
  const q = new URLSearchParams();
  if (params?.profession) q.set('profession', params.profession);
  if (params?.difficulty) q.set('difficulty', params.difficulty);
  if (params?.status) q.set('status', params.status);
  if (params?.search) q.set('search', params.search);
  if (params?.page) q.set('page', String(params.page));
  if (params?.pageSize) q.set('pageSize', String(params.pageSize));
  const suffix = q.toString() ? `?${q.toString()}` : '';
  return apiRequest(`/v1/admin/pronunciation/drills${suffix}`);
}

export async function fetchAdminPronunciationDrill(drillId: string) {
  return apiRequest(`/v1/admin/pronunciation/drills/${encodeURIComponent(drillId)}`);
}

export async function createAdminPronunciationDrill(body: Record<string, unknown>) {
  return apiRequest('/v1/admin/pronunciation/drills', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export async function updateAdminPronunciationDrill(drillId: string, body: Record<string, unknown>) {
  return apiRequest(`/v1/admin/pronunciation/drills/${encodeURIComponent(drillId)}`, {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

export async function archiveAdminPronunciationDrill(drillId: string) {
  return apiRequest(`/v1/admin/pronunciation/drills/${encodeURIComponent(drillId)}/archive`, {
    method: 'POST',
    body: JSON.stringify({}),
  });
}

/** Permanently deletes an archived pronunciation drill + all learner attempts/assessments. system_admin only. */
export async function forceDeleteAdminPronunciationDrill(drillId: string) {
  return apiRequest(`/v1/admin/pronunciation/drills/${encodeURIComponent(drillId)}/force-delete`, {
    method: 'POST',
  });
}

export async function adminPronunciationAiDraft(body: {
  phoneme?: string;
  focus?: string;
  profession?: string;
  difficulty?: string;
  prompt?: string;
  primaryRuleId?: string;
}) {
  return apiRequest('/v1/admin/pronunciation/drills/ai-draft', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export type AdminPronunciationGenerateAudioResponse = {
  mediaAssetId: string;
  sha256: string;
  durationMs: number;
  bytes: number;
  providerName: string;
  mimeType: string;
  storageKey: string;
  url: string;
};

export async function generateAdminPronunciationModelAudio(
  drillId: string,
  body: { text: string; voiceId?: string },
): Promise<AdminPronunciationGenerateAudioResponse> {
  return apiRequest(
    `/v1/admin/pronunciation/drills/${encodeURIComponent(drillId)}/generate-model-audio`,
    {
      method: 'POST',
      body: JSON.stringify(body),
    },
  ) as Promise<AdminPronunciationGenerateAudioResponse>;
}
