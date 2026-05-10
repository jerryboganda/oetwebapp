// Shared freeze-effective logic for all learner billing surfaces.

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
