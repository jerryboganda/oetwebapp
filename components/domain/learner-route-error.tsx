'use client';

import { useEffect } from 'react';
import { ErrorState } from '@/components/ui/empty-error';

export interface LearnerRouteErrorProps {
  error: Error & { digest?: string };
  reset: () => void;
}

const DEFAULT_MESSAGE = 'An unexpected error occurred. Please try again or contact support if the problem persists.';

/** The in-shell error state a learner route segment's error.tsx renders (inside the workspace, not full screen). */
export function LearnerRouteError({
  error,
  reset,
  title,
  message = DEFAULT_MESSAGE,
}: LearnerRouteErrorProps & { title: string; message?: string }) {
  useEffect(() => {
    console.error(`[${title}]`, error);
  }, [error, title]);

  return <ErrorState className="mx-auto w-full max-w-lg" title={title} message={message} onRetry={reset} retryLabel="Try again" />;
}
