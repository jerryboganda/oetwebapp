'use client';

import { useCallback } from 'react';
import { analytics, type AnalyticsEvent } from '@/lib/analytics';

export function useAnalytics() {
  const track = useCallback((event: AnalyticsEvent, properties?: Record<string, any>) => {
    analytics.track(event, properties);
  }, []);

  return { track };
}
