import { Skeleton } from '@/components/ui/skeleton';

export default function Loading() {
  return (
    <div className="space-y-6 p-6">
      <Skeleton className="h-12 w-64" />
      <Skeleton className="h-6 w-96" />
      <Skeleton className="h-16 w-full rounded-xl" />
      <Skeleton className="h-48 w-full rounded-xl" />
    </div>
  );
}
