'use client';

import { useEffect } from 'react';
import { analytics, type AnalyticsEvent } from '@/lib/analytics';

type BeaconProperties = Record<string, string | number | boolean | null | undefined>;

/**
 * Tiny client boundary that only exists so the parent page can stay
 * a pure React Server Component while still firing a view analytics event.
 *
 * Usage:
 *   <PageViewBeacon event="exam_guide_viewed" />
 */
export function PageViewBeacon({
  event,
  properties,
}: {
  event: AnalyticsEvent;
  properties?: BeaconProperties;
}) {
  useEffect(() => {
    analytics.track(event, properties);
    // Fire once per mount. Event/properties are a stable contract per page.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  return null;
}
