'use client';

import { cn } from '@/lib/utils';

interface ExplanationPanelProps {
  isCorrect: boolean;
  explanation: {
    whyCorrect: string;
    whyWrong: string;
    trapName: string;
    avoidTip: string;
  } | null;
  onAddToReview: () => void;
}

export default function ExplanationPanel({ isCorrect, explanation, onAddToReview }: ExplanationPanelProps) {
  return (
    <div className="rounded-xl border border-border bg-surface p-4 text-sm space-y-3">
      {/* Result banner */}
      <div
        className={cn(
          'flex items-center gap-2 rounded-lg px-4 py-3 font-semibold',
          isCorrect
            ? 'bg-emerald-50 text-emerald-700 dark:bg-emerald-900/20 dark:text-emerald-400'
            : 'bg-rose-50 text-rose-700 dark:bg-rose-900/20 dark:text-rose-400',
        )}
      >
        <span>{isCorrect ? '✓' : '✗'}</span>
        <span>{isCorrect ? 'Correct!' : 'Incorrect'}</span>
      </div>

      {/* Explanation details */}
      {explanation ? (
        <dl className="space-y-2">
          <div>
            <dt className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Why correct</dt>
            <dd className="mt-0.5 text-foreground">{explanation.whyCorrect}</dd>
          </div>
          <div>
            <dt className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Why wrong</dt>
            <dd className="mt-0.5 text-foreground">{explanation.whyWrong}</dd>
          </div>
          <div>
            <dt className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Trap</dt>
            <dd className="mt-0.5 text-foreground">{explanation.trapName}</dd>
          </div>
          <div>
            <dt className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Tip</dt>
            <dd className="mt-0.5 text-foreground">{explanation.avoidTip}</dd>
          </div>
        </dl>
      ) : null}

      {/* Add to review */}
      <button
        type="button"
        onClick={onAddToReview}
        className="text-xs font-medium text-primary underline-offset-2 hover:underline"
      >
        Add to review queue
      </button>
    </div>
  );
}
