'use client';

import { LearnerDashboardShell } from '@/components/layout';
import { CatalogStorefront } from '@/components/domain/catalog';
import { AiPackagesStorefront } from '@/components/domain/billing';

export default function SubscriptionsPage() {
  return (
    <LearnerDashboardShell>
      <CatalogStorefront variant="dashboard" />
      <AiPackagesStorefront />
    </LearnerDashboardShell>
  );
}
