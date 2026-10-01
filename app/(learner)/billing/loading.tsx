import { Skeleton } from '@/components/ui/skeleton';

export default function Loading() {
  return (
    <>
      {/* Hero */}
      <Skeleton className="h-44 w-full rounded-2xl" />
      {/* Tab bar */}
      <Skeleton className="h-12 w-full rounded-2xl" />
      {/* Content grid */}
      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <Skeleton className="h-64 w-full rounded-2xl" />
        <Skeleton className="h-64 w-full rounded-2xl" />
      </div>
    </>
  );
}
