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
    console.error('[App Error]', error);
  }, [error]);

  return (
    <div className="min-h-screen flex items-center justify-center p-6">
      <ErrorState
        className="w-full max-w-md"
        message={`Unable to complete this action. Retry this page or contact support if the problem persists.${error.digest ? ` Support ref ${error.digest}.` : ''}`}
        onRetry={reset}
      />
    </div>
  );
}
