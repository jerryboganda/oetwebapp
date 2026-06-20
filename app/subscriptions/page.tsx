'use client';

import { LearnerDashboardShell } from '@/components/layout';
import { CatalogStorefront } from '@/components/domain/catalog';

export default function SubscriptionsPage() {
  return (
    <LearnerDashboardShell>
      <CatalogStorefront variant="dashboard" />
    </LearnerDashboardShell>
  );
}
