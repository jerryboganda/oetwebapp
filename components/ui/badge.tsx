import { cn } from '@/lib/utils';
import { type HTMLAttributes } from 'react';

/* ─── Generic Badge ─── */
export interface BadgeProps extends HTMLAttributes<HTMLSpanElement> {
  variant?: 'default' | 'success' | 'warning' | 'danger' | 'info' | 'muted';
  size?: 'sm' | 'md';
}

const badgeVariants: Record<string, string> = {
  default: 'bg-primary/10 text-primary border border-primary/20',
  success: 'bg-emerald-50 text-emerald-700 border border-emerald-200/60',
  warning: 'bg-amber-50 text-amber-700 border border-amber-200/60',
  danger: 'bg-red-50 text-red-700 border border-red-200/60',
  info: 'bg-blue-50 text-blue-700 border border-blue-200/60',
  muted: 'bg-gray-100 text-gray-600 border border-gray-200',
};

export function Badge({ variant = 'default', size = 'sm', className, children, ...props }: BadgeProps) {
  return (
    <span
      className={cn(
        'inline-flex items-center font-semibold rounded-full',
        badgeVariants[variant],
        size === 'sm' ? 'px-2 py-0.5 text-xs' : 'px-3 py-1 text-sm',
        className,
      )}
      {...props}
    >
      {children}
    </span>
  );
}

/* ─── Status Badge ─── */
export type StatusType = 'not_started' | 'in_progress' | 'completed' | 'failed' | 'queued' | 'processing' | 'pending_review' | 'reviewed';

const statusConfig: Record<StatusType, { label: string; variant: BadgeProps['variant'] }> = {
  not_started: { label: 'Not Started', variant: 'muted' },
  in_progress: { label: 'In Progress', variant: 'info' },
  completed: { label: 'Completed', variant: 'success' },
  failed: { label: 'Failed', variant: 'danger' },
  queued: { label: 'Queued', variant: 'muted' },
  processing: { label: 'Processing', variant: 'warning' },
  pending_review: { label: 'Pending Review', variant: 'info' },
  reviewed: { label: 'Reviewed', variant: 'success' },
};

export function StatusBadge({ status, className }: { status: StatusType; className?: string }) {
  const { label, variant } = statusConfig[status];
  return <Badge variant={variant} className={className}>{label}</Badge>;
}

/* ─── Score Range Badge ─── */
export function ScoreRangeBadge({ low, high, label, className }: { low: number; high: number; label?: string; className?: string }) {
  return (
    <span className={cn('inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-primary/10 text-primary font-bold text-sm', className)}>
      {label && <span className="text-xs font-semibold text-primary/70">{label}</span>}
      {low}–{high}
    </span>
  );
}

/* ─── Confidence Badge ─── */
export type ConfidenceLevel = 'high' | 'medium' | 'low';

const confidenceConfig: Record<ConfidenceLevel, { label: string; variant: BadgeProps['variant'] }> = {
  high: { label: 'High Confidence', variant: 'success' },
  medium: { label: 'Medium Confidence', variant: 'warning' },
  low: { label: 'Low Confidence', variant: 'danger' },
};

export function ConfidenceBadge({ level, className }: { level: ConfidenceLevel; className?: string }) {
  const { label, variant } = confidenceConfig[level];
  return <Badge variant={variant} className={className}>{label}</Badge>;
}

/* ─── Criterion Chip ─── */
export function CriterionChip({ label, active, onClick, className }: { label: string; active?: boolean; onClick?: () => void; className?: string }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'px-3 py-1.5 rounded-full text-xs font-semibold border transition-all duration-200 active:scale-95 shadow-sm',
        active
          ? 'bg-primary text-white border-primary ring-2 ring-primary/20 ring-offset-1'
          : 'bg-surface text-navy border-gray-200/80 hover:border-primary/50 hover:bg-gray-50 hover:text-primary',
        className,
      )}
    >
      {label}
    </button>
  );
}
