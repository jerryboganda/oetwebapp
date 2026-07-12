/**
 * Per-user access & allocation API — admin "Add User" flow and the per-user
 * access editor on the user detail page.
 *
 * Uses the same shared client (`apiClient`) as lib/materials-api.ts so every
 * call inherits auth, CSRF, retry-on-5xx/408/429, and normalized `ApiError`
 * behavior. Picker option lists (plans/add-ons/recall sets) delegate to the
 * already-normalized fetchers in lib/admin.ts and lib/api.ts rather than
 * re-implementing that coercion logic here.
 */

import { apiClient } from './api';
import { adminListRecallSetTags, type RecallSetTagDto } from './api';
import { getAdminBillingAddOnData, getAdminBillingPlanData } from './admin';
import type { AdminBillingAddOn, AdminBillingPlan } from './types/admin';

export type { RecallSetTagDto };

// ── Module keys ──────────────────────────────────────────────────────────

export type ModuleKey = 'Recalls' | 'MaterialsLibrary' | 'VideoLibrary' | 'Mocks';

export const MODULE_KEYS: ModuleKey[] = ['Recalls', 'MaterialsLibrary', 'VideoLibrary', 'Mocks'];

export const MODULE_LABELS: Record<ModuleKey, string> = {
  Recalls: 'Recalls',
  MaterialsLibrary: 'Materials Library',
  VideoLibrary: 'Videos',
  Mocks: 'Mocks',
};

// ── Types ────────────────────────────────────────────────────────────────

export interface UserAccessSubscription {
  id: string;
  planCode: string;
  planName: string;
  status: string;
  expiresAt: string | null;
  isPrimary: boolean;
  /** UI-only: true while this row is a local draft not yet persisted via `grantUserPackage`. */
  isPending?: boolean;
  /** UI-only: whether to grant the plan's included credits when this pending package is persisted. */
  grantIncludedCredits?: boolean;
}

export interface UserAccessAddOn {
  code: string;
  subscriptionId?: string;
  /** UI-only: true while this row is a local draft not yet persisted via `grantUserAddon`. */
  isPending?: boolean;
}

export interface UserAccessModuleOverride {
  moduleKey: ModuleKey;
  enabled: boolean;
}

export interface UserAccess {
  subscriptions: UserAccessSubscription[];
  addOns: UserAccessAddOn[];
  moduleOverrides: UserAccessModuleOverride[];
  materialFolderIds: string[];
  recallSetCodes: string[];
  accessExpiresAt: string | null;
}

export function createEmptyUserAccess(): UserAccess {
  return {
    subscriptions: [],
    addOns: [],
    moduleOverrides: MODULE_KEYS.map((moduleKey) => ({ moduleKey, enabled: false })),
    materialFolderIds: [],
    recallSetCodes: [],
    accessExpiresAt: null,
  };
}

export function isModuleEnabled(overrides: UserAccessModuleOverride[], moduleKey: ModuleKey): boolean {
  return overrides.find((o) => o.moduleKey === moduleKey)?.enabled ?? false;
}

// ── Create user ──────────────────────────────────────────────────────────

export interface CreateAdminUserPayload {
  name: string;
  email: string;
  role: 'learner';
  professionId: string;
  mobileNumber?: string;
  password?: string;
  sendInvite: boolean;
}

export interface CreateAdminUserResult {
  id: string;
  email: string;
  role: string;
  temporaryPassword?: string | null;
  invitation?: {
    purpose: string;
    deliveryChannel: string;
    destinationHint: string;
    expiresAt: string;
    retryAfterSeconds: number;
  } | null;
}

export async function createAdminUser(payload: CreateAdminUserPayload): Promise<CreateAdminUserResult> {
  return apiClient.post<CreateAdminUserResult>('/v1/admin/users', payload);
}

// ── Access read/write ────────────────────────────────────────────────────

export async function fetchUserAccess(userId: string): Promise<UserAccess> {
  return apiClient.get<UserAccess>(`/v1/admin/users/${encodeURIComponent(userId)}/access`);
}

export interface GrantUserPackagePayload {
  planCode: string;
  expiresAt?: string | null;
  makePrimary?: boolean;
  grantIncludedCredits?: boolean;
}

export async function grantUserPackage(userId: string, payload: GrantUserPackagePayload): Promise<UserAccess> {
  return apiClient.post<UserAccess>(`/v1/admin/users/${encodeURIComponent(userId)}/access/packages`, payload);
}

export async function removeUserPackage(userId: string, subscriptionId: string): Promise<UserAccess> {
  return apiClient.delete<UserAccess>(
    `/v1/admin/users/${encodeURIComponent(userId)}/access/packages/${encodeURIComponent(subscriptionId)}`,
  );
}

export interface GrantUserAddonPayload {
  addonCode: string;
  subscriptionId?: string;
  quantity?: number;
}

export async function grantUserAddon(userId: string, payload: GrantUserAddonPayload): Promise<UserAccess> {
  return apiClient.post<UserAccess>(`/v1/admin/users/${encodeURIComponent(userId)}/access/addons`, payload);
}

export interface PutUserAccessScopePayload {
  modules: UserAccessModuleOverride[];
  materialFolderIds: string[];
  recallSetCodes: string[];
  accessExpiresAt?: string | null;
  clearAccessExpiry?: boolean;
}

export async function putUserAccessScope(userId: string, payload: PutUserAccessScopePayload): Promise<UserAccess> {
  return apiClient.put<UserAccess>(`/v1/admin/users/${encodeURIComponent(userId)}/access/scope`, payload);
}

// ── Picker option fetchers ───────────────────────────────────────────────
//
// These delegate to existing, already-normalized fetchers elsewhere in lib/
// (lib/admin.ts for billing plans/add-ons, lib/api.ts for recall set tags)
// rather than re-implementing response coercion here.

export async function fetchAdminBillingPlans(): Promise<AdminBillingPlan[]> {
  return getAdminBillingPlanData({ status: 'active' });
}

export async function fetchAdminAddons(): Promise<AdminBillingAddOn[]> {
  return getAdminBillingAddOnData({ status: 'active' });
}

export async function fetchAdminRecallSetTags(): Promise<RecallSetTagDto[]> {
  return adminListRecallSetTags();
}
