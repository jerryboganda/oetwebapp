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
  /** Explicit per-user Video Library ids. Empty = inherit; new videos published after the
   * initial scope are automatically included even when this list is non-empty. */
  videoIds: string[];
  recallSetCodes: string[];
  accessExpiresAt: string | null;
}

export function createEmptyUserAccess(): UserAccess {
  return {
    subscriptions: [],
    addOns: [],
    moduleOverrides: MODULE_KEYS.map((moduleKey) => ({ moduleKey, enabled: false })),
    materialFolderIds: [],
    videoIds: [],
    recallSetCodes: [],
    accessExpiresAt: null,
  };
}

export function isModuleEnabled(overrides: UserAccessModuleOverride[], moduleKey: ModuleKey): boolean {
  return overrides.find((o) => o.moduleKey === moduleKey)?.enabled ?? false;
}

/**
 * True if a subscription still counts as "active enough" to unlock gated
 * modules — mirrors `EffectiveEntitlementResolver`'s eligibility gate
 * (Active/Trial/FreezeRequested, not expired). A pending (not-yet-saved)
 * draft counts as eligible: the backend always creates a freshly granted
 * package as Active, and pending packages are persisted before module/scope
 * overrides on save (see `handleSaveAccess`), so by the time the override
 * takes effect server-side the package already exists.
 *
 * Best-effort UI hint only — the backend remains the real gate.
 */
export function isEligibleSubscription(sub: Pick<UserAccessSubscription, 'status' | 'expiresAt' | 'isPending'>): boolean {
  if (sub.isPending) return true;
  const status = sub.status.toLowerCase();
  if (status === 'suspended' || status === 'pending' || status === 'cancelled') return false;
  if (sub.expiresAt && new Date(sub.expiresAt).getTime() <= Date.now()) return false;
  return true;
}

/**
 * Module/content-scope grants (Recalls, Materials, Videos, Mocks — see
 * `EffectiveEntitlementResolver.ResolveCoreAsync`) only ever take effect on
 * top of an eligible package; with zero eligible packages the resolver
 * returns fail-low before it even reads `UserModuleOverride` rows, so every
 * toggle below is a silent no-op. Used to warn admins in the Advanced panel,
 * mirroring the `needsPlan` guard Quick Grant already enforces.
 */
export function hasEligiblePackage(subscriptions: UserAccessSubscription[]): boolean {
  return subscriptions.some(isEligibleSubscription);
}

// ── Create user ──────────────────────────────────────────────────────────

export interface CreateAdminUserPayload {
  name: string;
  email: string;
  role: 'learner';
  professionId: string;
  targetExamDate: string;
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
  videoIds: string[];
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

// ── Video allocation options ─────────────────────────────────────────────
//
// Lightweight video rows for the per-user "Videos scope" allocator, grouped in
// the picker by Section (subtestCode) → Language (en/ar). Backed by
// GET /v1/admin/video-library/videos/allocatable.

export interface AllocatableVideo {
  id: string;
  title: string;
  /** OET section: 'listening' | 'reading' | 'writing' | 'speaking' | null (uncategorized). */
  subtestCode: string | null;
  /** 'en' | 'ar' | null (unspecified). */
  language: string | null;
  /** Empty = visible to all professions. */
  professionIds: string[];
  /** Curated shelf/category name(s) this video belongs to (e.g. "Writing / Medicine / Arabic /
   *  December Batch / Sessions"). Many videos share no distinguishing words in their own title,
   *  so this is often the only thing a picker can search or bulk-select by. */
  categoryNames: string[];
}

export async function fetchAllocatableVideos(): Promise<AllocatableVideo[]> {
  return apiClient.get<AllocatableVideo[]>('/v1/admin/video-library/videos/allocatable');
}
