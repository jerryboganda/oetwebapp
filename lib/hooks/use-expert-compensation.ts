'use client';

import { useCallback, useEffect, useState } from 'react';
import {
  fetchExpertCompensationSummary,
  fetchExpertEarningsHistory,
  fetchExpertPayouts,
} from '@/lib/api';
import type {
  ExpertCompensationSummary,
  ExpertEarningsHistory,
  ExpertEarningItem,
  ExpertPayoutItem,
} from '@/lib/types/expert';

export function useExpertCompensation() {
  const [summary, setSummary] = useState<ExpertCompensationSummary | null>(null);
  const [earnings, setEarnings] = useState<ExpertEarningItem[]>([]);
  const [earningsTotal, setEarningsTotal] = useState(0);
  const [payouts, setPayouts] = useState<ExpertPayoutItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [pageSize] = useState(25);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [summaryData, earningsData, payoutsData] = await Promise.all([
        fetchExpertCompensationSummary() as Promise<ExpertCompensationSummary>,
        fetchExpertEarningsHistory(page, pageSize) as Promise<ExpertEarningsHistory>,
        fetchExpertPayouts() as Promise<{ items: ExpertPayoutItem[]; totalCount: number }>,
      ]);
      setSummary(summaryData);
      setEarnings(earningsData.items);
      setEarningsTotal(earningsData.totalCount);
      setPayouts(payoutsData.items);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load compensation data');
    } finally {
      setLoading(false);
    }
  }, [page, pageSize]);

  useEffect(() => { load(); }, [load]);

  return { summary, earnings, earningsTotal, payouts, loading, error, page, setPage, refresh: load };
}
