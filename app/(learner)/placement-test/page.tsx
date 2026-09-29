'use client';

import { LearnerDashboardShell } from '@/components/layout';
import { PlacementTestRunner } from '@/components/placement/placement-test-runner';

/**
 * Free General-English placement test. Authenticated learners only; the
 * auth guard preserves the destination through sign-in/MFA/device
 * challenges, and the exam-date gate exempts this route so the placement
 * journey is never diverted.
 */
export default function PlacementTestPage() {
  return (
    <LearnerDashboardShell pageTitle="Placement Test" distractionFree>
      <PlacementTestRunner />
    </LearnerDashboardShell>
  );
}
