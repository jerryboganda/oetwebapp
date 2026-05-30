'use client';

/**
 * RubricPanel — six-criterion OET writing rubric editor (spec §12).
 *
 * Each criterion uses a clamped number input + band stepper buttons with the
 * correct max range (C1 Purpose 0–3; C2–C6 0–7). A live raw total /38 updates
 * as the tutor edits. Values are held as strings (the score "draft") so that a
 * blank field means "no override / keep AI value" rather than 0; the parent
 * collapses the draft into a numeric override on submit.
 *
 * The AI suggestion (from context.aiGrade / preAssessment.estimatedBands) is
 * shown as an explicitly-labelled starting point and is editable.
 */

import { Minus, Plus, Sparkles } from 'lucide-react';
import { cn } from '@/lib/utils';
import type { WritingCriteriaScoresDto, WritingCriterionCode } from '@/lib/writing/types';
import {
  CRITERION_CODES,
  CRITERION_LABEL,
  CRITERION_MAX,
  RAW_TOTAL_MAX,
  clampCriterionScore,
  draftRawTotal,
  isDraftComplete,
  parseScoreInput,
  type ScoreDraft,
} from './shared';

export interface RubricPanelProps {
  draft: ScoreDraft;
  onChange: (next: ScoreDraft) => void;
  /** AI/heuristic suggested bands, rendered as ghost guidance per criterion. */
  aiSuggestion?: WritingCriteriaScoresDto | null;
  readOnly?: boolean;
  className?: string;
}

export function RubricPanel({ draft, onChange, aiSuggestion, readOnly = false, className }: RubricPanelProps) {
  const total = draftRawTotal(draft);
  const complete = isDraftComplete(draft);

  const setValue = (code: WritingCriterionCode, raw: string) => {
    onChange({ ...draft, [code]: raw });
  };

  const step = (code: WritingCriterionCode, delta: number) => {
    const current = parseScoreInput(code, draft[code]) ?? 0;
    const next = clampCriterionScore(code, current + delta);
    onChange({ ...draft, [code]: String(next) });
  };

  return (
    <section aria-labelledby="rubric-heading" className={cn('space-y-3', className)}>
      <div className="flex items-center justify-between gap-3">
        <h3 id="rubric-heading" className="text-sm font-bold text-navy">Rubric scores</h3>
        <div className="text-right">
          <p className="text-xs font-bold uppercase tracking-wider text-muted">Raw total</p>
          <p className={cn('text-lg font-black tabular-nums', complete ? 'text-navy' : 'text-muted')}>
            {total}
            <span className="text-sm font-bold text-muted">/{RAW_TOTAL_MAX}</span>
          </p>
        </div>
      </div>

      <ul className="space-y-2">
        {CRITERION_CODES.map((code) => {
          const max = CRITERION_MAX[code];
          const value = parseScoreInput(code, draft[code]);
          const suggested = aiSuggestion ? aiSuggestion[code] : null;
          return (
            <li key={code} className="rounded-xl border border-border bg-surface p-3">
              <div className="flex items-center justify-between gap-3">
                <label htmlFor={`rubric-${code}`} className="text-sm font-bold text-navy">
                  {CRITERION_LABEL[code]}
                  <span className="ml-1 text-xs font-normal text-muted">(0–{max})</span>
                </label>
                <div className="flex items-center gap-1">
                  {!readOnly ? (
                    <button
                      type="button"
                      onClick={() => step(code, -1)}
                      disabled={(value ?? 0) <= 0}
                      className="flex h-7 w-7 items-center justify-center rounded-md border border-border text-muted transition-colors hover:bg-background-light disabled:opacity-40"
                      aria-label={`Decrease ${CRITERION_LABEL[code]}`}
                    >
                      <Minus className="h-3.5 w-3.5" aria-hidden="true" />
                    </button>
                  ) : null}
                  <input
                    id={`rubric-${code}`}
                    type="number"
                    inputMode="numeric"
                    min={0}
                    max={max}
                    step={1}
                    value={draft[code]}
                    readOnly={readOnly}
                    onChange={(e) => setValue(code, e.target.value)}
                    onBlur={(e) => {
                      const parsed = parseScoreInput(code, e.target.value);
                      setValue(code, parsed === null ? '' : String(parsed));
                    }}
                    placeholder="—"
                    aria-describedby={suggested !== null ? `rubric-ai-${code}` : undefined}
                    className="h-8 w-16 rounded-md border border-border bg-background-light px-2 text-center text-sm font-bold tabular-nums text-navy focus:outline-none focus:ring-2 focus:ring-primary/40 read-only:opacity-70"
                  />
                  {!readOnly ? (
                    <button
                      type="button"
                      onClick={() => step(code, +1)}
                      disabled={(value ?? 0) >= max}
                      className="flex h-7 w-7 items-center justify-center rounded-md border border-border text-muted transition-colors hover:bg-background-light disabled:opacity-40"
                      aria-label={`Increase ${CRITERION_LABEL[code]}`}
                    >
                      <Plus className="h-3.5 w-3.5" aria-hidden="true" />
                    </button>
                  ) : null}
                </div>
              </div>
              {suggested !== null ? (
                <p id={`rubric-ai-${code}`} className="mt-1.5 flex items-center gap-1 text-xs text-muted">
                  <Sparkles className="h-3 w-3 text-primary" aria-hidden="true" />
                  AI suggestion: <span className="font-semibold text-foreground">{suggested}/{max}</span>
                  {!readOnly && value !== suggested ? (
                    <button
                      type="button"
                      onClick={() => setValue(code, String(clampCriterionScore(code, suggested)))}
                      className="ml-1 rounded text-primary underline-offset-2 hover:underline focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                    >
                      use
                    </button>
                  ) : null}
                </p>
              ) : null}
            </li>
          );
        })}
      </ul>

      {!complete ? (
        <p className="text-xs text-muted">
          Blank criteria keep the AI value (treated as 0 in the live total until you set a band).
        </p>
      ) : null}
    </section>
  );
}
