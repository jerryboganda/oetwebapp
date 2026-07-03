'use client';

import { Suspense } from 'react';
import { LearnerDashboardShell } from '@/components/layout';
import { SubscriptionsCatalog } from '@/components/domain/catalog/subscriptions-catalog';

export default function SubscriptionsPage() {
  return (
    <LearnerDashboardShell>
      <Suspense fallback={<div className="h-40 animate-pulse rounded-2xl border border-border bg-surface" />}>
        <SubscriptionsCatalog />
      </Suspense>
    </LearnerDashboardShell>
  );
}
