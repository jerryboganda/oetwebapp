// Local copy of freeze-effective logic used by app/billing/page.tsx.
// Kept here so the billing sub-pages (upgrade, score-guarantee, referral)
// share identical semantics without modifying lib/api.ts.
// TODO(billing-impl-c): consider extracting `isFreezeEffective` to lib/freeze
// and importing from a single source of truth.

import type { LearnerFreezeStatus } from '@/lib/types/freeze';

export const FREEZE_BLOCKED_MESSAGE =
  'Billing actions are read-only while your account is frozen.';
export const FREEZE_UNVERIFIED_MESSAGE =
  'Billing actions are temporarily paused because freeze status could not be verified. Refresh the page before retrying.';

function normalizeFreezeStatus(status?: string | null): string {
  return String(status ?? '').toLowerCase();
}

export function isFreezeEffective(freezeState: LearnerFreezeStatus | null): boolean {
  const currentFreeze = freezeState?.currentFreeze;
  if (!currentFreeze) return false;
  const status = normalizeFreezeStatus(currentFreeze.status);
  if (status === 'active') return true;
  if (status === 'scheduled' && currentFreeze.scheduledStartAt) {
    return new Date(currentFreeze.scheduledStartAt).getTime() <= Date.now();
  }
  return false;
}
