import { Card } from '@/components/ui/card';
import { CardSkeleton, Skeleton } from '@/components/ui/skeleton';
import { cn } from '@/lib/utils';

type LearnerSkeletonVariant = 'hero' | 'card-grid' | 'chart-panel' | 'list' | 'side-rail' | 'dashboard';

function LearnerHeroSkeleton() {
  return (
    <Card className="rounded-[24px] p-5 sm:p-6" aria-hidden="true">
      <div className="flex items-start gap-4">
        <Skeleton variant="circle" className="h-12 w-12" />
        <div className="min-w-0 flex-1 space-y-3">
          <Skeleton className="h-3 w-36 rounded-full" />
          <Skeleton className="h-8 w-full max-w-lg" />
          <Skeleton lines={2} className="max-w-2xl" />
          <div className="flex flex-wrap gap-2.5 pt-1">
            <Skeleton className="h-14 w-full rounded-2xl sm:w-44" />
            <Skeleton className="h-14 w-full rounded-2xl sm:w-44" />
            <Skeleton className="h-14 w-full rounded-2xl sm:w-44" />
          </div>
        </div>
      </div>
    </Card>
  );
}

function LearnerCardGridSkeleton({ count = 2 }: { count?: number }) {
  return (
    <div className="grid grid-cols-1 gap-6 lg:grid-cols-2" aria-hidden="true">
      {Array.from({ length: count }).map((_, index) => <CardSkeleton key={index} />)}
    </div>
  );
}

function LearnerChartPanelSkeleton() {
  return (
    <Card className="p-5" aria-hidden="true">
      <Skeleton className="mb-4 h-5 w-40" />
      <div className="flex items-end gap-2 pt-8">
        {[72, 44, 88, 56, 64, 80].map((height, index) => (
          <Skeleton key={index} className="w-full rounded-t-2xl" height={height} />
        ))}
      </div>
    </Card>
  );
}

function LearnerListSkeleton({ rows = 4 }: { rows?: number }) {
  return (
    <div className="space-y-3" aria-hidden="true">
      {Array.from({ length: rows }).map((_, index) => (
        <Card key={index} className="p-4">
          <div className="flex items-center gap-3">
            <Skeleton variant="circle" className="h-10 w-10" />
            <div className="min-w-0 flex-1 space-y-2">
              <Skeleton className="h-4 w-2/3" />
              <Skeleton className="h-3 w-1/3" />
            </div>
            <Skeleton className="h-9 w-20 rounded-xl" />
          </div>
        </Card>
      ))}
    </div>
  );
}

function LearnerSideRailSkeleton() {
  return (
    <div className="space-y-5" aria-hidden="true">
      <LearnerChartPanelSkeleton />
      <CardSkeleton />
      <CardSkeleton />
    </div>
  );
}

export function LearnerSkeleton({ variant, className }: { variant: LearnerSkeletonVariant; className?: string }) {
  return (
    <div className={cn('w-full', className)} role="status" aria-busy="true" aria-label="Loading learner workspace">
      {variant === 'hero' ? <LearnerHeroSkeleton /> : null}
      {variant === 'card-grid' ? <LearnerCardGridSkeleton /> : null}
      {variant === 'chart-panel' ? <LearnerChartPanelSkeleton /> : null}
      {variant === 'list' ? <LearnerListSkeleton /> : null}
      {variant === 'side-rail' ? <LearnerSideRailSkeleton /> : null}
      {variant === 'dashboard' ? (
        <div className="space-y-6">
          <LearnerHeroSkeleton />
          <LearnerCardGridSkeleton />
          <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
            <div className="lg:col-span-8">
              <LearnerListSkeleton />
            </div>
            <div className="lg:col-span-4">
              <LearnerSideRailSkeleton />
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}
