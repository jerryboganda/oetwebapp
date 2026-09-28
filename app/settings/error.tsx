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
    console.error('[Settings Error]', error);
  }, [error]);

  return (
    <div className="min-h-screen flex items-center justify-center p-6">
      <ErrorState
        className="w-full max-w-md"
        title="Settings Error"
        message="An unexpected error occurred. Please try again or contact support if the problem persists."
        onRetry={reset}
      />
    </div>
  );
}
