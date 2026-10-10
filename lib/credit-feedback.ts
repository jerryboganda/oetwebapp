'use client';

import type { QueryClient } from '@tanstack/react-query';
import { toast } from '@/components/ui/toaster';
import { queryKeys } from '@/lib/query/keys';
import { describeCreditsAsActivities } from '@/lib/format-allowance';
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
    const remaining = bucketRemaining(snapshot, subtest);
    return `${consumedDedicated} ${label} Credit${consumedDedicated === 1 ? '' : 's'} used.${remaining >= 0 ? ` ${remaining} ${label} Credit${remaining === 1 ? '' : 's'} remaining.` : ''}`;
  }
  if (consumedShared > 0 && consumedDedicated === 0 && consumedFlexible === 0) {
    const remaining = snapshot.sharedCredits ?? 0;
    return `${consumedShared} Shared Credit${consumedShared === 1 ? '' : 's'} used for ${label}. ${remaining} Shared Credit${remaining === 1 ? '' : 's'} remaining.`;
  }
  if (consumedFlexible > 0 && consumedDedicated === 0 && consumedShared === 0) {
    const remaining = bucketRemaining(snapshot, 'flexible_ws');
    return `${consumedFlexible} Flexible W/S Credit${consumedFlexible === 1 ? '' : 's'} used for ${label}.${remaining >= 0 ? ` ${remaining} remaining.` : ''}`;
  }

  // Mixed pools: name each pool used and what it has left, plus the letters/cards
  // that remainder still buys (2 credits each).
  const used: string[] = [];
  const left: string[] = [];
  const addPool = (consumed: number, name: string, remaining: number) => {
    if (consumed <= 0) return;
    used.push(`${consumed} ${name}`);
    if (remaining < 0) return;
    const buys = describeCreditsAsActivities(subtest, remaining);
    left.push(`${remaining} ${name}${buys ? ` (= ${buys})` : ''}`);
  };
  addPool(consumedDedicated, label, bucketRemaining(snapshot, subtest));
  addPool(consumedFlexible, 'Flexible W/S', bucketRemaining(snapshot, 'flexible_ws'));
  addPool(consumedShared, 'Shared', snapshot.sharedCredits ?? 0);
  const total = consumedDedicated + consumedFlexible + consumedShared;
  return `${label} activity used ${used.join(' + ')} credit${total === 1 ? '' : 's'}.${left.length > 0 ? ` Remaining: ${left.join(', ')}.` : ''}`;
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
 * Writes an authoritative snapshot straight into the dashboard credit-card
 * caches that already exist. The caller has just fetched it, so invalidating
 * those queries instead made every mounted observer fetch the very same
 * snapshot a second time. A key with no cache entry is left alone (there is
 * nothing stale to refresh; the next mount fetches on its own).
 */
async function seedCreditCaches(queryClient: QueryClient, snapshot: AiPackageCreditSnapshot): Promise<void> {
  for (const userId of new Set(['current', snapshot.userId])) {
    const queryKey = queryKeys.dashboard.aiPackageCredits(userId);
    if (!queryClient.getQueryCache().find({ queryKey })) continue;
    // A fetch already in flight started before this snapshot; do not let it land after it.
    await queryClient.cancelQueries({ queryKey });
    queryClient.setQueryData(queryKey, snapshot);
  }
}

/** One fetch of the ledger snapshot, shared with the dashboard credit-card caches. */
async function loadCreditSnapshot(): Promise<AiPackageCreditSnapshot> {
  const [snapshot, queryClient] = await Promise.all([
    import('@/lib/api').then((m) => m.fetchMyAiPackageCredits()),
    import('@/components/providers/query-provider').then((m) => m.getQueryClient()),
  ]);
  await seedCreditCaches(queryClient, snapshot);
  return snapshot;
}

/**
 * Refreshes the dashboard credit card caches so the candidate sees the same
 * authoritative balances the admin reads. Safe to call after any metered
 * activity — including ones that consumed nothing (idempotent per paper),
 * which is why it can't rely on a debit row existing.
 */
export async function refreshCreditCards(): Promise<void> {
  try {
    await loadCreditSnapshot();
  } catch {
    // Balance refresh must never block the activity itself.
  }
}

/**
 * Rule E (live balance updates): immediately after a metered activity starts
 * or a graded submission completes, show what was used and what remains.
 * Reads the same authoritative ledger the admin sees, then toasts the
 * consumption message and refreshes the dashboard credit card cache.
 */
export async function announceCreditUsage(subtest: MeteredSubtest): Promise<void> {
  try {
    const snapshot = await loadCreditSnapshot();

    const tx = await latestMatchingDebit(snapshot, subtest);
    if (!tx) return;
    const message = buildConsumptionMessage(tx, snapshot);
    if (!message) return;
    toast.success(message, { duration: 6000 });
  } catch {
    // Balance feedback must never block the activity itself.
  }
}

/** Show the wallet copy returned after a real debit. Empty/resume payloads stay silent. */
export function showCreditFeedback(message?: string | null): void {
  const trimmed = message?.trim();
  if (!trimmed) return;
  toast.success(trimmed);
}
