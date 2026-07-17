'use client';

/**
 * Phase 3 closure — admin form to grant or update a per-user Reading
 * policy override. Backed by `/v1/admin/reading-policy/users/{userId}`
 * (GET + PUT) which already exists at
 * `backend/src/OetWithDrHesham.Api/Endpoints/ReadingPolicyAdminEndpoints.cs:34-48`.
 *
 * Sits under the new `/admin/policies` namespace so future per-domain
 * policy admin pages (Listening, Mocks) can hang alongside.
 */

import { Sliders } from 'lucide-react';
import {
  AdminSettingsLayout,
  SettingsSection,
} from '@/components/admin/layout/admin-settings-layout';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { ReadingUserOverrideForm } from '@/components/domain/admin/ReadingUserOverrideForm';

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Policies', href: '/admin/policies' },
  { label: 'Reading', href: '/admin/policies/reading' },
  { label: 'Per-user overrides' },
];

export default function AdminReadingUserPolicyPage() {
  const { isAuthenticated, role } = useAdminAuth();
  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminSettingsLayout
      title="Reading per-user overrides"
      description="Grant accessibility extra-time entitlements or block individual learners from starting new Reading attempts. The global Reading policy is unaffected."
      breadcrumbs={BREADCRUMBS}
      eyebrow="Policies"
      icon={<Sliders className="h-5 w-5" />}
    >
      <SettingsSection
        title="Override a single learner"
        description="Look up the learner by userId to load any existing override, then edit and save. Every save writes an AuditEvent under Action=ReadingUserOverrideUpsert."
      >
        <ReadingUserOverrideForm />
      </SettingsSection>
    </AdminSettingsLayout>
  );
}
