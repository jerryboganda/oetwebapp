/**
 * AI Conversation (learner sessions + transcript export) and admin
 * conversation templates/sessions/voice — extracted from `lib/api.ts`.
 * Re-exported there, so `@/lib/api` imports keep working.
 */
import { ApiError, apiBlobRequest, apiRequest, getHeaders, isRetryable, resolveApiUrl } from './client';
import { fetchWithTimeout } from '../network/fetch-with-timeout';

export async function createConversation(params: {
  contentId?: string;
  examFamilyCode?: string;
  taskTypeCode: string;
  profession?: string;
  difficulty?: string;
}) {
  return apiRequest('/v1/conversations', {
    method: 'POST',
    body: JSON.stringify(params),
  });
}

export async function getConversation(sessionId: string) {
  return apiRequest(`/v1/conversations/${encodeURIComponent(sessionId)}`);
}

export async function resumeConversation(sessionId: string, resumeToken?: string) {
  return apiRequest(`/v1/conversations/${encodeURIComponent(sessionId)}/resume`, {
    method: 'POST',
    body: JSON.stringify(resumeToken ? { resumeToken } : {}),
  });
}

export function conversationTranscriptExportUrl(sessionId: string, format: 'txt' | 'pdf' = 'txt') {
  return resolveApiUrl(`/v1/conversations/${encodeURIComponent(sessionId)}/transcript/export?format=${encodeURIComponent(format)}`);
}

export async function downloadConversationTranscript(sessionId: string, format: 'txt' | 'pdf' = 'txt'): Promise<Blob> {
  const path = `/v1/conversations/${encodeURIComponent(sessionId)}/transcript/export?format=${encodeURIComponent(format)}`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    method: 'GET',
    headers: await getHeaders(path, undefined, { json: false }),
  });
  if (!response.ok) {
    throw new ApiError(response.status, 'transcript_export_failed', `Transcript export failed: ${response.status}`, isRetryable(response.status));
  }
  return response.blob();
}

export async function completeConversation(sessionId: string) {
  return apiRequest(`/v1/conversations/${encodeURIComponent(sessionId)}/complete`, {
    method: 'POST',
  });
}

export async function getConversationEvaluation(sessionId: string) {
  return apiRequest(`/v1/conversations/${encodeURIComponent(sessionId)}/evaluation`);
}

export async function getConversationHistory(page = 1, pageSize = 10) {
  return apiRequest(`/v1/conversations/history?page=${page}&pageSize=${pageSize}`);
}

export async function getConversationTaskTypes() {
  return apiRequest('/v1/conversations/task-types');
}

export async function getConversationEntitlement() {
  return apiRequest('/v1/conversations/entitlement');
}

export async function fetchAdminConversationTemplates(params?: {
  profession?: string;
  status?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}) {
  const q = new URLSearchParams();
  if (params?.profession) q.set('profession', params.profession);
  if (params?.status) q.set('status', params.status);
  if (params?.search) q.set('search', params.search);
  if (params?.page) q.set('page', String(params.page));
  if (params?.pageSize) q.set('pageSize', String(params.pageSize));
  const qs = q.toString();
  return apiRequest(`/v1/admin/conversation/templates${qs ? `?${qs}` : ''}`);
}

export async function fetchAdminConversationTemplate(templateId: string) {
  return apiRequest(`/v1/admin/conversation/templates/${encodeURIComponent(templateId)}`);
}

