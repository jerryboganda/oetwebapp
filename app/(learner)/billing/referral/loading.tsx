import { Skeleton } from '@/components/ui/skeleton';

export default function Loading() {
  return (
    <>
      {/* Hero */}
      <Skeleton className="h-44 w-full rounded-2xl" />
      {/* How it works */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        {[0, 1, 2].map((i) => (
          <Skeleton key={i} className="h-36 rounded-2xl" />
        ))}
      </div>
      {/* Code panel */}
      <Skeleton className="h-48 w-full rounded-2xl" />
    </>
  );
}
