'use client';

import { cn } from '@/lib/utils';
import Link from 'next/link';
import { type ReactNode } from 'react';
import { motion, useReducedMotion } from 'motion/react';
import { getSurfaceMotion, prefersReducedMotion } from '@/lib/motion';
import { Button } from './button';

/* ─── Empty State ─── */
interface EmptyStateProps {
  icon?: ReactNode;
  title: string;
  description?: string;
  /** Next step. Pass `href` for navigation (renders a link), `onClick` for an in-page action. */
  action?: { label: string; onClick?: () => void; href?: string };
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
        'flex flex-col items-center justify-center rounded-2xl border border-dashed border-border bg-background-light px-6 py-12 text-center shadow-sm',
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
      {action?.href ? (
        <Button asChild>
          <Link href={action.href}>{action.label}</Link>
        </Button>
      ) : action ? (
        <Button onClick={action.onClick}>{action.label}</Button>
      ) : null}
    </motion.div>
  );
}

/* ─── Error State ─── */
interface ErrorStateProps {
  title?: string;
  message?: string;
  onRetry?: () => void;
  retryLabel?: string;
  className?: string;
}

export function ErrorState({ title = 'This page could not be loaded', message = 'Unable to complete this action. Please retry or check your connection.', onRetry, retryLabel = 'Retry this page', className }: ErrorStateProps) {
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const motionProps = getSurfaceMotion('section', reducedMotion);

  return (
    <motion.div
      role="alert"
      {...motionProps}
      className={cn(
        'flex flex-col items-center justify-center rounded-2xl border border-danger/20 bg-danger/5 px-6 py-12 text-center shadow-sm dark:border-danger/30 dark:bg-danger/10',
        className,
      )}
    >
      <div className="mb-4 flex h-16 w-16 items-center justify-center rounded-2xl bg-surface text-danger shadow-sm" aria-hidden="true">
        <svg className="w-8 h-8" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" /></svg>
      </div>
      <h3 className="mb-1 text-lg font-bold tracking-tight text-navy">{title}</h3>
      <p className="mb-4 max-w-sm text-sm leading-6 text-muted">{message}</p>
      {onRetry && <Button variant="outline" onClick={onRetry}>{retryLabel}</Button>}
    </motion.div>
  );
}
