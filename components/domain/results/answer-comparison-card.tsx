import type { ReactNode } from 'react';
import { CheckCircle2, ChevronDown, Clock, Lightbulb, MinusCircle, XCircle } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Badge } from '@/components/ui/badge';
import { formatDurationMs } from '@/lib/results/format-answer';

export interface AnswerComparisonCardProps {
  /** Header label, e.g. "Part B · Question 2". */
  label: string;
  stem?: string | null;
  isCorrect: boolean;
  /** When true, renders a neutral/amber "not answered" state instead of red. */
  unanswered?: boolean;
  /** Pre-formatted candidate answer (use formatAnswerValue for unknowns). */
  yourAnswer: string;
  /** The system's correct answer — always shown in the green cell when present. */
  correctAnswer?: string | null;
  pointsEarned?: number | null;
  maxPoints?: number | null;
  timeMs?: number | null;
  missReason?: { title: string; detail?: string } | null;
  distractor?: string | null;
  explanation?: ReactNode;
  /** Module extras rendered below the grid (transcript reveal, box explanations…). */
  children?: ReactNode;
  /** Override the open/closed default (defaults to open unless correct). */
  defaultOpen?: boolean;
  className?: string;
  testId?: string;
}

type ItemState = 'correct' | 'incorrect' | 'unanswered';

const shellTint: Record<ItemState, string> = {
  correct: 'border-success/30 bg-success/10 border-l-4 border-l-success',
  incorrect: 'border-danger/30 bg-danger/10 border-l-4 border-l-danger',
  unanswered: 'border-warning/30 bg-warning/10 border-l-4 border-l-warning',
};

const iconColor: Record<ItemState, string> = {
  correct: 'text-success',
  incorrect: 'text-danger',
  unanswered: 'text-warning',
};

const statusMeta: Record<ItemState, { label: string; variant: 'success' | 'danger' | 'warning' }> = {
  correct: { label: 'Correct', variant: 'success' },
  incorrect: { label: 'Incorrect', variant: 'danger' },
  unanswered: { label: 'Not answered', variant: 'warning' },
};

const yourCellTint: Record<ItemState, string> = {
  correct: 'border-success/30 bg-success/10',
  incorrect: 'border-danger/30 bg-danger/10',
  unanswered: 'border-border bg-background-light',
};

const yourLabelColor: Record<ItemState, string> = {
  correct: 'text-success',
  incorrect: 'text-danger',
  unanswered: 'text-muted',
};

/**
 * The canonical MCQ / short-answer review card shared by Reading and Listening.
 * Shows the candidate's answer beside the system's correct answer, with the
 * whole card tinted green (correct) / red (incorrect) / amber (unanswered).
 */
export function AnswerComparisonCard({
  label,
  stem,
  isCorrect,
  unanswered = false,
  yourAnswer,
  correctAnswer,
  pointsEarned,
  maxPoints,
  timeMs,
  missReason,
  distractor,
  explanation,
  children,
  defaultOpen,
  className,
  testId,
}: AnswerComparisonCardProps) {
  const state: ItemState = unanswered ? 'unanswered' : isCorrect ? 'correct' : 'incorrect';
  const StatusIcon = state === 'correct' ? CheckCircle2 : state === 'unanswered' ? MinusCircle : XCircle;
  const status = statusMeta[state];
  const open = defaultOpen ?? state !== 'correct';

  return (
    <details
      className={cn('group rounded-2xl border shadow-sm transition-shadow open:shadow-md', shellTint[state], className)}
      data-testid={testId}
      open={open}
    >
      <summary className="flex cursor-pointer list-none items-start gap-3 p-4 sm:p-5">
        <StatusIcon className={cn('mt-0.5 h-6 w-6 shrink-0', iconColor[state])} aria-hidden />
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-xs font-black uppercase tracking-[0.14em] text-muted">{label}</span>
            <Badge variant={status.variant} size="sm">{status.label}</Badge>
            {typeof pointsEarned === 'number' && typeof maxPoints === 'number' ? (
              <Badge variant="muted" size="sm">{pointsEarned}/{maxPoints}</Badge>
            ) : null}
            {typeof timeMs === 'number' && timeMs > 0 ? (
              <Badge variant="muted" size="sm" className="inline-flex items-center gap-1">
                <Clock className="h-3 w-3" aria-hidden />
                {formatDurationMs(timeMs)}
              </Badge>
            ) : null}
          </div>
          {stem ? <p className="mt-1.5 text-sm leading-6 text-navy/90 dark:text-white/80">{stem}</p> : null}
        </div>
        <ChevronDown
          className="mt-1 h-5 w-5 shrink-0 text-muted transition-transform duration-200 group-open:rotate-180"
          aria-hidden
        />
      </summary>

      <div className="space-y-3 px-4 pb-4 sm:px-5 sm:pb-5">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <div className={cn('rounded-xl border p-3', yourCellTint[state])}>
            <p className={cn('text-[11px] font-black uppercase tracking-[0.14em]', yourLabelColor[state])}>Your answer</p>
            <p className="mt-1 break-words text-sm font-semibold text-navy dark:text-white">{yourAnswer}</p>
          </div>
          {correctAnswer ? (
            <div className="rounded-xl border border-success/30 bg-success/10 p-3">
              <p className="text-[11px] font-black uppercase tracking-[0.14em] text-success">Correct answer</p>
              <p className="mt-1 break-words text-sm font-semibold text-navy dark:text-white">{correctAnswer}</p>
            </div>
          ) : null}
        </div>

        {missReason ? (
          <div className="flex items-start gap-2 rounded-xl border border-amber-300/60 bg-amber-50 p-3 text-sm text-amber-900 dark:border-amber-500/40 dark:bg-amber-500/10 dark:text-amber-200">
            <Lightbulb className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
            <span>
              <span className="font-bold">{missReason.title}</span>
              {missReason.detail ? <span className="mt-0.5 block text-xs leading-5 opacity-90">{missReason.detail}</span> : null}
            </span>
          </div>
        ) : null}

        {distractor ? (
          <div className="rounded-xl border border-border bg-background-light p-3">
            <p className="text-[11px] font-black uppercase tracking-[0.14em] text-muted">Distractor type you chose</p>
            <p className="mt-1 text-sm font-semibold text-navy dark:text-white">{distractor}</p>
          </div>
        ) : null}

        {explanation ? (
          <div className="rounded-xl border border-border bg-background-light p-3">
            <p className="text-[11px] font-black uppercase tracking-[0.14em] text-muted">Explanation</p>
            <div className="mt-1 text-sm leading-6 text-navy dark:text-white/90">{explanation}</div>
          </div>
        ) : null}

        {children}
      </div>
    </details>
  );
}
