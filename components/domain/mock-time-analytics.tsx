'use client';

import React from 'react';
import { Clock, Hourglass, Timer, AlertCircle } from 'lucide-react';

export interface TimeAnalyticsSection {
  sectionId: string;
  subtest: string;
  startedAt?: string | null;
  submittedAt?: string | null;
  completedAt?: string | null;
  deadlineAt?: string | null;
  secondsUsed?: number | null;
}

export interface TimeAnalyticsPerQuestion {
  sectionId: string;
  subtest: string;
  itemId: string;
  secondsSpent?: number | null;
  correct?: boolean | null;
}

export interface TimeAnalyticsBreakdownProps {
  timingAnalysis: TimeAnalyticsSection[];
  perQuestionTiming?: TimeAnalyticsPerQuestion[] | null;
  className?: string;
}

function formatDuration(seconds: number | null | undefined): string {
  if (seconds == null || !Number.isFinite(seconds) || seconds < 0) return 'N/A';
  const total = Math.round(seconds);
  const mins = Math.floor(total / 60);
  const secs = total % 60;
  if (mins === 0) return `${secs}s`;
  if (secs === 0) return `${mins}m`;
  return `${mins}m ${secs}s`;
}

function parseTimestamp(value: string | null | undefined): number | null {
  if (!value) return null;
  const t = Date.parse(value);
  return Number.isFinite(t) ? t : null;
}

interface ComputedRow {
  key: string;
  subtest: string;
  sectionId: string;
  usedSeconds: number | null;
  allottedSeconds: number | null;
  utilisationPct: number | null;
  overran: boolean;
  hasDeadline: boolean;
}

function computeRow(section: TimeAnalyticsSection): ComputedRow {
  const startedMs = parseTimestamp(section.startedAt);
  const deadlineMs = parseTimestamp(section.deadlineAt);
  const submittedMs =
    parseTimestamp(section.submittedAt) ?? parseTimestamp(section.completedAt);

  let usedSeconds: number | null =
    section.secondsUsed != null && Number.isFinite(section.secondsUsed)
      ? Math.max(0, Math.round(section.secondsUsed))
      : null;
  if (usedSeconds == null && startedMs != null && submittedMs != null) {
    usedSeconds = Math.max(0, Math.round((submittedMs - startedMs) / 1000));
  }

  let allottedSeconds: number | null = null;
  if (startedMs != null && deadlineMs != null && deadlineMs > startedMs) {
    allottedSeconds = Math.round((deadlineMs - startedMs) / 1000);
  }

  let utilisationPct: number | null = null;
  if (usedSeconds != null && allottedSeconds != null && allottedSeconds > 0) {
    utilisationPct = (usedSeconds / allottedSeconds) * 100;
  }

  const overran =
    usedSeconds != null && allottedSeconds != null && usedSeconds > allottedSeconds;

  return {
    key: section.sectionId || `${section.subtest}-row`,
    subtest: section.subtest,
    sectionId: section.sectionId,
    usedSeconds,
    allottedSeconds,
    utilisationPct,
    overran,
    hasDeadline: allottedSeconds != null,
  };
}

/**
 * TimeAnalyticsBreakdown — V1 timing renderer for the mock report page.
 *
 * Renders one row per section with a bar comparing used vs. allotted time
 * (allotted is derived from `deadlineAt - startedAt` when both are present).
 * When `perQuestionTiming` is populated, surfaces a small "longest 3 questions"
 * panel so learners can see where they spent disproportionate time.
 *
 * If neither timing nor per-question data is meaningful, falls back to a
 * muted "Timing analysis pending" state.
 */
