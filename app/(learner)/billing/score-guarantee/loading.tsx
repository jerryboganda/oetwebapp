import { Skeleton } from '@/components/ui/skeleton';

export default function Loading() {
  return (
    <>
      {/* Hero */}
      <Skeleton className="h-44 w-full rounded-2xl" />
      {/* Terms */}
      <Skeleton className="h-48 w-full rounded-2xl" />
      {/* Activate / pledge summary */}
      <Skeleton className="h-56 w-full rounded-2xl" />
    </>
  );
}
