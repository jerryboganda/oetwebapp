/**
 * Billing page copy (admin-editable learner-page strings) — extracted from
 * `lib/api.ts`. Re-exported there, so `@/lib/api` imports keep working.
 */
import { apiRequest } from './client';

export interface AdminBillingContentEntry {
  key: string;
  value: string;
  section?: string | null;
  description?: string | null;
  updatedAt?: string;
  updatedByAdminName?: string | null;
}

/** Stored copy overrides only — defaults live in lib/billing-copy-defaults.ts. */
export async function fetchAdminBillingContent(): Promise<{ entries: AdminBillingContentEntry[] }> {
  return apiRequest('/v1/admin/billing/content');
}

export async function replaceAdminBillingContent(
  entries: Array<{ key: string; value: string; section?: string; description?: string }>,
): Promise<{ entries: AdminBillingContentEntry[] }> {
  return apiRequest('/v1/admin/billing/content', {
    method: 'PUT',
    body: JSON.stringify({ entries }),
  });
}

export async function deleteAdminBillingContentEntry(key: string): Promise<{ key: string; deleted: boolean }> {
  return apiRequest(`/v1/admin/billing/content/${encodeURIComponent(key)}`, { method: 'DELETE' });
}

/** Public learner-page copy overrides as a flat { key: value } map. */
export async function fetchBillingContent(): Promise<Record<string, string>> {
  return apiRequest('/v1/billing/content') as Promise<Record<string, string>>;
}
