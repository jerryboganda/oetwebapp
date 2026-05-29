'use client';

import { useMemo } from 'react';
import { cn } from '@/lib/utils';

/**
 * WORK-STREAM 7a — Distractor heatmap.
 *
 * Replaces the plain wrong-answer histogram on the admin Listening analytics
 * dashboard with a colour-intensity grid. Each row is one MCQ item; each cell
 * is an answer option (A/B/C/…). Cell intensity encodes how many learners
 * chose that option — hotter = noisier distractor — and the correct option is
 * outlined so the eye can immediately see whether the noise sits on a
 * distractor (a real trap) or is just spread thin.
 *
 * Data shape mirrors the backend `ListeningDistractorHeat` DTO
 * (`lib/listening-authoring-api.ts`): a per-question `wrongAnswerHistogram`
 * keyed by the chosen answer text/label plus the `correctAnswer`. The correct
 * option's own count is not transmitted (only wrong picks are), so its cell is
 * rendered as a neutral, outlined "key" cell rather than a heat cell.
 *
 * Reduced-motion safe: intensity is a static background; the only transition is
 * a colour fade gated behind `motion-safe:` so `prefers-reduced-motion` users
 * see an instant paint with no movement.
 */

export interface DistractorHeatmapRow {
  paperId: string;
  questionNumber: number;
  correctAnswer: string;
  /** Wrong-answer choice frequency, keyed by option label or answer text. */
  wrongAnswerHistogram: Record<string, number>;
}

export interface DistractorHeatmapProps {
  rows: DistractorHeatmapRow[];
  /** Optional empty-state copy. */
  emptyLabel?: string;
  className?: string;
}

// Five-stop ramp from cool/quiet to hot/noisy. Uses the admin danger token so
// the hottest distractors read in the same red the rest of the dashboard uses
// for "hardest". `intensity` is the normalised 0..1 share of the row's peak.
function heatClass(intensity: number, isCorrect: boolean): string {
  if (isCorrect) {
    return 'border-2 border-[var(--admin-success)] bg-[var(--admin-success-tint)] text-[var(--admin-success)]';
  }
  if (intensity <= 0) {
    return 'border border-[var(--admin-border)] bg-[var(--admin-bg-subtle)] text-[var(--admin-fg-muted)]';
  }
  if (intensity < 0.25) {
    return 'border border-[var(--admin-border)] bg-[var(--admin-warning-tint)] text-[var(--admin-fg-default)]';
  }
  if (intensity < 0.5) {
    return 'border border-transparent bg-[var(--admin-warning)]/40 text-[var(--admin-fg-strong)]';
  }
  if (intensity < 0.75) {
    return 'border border-transparent bg-[var(--admin-danger)]/55 text-[var(--admin-danger-fg)]';
  }
  return 'border border-transparent bg-[var(--admin-danger)] text-[var(--admin-danger-fg)]';
}

export function DistractorHeatmap({ rows, emptyLabel, className }: DistractorHeatmapProps) {
  // Build the union of option labels across all rows so every row aligns to the
  // same columns. Sorted so single-letter MCQ keys (A/B/C) order naturally and
  // free-text answers fall after them alphabetically.
  const columns = useMemo(() => {
    const set = new Set<string>();
    for (const row of rows) {
      if (row.correctAnswer) set.add(row.correctAnswer);
      for (const key of Object.keys(row.wrongAnswerHistogram)) set.add(key);
    }
    return Array.from(set).sort((a, b) => a.localeCompare(b, undefined, { numeric: true }));
  }, [rows]);

  if (rows.length === 0) {
    return (
      <p className="text-sm text-admin-fg-muted">
        {emptyLabel ?? 'No MCQ distractor noise detected in this window.'}
      </p>
    );
  }

  return (
    <div className={cn('overflow-x-auto', className)}>
      <table
        className="w-full border-separate border-spacing-1 text-xs"
        role="table"
        aria-label="Distractor heatmap: learner option-choice frequency per question"
      >
        <caption className="sr-only">
          Each row is a question; each cell is an answer option shaded by how many learners chose
          it. The correct option is outlined in green.
        </caption>
        <thead>
          <tr>
            <th scope="col" className="px-2 py-1 text-left font-semibold text-admin-fg-muted">
              Question
            </th>
            {columns.map((label) => (
              <th
                key={label}
                scope="col"
                className="px-2 py-1 text-center font-semibold text-admin-fg-muted"
              >
                {label || '(blank)'}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => {
            const peak = Math.max(1, ...Object.values(row.wrongAnswerHistogram));
            return (
              <tr key={`${row.paperId}-${row.questionNumber}`}>
                <th
                  scope="row"
                  className="whitespace-nowrap px-2 py-1 text-left text-sm font-semibold text-admin-fg-strong"
                >
                  Q{row.questionNumber}
                </th>
                {columns.map((label) => {
                  const isCorrect =
                    !!row.correctAnswer &&
                    label.toLowerCase().trim() === row.correctAnswer.toLowerCase().trim();
                  const count = row.wrongAnswerHistogram[label] ?? 0;
                  const intensity = count / peak;
                  const choseLabel = count === 1 ? '1 learner' : `${count} learners`;
                  const title = isCorrect
                    ? `${label || '(blank)'} — correct answer`
                    : `${label || '(blank)'} — ${choseLabel} chose this`;
                  return (
                    <td key={label} className="p-0">
                      <div
                        title={title}
                        aria-label={title}
                        className={cn(
                          'flex h-8 min-w-[2.25rem] items-center justify-center rounded-admin px-1 font-mono text-[11px] font-semibold motion-safe:transition-colors',
                          heatClass(intensity, isCorrect),
                        )}
                      >
                        {isCorrect ? '✓' : count > 0 ? count : ''}
                      </div>
                    </td>
                  );
                })}
              </tr>
            );
          })}
        </tbody>
      </table>

      {/* Legend */}
      <div className="mt-3 flex flex-wrap items-center gap-3 text-[11px] text-admin-fg-muted">
        <span className="inline-flex items-center gap-1.5">
          <span
            aria-hidden="true"
            className="h-3 w-3 rounded-sm border-2 border-[var(--admin-success)] bg-[var(--admin-success-tint)]"
          />
          Correct option
        </span>
        <span className="inline-flex items-center gap-1.5">
          <span
            aria-hidden="true"
            className="h-3 w-3 rounded-sm bg-[var(--admin-warning-tint)]"
          />
          Light noise
        </span>
        <span className="inline-flex items-center gap-1.5">
          <span aria-hidden="true" className="h-3 w-3 rounded-sm bg-[var(--admin-danger)]" />
          Heavy noise
        </span>
      </div>
    </div>
  );
}
