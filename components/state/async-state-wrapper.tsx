'use client';

import { type ReactNode } from 'react';
import { motion, useReducedMotion } from 'motion/react';
import { cn } from '@/lib/utils';
import { getSurfaceMotion, prefersReducedMotion } from '@/lib/motion';
import { PageSkeleton } from '../ui/skeleton';
import { ErrorState } from '../ui/empty-error';
import { InlineAlert } from '../ui/alert';

/* Async State Wrapper */
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
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const motionProps = getSurfaceMotion('state', reducedMotion);

  const content = (() => {
    switch (status) {
      case 'loading':
        return loadingContent ? (
          <motion.div key="loading" {...motionProps} className={cn('w-full min-w-0', className)}>
            {loadingContent}
          </motion.div>
        ) : (
          <PageSkeleton key="loading" className={className} />
        );

      case 'error':
        return (
          <motion.div key="error" {...motionProps} className={cn('w-full min-w-0', className)}>
            <ErrorState message={errorMessage} onRetry={onRetry} />
          </motion.div>
        );

      case 'empty':
        return (
          <motion.div key="empty" {...motionProps} className={cn('w-full min-w-0', className)}>
            {emptyContent ?? <div className="py-12 text-center text-sm text-muted">No data available.</div>}
          </motion.div>
        );

      case 'partial':
        return (
          <motion.div key="partial" {...motionProps} className={cn('w-full min-w-0', className)}>
            <InlineAlert variant="warning" className="mb-4" dismissible>
              {partialMessage}
            </InlineAlert>
            <div>{children}</div>
          </motion.div>
        );

      case 'success':
      default:
        return (
          <motion.div key="success" {...motionProps} className={cn('w-full min-w-0', className)}>
            {children}
          </motion.div>
        );
    }
  })();

  return (
    <motion.div layout aria-live="polite" aria-busy={status === 'loading'} className="w-full min-w-0">
      {content}
    </motion.div>
  );
}

/* Loading Page */
export function LoadingPage() {
  return (
    <div className="flex flex-1 items-center justify-center p-8" aria-live="polite" aria-busy="true">
      <PageSkeleton />
    </div>
  );
}

/* Partial Data Warning */
export function PartialDataWarning({ message, className }: { message?: string; className?: string }) {
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const motionProps = getSurfaceMotion('state', reducedMotion);

  return (
    <motion.div layout {...motionProps} className={cn('w-full min-w-0', className)}>
      <InlineAlert variant="warning" dismissible>
        {message ?? 'Some data may be outdated or still loading. Results shown may be partial.'}
      </InlineAlert>
    </motion.div>
  );
}
