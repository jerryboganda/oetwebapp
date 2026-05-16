'use client';

import { useCallback } from 'react';
import { analytics, type AnalyticsEvent, type EventProperties } from '@/lib/analytics';

export function useAnalytics() {
  const track = useCallback((event: AnalyticsEvent, properties?: EventProperties) => {
    analytics.track(event, properties);
  }, []);

  return { track };
}
