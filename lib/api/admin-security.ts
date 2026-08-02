/**
 * Admin security console client (Course Platform Security Requirements
 * §4.4): security-events feed, computed alerts, and per-account
 * session/device management. `AdminSecurityRead`/`AdminSecurityWrite`-gated
 * on the backend (see AdminSecurityEndpoints.cs).
 */

import { apiClient } from '@/lib/api';

export interface AdminSecurityEventRow {
  id: string;
  occurredAt: string;
  authAccountId: string | null;
  kind: string;
  severity: string;
  ipAddress: string | null;
  countryCode: string | null;
  userAgent: string | null;
  platform: string | null;
  sessionFamilyId: string | null;
  deviceId: string | null;
  detailsJson: string | null;
}

export interface AdminSecurityEventPage {
  items: AdminSecurityEventRow[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface FetchAdminSecurityEventsParams {
  accountId?: string;
  kind?: string;
  severity?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

export async function fetchAdminSecurityEvents(
  params?: FetchAdminSecurityEventsParams,
): Promise<AdminSecurityEventPage> {
  const qs = new URLSearchParams();
  if (params?.accountId) qs.set('accountId', params.accountId);
  if (params?.kind) qs.set('kind', params.kind);
  if (params?.severity) qs.set('severity', params.severity);
  if (params?.from) qs.set('from', params.from);
  if (params?.to) qs.set('to', params.to);
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  return apiClient.get<AdminSecurityEventPage>(`/v1/admin/security/events${q ? `?${q}` : ''}`);
}

export interface AdminSecuritySession {
  familyId: string;
  ipAddress: string | null;
  countryCode: string | null;
  deviceInfo: string | null;
  createdAt: string;
  lastUsedAt: string | null;
  expiresAt: string;
}

export interface AdminSecurityDevice {
  id: string;
  deviceId: string;
  deviceName: string | null;
  platform: string | null;
  trustedAt: string;
  lastSeenAt: string | null;
  revokedAt: string | null;
}

export async function fetchAdminUserSessions(userId: string): Promise<AdminSecuritySession[]> {
  return apiClient.get<AdminSecuritySession[]>(`/v1/admin/users/${encodeURIComponent(userId)}/security/sessions`);
}

export async function fetchAdminUserDevices(userId: string): Promise<AdminSecurityDevice[]> {
  return apiClient.get<AdminSecurityDevice[]>(`/v1/admin/users/${encodeURIComponent(userId)}/security/devices`);
}

export async function revokeAdminUserSession(
  userId: string,
  familyId: string,
): Promise<{ revoked: boolean }> {
  return apiClient.post<{ revoked: boolean }>(
    `/v1/admin/users/${encodeURIComponent(userId)}/security/sessions/${encodeURIComponent(familyId)}/revoke`,
    {},
  );
}

export async function resetAdminUserDevice(userId: string): Promise<{ reset: boolean }> {
  return apiClient.post<{ reset: boolean }>(`/v1/admin/users/${encodeURIComponent(userId)}/security/devices/reset`, {});
}

export async function blockAdminUserPlayback(userId: string): Promise<{ revoked: number }> {
  return apiClient.post<{ revoked: number }>(`/v1/admin/users/${encodeURIComponent(userId)}/security/block-playback`, {});
}

export async function toggleAdminUserDeviceExemption(
  userId: string,
  exempt: boolean,
): Promise<{ exempt: boolean }> {
  return apiClient.post<{ exempt: boolean }>(
    `/v1/admin/users/${encodeURIComponent(userId)}/security/device-exemption`,
    { exempt },
  );
}

export async function clearAdminUserDeviceCooldown(userId: string): Promise<{ cleared: boolean }> {
  return apiClient.post<{ cleared: boolean }>(
    `/v1/admin/users/${encodeURIComponent(userId)}/security/clear-cooldown`,
    {},
  );
}
