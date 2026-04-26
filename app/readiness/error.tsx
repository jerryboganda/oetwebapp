'use client';

import { Button } from "@/components/ui/button";
import { useEffect } from 'react';

export default function Error({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    console.error('[Readiness Error]', error);
  }, [error]);

  return (
    <div className="min-h-screen flex items-center justify-center p-6" role="alert">
      <div className="text-center max-w-md space-y-4">
        <div className="w-16 h-16 rounded-full bg-red-100 flex items-center justify-center mx-auto">
          <svg className="w-8 h-8 text-red-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4c-.77-.833-1.964-.833-2.732 0L4.082 16.5c-.77.833.192 2.5 1.732 2.5z" />
          </svg>
        </div>
        <h2 className="text-xl font-semibold text-navy">Readiness Error</h2>
        <p className="text-muted text-sm">
          An unexpected error occurred. Please try again or contact support if the problem persists.
        </p>
        <Button onClick={reset} variant="primary">
          Try again
        </Button>
      </div>
    </div>
  );
}