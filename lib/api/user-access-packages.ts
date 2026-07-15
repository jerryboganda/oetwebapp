/**
 * Admin per-user package allocation — the parts added by the 2026-07-15
 * access & payment work, layered on top of lib/user-access.ts (which owns the
 * base fetch/grant/remove/scope client).
 *
 * Mirrors:
 *   - AdminEndpoints `/v1/admin/users/{userId}/access/packages/{id}/suspend|restore`
 *   - AdminUserAccessPackageRequest (StartsAt, OverrideProfessionMismatch)
 *   - UserAccessSubscriptionDto (StartedAt, FulfilmentStatus)
 *
 * Uses the shared apiClient from lib/api.ts so retry / CSRF / auth headers stay
 * consistent with the rest of the app.
 */
import { apiClient } from '@/lib/api';
import type { AdminBillingPlan } from '@/lib/types/admin';
import type { GrantUserPackagePayload, UserAccess, UserAccessSubscription } from '@/lib/user-access';

/** `Subscription.FulfilmentStatus`. `pending_manual` packages stay Pending — they
 *  grant nothing until an admin marks them fulfilled. */
export type PackageFulfilmentStatus = 'auto' | 'pending_manual' | 'fulfilled';

/** Fallback when a plan carries no `accessDurationDays` (every seeded plan has 180). */
export const DEFAULT_ACCESS_DURATION_DAYS = 180;

/**
 * A subscription row as the access endpoints actually return it, plus the
 * UI-only draft fields carried on locally added packages until the caller
 * persists them via `grantUserPackage`.
 */
export interface UserAccessSubscriptionRow extends UserAccessSubscription {
  startedAt?: string;
  fulfilmentStatus?: PackageFulfilmentStatus | string;
  /** UI-only draft: start of the access window (defaults to now server-side). */
  startsAt?: string | null;
  /** UI-only draft: admin acknowledged the plan/learner profession mismatch. */
  overrideProfessionMismatch?: boolean;
}

/** `grantUserPackage` payload including the fields added to AdminUserAccessPackageRequest. */
export interface GrantUserPackageInput extends GrantUserPackagePayload {
  startsAt?: string | null;
  overrideProfessionMismatch?: boolean;
}

export function suspendUserPackage(userId: string, subscriptionId: string): Promise<UserAccess> {
  return apiClient.post<UserAccess>(
    `/v1/admin/users/${encodeURIComponent(userId)}/access/packages/${encodeURIComponent(subscriptionId)}/suspend`,
    {},
  );
}

export function restoreUserPackage(userId: string, subscriptionId: string): Promise<UserAccess> {
  return apiClient.post<UserAccess>(
    `/v1/admin/users/${encodeURIComponent(userId)}/access/packages/${encodeURIComponent(subscriptionId)}/restore`,
    {},
  );
}

// ── Allocation rules (mirrored from the backend so the form can pre-empt them) ──

export function planAccessDurationDays(plan: AdminBillingPlan | undefined): number {
  const days = plan?.accessDurationDays;
  return typeof days === 'number' && days > 0 ? days : DEFAULT_ACCESS_DURATION_DAYS;
}

/**
 * Mirrors `UserAccessAllocationService.EnsureProfessionMatchAsync`: a plan tied to
 * a profession may only be attached to a learner registered under it. A blank
 * learner profession IS a mismatch server-side; `undefined` means the caller did
 * not tell us the learner's profession, so we cannot judge.
 */
export function isProfessionMismatch(
  plan: AdminBillingPlan | undefined,
  learnerProfessionId: string | null | undefined,
): boolean {
  if (!plan || learnerProfessionId === undefined) return false;
  const planProfession = (plan.profession ?? '').trim();
  if (!planProfession || planProfession.toLowerCase() === 'all') return false;
  return planProfession.toLowerCase() !== (learnerProfessionId ?? '').trim().toLowerCase();
}
