import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Skeleton } from '@/components/ui/skeleton';

export default function Loading() {
  return (
    <LearnerDashboardShell pageTitle="Referral program" backHref="/billing">
      <div className="space-y-6" aria-busy="true" aria-live="polite">
        {/* Hero */}
        <Skeleton className="h-44 w-full rounded-2xl" />
        {/* How it works */}
        <Skeleton className="h-44 w-full rounded-2xl" />
        {/* Code panel */}
        <Skeleton className="h-48 w-full rounded-2xl" />
        {/* Stats grid */}
        <div className="grid gap-3 sm:grid-cols-3">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-28 rounded-2xl" />
          ))}
        </div>
      </div>
    </LearnerDashboardShell>
  );
}