// ── Freeze copy helpers ─────────────────────────────────────────────
// Shared between the billing freeze modal and the standalone /freeze page so
// eligibility reason codes are described identically everywhere.

/** Human-readable explanations for each backend freeze eligibility reason code. */
export const FREEZE_REASON_LABELS: Record<string, string> = {
  policy_disabled: 'Freeze requests are currently disabled by policy.',
  self_service_disabled: 'Self-service freeze requests are currently disabled.',
  current_freeze_exists: 'There is already an open freeze request for this account.',
  self_service_entitlement_used: 'Your one freeze for this subscription has already been used.',
  reason_required: 'A reason is required before you can submit a freeze request.',
  duration_invalid: 'The requested duration is outside the allowed policy window.',
  date_range_invalid: 'The end time must be after the start time.',
  scheduling_disabled: 'Future-dated freezes are not enabled under the current policy.',
  subscription_missing: 'No active subscription record was found for this account.',
  active_paid_excluded: 'Active paid subscriptions are excluded by the current policy.',
  past_due_excluded: 'Past-due subscriptions are excluded by the current policy.',
  trial_excluded: 'Trial plans are excluded by the current policy.',
  cancelled_excluded: 'Cancelled subscriptions are excluded by the current policy.',
  expired_excluded: 'Expired subscriptions are excluded by the current policy.',
  suspended_excluded: 'Suspended accounts are excluded by the current policy.',
  complimentary_excluded: 'Complimentary plans are excluded by the current policy.',
};

/** Map a list of reason codes to friendly sentences (falls back to a de-snaked code). */
export function describeFreezeReasonCodes(reasonCodes?: string[] | null): string[] {
  if (!reasonCodes || reasonCodes.length === 0) {
    return [];
  }
  return reasonCodes.map((code) => FREEZE_REASON_LABELS[code] ?? code.replace(/_/g, ' '));
}
