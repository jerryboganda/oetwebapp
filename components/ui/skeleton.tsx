import { cn } from '@/lib/utils';

/* Skeleton Loader */
interface SkeletonProps {
  className?: string;
  variant?: 'text' | 'rectangle' | 'circle';
  width?: string | number;
  height?: string | number;
  lines?: number;
}

export function Skeleton({ className, variant = 'rectangle', width, height, lines }: SkeletonProps) {
  const pulseClassName = 'motion-safe:animate-pulse motion-reduce:animate-none';

  if (lines && lines > 1) {
    return (
      <div className={cn('flex flex-col gap-2', className)} role="status" aria-busy="true" aria-label="Loading">
        {Array.from({ length: lines }).map((_, i) => (
          <div
            key={i}
            className={cn('rounded-xl bg-gray-200/80 dark:bg-gray-700/50', pulseClassName, i === lines - 1 && 'w-3/4')}
            style={{ height: height ?? 16 }}
          />
        ))}
      </div>
    );
  }

  const variantStyles = {
    text: 'h-4 rounded',
    rectangle: 'rounded',
    circle: 'rounded-full',
  } as const;

  return (
    <div
      className={cn('bg-gray-200/80 dark:bg-gray-700/50', pulseClassName, variantStyles[variant], className)}
      style={{ width, height }}
      role="status"
      aria-busy="true"
      aria-label="Loading"
    />
  );
}

/* Card Skeleton */
export function CardSkeleton({ className }: { className?: string }) {
  return (
    <div
      className={cn('rounded-[24px] border border-gray-200 bg-surface p-5 shadow-sm', className)}
      role="status"
      aria-busy="true"
      aria-label="Loading card"
    >
      <Skeleton variant="text" className="mb-3 h-5 w-1/3" />
      <Skeleton lines={3} className="mb-4" />
      <div className="flex gap-2">
        <Skeleton className="h-10 w-24 rounded-2xl" />
        <Skeleton className="h-10 w-24 rounded-2xl" />
      </div>
    </div>
  );
}

/* Page Skeleton */
export function PageSkeleton({ className }: { className?: string }) {
  return (
    <div
      className={cn('flex flex-col gap-8', className)}
      role="status"
      aria-busy="true"
      aria-label="Loading page"
    >
      <div className="rounded-[24px] border border-gray-200 bg-surface px-5 py-5 shadow-sm sm:px-6 sm:py-6">
        <div className="space-y-4">
          <div className="flex items-start gap-4">
            <Skeleton variant="circle" className="h-12 w-12" />
            <div className="min-w-0 flex-1 space-y-3">
              <Skeleton className="h-3 w-32 rounded-full" />
              <Skeleton className="h-8 w-80 max-w-full" />
              <Skeleton lines={2} className="max-w-2xl" />
            </div>
          </div>
          <div className="flex flex-wrap gap-3">
            <Skeleton className="h-14 w-full sm:w-44 rounded-2xl" />
            <Skeleton className="h-14 w-full sm:w-44 rounded-2xl" />
            <Skeleton className="h-14 w-full sm:w-44 rounded-2xl" />
          </div>
        </div>
      </div>
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
        <CardSkeleton />
        <CardSkeleton />
        <CardSkeleton />
      </div>
    </div>
  );
}
