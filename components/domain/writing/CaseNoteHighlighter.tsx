'use client';

import { useState, useMemo } from 'react';
import { CheckCircle2, XCircle, HelpCircle } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import type { WritingCaseNoteRelevance } from '@/lib/writing/types';

export interface CaseNoteSentence {
  index: number;
  text: string;
  /**
   * Ground-truth relevance — when supplied, the component renders a
   * graded view (green ✓ / red ✗ / yellow ?). When omitted, the
   * component renders the input view.
   */
  groundTruth?: WritingCaseNoteRelevance;
}

export interface CaseNoteHighlighterProps {
  caseNotes: CaseNoteSentence[];
  /**
   * Called when the learner taps "Check answers". Passes the indices
   * the learner marked as relevant. Parent calls the grading endpoint
   * and (optionally) re-renders this component with `groundTruth`
   * populated on each sentence to show the scored view.
   */
  onSubmit?: (selectedIndices: number[]) => void;
  /**
   * When true, the component is in scored mode: the user's selection
   * is locked, the Check Answers button is hidden, and per-sentence
   * verdicts are rendered. This is typically derived from whether
   * `groundTruth` is present on the sentences.
   */
  scored?: boolean;
  /**
   * Pre-selected indices when re-rendering in scored mode. Required
   * for the scored view to render colour codes correctly.
   */
  initialSelectedIndices?: number[];
  className?: string;
}

function verdictFor(
  index: number,
  selected: Set<number>,
  groundTruth: WritingCaseNoteRelevance | undefined,
): 'correct' | 'incorrect' | 'missed' | 'partial' | 'neutral' {
  if (!groundTruth) return 'neutral';
  const learnerMarked = selected.has(index);
  if (groundTruth === 'relevant') return learnerMarked ? 'correct' : 'missed';
  if (groundTruth === 'irrelevant') return learnerMarked ? 'incorrect' : 'correct';
  // 'maybe' — accept either decision but flag as partial
  return 'partial';
}

const VERDICT_STYLES: Record<string, { tone: string; icon?: typeof CheckCircle2; label: string }> = {
  correct: {
    tone: 'border-success/40 bg-success/10 text-navy dark:text-white',
    icon: CheckCircle2,
    label: 'Correctly identified',
  },
  incorrect: {
    tone: 'border-danger/40 bg-danger/10 text-navy dark:text-white',
    icon: XCircle,
    label: 'Should not have been marked',
  },
  missed: {
    tone: 'border-warning/40 bg-warning/10 text-navy dark:text-white',
    icon: HelpCircle,
    label: 'Should have been marked',
  },
  partial: {
    tone: 'border-border bg-background-light text-navy dark:text-white',
    icon: HelpCircle,
    label: 'Either answer acceptable',
  },
  neutral: {
    tone: 'border-border bg-surface hover:border-primary/50',
    label: 'Sentence',
  },
};

/**
 * Case-note analysis drill — click-to-toggle sentence relevance.
 *
 * Two modes:
 *   - Input mode (default): each sentence is a clickable card; click
 *     to mark relevant, click again to unmark. Submit triggers
 *     scoring.
 *   - Scored mode (passes `scored` + `groundTruth` on sentences):
 *     each sentence is color-coded vs the ground truth.
 */
export function CaseNoteHighlighter({
  caseNotes,
  onSubmit,
  scored = false,
  initialSelectedIndices,
  className,
}: CaseNoteHighlighterProps) {
  const [selected, setSelected] = useState<Set<number>>(
    () => new Set(initialSelectedIndices ?? []),
  );

  const toggle = (index: number) => {
    if (scored) return;
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(index)) next.delete(index);
      else next.add(index);
      return next;
    });
  };

  const handleSubmit = () => {
    onSubmit?.([...selected].sort((a, b) => a - b));
  };

  const handleReset = () => {
    if (scored) return;
    setSelected(new Set());
  };

  const totalRelevantInTruth = useMemo(
    () => caseNotes.filter((s) => s.groundTruth === 'relevant').length,
    [caseNotes],
  );

  return (
    <Card padding="lg" className={cn('flex flex-col gap-3', className)}>
      <CardContent>
        <header className="mb-3">
          <h3 className="font-extrabold text-base">Mark relevant sentences</h3>
          <p className="text-xs text-muted mt-0.5">
            Click each sentence that belongs in the letter. Click again to unmark.
            {scored ? ` ${totalRelevantInTruth} sentences were truly relevant.` : ''}
          </p>
        </header>
        <ul className="space-y-2" aria-label="Case-note sentences">
          {caseNotes.map((s) => {
            const v = verdictFor(s.index, selected, s.groundTruth);
            const meta = VERDICT_STYLES[v];
            const Icon = meta.icon;
            const learnerMarked = selected.has(s.index);
            return (
              <li key={s.index}>
                <button
                  type="button"
                  onClick={() => toggle(s.index)}
                  disabled={scored}
                  aria-pressed={learnerMarked}
                  className={cn(
                    'w-full text-left flex items-start gap-2 rounded-lg border p-2.5 transition-colors',
                    'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
                    meta.tone,
                    !scored && (learnerMarked ? 'ring-2 ring-primary/40' : ''),
                    scored && 'cursor-default',
                  )}
                >
                  {Icon ? (
                    <Icon className="w-4 h-4 mt-0.5 shrink-0" aria-hidden="true" />
                  ) : (
                    <span
                      className={cn(
                        'inline-block w-4 h-4 mt-0.5 shrink-0 rounded-sm border',
                        learnerMarked ? 'bg-primary border-primary' : 'border-current/60',
                      )}
                      aria-hidden="true"
                    />
                  )}
                  <span className="text-sm flex-1 leading-snug">{s.text}</span>
                  {scored ? (
                    <span className="text-[10px] uppercase tracking-wider font-bold opacity-80">{meta.label}</span>
                  ) : null}
                </button>
              </li>
            );
          })}
        </ul>
        {!scored ? (
          <footer className="mt-4 flex items-center justify-between gap-2">
            <Button type="button" variant="ghost" size="sm" onClick={handleReset}>
              Reset
            </Button>
            <Button
              type="button"
              variant="primary"
              size="md"
              onClick={handleSubmit}
              disabled={selected.size === 0}
              aria-disabled={selected.size === 0}
            >
              Check answers
            </Button>
          </footer>
        ) : null}
      </CardContent>
    </Card>
  );
}
