'use client';

import { useState } from 'react';
import { Loader2, RefreshCw } from 'lucide-react';
import { cn } from '@/lib/utils';
import {
  recalcReadingPaper,
  type ReadingRecalcInput,
  type ReadingRecalcResult,
  type ReadingRecalcScope,
  type ReadingTutorArea,
} from '@/lib/reading-tutor-api';

/**
 * ReadingRecalcPanel — re-grades a paper's attempts against the current
 * accepted-answer set. Attempts that carry a manual override are skipped (the
 * backend reports the count). Scope `thisAttempt` requires an attempt id.
 */

export interface ReadingRecalcPanelProps {
  paperId: string;
  area: ReadingTutorArea;
  className?: string;
}

export function ReadingRecalcPanel({ paperId, area, className }: ReadingRecalcPanelProps) {
  const [scope, setScope] = useState<ReadingRecalcScope>('allAttemptsForPaper');
  const [attemptId, setAttemptId] = useState('');
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<ReadingRecalcResult | null>(null);

  async function handleRun(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setResult(null);

    if (scope === 'thisAttempt' && !attemptId.trim()) {
      setError('An attempt id is required to recalculate a single attempt.');
      return;
    }

    const body: ReadingRecalcInput = {
      scope,
      attemptId: scope === 'thisAttempt' ? attemptId.trim() : null,
    };

    setRunning(true);
    try {
      const data = await recalcReadingPaper(paperId, body, area);
      setResult(data);
    } catch {
      setError('Recalculation failed. Please try again.');
    } finally {
      setRunning(false);
    }
  }

  return (
    <form
      onSubmit={handleRun}
      aria-label="Recalculate scores"
      className={cn(
        'space-y-4 rounded-admin-lg border border-admin-border bg-admin-bg-surface p-4 sm:p-5',
        className,
      )}
    >
      <div className="flex items-center gap-2">
        <RefreshCw className="h-4 w-4 text-admin-fg-muted" aria-hidden="true" />
        <h3 className="text-sm font-semibold text-admin-fg-strong">Recalculate scores</h3>
      </div>
      <p className="text-sm text-admin-fg-muted">
        Re-grade attempts against the current accepted-answer set. Attempts with a manual override are left untouched.
      </p>

      <fieldset className="flex flex-wrap gap-4" disabled={running}>
        <legend className="sr-only">Recalculation scope</legend>
        <label className="inline-flex items-center gap-2 text-sm text-admin-fg-default">
          <input
            type="radio"
            name={`recalc-scope-${paperId}`}
            value="allAttemptsForPaper"
            checked={scope === 'allAttemptsForPaper'}
            onChange={() => setScope('allAttemptsForPaper')}
            className="h-4 w-4"
          />
          All attempts for this paper
        </label>
        <label className="inline-flex items-center gap-2 text-sm text-admin-fg-default">
          <input
            type="radio"
            name={`recalc-scope-${paperId}`}
            value="thisAttempt"
            checked={scope === 'thisAttempt'}
            onChange={() => setScope('thisAttempt')}
            className="h-4 w-4"
          />
          A single attempt
        </label>
      </fieldset>

      {scope === 'thisAttempt' ? (
        <div className="flex flex-col gap-1.5">
          <label htmlFor={`recalc-attempt-${paperId}`} className="text-sm font-medium text-admin-fg-default">
            Attempt id
          </label>
          <input
            id={`recalc-attempt-${paperId}`}
            type="text"
            value={attemptId}
            disabled={running}
            onChange={(event) => setAttemptId(event.target.value)}
            className="rounded-admin-lg border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-default focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
          />
        </div>
      ) : null}

      {error ? (
        <p role="alert" className="text-sm text-[var(--admin-danger)]">
          {error}
        </p>
      ) : null}

      {result ? (
        <div role="status" className="rounded-admin-lg border border-admin-border bg-admin-bg-subtle p-3 text-sm text-admin-fg-default">
          <p>
            Recalculated <span className="font-semibold">{result.recalculatedCount}</span> of{' '}
            <span className="font-semibold">{result.totalConsidered}</span> attempt
            {result.totalConsidered === 1 ? '' : 's'}.
          </p>
          {result.skippedOverrideCount > 0 ? (
            <p className="mt-0.5 text-admin-fg-muted">
              Skipped {result.skippedOverrideCount} with a manual override.
            </p>
          ) : null}
        </div>
      ) : null}

      <button
        type="submit"
        disabled={running}
        className="inline-flex items-center gap-2 rounded-admin-lg bg-[var(--admin-primary)] px-4 py-2 text-sm font-medium text-[var(--admin-primary-fg)] transition-colors hover:bg-[var(--admin-primary-hover)] disabled:opacity-50"
      >
        {running ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : null}
        Run recalculation
      </button>
    </form>
  );
}
