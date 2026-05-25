'use client';

import { useParams, useRouter } from 'next/navigation';
import { useState } from 'react';
import { ArrowLeft, Clock } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { useTimer } from '@/hooks/useTimer';
import { endPracticeSession } from '@/lib/reading-pathway-api';

export default function MockSessionPage() {
  const { sessionId } = useParams<{ sessionId: string }>();
  const router = useRouter();
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  const MOCK_DURATION_SECONDS = 60 * 60; // 60 minutes

  const { remaining, isExpired } = useTimer(
    MOCK_DURATION_SECONDS,
    'down',
    () => handleSubmit(),
    `mock_${sessionId}`,
  );

  function formatTime(seconds: number): string {
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
  }

  async function handleSubmit() {
    if (submitting) return;
    setSubmitting(true);
    setSubmitError(null);
    try {
      await endPracticeSession(sessionId as string);
      router.push(`/reading/mocks/${encodeURIComponent(sessionId as string)}/results`);
    } catch (err) {
      setSubmitError(err instanceof Error ? err.message : 'Failed to submit mock. Please try again.');
      setSubmitting(false);
    }
  }

  return (
    <LearnerDashboardShell pageTitle="Mock Test">
      <main className="space-y-8">
        <Link
          href="/reading/mocks"
          className="inline-flex items-center gap-1.5 text-sm text-muted hover:text-navy dark:hover:text-white"
        >
          <ArrowLeft className="h-4 w-4" aria-hidden />
          Back to Mocks
        </Link>

        <div className="flex items-start justify-between">
          <div>
            <h1 className="text-2xl font-bold text-navy dark:text-white">Mock Test in Progress</h1>
            <p className="mt-1 text-sm text-muted">Session: {sessionId}</p>
          </div>
          {/* Timer */}
          <div className={[
            'flex items-center gap-2 rounded-xl border px-4 py-3',
            isExpired
              ? 'border-red-300 bg-red-50 dark:border-red-700 dark:bg-red-900/20'
              : remaining < 300
              ? 'border-amber-300 bg-amber-50 dark:border-amber-700 dark:bg-amber-900/20'
              : 'border-border bg-surface',
          ].join(' ')}>
            <Clock
              className={[
                'h-5 w-5',
                isExpired ? 'text-red-500' : remaining < 300 ? 'text-amber-500' : 'text-muted',
              ].join(' ')}
              aria-hidden
            />
            <span className={[
              'text-2xl font-bold tabular-nums',
              isExpired ? 'text-red-600 dark:text-red-400' : remaining < 300 ? 'text-amber-600 dark:text-amber-400' : 'text-navy dark:text-white',
            ].join(' ')}>
              {isExpired ? '00:00' : formatTime(remaining)}
            </span>
          </div>
        </div>

        {submitError ? (
          <div className="rounded-lg border border-red-200 bg-red-50 dark:border-red-800 dark:bg-red-900/20 px-4 py-3 text-sm text-red-700 dark:text-red-300">
            {submitError}
          </div>
        ) : null}

        {/* Questions placeholder */}
        <div className="rounded-xl border border-border bg-surface px-6 py-12 text-center">
          <p className="text-sm font-medium text-navy dark:text-white">Questions loading…</p>
          <p className="mt-1 text-xs text-muted">
            The reading passage and questions will appear here once the mock content is fully loaded.
          </p>
        </div>

        {/* Submit button */}
        <div className="flex justify-end">
          <button
            type="button"
            disabled={submitting}
            onClick={handleSubmit}
            className="rounded-lg bg-blue-600 px-6 py-2.5 text-sm font-semibold text-white hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {submitting ? 'Submitting…' : 'Submit Mock'}
          </button>
        </div>
      </main>
    </LearnerDashboardShell>
  );
}
