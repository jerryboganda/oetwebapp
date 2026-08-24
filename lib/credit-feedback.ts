'use client';

import { toast } from '@/components/admin/ui/toaster';
import { queryKeys } from '@/lib/query/hooks';
import type { AiPackageCreditSnapshot, AiPackageCreditTransaction } from '@/lib/billing-types';

export type MeteredSubtest = 'reading' | 'listening' | 'writing' | 'speaking' | 'mock';

const DEBIT_REASONS = new Set(['GradingDeduct', 'ObjectivePracticeDeduct', 'MockDeduct']);
const RECENT_WINDOW_MS = 5 * 60_000;

function capitalize(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

function bucketRemaining(snapshot: AiPackageCreditSnapshot, key: string): number {
  return snapshot.buckets?.find((bucket) => bucket.key === key)?.remaining ?? -1;
}

/**
 * Builds the Master-Catalogue consumption message for a ledger debit row,
 * e.g. "1 Reading Credit used. 2 Reading Credits remaining." or
 * "2 Shared Credits used for Writing. 3 Shared Credits remaining."
 * Returns null when nothing was actually consumed (unlimited pools write
 * zero-delta audit rows).
 */
export function buildConsumptionMessage(
  tx: AiPackageCreditTransaction,
  snapshot: AiPackageCreditSnapshot,
): string | null {
  const subtest = (tx.packageType ?? '').toLowerCase();
  const label = capitalize(subtest);

  if (tx.mockExamsDelta < 0 || subtest === 'mock') {
    const remaining = bucketRemaining(snapshot, 'mock');
    if (-tx.mockExamsDelta <= 0) return null;
    return `1 Full Mock attempt used.${remaining >= 0 ? ` ${remaining} Mock Attempt${remaining === 1 ? '' : 's'} remaining.` : ''}`;
  }

  const consumedShared = Math.abs(Math.min(0, tx.sharedCreditsDelta ?? 0));
  const consumedFlexible = Math.abs(Math.min(0, tx.flexibleCreditsDelta));
  const consumedDedicated = subtest === 'writing'
    ? Math.abs(Math.min(0, tx.writingOnlyCreditsDelta))
    : Math.abs(Math.min(0, tx.speakingOnlyCreditsDelta));
  const consumedAllowance = subtest === 'reading'
    ? Math.abs(Math.min(0, tx.readingTestsDelta))
    : Math.abs(Math.min(0, tx.listeningTestsDelta));

  if (consumedShared + consumedFlexible + consumedDedicated + consumedAllowance === 0) {
    return null; // unlimited pool — no credits consumed
  }

  if (subtest === 'reading' || subtest === 'listening') {
    if (consumedAllowance > 0) {
      const remaining = bucketRemaining(snapshot, subtest);
      return `${consumedAllowance} ${label} Credit${consumedAllowance === 1 ? '' : 's'} used.${remaining >= 0 ? ` ${remaining} ${label} Credit${remaining === 1 ? '' : 's'} remaining.` : ''}`;
    }
    const remaining = snapshot.sharedCredits ?? 0;
    return `${consumedShared} Shared Credit${consumedShared === 1 ? '' : 's'} used for ${label}. ${remaining} Shared Credit${remaining === 1 ? '' : 's'} remaining.`;
  }

  // Writing / Speaking graded activity.
  if (consumedDedicated > 0 && consumedFlexible === 0 && consumedShared === 0) {
    return `${consumedDedicated} ${label} Credit${consumedDedicated === 1 ? '' : 's'} used.`;
  }
  if (consumedShared > 0 && consumedDedicated === 0 && consumedFlexible === 0) {
    const remaining = snapshot.sharedCredits ?? 0;
    return `${consumedShared} Shared Credit${consumedShared === 1 ? '' : 's'} used for ${label}. ${remaining} Shared Credit${remaining === 1 ? '' : 's'} remaining.`;
  }
  if (consumedFlexible > 0 && consumedDedicated === 0 && consumedShared === 0) {
    const remaining = bucketRemaining(snapshot, 'flexible_ws');
    return `${consumedFlexible} Flexible W/S Credit${consumedFlexible === 1 ? '' : 's'} used for ${label}.${remaining >= 0 ? ` ${remaining} remaining.` : ''}`;
  }

  const parts: string[] = [];
  if (consumedDedicated > 0) parts.push(`${consumedDedicated} ${label}`);
  if (consumedFlexible > 0) parts.push(`${consumedFlexible} Flexible W/S`);
  if (consumedShared > 0) parts.push(`${consumedShared} Shared`);
  return `${label} activity used ${parts.join(' + ')} credit${parts.length === 1 ? '' : 's'}.`;
}

async function latestMatchingDebit(
  snapshot: AiPackageCreditSnapshot,
  subtest: MeteredSubtest,
): Promise<AiPackageCreditTransaction | null> {
  const cutoff = Date.now() - RECENT_WINDOW_MS;
  return (
    snapshot.transactions
      .filter((tx) => DEBIT_REASONS.has(tx.reason) && new Date(tx.createdAt).getTime() >= cutoff)
      .filter((tx) => {
        if (subtest === 'mock') return tx.packageType?.toLowerCase() === 'mock' || tx.mockExamsDelta !== 0;
        return (tx.packageType ?? '').toLowerCase() === subtest;
      })
      .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())[0] ?? null
  );
}

/**
 * Rule E (live balance updates): immediately after a metered activity starts
 * or a graded submission completes, show what was used and what remains.
 * Reads the same authoritative ledger the admin sees, then toasts the
 * consumption message and refreshes the dashboard credit card cache.
 */
export async function announceCreditUsage(subtest: MeteredSubtest): Promise<void> {
  try {
    const [{ fetchMyAiPackageCredits }, { queryClient }] = await Promise.all([
      import('@/lib/api'),
      import('@/lib/query/hooks'),
    ]);
    const snapshot = await fetchMyAiPackageCredits();
    void queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.aiPackageCredits('current') });
    void queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.aiPackageCredits(snapshot.userId) });

    const tx = await latestMatchingDebit(snapshot, subtest);
    if (!tx) return;
    const message = buildConsumptionMessage(tx, snapshot);
    if (!message) return;
    toast.success(message, { duration: 6000 });
  } catch {
    // Balance feedback must never block the activity itself.
  }
}
