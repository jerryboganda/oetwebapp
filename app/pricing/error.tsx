'use client';

import { useEffect } from 'react';
import { Button } from '@/components/ui/button';

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
    <div className="flex min-h-[calc(100vh-9rem)] flex-col items-center justify-center gap-4 p-6 text-center">
      <div className="rounded-full bg-red-100 p-4 dark:bg-red-900/30">
        <svg className="h-8 w-8 text-red-600 dark:text-red-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v2m0 4h.01M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z" />
        </svg>
      </div>
      <h2 className="text-lg font-semibold text-navy dark:text-white">Something went wrong</h2>
      <p className="max-w-md text-sm text-muted">An unexpected error occurred in Pricing. Please try again.</p>
      <Button variant="primary" onClick={reset}>Try Again</Button>
    </div>
  );
}