export function TimeAnalyticsBreakdown({
  timingAnalysis,
  perQuestionTiming,
  className,
}: TimeAnalyticsBreakdownProps) {
  const rows = Array.isArray(timingAnalysis) ? timingAnalysis.map(computeRow) : [];
  const usableRows = rows.filter(
    (r) => r.usedSeconds != null || r.allottedSeconds != null,
  );

  const longestQuestions = Array.isArray(perQuestionTiming)
    ? perQuestionTiming
        .filter((q) => q.secondsSpent != null && Number.isFinite(q.secondsSpent))
        .slice()
        .sort((a, b) => (b.secondsSpent ?? 0) - (a.secondsSpent ?? 0))
        .slice(0, 3)
    : [];

  if (usableRows.length === 0 && longestQuestions.length === 0) {
    return (
      <div
        className={
          'rounded-2xl border border-border bg-background-light p-6 text-center ' +
          (className ?? '')
        }
      >
        <div className="mx-auto flex h-10 w-10 items-center justify-center rounded-full bg-border/40">
          <Hourglass className="h-5 w-5 text-muted" />
        </div>
        <p className="mt-3 text-sm font-bold text-navy">Timing analysis pending</p>
        <p className="mt-1 text-xs leading-5 text-muted">
          Per-section timing will appear here once your mock sections have been
          submitted and processed.
        </p>
      </div>
    );
  }

  return (
    <div className={'space-y-4 ' + (className ?? '')}>
      <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
        <div className="mb-4 flex items-center gap-2">
          <Clock className="h-5 w-5 text-primary" />
          <h3 className="text-sm font-black uppercase tracking-widest text-muted">
            Time per section
          </h3>
        </div>

        {usableRows.length === 0 ? (
          <p className="text-xs leading-5 text-muted">
            No section timing data was captured for this attempt.
          </p>
        ) : (
          <ul className="space-y-4">
            {usableRows.map((row) => {
              const pct = row.utilisationPct;
              const widthPct =
                pct != null ? Math.min(100, Math.max(0, pct)) : null;
              const overranWidthPct =
                pct != null && pct > 100 ? Math.min(100, pct - 100) : null;
              const barTone = row.overran
                ? 'bg-danger'
                : pct != null && pct >= 90
                  ? 'bg-warning'
                  : 'bg-primary';

              return (
                <li key={row.key}>
                  <div className="mb-1.5 flex flex-wrap items-baseline justify-between gap-2">
                    <div className="flex items-center gap-2">
                      <span className="text-sm font-bold text-navy capitalize">
                        {row.subtest || 'Section'}
                      </span>
                      {row.sectionId ? (
                        <span className="text-[10px] font-black uppercase tracking-widest text-muted">
                          {row.sectionId}
                        </span>
                      ) : null}
                    </div>
                    <div className="flex items-center gap-2 text-xs text-muted">
                      <span className="font-bold text-navy">
                        {formatDuration(row.usedSeconds)}
                      </span>
                      <span className="text-muted">
                        /
                        {row.hasDeadline
                          ? ` ${formatDuration(row.allottedSeconds)}`
                          : ' no limit'}
                      </span>
                      {row.utilisationPct != null ? (
                        <span
                          className={
                            'font-black ' +
                            (row.overran
                              ? 'text-danger'
                              : row.utilisationPct >= 90
                                ? 'text-warning'
                                : 'text-primary')
                          }
                        >
                          {Math.round(row.utilisationPct)}%
                        </span>
                      ) : null}
                    </div>
                  </div>
                  <div
                    className="relative h-2 w-full overflow-hidden rounded-full bg-border/30"
                    role="progressbar"
                    aria-valuenow={
                      row.utilisationPct != null
                        ? Math.min(100, Math.round(row.utilisationPct))
                        : 0
                    }
                    aria-valuemin={0}
                    aria-valuemax={100}
                    aria-label={`${row.subtest} time used`}
                  >
                    {widthPct != null ? (
                      <div
                        className={'h-full ' + barTone}
                        style={{ width: `${widthPct}%` }}
                      />
                    ) : null}
                    {overranWidthPct != null ? (
                      <div
                        className="absolute right-0 top-0 h-full border-l border-surface bg-danger/50"
                        style={{ width: `${overranWidthPct}%` }}
                        aria-hidden="true"
                      />
                    ) : null}
                  </div>
                  {row.overran ? (
                    <p className="mt-1 flex items-center gap-1 text-[11px] font-bold text-danger">
                      <AlertCircle className="h-3 w-3" />
                      Ran past the deadline by{' '}
                      {formatDuration(
                        (row.usedSeconds ?? 0) - (row.allottedSeconds ?? 0),
                      )}
                    </p>
                  ) : null}
                </li>
              );
            })}
          </ul>
        )}
      </div>

      {longestQuestions.length > 0 ? (
        <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
          <div className="mb-3 flex items-center gap-2">
            <Timer className="h-5 w-5 text-warning" />
            <h3 className="text-sm font-black uppercase tracking-widest text-muted">
              Longest {longestQuestions.length} question
              {longestQuestions.length === 1 ? '' : 's'}
            </h3>
          </div>
          <ul className="grid gap-2 sm:grid-cols-3">
            {longestQuestions.map((q, idx) => {
              const correct = q.correct === true;
              const wrong = q.correct === false;
              const toneClasses = correct
                ? 'border-success/30 bg-success/5'
                : wrong
                  ? 'border-danger/30 bg-danger/5'
                  : 'border-border bg-background-light';
              return (
                <li
                  key={`${q.sectionId || 'section'}-${q.itemId || idx}`}
                  className={`rounded-xl border p-3 ${toneClasses}`}
                >
                  <p className="text-[10px] font-black uppercase tracking-widest text-muted">
                    {q.subtest || 'Question'}
                  </p>
                  <p className="mt-1 text-sm font-black text-navy truncate">
                    {q.itemId || `#${idx + 1}`}
                  </p>
                  <div className="mt-2 flex items-center justify-between text-xs">
                    <span className="font-bold text-navy">
                      {formatDuration(q.secondsSpent)}
                    </span>
                    {correct ? (
                      <span className="text-[10px] font-black uppercase tracking-widest text-success">
                        Correct
                      </span>
                    ) : wrong ? (
                      <span className="text-[10px] font-black uppercase tracking-widest text-danger">
                        Incorrect
                      </span>
                    ) : null}
                  </div>
                </li>
              );
            })}
          </ul>
        </div>
      ) : null}
    </div>
  );
}

export default TimeAnalyticsBreakdown;
