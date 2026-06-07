import { apiClient } from './api';

/**
 * Admin "inspect and correct entitlements" API client.
 *
 * Lives in its own module (rather than in `lib/api.ts` alongside the other
 * `adminExtendSubscription` / `adminCancelSubscription` helpers) only to avoid a
 * merge clash with a concurrent edit to `lib/api.ts`. It reuses the exported
 * `apiClient.post` so behaviour (auth headers, error mapping) is identical to the
 * sibling helpers. Once `lib/api.ts` settles, this can be folded back into the
 * "Subscription lifecycle (admin manual actions)" section there.
 */

export interface AdminSubscriptionEntitlementAdjustPayload {
  /** Absolute SET; omit / undefined to leave unchanged. Clamped to >= 0 server-side. */
  writingAssessmentsRemaining?: number;
  /** Absolute SET; omit / undefined to leave unchanged. Clamped to >= 0 server-side. */
  speakingSessionsRemaining?: number;
  /** Absolute SET; omit / undefined to leave unchanged. Clamped to >= 0 server-side. */
  aiCreditsRemaining?: number;
  /** Absolute SET; omit / undefined to leave unchanged. */
  tutorBookUnlocked?: boolean;
  /** Absolute SET; omit / undefined to leave unchanged. */
  basicEnglishUnlocked?: boolean;
  /** Required — recorded with a before/after snapshot in the audit log. */
  reason: string;
}

export interface AdminSubscriptionEntitlementAdjustResult {
  id: string;
  writingAssessmentsRemaining: number;
  speakingSessionsRemaining: number;
  aiCreditsRemaining: number;
  tutorBookUnlocked: boolean;
  basicEnglishUnlocked: boolean;
  changedAt: string;
}

/**
 * POST /v1/admin/billing/subscriptions/{id}/entitlements — absolute SET of the OET 2026
 * entitlement counters / unlock flags on a subscription. Any field left `undefined` is
 * sent as `null` so the server leaves it unchanged.
 */
export async function adminAdjustSubscriptionEntitlements(
  subscriptionId: string,
  payload: AdminSubscriptionEntitlementAdjustPayload,
): Promise<AdminSubscriptionEntitlementAdjustResult> {
  return apiClient.post<AdminSubscriptionEntitlementAdjustResult>(
    `/v1/admin/billing/subscriptions/${encodeURIComponent(subscriptionId)}/entitlements`,
    {
      writingAssessmentsRemaining: payload.writingAssessmentsRemaining ?? null,
      speakingSessionsRemaining: payload.speakingSessionsRemaining ?? null,
      aiCreditsRemaining: payload.aiCreditsRemaining ?? null,
      tutorBookUnlocked: payload.tutorBookUnlocked ?? null,
      basicEnglishUnlocked: payload.basicEnglishUnlocked ?? null,
      reason: payload.reason,
    },
  );
}