export async function createAdminConversationTemplate(body: Record<string, unknown>) {
  return apiRequest('/v1/admin/conversation/templates', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export async function updateAdminConversationTemplate(templateId: string, body: Record<string, unknown>) {
  return apiRequest(`/v1/admin/conversation/templates/${encodeURIComponent(templateId)}`, {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

export async function publishAdminConversationTemplate(templateId: string) {
  return apiRequest(`/v1/admin/conversation/templates/${encodeURIComponent(templateId)}/publish`, {
    method: 'POST',
  });
}

export async function archiveAdminConversationTemplate(templateId: string) {
  return apiRequest(`/v1/admin/conversation/templates/${encodeURIComponent(templateId)}/archive`, {
    method: 'POST',
  });
}

/** Permanently deletes an archived conversation template + all learner sessions. system_admin only. */
export async function forceDeleteAdminConversationTemplate(templateId: string) {
  return apiRequest(`/v1/admin/conversation/templates/${encodeURIComponent(templateId)}/force-delete`, {
    method: 'POST',
  });
}

export async function fetchAdminConversationSettings() {
  return apiRequest('/v1/admin/conversation/settings');
}

export async function updateAdminConversationSettings(body: Record<string, unknown>) {
  return apiRequest('/v1/admin/conversation/settings', {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

export interface AdminLaunchReadinessSettings {
  enforceClientVersionGate: boolean;
  mobileMinSupportedVersion: string;
  mobileLatestVersion: string;
  mobileForceUpdate: boolean;
  iosAppStoreUrl: string | null;
  androidPlayStoreUrl: string | null;
  iosBundleId: string | null;
  appleTeamId: string | null;
  appleAssociatedDomainStatus: string | null;
  appleUniversalLinksStatus: string | null;
  iosSigningProfileReference: string | null;
  iosIapStatus: string | null;
  iosPushStatus: string | null;
  androidPackageName: string | null;
  androidSha256Fingerprints: string | null;
  androidSigningKeyReference: string | null;
  androidAssetLinksStatus: string | null;
  androidIapStatus: string | null;
  androidPushStatus: string | null;
  desktopMinSupportedVersion: string;
  desktopLatestVersion: string;
  desktopForceUpdate: boolean;
  desktopUpdateFeedUrl: string | null;
  desktopUpdateChannel: string | null;
  windowsSigningStatus: string | null;
  macSigningStatus: string | null;
  linuxSigningStatus: string | null;
  deviceValidationEvidenceUrl: string | null;
  deviceValidationNotes: string | null;
  realtimeLegalApprovalStatus: string | null;
  realtimePrivacyApprovalStatus: string | null;
  realtimeProtectedSmokeStatus: string | null;
  realtimeEvidenceUrl: string | null;
  realtimeSpendCapApproved: boolean;
  realtimeTopologyApproved: boolean;
  releaseOwnerApprovalStatus: string | null;
  launchNotes: string | null;
  updatedAt: string;
  updatedByAdminId: string | null;
  updatedByAdminName: string | null;
}

export async function fetchAdminLaunchReadinessSettings(): Promise<AdminLaunchReadinessSettings> {
  return apiRequest<AdminLaunchReadinessSettings>('/v1/admin/launch-readiness/settings');
}

export async function updateAdminLaunchReadinessSettings(
  body: Partial<AdminLaunchReadinessSettings>,
): Promise<AdminLaunchReadinessSettings> {
  return apiRequest<AdminLaunchReadinessSettings>('/v1/admin/launch-readiness/settings', {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

/** Public server-driven release policy for a given shell platform. */
export interface AppReleasePolicy {
  platform: string;
  minVersion: string;
  latestVersion: string;
  forceUpdate: boolean;
  storeUrl: string | null;
  updateFeedUrl: string | null;
  channel: string | null;
}

/**
 * Reads the anonymous release policy the forced-update gate uses on boot.
 * `platform` is 'android' | 'ios' | 'desktop' (or a synonym the backend maps).
 */
export async function fetchAppReleasePolicy(platform: string): Promise<AppReleasePolicy> {
  return apiRequest<AppReleasePolicy>(`/v1/app-release?platform=${encodeURIComponent(platform)}`);
}

export async function adminConversationTtsPreview(body: { text?: string; voice?: string; locale?: string; modelVariant?: string; instructions?: string }) {
  return apiBlobRequest('/v1/admin/conversation/tts-preview', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

// A voice from the ElevenLabs catalogue (GET /v1/voices), surfaced so admins
// can browse, audition, and pick the platform default voice id.
export interface AdminElevenLabsVoice {
  voiceId: string;
  name: string;
  category?: string | null;
  previewUrl?: string | null;
  labels?: Record<string, string> | null;
}

export async function getElevenLabsVoices(): Promise<{ voices: AdminElevenLabsVoice[] }> {
  return apiRequest<{ voices: AdminElevenLabsVoice[] }>('/v1/admin/voice-design/elevenlabs/voices');
}

export async function fetchAdminConversationSessions(params?: {
  userId?: string;
  state?: string;
  taskTypeCode?: string;
  page?: number;
  pageSize?: number;
}) {
  const q = new URLSearchParams();
  if (params?.userId) q.set('userId', params.userId);
  if (params?.state) q.set('state', params.state);
  if (params?.taskTypeCode) q.set('taskTypeCode', params.taskTypeCode);
  if (params?.page) q.set('page', String(params.page));
  if (params?.pageSize) q.set('pageSize', String(params.pageSize));
  const qs = q.toString();
  return apiRequest(`/v1/admin/conversation/sessions${qs ? `?${qs}` : ''}`);
}

export async function fetchAdminConversationSessionDetail(sessionId: string) {
  return apiRequest(`/v1/admin/conversation/sessions/${encodeURIComponent(sessionId)}`);
}
