import { QueryObserver } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AiPackageCreditSnapshot } from '@/lib/billing-types';

const mocks = vi.hoisted(() => ({
  fetchMyAiPackageCredits: vi.fn(),
  toastSuccess: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
  fetchMyAiPackageCredits: mocks.fetchMyAiPackageCredits,
}));

vi.mock('@/components/ui/toaster', () => ({
  toast: { success: mocks.toastSuccess },
}));

// One real client shared by the module under test and the assertions.
vi.mock('@/components/providers/query-provider', async () => {
  const { QueryClient } = await import('@tanstack/react-query');
  const client = new QueryClient();
  return { getQueryClient: () => client };
});

import { getQueryClient } from '@/components/providers/query-provider';
import { queryKeys } from '@/lib/query/keys';
import { announceCreditUsage, refreshCreditCards } from '../credit-feedback';

function snapshot(overrides: Record<string, unknown> = {}): AiPackageCreditSnapshot {
  return {
    userId: 'learner-1',
    sharedCredits: 3,
    flexibleCredits: 0,
    writingOnlyCredits: 0,
    speakingOnlyCredits: 0,
    listeningTestsRemaining: 2,
    readingTestsRemaining: 2,
    mockExamsRemaining: 0,
    expiredBecausePassed: false,
    buckets: [{ key: 'reading', remaining: 2 }],
    transactions: [],
    ...overrides,
  } as unknown as AiPackageCreditSnapshot;
}

const stale = snapshot({ sharedCredits: 99 });

describe('credit feedback cache refresh', () => {
  const client = getQueryClient();

  beforeEach(() => {
    vi.clearAllMocks();
    client.clear();
  });

  it('writes the fetched snapshot into the credit-card caches that exist', async () => {
    const fresh = snapshot({ sharedCredits: 3 });
    mocks.fetchMyAiPackageCredits.mockResolvedValue(fresh);
    client.setQueryData(queryKeys.dashboard.aiPackageCredits('current'), stale);
    client.setQueryData(queryKeys.dashboard.aiPackageCredits('learner-1'), stale);

    await refreshCreditCards();

    expect(mocks.fetchMyAiPackageCredits).toHaveBeenCalledTimes(1);
    // toEqual: the cache structurally shares data, so it may hand back an equal copy.
    expect(client.getQueryData(queryKeys.dashboard.aiPackageCredits('current'))).toEqual(fresh);
    expect(client.getQueryData(queryKeys.dashboard.aiPackageCredits('learner-1'))).toEqual(fresh);
    // Fresh data, not an invalidation that would send every observer back to the network.
    expect(client.getQueryState(queryKeys.dashboard.aiPackageCredits('learner-1'))?.isInvalidated).toBe(false);
  });

  it('does not create caches for keys nobody has mounted', async () => {
    mocks.fetchMyAiPackageCredits.mockResolvedValue(snapshot());

    await refreshCreditCards();

    expect(client.getQueryCache().find({ queryKey: queryKeys.dashboard.aiPackageCredits('current') })).toBeUndefined();
    expect(client.getQueryCache().find({ queryKey: queryKeys.dashboard.aiPackageCredits('learner-1') })).toBeUndefined();
  });

  it('does not make a mounted dashboard credit card fetch the snapshot a second time', async () => {
    const fresh = snapshot({ sharedCredits: 3 });
    mocks.fetchMyAiPackageCredits.mockResolvedValue(fresh);
    const dashboardFetch = vi.fn().mockResolvedValue(stale);
    const observer = new QueryObserver(client, {
      queryKey: queryKeys.dashboard.aiPackageCredits('learner-1'),
      queryFn: dashboardFetch,
      staleTime: 2 * 60_000,
    });
    const unsubscribe = observer.subscribe(() => undefined);
    await vi.waitFor(() => expect(observer.getCurrentResult().data).toEqual(stale));
    expect(dashboardFetch).toHaveBeenCalledTimes(1);

    await refreshCreditCards();
    // Give an (unwanted) refetch the chance to start.
    await new Promise((resolve) => setTimeout(resolve, 25));

    expect(dashboardFetch).toHaveBeenCalledTimes(1);
    expect(observer.getCurrentResult().data).toEqual(fresh);
    unsubscribe();
  });

  it('announceCreditUsage shares the same single fetch and toasts what was used', async () => {
    const fresh = snapshot({
      transactions: [
        {
          reason: 'ObjectivePracticeDeduct',
          createdAt: new Date().toISOString(),
          packageType: 'Reading',
          mockExamsDelta: 0,
          sharedCreditsDelta: 0,
          flexibleCreditsDelta: 0,
          writingOnlyCreditsDelta: 0,
          speakingOnlyCreditsDelta: 0,
          readingTestsDelta: -1,
          listeningTestsDelta: 0,
        },
      ],
    });
    mocks.fetchMyAiPackageCredits.mockResolvedValue(fresh);
    client.setQueryData(queryKeys.dashboard.aiPackageCredits('learner-1'), stale);

    await announceCreditUsage('reading');

    expect(mocks.fetchMyAiPackageCredits).toHaveBeenCalledTimes(1);
    expect(mocks.toastSuccess).toHaveBeenCalledWith('1 Reading Credit used. 2 Reading Credits remaining.', { duration: 6000 });
    expect(client.getQueryData(queryKeys.dashboard.aiPackageCredits('learner-1'))).toEqual(fresh);
  });

  it('never throws into the activity when the ledger cannot be read', async () => {
    mocks.fetchMyAiPackageCredits.mockRejectedValue(new Error('offline'));

    await expect(refreshCreditCards()).resolves.toBeUndefined();
    await expect(announceCreditUsage('reading')).resolves.toBeUndefined();
    expect(mocks.toastSuccess).not.toHaveBeenCalled();
  });
});
