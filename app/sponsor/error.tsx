'use client';

import { useEffect } from 'react';
import { ErrorState } from '@/components/ui/empty-error';

export default function SponsorError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    console.error('[Sponsor Error]', error);
  }, [error]);

  return (
    <div className="flex min-h-[calc(100vh-9rem)] items-center justify-center">
      <ErrorState
        title="Sponsor Portal Error"
        message="An unexpected error occurred. Please try again or contact support if the problem persists."
        onRetry={reset}
        className="w-full max-w-xl"
      />
    </div>
  );
}
