import type { ReactNode } from 'react';
import { cn } from '@/lib/utils';
import { ProgressBar } from '@/components/ui/progress';

export interface CriterionScoreRowProps {
  label: string;
  score: number;
  max: number;
  /** Target score for this criterion; drives the performance tint. */
  target?: number;
  feedback?: ReactNode;
  exemplar?: ReactNode;
  /** Small trailing meta line (e.g. "Linked rules: …"). */
  meta?: ReactNode;
  className?: string;
}

type PerfTone = 'success' | 'warning' | 'danger';

const shellTint: Record<PerfTone, string> = {
  success: 'border-success/30 bg-success/5',
  warning: 'border-warning/30 bg-warning/5',
  danger: 'border-danger/30 bg-danger/5',
};

const scoreText: Record<PerfTone, string> = {
  success: 'text-success',
  warning: 'text-warning',
  danger: 'text-danger',
};

/**
 * A rubric criterion row for the AI/tutor-graded modules (Writing, Speaking).
 * There is no "correct option" to compare against, so instead the row is tinted
 * green / amber / red by how close the score is to its target.
 */
export function CriterionScoreRow({
  label,
  score,
  max,
  target,
  feedback,
  exemplar,
  meta,
  className,
}: CriterionScoreRowProps) {
  const ratio = max > 0 ? score / max : 0;
  const targetRatio = typeof target === 'number' && max > 0 ? target / max : 0.75;
  const tone: PerfTone = ratio >= targetRatio ? 'success' : ratio >= targetRatio * 0.7 ? 'warning' : 'danger';

  return (
    <div className={cn('rounded-xl border p-3', shellTint[tone], className)}>
      <div className="flex items-baseline justify-between gap-3">
        <span className="text-sm font-bold text-navy dark:text-white" dir="ltr">{label}</span>
        <span className={cn('font-mono text-sm font-bold tabular-nums', scoreText[tone])}>
          {score}/{max}
        </span>
      </div>
      <div className="mt-2">
        <ProgressBar value={score} max={max} color={tone} ariaLabel={`${label}: ${score} of ${max}`} />
      </div>
      {feedback ? <p className="mt-2 text-xs leading-5 text-muted" dir="ltr">{feedback}</p> : null}
      {exemplar ? (
        <div className="mt-2 rounded-lg bg-emerald-50 p-2 text-xs leading-5 text-emerald-800 dark:bg-emerald-950/40 dark:text-emerald-200" dir="ltr">
          {exemplar}
        </div>
      ) : null}
      {meta ? <div className="mt-2 text-[11px] text-muted">{meta}</div> : null}
    </div>
  );
}
