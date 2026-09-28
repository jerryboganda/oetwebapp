import { Skeleton } from '@/components/ui/skeleton';

export default function Loading() {
  return (
    <>
      <div className="space-y-6" aria-busy="true" aria-live="polite">
        {/* Hero */}
        <Skeleton className="h-44 w-full rounded-2xl" />
        {/* Activate / pledge summary */}
        <Skeleton className="h-48 w-full rounded-2xl" />
        {/* Claim panel */}
        <Skeleton className="h-56 w-full rounded-2xl" />
      </div>
    </>
  );
}