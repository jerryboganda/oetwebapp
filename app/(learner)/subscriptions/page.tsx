'use client';

import { Suspense } from 'react';
import { LearnerNavActions } from '@/components/layout/learner-dashboard-shell';
import { SubscriptionsCatalog } from '@/components/domain/catalog/subscriptions-catalog';
import { CartNavButton } from '@/components/cart';

export default function SubscriptionsPage() {
  return (
    <>
      <LearnerNavActions>
        <CartNavButton />
      </LearnerNavActions>
      <Suspense fallback={<div className="h-40 animate-pulse rounded-2xl border border-border bg-surface" />}>
        <SubscriptionsCatalog />
      </Suspense>
    </>
  );
}
