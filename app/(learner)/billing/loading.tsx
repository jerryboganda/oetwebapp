import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Skeleton } from '@/components/ui/skeleton';

export default function Loading() {
  return (
    <LearnerDashboardShell pageTitle="Billing & subscriptions" backHref="/">
      <div className="space-y-6" aria-busy="true" aria-live="polite">
        {/* Hero */}
        <Skeleton className="h-44 w-full rounded-2xl" />
        {/* Tab bar */}
        <Skeleton className="h-12 w-full rounded-2xl" />
        {/* Content grid */}
        <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
          <Skeleton className="h-64 w-full rounded-2xl" />
          <Skeleton className="h-64 w-full rounded-2xl" />
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
