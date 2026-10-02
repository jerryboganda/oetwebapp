'use client';

import { Suspense } from 'react';
import { LearnerNavActions } from '@/components/layout/learner-dashboard-shell';
import { SubscriptionsCatalog } from '@/components/domain/catalog/subscriptions-catalog';
import { CartNavButton } from '@/components/cart';
import { Skeleton } from '@/components/ui/skeleton';

export default function SubscriptionsPage() {
  return (
    <>
      <LearnerNavActions>
        <CartNavButton />
      </LearnerNavActions>
      <Suspense fallback={<Skeleton className="h-40 rounded-2xl" />}>
        <SubscriptionsCatalog />
      </Suspense>
    </>
  );
}
