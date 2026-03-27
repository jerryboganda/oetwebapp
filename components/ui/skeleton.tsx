import { cn } from '@/lib/utils';

/* ─── Skeleton Loader ─── */
interface SkeletonProps {
  className?: string;
  variant?: 'text' | 'rectangle' | 'circle';
  width?: string | number;
  height?: string | number;
  lines?: number;
}

export function Skeleton({ className, variant = 'rectangle', width, height, lines }: SkeletonProps) {
  if (lines && lines > 1) {
    return (
      <div className={cn('flex flex-col gap-2', className)} role="status" aria-busy="true" aria-label="Loading">
        {Array.from({ length: lines }).map((_, i) => (
          <div
            key={i}
            className={cn('bg-gray-200 rounded animate-pulse', i === lines - 1 && 'w-3/4')}
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
  };

  return (
    <div
      className={cn('bg-gray-200 animate-pulse', variantStyles[variant], className)}
      style={{ width, height }}
      role="status"
      aria-busy="true"
      aria-label="Loading"
    />
  );
}

/* ─── Card Skeleton ─── */
export function CardSkeleton({ className }: { className?: string }) {
  return (
    <div className={cn('bg-surface border border-border rounded-2xl p-5 shadow-clinical', className)} role="status" aria-busy="true" aria-label="Loading card">
      <Skeleton variant="text" className="w-1/3 h-5 mb-3" />
      <Skeleton lines={3} className="mb-4" />
      <div className="flex gap-2">
        <Skeleton className="w-20 h-8" />
        <Skeleton className="w-20 h-8" />
      </div>
    </div>
  );
}

/* ─── Page Skeleton ─── */
export function PageSkeleton({ className }: { className?: string }) {
  return (
    <div className={cn('flex flex-col gap-6 p-6', className)} role="status" aria-busy="true" aria-label="Loading page">
      <Skeleton className="w-48 h-8" />
      <Skeleton className="w-72 h-4" />
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
        <CardSkeleton />
        <CardSkeleton />
        <CardSkeleton />
      </div>
    </div>
  );
}
