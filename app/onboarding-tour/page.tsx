'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { analytics } from '@/lib/analytics';

/**
 * The legacy static slideshow has been superseded by the anchored, role-aware
 * product tours (see components/onboarding/*). This route now hands off to the
 * dashboard, where the dashboard tour auto-starts for first-time users and can be
 * replayed any time from the Help ("?") menu. The legacy analytics event is kept
 * for funnel continuity.
 */
export default function OnboardingTourRedirect() {
  const router = useRouter();

  useEffect(() => {
    analytics.track('onboarding_tour_started');
    router.replace('/dashboard');
  }, [router]);

  return null;
}
