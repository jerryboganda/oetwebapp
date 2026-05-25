import { useCallback, useEffect, useRef, useState } from 'react';
import {
  getTodayPlan,
  markPlanItemComplete,
  skipPlanItem,
  type DailyPlanDto,
} from '@/lib/reading-pathway-api';

const REFRESH_INTERVAL_MS = 5 * 60 * 1000; // 5 minutes

export function useDailyPlan() {
  const [plan, setPlan] = useState<DailyPlanDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const refresh = useCallback(async () => {
    setIsLoading(true);
    try {
      const data = await getTodayPlan();
      setPlan(data);
    } catch {
      // silently fail on refresh; initial load already surfaces errors if needed
    } finally {
      setIsLoading(false);
    }
  }, []);

  const markComplete = useCallback(async (id: string) => {
    try {
      await markPlanItemComplete(id);
    } catch {
      // best-effort
    }
    await refresh();
  }, [refresh]);

  const skip = useCallback(async (id: string, reason: string) => {
    try {
      await skipPlanItem(id, reason);
    } catch {
      // best-effort
    }
    await refresh();
  }, [refresh]);

  useEffect(() => {
    refresh();
    intervalRef.current = setInterval(refresh, REFRESH_INTERVAL_MS);
    return () => {
      if (intervalRef.current) clearInterval(intervalRef.current);
    };
  }, [refresh]);

  return { plan, isLoading, markComplete, skip, refresh };
}
