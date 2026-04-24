'use client';

import { useEffect } from 'react';
import { analytics, type AnalyticsEvent } from '@/lib/analytics';

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
  properties?: Record<string, unknown>;
}) {
  useEffect(() => {
    analytics.track(event, properties);
    // We intentionally fire once per mount. The event/properties are considered
    // a stable contract per page, so linting rules re:dep array don't apply here.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  return null;
}
