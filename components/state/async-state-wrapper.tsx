'use client';

import { type ReactNode } from 'react';
import { PageSkeleton } from '../ui/skeleton';
import { ErrorState } from '../ui/empty-error';
import { InlineAlert } from '../ui/alert';

/* ─── Async State Wrapper ─── */
type AsyncStatus = 'loading' | 'error' | 'empty' | 'partial' | 'success';

interface AsyncStateWrapperProps {
  status: AsyncStatus;
  children: ReactNode;
  /** Error retry handler */
  onRetry?: () => void;
  /** Error message */
  errorMessage?: string;
  /** Empty state content */
  emptyContent?: ReactNode;
  /** Loading content override */
  loadingContent?: ReactNode;
  /** Partial data warning message */
  partialMessage?: string;
  className?: string;
}

export function AsyncStateWrapper({
  status,
  children,
  onRetry,
  errorMessage,
  emptyContent,
  loadingContent,
  partialMessage = 'Some data may be incomplete or still loading.',
  className,
}: AsyncStateWrapperProps) {
  const content = (() => {
    switch (status) {
      case 'loading':
        return loadingContent ?? <PageSkeleton className={className} />;

      case 'error':
        return <ErrorState message={errorMessage} onRetry={onRetry} className={className} />;

      case 'empty':
        return emptyContent ?? <div className="py-12 text-center text-sm text-muted">No data available.</div>;

      case 'partial':
        return (
          <div className={className}>
            <InlineAlert variant="warning" className="mb-4" dismissible>
              {partialMessage}
            </InlineAlert>
            {children}
          </div>
        );

      case 'success':
      default:
        return <>{children}</>;
    }
  })();

  return <div aria-live="polite" aria-busy={status === 'loading'}>{content}</div>;
}

/* ─── Loading Page ─── */
export function LoadingPage() {
  return (
    <div className="flex-1 flex items-center justify-center p-8">
      <PageSkeleton />
    </div>
  );
}

/* ─── Partial Data Warning ─── */
export function PartialDataWarning({ message, className }: { message?: string; className?: string }) {
  return (
    <InlineAlert variant="warning" className={className} dismissible>
      {message ?? 'Some data may be outdated or still loading. Results shown may be partial.'}
    </InlineAlert>
  );
}
