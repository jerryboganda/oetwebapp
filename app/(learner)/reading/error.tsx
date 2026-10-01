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
    // P0-K 2026-05 hardening: dev-only console; Sentry captures via Next.js
    // built-in error boundary instrumentation in production.
    if (typeof process !== 'undefined' && process.env.NODE_ENV !== 'production') {
      // eslint-disable-next-line no-console
      console.error('[Reading Practice Error]', error);
    }
  }, [error]);

  return (
    <ErrorState
      className="mx-auto mt-6 w-full max-w-lg sm:mt-10"
      title="Reading Practice Error"
      message="An unexpected error occurred. Please try again or contact support if the problem persists."
      onRetry={reset}
      retryLabel="Try again"
    />
  );
}