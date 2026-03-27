import { cn } from '@/lib/utils';
import { type ReactNode } from 'react';
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
  return (
    <div role="status" className={cn('flex flex-col items-center justify-center text-center py-12 px-4', className)}>
      {icon && <div className="w-16 h-16 rounded-full bg-gray-100 flex items-center justify-center text-muted mb-4" aria-hidden="true">{icon}</div>}
      <h3 className="text-lg font-bold text-navy mb-1">{title}</h3>
      {description && <p className="text-sm text-muted max-w-sm mb-4">{description}</p>}
      {action && <Button onClick={action.onClick}>{action.label}</Button>}
    </div>
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
  return (
    <div role="alert" className={cn('flex flex-col items-center justify-center text-center py-12 px-4', className)}>
      <div className="w-16 h-16 rounded-full bg-red-50 flex items-center justify-center text-red-500 mb-4" aria-hidden="true">
        <svg className="w-8 h-8" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" /></svg>
      </div>
      <h3 className="text-lg font-bold text-navy mb-1">{title}</h3>
      <p className="text-sm text-muted max-w-sm mb-4">{message}</p>
      {onRetry && <Button variant="outline" onClick={onRetry}>Try Again</Button>}
    </div>
  );
}
