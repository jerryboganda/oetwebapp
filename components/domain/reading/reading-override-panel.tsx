'use client';

import { useState } from 'react';
import { Loader2, ShieldCheck } from 'lucide-react';
import { cn } from '@/lib/utils';
import {
  clearReadingAttemptScoreOverride,
  overrideReadingAttemptScore,
  type ReadingPrivilegedAttemptReview,
  type ReadingScoreOverrideInput,
  type ReadingTutorArea,
} from '@/lib/reading-tutor-api';

/**
 * ReadingOverridePanel — manual score override control shared by the admin and
 * expert tutor surfaces.
 *
 * A tutor may set EITHER a raw score or a scaled score (both optional on the
 * wire, but the panel requires at least one) plus a mandatory reason. Clearing
 * the override reverts to the system-graded score. Both actions return the
 * refreshed privileged review which is bubbled up via `onUpdated`.
 */

type OverrideField = 'raw' | 'scaled';

export interface ReadingOverridePanelProps {
  attemptId: string;
  area: ReadingTutorArea;
  review: ReadingPrivilegedAttemptReview;
  onUpdated: (review: ReadingPrivilegedAttemptReview) => void;
  className?: string;
}

export function ReadingOverridePanel({
  attemptId,
  area,
  review,
  onUpdated,
  className,
}: ReadingOverridePanelProps) {
  const [field, setField] = useState<OverrideField>('raw');
  const [value, setValue] = useState('');
  const [reason, setReason] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [clearing, setClearing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  const busy = submitting || clearing;

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setSuccess(null);

    const trimmedReason = reason.trim();
    if (!trimmedReason) {
      setError('A reason is required to override the score.');
      return;
    }
    const parsed = Number(value);
    if (value.trim() === '' || Number.isNaN(parsed)) {
      setError('Enter a valid numeric score.');
      return;
    }

    const body: ReadingScoreOverrideInput = {
      reason: trimmedReason,
      rawScore: field === 'raw' ? parsed : null,
      scaledScore: field === 'scaled' ? parsed : null,
    };

    setSubmitting(true);
    try {
      const updated = await overrideReadingAttemptScore(attemptId, body, area);
      onUpdated(updated);
      setSuccess('Override applied.');
      setValue('');
      setReason('');
    } catch {
      setError('Failed to apply the override. Please try again.');
    } finally {
      setSubmitting(false);
    }
  }

  async function handleClear() {
    setError(null);
    setSuccess(null);
    setClearing(true);
    try {
      const updated = await clearReadingAttemptScoreOverride(attemptId, area);
      onUpdated(updated);
      setSuccess('Override cleared.');
    } catch {
      setError('Failed to clear the override. Please try again.');
    } finally {
      setClearing(false);
    }
  }

  const fieldId = `override-value-${attemptId}`;
  const reasonId = `override-reason-${attemptId}`;

  return (
    <form
      onSubmit={handleSubmit}
      aria-label="Score override"
      className={cn(
        'space-y-3 rounded-xl border border-slate-200 bg-white p-4 dark:border-slate-700 dark:bg-slate-900',
        className,
      )}
    >
      <div className="flex items-center gap-2">
        <ShieldCheck className="h-4 w-4 text-slate-500 dark:text-slate-400" aria-hidden="true" />
        <h3 className="text-sm font-semibold text-slate-900 dark:text-slate-100">Manual score override</h3>
      </div>

      <fieldset className="flex flex-wrap gap-4" disabled={busy}>
        <legend className="sr-only">Override type</legend>
        <label className="inline-flex items-center gap-2 text-sm text-slate-700 dark:text-slate-300">
          <input
            type="radio"
            name={`override-field-${attemptId}`}
            value="raw"
            checked={field === 'raw'}
            onChange={() => setField('raw')}
            className="h-4 w-4"
          />
          Raw score{review.maxRawScore ? ` (0–${review.maxRawScore})` : ''}
        </label>
        <label className="inline-flex items-center gap-2 text-sm text-slate-700 dark:text-slate-300">
          <input
            type="radio"
            name={`override-field-${attemptId}`}
            value="scaled"
            checked={field === 'scaled'}
            onChange={() => setField('scaled')}
            className="h-4 w-4"
          />
          Scaled score (0–500)
        </label>
      </fieldset>

      <div className="flex flex-col gap-1.5">
        <label htmlFor={fieldId} className="text-sm font-medium text-slate-700 dark:text-slate-300">
          {field === 'raw' ? 'Raw score' : 'Scaled score'}
        </label>
        <input
          id={fieldId}
          type="number"
          inputMode="numeric"
          min={0}
          max={field === 'raw' ? review.maxRawScore || undefined : 500}
          value={value}
          disabled={busy}
          onChange={(event) => setValue(event.target.value)}
          className="w-40 rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 focus:outline-none focus:ring-2 focus:ring-slate-400 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-100"
        />
      </div>

      <div className="flex flex-col gap-1.5">
        <label htmlFor={reasonId} className="text-sm font-medium text-slate-700 dark:text-slate-300">
          Reason <span className="text-red-600 dark:text-red-400" aria-hidden="true">*</span>
        </label>
        <textarea
          id={reasonId}
          rows={2}
          required
          value={reason}
          disabled={busy}
          placeholder="Explain why you are overriding the system-graded score…"
          onChange={(event) => setReason(event.target.value)}
          className="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 focus:outline-none focus:ring-2 focus:ring-slate-400 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-100"
        />
      </div>

      {error ? (
        <p role="alert" className="text-sm text-red-600 dark:text-red-400">
          {error}
        </p>
      ) : null}
      {success ? (
        <p role="status" className="text-sm text-emerald-700 dark:text-emerald-400">
          {success}
        </p>
      ) : null}

      <div className="flex flex-wrap items-center gap-3">
        <button
          type="submit"
          disabled={busy}
          className="inline-flex items-center gap-2 rounded-lg bg-slate-900 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-slate-800 disabled:opacity-50 dark:bg-slate-100 dark:text-slate-900 dark:hover:bg-white"
        >
          {submitting ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : null}
          Apply override
        </button>
        {review.hasOverride ? (
          <button
            type="button"
            onClick={handleClear}
            disabled={busy}
            className="inline-flex items-center gap-2 rounded-lg border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 transition-colors hover:bg-slate-50 disabled:opacity-50 dark:border-slate-600 dark:text-slate-200 dark:hover:bg-slate-800"
          >
            {clearing ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : null}
            Clear override
          </button>
        ) : null}
      </div>
    </form>
  );
}
