import { Skeleton } from '@/components/ui/skeleton';

export default function ExpertOnboardingLoading() {
  return (
    <div className="flex flex-1 items-center justify-center p-8">
      <div className="w-full max-w-2xl space-y-8">
        <div className="flex items-center gap-2">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-8 w-8 rounded-full" />
          ))}
        </div>
        <Skeleton className="h-[400px] w-full rounded-2xl" />
        <div className="flex justify-between">
          <Skeleton className="h-10 w-24 rounded-xl" />
          <Skeleton className="h-10 w-32 rounded-xl" />
        </div>
      </div>
    </div>
  );
}
