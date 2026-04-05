'use client';

import { useEffect } from 'react';
import { LearnerWorkspaceContainer } from '@/components/layout/learner-workspace-container';
import { ErrorState } from '@/components/ui/empty-error';

export default function AdminError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    console.error('[Admin Error]', error);
  }, [error]);

  return (
    <LearnerWorkspaceContainer className="flex min-h-[calc(100vh-9rem)] items-center justify-center">
      <ErrorState
        title="Admin Console Error"
        message="An unexpected error occurred in the admin console. Please try again or contact support if the problem persists."
        onRetry={reset}
        className="w-full max-w-xl"
      />
    </LearnerWorkspaceContainer>
  );
}
