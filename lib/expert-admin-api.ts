/**
 * Admin Expert API client. Matches backend endpoints in
 * `ExpertAdminEndpoints.cs`. Powers the /admin/experts/specialties surface
 * that backfills the auto-assigner's profession-competency map.
 */

import { env } from './env';
import { ensureFreshAccessToken } from './auth-client';
import { fetchWithTimeout } from './network/fetch-with-timeout';

export interface ExpertSpecialtiesRow {
  id: string;
  displayName: string;
  email: string;
  isActive: boolean;
  specialties: string[];
}

export interface ExpertSpecialtiesUpdateResult {
  id: string;
  displayName: string;
  specialties: string[];
}

function resolveUrl(path: string): string {
  if (path.startsWith('http')) return path;
  const base = env.apiBaseUrl || '';
  return base ? `${base.replace(/\/$/, '')}${path}` : path;
}

const CSRF_SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS']);

function readCsrfCookie(): string | null {
  if (typeof document === 'undefined') return null;
  const m = document.cookie.match(/(?:^|;\s*)oet_csrf=([^;]+)/);
  return m ? decodeURIComponent(m[1]) : null;
}

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const token = await ensureFreshAccessToken();
  const headers = new Headers(init?.headers);
  headers.set('Accept', 'application/json');
  if (token) headers.set('Authorization', `Bearer ${token}`);
  if (init?.body && typeof init.body === 'string' && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  const method = (init?.method ?? 'GET').toUpperCase();
  if (!CSRF_SAFE_METHODS.has(method) && !headers.has('x-csrf-token')) {
    const csrf = readCsrfCookie();
    if (csrf) headers.set('x-csrf-token', csrf);
  }
  const res = await fetchWithTimeout(resolveUrl(path), {
    ...init,
    headers,
    credentials: init?.credentials ?? 'include',
  });
  if (!res.ok) {
    let detail: unknown = null;
    try { detail = await res.json(); } catch { /* ignore */ }
    const err = new Error(`HTTP ${res.status}`) as Error & { status?: number; detail?: unknown };
    err.status = res.status;
    err.detail = detail;
    throw err;
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

export const fetchExpertSpecialties = () =>
  api<ExpertSpecialtiesRow[]>('/v1/admin/experts');

export const updateExpertSpecialties = (expertId: string, specialties: string[]) =>
  api<ExpertSpecialtiesUpdateResult>(`/v1/admin/experts/${expertId}/specialties`, {
    method: 'PATCH',
    body: JSON.stringify({ specialties }),
  });
