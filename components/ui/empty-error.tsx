'use client';

import { cn } from '@/lib/utils';
import { type ReactNode } from 'react';
import { motion, useReducedMotion } from 'motion/react';
import { getSurfaceMotion, prefersReducedMotion } from '@/lib/motion';
import { Button } from './button';

/* ─── Empty State ─── */
interface EmptyStateProps {
  icon?: ReactNode;
  title: string;
  description?: string;
  action?: { label: string; onClick: () => void };
  className?: string;
}

export function EmptyState({ icon, title, description, action, className }: EmptyStateProps) {
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const motionProps = getSurfaceMotion('section', reducedMotion);

  return (
    <motion.div
      role="status"
      {...motionProps}
      className={cn(
        'flex flex-col items-center justify-center rounded-[24px] border border-dashed border-gray-200 bg-background-light px-6 py-12 text-center shadow-sm',
        className,
      )}
    >
      {icon ? (
        <div className="mb-4 flex h-16 w-16 items-center justify-center rounded-2xl bg-surface text-muted shadow-sm" aria-hidden="true">
          {icon}
        </div>
      ) : null}
      <h3 className="mb-1 text-lg font-bold tracking-tight text-navy">{title}</h3>
      {description && <p className="mb-4 max-w-sm text-sm leading-6 text-muted">{description}</p>}
      {action && <Button onClick={action.onClick}>{action.label}</Button>}
    </motion.div>
  );
}

/* ─── Error State ─── */
interface ErrorStateProps {
  title?: string;
  message?: string;
  onRetry?: () => void;
  className?: string;
}

export function ErrorState({ title = 'Something went wrong', message = 'An unexpected error occurred. Please try again.', onRetry, className }: ErrorStateProps) {
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const motionProps = getSurfaceMotion('section', reducedMotion);

  return (
    <motion.div
      role="alert"
      {...motionProps}
      className={cn(
        'flex flex-col items-center justify-center rounded-[24px] border border-red-100 bg-red-50/70 px-6 py-12 text-center shadow-sm dark:border-red-900 dark:bg-red-950/70',
        className,
      )}
    >
      <div className="mb-4 flex h-16 w-16 items-center justify-center rounded-2xl bg-white text-red-500 shadow-sm dark:bg-gray-900 dark:text-red-400" aria-hidden="true">
        <svg className="w-8 h-8" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" /></svg>
      </div>
      <h3 className="mb-1 text-lg font-bold tracking-tight text-navy">{title}</h3>
      <p className="mb-4 max-w-sm text-sm leading-6 text-muted">{message}</p>
      {onRetry && <Button variant="outline" onClick={onRetry}>Try Again</Button>}
    </motion.div>
  );
}
