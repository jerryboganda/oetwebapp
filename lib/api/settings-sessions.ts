import { apiRequest, type ApiRecord } from './client';
import { normalizeRouteValues } from './route-normalizer';
import { type SettingsSectionData, SettingsSectionId } from '../mock-data';
import { type CurrentUser } from '../types/auth';

/**
 * Exam families, settings sections, avatar, active sessions, trusted device.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export interface ExamFamiliesResponse {
  examFamilies?: { code: string; label?: string }[];
}

export async function fetchExamFamilies(): Promise<ExamFamiliesResponse> {
  return apiRequest<ExamFamiliesResponse>('/v1/reference/exam-families');
}

export interface SettingsDataResponse {
  audio?: { lowBandwidthMode?: boolean };
  [key: string]: unknown;
}

export async function fetchSettingsData(): Promise<SettingsDataResponse> {
  return apiRequest<SettingsDataResponse>('/v1/settings');
}

export async function fetchSettingsSection(section: SettingsSectionId): Promise<SettingsSectionData> {
  const data = await apiRequest<ApiRecord>(`/v1/settings/${section}`);
  return {
    section,
    values: normalizeRouteValues(data.values ?? {}),
  };
}

export interface UpdateSettingsSectionResponse {
  values?: Record<string, unknown>;
  [key: string]: unknown;
}

/**
 * `currentPassword` is only required by the backend when `section === 'profile'` and
 * `values.email` differs from the account's current email (H1 security gate — see
 * LearnerService.PatchSettingsSectionAsync). Every other section/field ignores it.
 */
export async function updateSettingsSection(section: 'profile' | 'goals' | 'notifications' | 'privacy' | 'accessibility' | 'audio' | 'study', values: Record<string, unknown>, currentPassword?: string): Promise<UpdateSettingsSectionResponse> {
  return apiRequest<UpdateSettingsSectionResponse>(`/v1/settings/${section}`, {
    method: 'PATCH',
    body: JSON.stringify(currentPassword ? { values, currentPassword } : { values }),
  });
}

/** Sets or clears (null) the learner's avatar. `avatarUrl` must already be a `/v1/media/{id}/content` path from `uploadMedia`. */
export async function updateMyAvatar(avatarUrl: string | null): Promise<CurrentUser> {
  return apiRequest<CurrentUser>('/v1/me/avatar', {
    method: 'PUT',
    body: JSON.stringify({ avatarUrl }),
  });
}

// ── Session Management ──

export interface ActiveSession {
  id: string;
  deviceInfo: string | null;
  ipAddress: string | null;
  lastUsedAt: string | null;
  createdAt: string;
  isCurrent: boolean;
  countryCode?: string | null;
  platform?: string | null;
  deviceId?: string | null;
}

/** The account's currently-trusted device (security spec §3.2) — null until
 * one is bootstrapped on first sign-in with a device id. */
export interface TrustedDeviceSelf {
  deviceName: string | null;
  platform: string | null;
  trustedAt: string;
  lastSeenAt: string | null;
  isCurrentDevice: boolean;
  activeDeviceCount?: number;
  maxDevices?: number;
}

export async function fetchActiveSessions(): Promise<ActiveSession[]> {
  const data = await apiRequest<{ sessions: ActiveSession[] }>('/v1/auth/sessions');
  return Array.isArray(data.sessions) ? data.sessions : [];
}

export async function fetchTrustedDevice(): Promise<TrustedDeviceSelf | null> {
  return (await apiRequest<TrustedDeviceSelf | null>('/v1/auth/device')) ?? null;
}

export async function revokeSession(sessionId: string): Promise<void> {
  await apiRequest<void>(`/v1/auth/sessions/${sessionId}`, { method: 'DELETE' });
}

export async function revokeAllOtherSessions(): Promise<{ revokedCount: number }> {
  return apiRequest<{ revokedCount: number }>('/v1/auth/sessions', { method: 'DELETE' });
}

