'use client';

import { useEffect } from 'react';
import { ErrorState } from '@/components/ui/empty-error';

export default function PricingError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    console.error('[Pricing Error]', error);
  }, [error]);

  return (
    <div className="flex min-h-[calc(var(--app-viewport-height,100dvh)-9rem)] items-center justify-center p-6">
      <ErrorState
        className="w-full max-w-md"
        title="Something went wrong"
        message="An unexpected error occurred in Pricing. Please try again."
        onRetry={reset}
      />
    </div>
  );
}
