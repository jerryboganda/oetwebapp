'use client';

/**
 * Phase 3 closure — admin form to grant or update a per-user Reading
 * policy override. Backed by `/v1/admin/reading-policy/users/{userId}`
 * (GET + PUT) which already exists at
 * `backend/src/OetLearner.Api/Endpoints/ReadingPolicyAdminEndpoints.cs:34-48`.
 *
 * Sits under the new `/admin/policies` namespace so future per-domain
 * policy admin pages (Listening, Mocks) can hang alongside.
 */

import { Sliders } from 'lucide-react';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { ReadingUserOverrideForm } from '@/components/domain/admin/ReadingUserOverrideForm';

export default function AdminReadingUserPolicyPage() {
  const { isAuthenticated, role } = useAdminAuth();
  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="Reading user policy overrides">
      <AdminRouteHero
        eyebrow="Policies"
        icon={Sliders}
        accent="amber"
        title="Reading — per-user overrides"
        description="Grant accessibility extra-time entitlements or block individual learners from starting new Reading attempts. The global Reading policy is unaffected."
      />
      <AdminRoutePanel
        title="Override a single learner"
        description="Look up the learner by userId to load any existing override, then edit and save. Every save writes an AuditEvent under Action=ReadingUserOverrideUpsert."
      >
        <ReadingUserOverrideForm />
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
