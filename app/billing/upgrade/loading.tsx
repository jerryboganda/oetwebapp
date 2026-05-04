import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Skeleton } from '@/components/ui/skeleton';

export default function Loading() {
  return (
    <LearnerDashboardShell pageTitle="Compare plans" backHref="/billing">
      <div className="space-y-6" aria-busy="true" aria-live="polite">
        {/* Hero band */}
        <Skeleton className="h-44 w-full rounded-2xl" />
        {/* Usage snapshot */}
        <Skeleton className="h-40 w-full rounded-2xl" />
        {/* Plan cards */}
        <div className="grid gap-4 md:grid-cols-3">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-64 rounded-2xl" />
          ))}
        </div>
      </div>
    </LearnerDashboardShell>
  );
}