'use client';

/**
 * Speaking module rebuild (2026-06-11 spec).
 *
 * Results page for the two-card Speaking exam. AI-mode exams show the OFFICIAL
 * per-card AI scores + combined readiness band. Live-tutor exams show an
 * "awaiting examiner marking" state until the tutor submits.
 */
import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import {
  getSpeakingExamResults,
  type SpeakingExamResults,
} from '@/lib/api/speaking-exams';
import { ApiError } from '@/lib/api';

const POLL_INTERVAL_MS = 4_000;

export default function SpeakingExamResultsPage() {
  const params = useParams<{ id: string }>();
  const examId = params?.id ?? '';
  const [results, setResults] = useState<SpeakingExamResults | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    if (!examId) return;
    try {
      const r = await getSpeakingExamResults(examId);
      setResults(r);
      setError(null);
    } catch (err) {
      setError(err instanceof ApiError ? err.userMessage : 'Could not load results.');
    } finally {
      setLoading(false);
    }
  }, [examId]);

  useEffect(() => {
    void refresh();
    const interval = window.setInterval(() => void refresh(), POLL_INTERVAL_MS);
    return () => window.clearInterval(interval);
  }, [refresh]);

  if (loading) {
    return (
      <div className="flex min-h-[60vh] items-center justify-center">
        <Loader2 className="h-6 w-6 animate-spin text-muted" />
      </div>
    );
  }

  if (error && !results) {
    return (
      <div className="mx-auto max-w-lg px-4 py-12 text-center">
        <p className="text-sm text-rose-700">{error}</p>
        <Button className="mt-4" variant="outline" onClick={() => void refresh()}>
          Retry
        </Button>
      </div>
    );
  }

  if (!results) return null;

  const pending = results.overallStatus !== 'scored';
  const awaitingTutor = results.overallStatus === 'awaiting_tutor';

  return (
    <div className="mx-auto max-w-2xl px-4 py-8">
      <h1 className="text-xl font-semibold text-foreground">Speaking exam results</h1>

      {pending ? (
        <div className="mt-4 rounded-xl border border-border bg-surface p-5">
          <div className="flex items-center gap-2 text-sm text-muted">
            <Loader2 className="h-4 w-4 animate-spin" />
            {awaitingTutor
              ? 'Your tutor is marking this exam. Your result will appear here once marking is complete.'
              : 'Scoring your exam… this usually takes a moment. This page refreshes automatically.'}
          </div>
        </div>
      ) : (
        <div className="mt-4 rounded-xl border border-emerald-200 bg-emerald-50 p-5">
          <p className="text-xs font-semibold uppercase tracking-wide text-emerald-700">
            Combined result
          </p>
          <div className="mt-1 flex items-baseline gap-3">
            <span className="text-4xl font-bold tabular-nums text-emerald-800">
              {results.combinedScaledScore ?? '—'}
            </span>
            <span className="text-sm text-emerald-700">/ 500</span>
            {results.readinessBand ? (
              <span className="ml-auto rounded-full bg-emerald-600 px-3 py-1 text-xs font-semibold uppercase text-white">
                Band {results.readinessBand}
              </span>
            ) : null}
          </div>
        </div>
      )}

      <div className="mt-6 space-y-4">
        {results.cards.map((card) => (
          <section key={card.cardNumber} className="rounded-xl border border-border bg-surface p-5">
            <div className="flex items-center justify-between">
              <h2 className="text-sm font-semibold text-foreground">
                Card {card.cardNumber === 1 ? 'A' : 'B'}
              </h2>
              <span
                className={cn(
                  'rounded-full px-2.5 py-0.5 text-xs font-medium',
                  card.status === 'scored'
                    ? 'bg-emerald-100 text-emerald-700'
                    : 'bg-amber-100 text-amber-700',
                )}
              >
                {card.status === 'scored'
                  ? 'Scored'
                  : card.status === 'awaiting_tutor'
                    ? 'Awaiting tutor'
                    : 'Processing'}
              </span>
            </div>

            {card.assessment ? (
              <div className="mt-3 space-y-3">
                <div className="flex items-baseline gap-2">
                  <span className="text-2xl font-bold tabular-nums text-foreground">
                    {card.assessment.estimatedScaledScore}
                  </span>
                  <span className="text-sm text-muted">/ 500</span>
                  <span className="ml-auto text-xs uppercase tracking-wide text-muted">
                    Band {card.assessment.readinessBand}
                  </span>
                </div>
                {card.assessment.overallSummary ? (
                  <p className="text-sm leading-relaxed text-muted">
                    {card.assessment.overallSummary}
                  </p>
                ) : null}
                <div className="grid grid-cols-2 gap-2">
                  {Object.entries(card.assessment.criterionScores).map(([code, c]) => (
                    <div
                      key={code}
                      className="flex items-center justify-between rounded-md bg-background/60 px-3 py-1.5 text-xs"
                    >
                      <span className="capitalize text-muted">
                        {code.replace(/([A-Z])/g, ' $1').trim()}
                      </span>
                      <span className="font-semibold text-foreground">
                        {c.score}/{c.maxScore}
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            ) : (
              <p className="mt-3 text-sm text-muted">Not yet available.</p>
            )}
          </section>
        ))}
      </div>

      <div className="mt-6 flex justify-center">
        <Button asChild variant="outline">
          <Link href="/speaking">Back to Speaking</Link>
        </Button>
      </div>
    </div>
  );
}
