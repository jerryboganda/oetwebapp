'use client';

import { useEffect } from 'react';
import { ErrorState } from '@/components/ui/empty-error';

export default function Error({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    console.error('[Progress Error]', error);
  }, [error]);

  return (
    <ErrorState
      className="mx-auto mt-6 w-full max-w-lg sm:mt-10"
      title="Progress Error"
      message="An unexpected error occurred. Please try again or contact support if the problem persists."
      onRetry={reset}
      retryLabel="Try again"
    />
  );
}