import { cn } from '@/lib/utils';
import { Sparkles } from 'lucide-react';
import { type HTMLAttributes } from 'react';

/* ─── Generic Badge ─── */
export interface BadgeProps extends HTMLAttributes<HTMLSpanElement> {
  variant?: 'default' | 'success' | 'warning' | 'danger' | 'info' | 'muted' | 'outline' | 'violet' | 'sky' | 'rose' | 'emerald' | 'slate' | 'indigo';
  size?: 'sm' | 'md';
}

const badgeVariants: Record<string, string> = {
  default: 'bg-primary/10 text-primary border border-primary/20',
  success: 'bg-emerald-50 text-emerald-700 border border-emerald-200/60 dark:bg-emerald-950 dark:text-emerald-300 dark:border-emerald-800/60',
  warning: 'bg-amber-50 text-amber-800 border border-amber-200/60 dark:bg-amber-950 dark:text-amber-300 dark:border-amber-800/60',
  danger: 'bg-red-50 text-red-700 border border-red-200/60 dark:bg-red-950 dark:text-red-300 dark:border-red-800/60',
  info: 'bg-blue-50 text-blue-700 border border-blue-200/60 dark:bg-blue-950 dark:text-blue-300 dark:border-blue-800/60',
  muted: 'bg-surface text-muted border border-border dark:bg-surface dark:text-muted dark:border-border',
  outline: 'bg-transparent text-navy border border-border',
  violet:  'bg-violet-50 text-violet-700 border border-violet-200/60 dark:bg-violet-950 dark:text-violet-300 dark:border-violet-800/60',
  sky:     'bg-sky-50    text-sky-700    border border-sky-200/60    dark:bg-sky-950    dark:text-sky-300    dark:border-sky-800/60',
  rose:    'bg-rose-50   text-rose-700   border border-rose-200/60   dark:bg-rose-950   dark:text-rose-300   dark:border-rose-800/60',
  emerald: 'bg-emerald-50 text-emerald-700 border border-emerald-200/60 dark:bg-emerald-950 dark:text-emerald-300 dark:border-emerald-800/60',
  slate:   'bg-background-light text-muted border border-border',
  indigo: 'bg-indigo-50 text-indigo-700 border border-indigo-200/60 dark:bg-indigo-950 dark:text-indigo-300 dark:border-indigo-800/60',
};

export function Badge({ variant = 'default', size = 'sm', className, children, ...props }: BadgeProps) {
  return (
    <span
      className={cn(
        'inline-flex items-center font-bold rounded-full',
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
      {label && <span className="text-xs font-bold text-primary/70">{label}</span>}
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
        'px-3.5 py-2.5 rounded-full text-xs font-bold border transition-[color,background-color,border-color,box-shadow,transform] duration-200 active:scale-95 shadow-sm',
        active
          ? 'bg-primary text-white dark:bg-violet-700 border-primary ring-2 ring-primary/20 ring-offset-1'
          : 'bg-surface text-navy border-border hover:border-primary/50 hover:bg-background-light hover:text-primary dark:text-white dark:border-border dark:hover:bg-background-light dark:hover:border-primary/50',
        className,
      )}
    >
      {label}
    </button>
  );
}

/**
 * Category chip — uses a stable colour per functional category for visual
 * differentiation at a glance. Subtle pastel backgrounds with darker text.
 */
const categoryVariantMap: Record<string, BadgeProps['variant']> = {
  condition: 'rose',
  anatomy: 'sky',
  procedure: 'violet',
  investigation: 'info',
  professional_communication: 'indigo',
  equipment: 'slate',
  medication: 'emerald',
  clinical_communication: 'indigo',
  pharmacology: 'emerald',
  conditions: 'rose',
  symptoms: 'warning',
  symptom: 'warning',
  procedures: 'violet',
  diagnostics: 'info',
  patient_communication: 'violet',
  counselling: 'sky',
  documentation: 'slate',
  medical: 'rose',
  intervention: 'danger',
  general: 'muted',
};

export function CategoryBadge({ category, size }: { category: string; size?: 'sm' | 'md' }) {
  const key = (category || '').toLowerCase().replace(/\s+/g, '_').trim();
  const label = (category || '').replace(/[_-]/g, ' ');
  const variant = categoryVariantMap[key] ?? 'indigo';
  return <Badge variant={variant} size={size}>{label}</Badge>;
}

/**
 * Recall repeat tag — "Nx" where N is how many times a term has appeared across
 * recall exams (its ExamFrequencyCount). A recurring word is shown once and
 * counted, rather than appearing again and again — and its audio is reused
 * rather than regenerated. Tiered styling escalates the visual weight with the
 * count so high-frequency words stand out at a glance:
 *  - 2x  → calm sky pill
 *  - 3x  → elevated violet pill with a ring
 *  - 4x+ → top-tier amber→rose gradient with a sparkle and ring/shadow
 *
 * Renders nothing for counts below 2 (a word seen once is not "repeated").
 */
export function RecallTierBadge({ count, className }: { count: number; className?: string }) {
  if (!count || count < 2) return null;

  const tier = count >= 4 ? 'top' : count === 3 ? 'mid' : 'base';
  const tierClasses: Record<typeof tier, string> = {
    base: 'bg-sky-100 text-sky-700 dark:bg-sky-900/30 dark:text-sky-300',
    mid: 'bg-violet-100 text-violet-700 ring-1 ring-violet-300/60 dark:bg-violet-900/30 dark:text-violet-300 dark:ring-violet-700/50',
    top: 'bg-gradient-to-r from-amber-200 to-rose-200 text-rose-800 ring-1 ring-rose-300/70 shadow-sm dark:from-amber-900/40 dark:to-rose-900/40 dark:text-rose-200 dark:ring-rose-700/50',
  };

  return (
    <span
      title={`Appeared ${count} times across recall exams`}
      className={cn(
        'inline-flex items-center gap-0.5 rounded-full px-2 py-0.5 text-xs font-bold',
        tierClasses[tier],
        className,
      )}
    >
      {tier === 'top' && <Sparkles size={12} className="text-rose-500 dark:text-rose-300" aria-hidden="true" />}
      {count}x
    </span>
  );
}

/**
 * Source / provenance chip — subtle emerald tint for platform-authored content,
 * muted slate for external/unknown provenance.
 */
export function SourceBadge({ label, size }: { label: string; size?: 'sm' | 'md' }) {
  const isPlatform = label.toLowerCase().includes('platform');
  return <Badge variant={isPlatform ? 'emerald' : 'slate'} size={size}>{label}</Badge>;
}
