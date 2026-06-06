/**
 * Admin Expert API client. Matches backend endpoints in
 * `ExpertAdminEndpoints.cs`. Powers the /admin/experts/specialties surface
 * that backfills the auto-assigner's profession-competency map.
 */

import { apiClient } from './api';

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

// Delegates to the shared API client (lib/api.ts) so calls inherit auth, CSRF,
// credentials, timeout, retry-on-5xx/408/429, and a normalized `ApiError`
// (status + code + retryable, detectable via `isApiError`). Call sites keep
// passing `JSON.stringify(...)` bodies, forwarded verbatim with a JSON
// Content-Type.
async function api<T>(path: string, init?: RequestInit): Promise<T> {
  return apiClient.request<T>(path, init);
}

export const fetchExpertSpecialties = () =>
  api<ExpertSpecialtiesRow[]>('/v1/admin/experts');

export const updateExpertSpecialties = (expertId: string, specialties: string[]) =>
  api<ExpertSpecialtiesUpdateResult>(`/v1/admin/experts/${expertId}/specialties`, {
    method: 'PATCH',
    body: JSON.stringify({ specialties }),
  });
