'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { Brain } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { ListenAndType } from '@/components/domain/recalls/listen-and-type';
import { QuizRunner } from '@/components/domain/recalls/quiz-runner';
import { QuizModePicker, QUIZ_MODE_KEYS, type RecallQuizMode } from '@/components/domain/recalls/quiz-mode-picker';
import { fetchRecallsToday, fetchRecallsQueue, type RecallsTodayResponse, type RecallsQueueItem } from '@/lib/api';
import { analytics } from '@/lib/analytics';

/**
 * /recalls/cards — unified recall runner.
 *
 * Lets the learner pick one of six quiz modes (docs/RECALLS-MODULE-PLAN.md §6)
 * and runs the queue end-to-end. The default mode (and only one wired in this
 * pass) is listen-and-type, which is the highest-signal OET-specific drill.
 * Other modes route into the existing /vocabulary and /review flows pending
 * full migration.
 */
export default function RecallsCardsPage() {
  const params = useSearchParams();
  const initialMode = useMemo<RecallQuizMode>(() => {
    const m = params?.get('mode');
    return (QUIZ_MODE_KEYS as readonly string[]).includes(m ?? '') ? (m as RecallQuizMode) : 'listen_and_type';
  }, [params]);

  const [mode, setMode] = useState<RecallQuizMode>(initialMode);
  const [today, setToday] = useState<RecallsTodayResponse | null>(null);
  const [queue, setQueue] = useState<RecallsQueueItem[] | null>(null);
  const [index, setIndex] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('recalls_cards_viewed');
    Promise.all([fetchRecallsToday(), fetchRecallsQueue(20)])
      .then(([t, q]) => {
        setToday(t);
        setQueue(q.filter((i) => i.kind === 'vocab' || mode !== 'listen_and_type'));
      })
      .catch(() => setError('Could not load your recall queue.'))
      .finally(() => setLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Use a strict bounds check so we can render a real "Session complete" state
  // once the learner has worked through the whole queue, instead of clamping
  // at the final card forever (which is what `Math.min(index, len - 1)` did).
  const sessionComplete = !!queue && queue.length > 0 && index >= queue.length;
  const current = queue && queue.length > 0 && !sessionComplete ? queue[index] : null;

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Recalls / Cards"
          title="Drill your active recall queue"
          description="Choose a mode. Hear it, type it, recognise it, define it, contextualise it."
          icon={Brain}
          highlights={[
            { icon: Brain, label: 'Due today', value: `${today?.dueToday ?? 0}` },
            { icon: Brain, label: 'Readiness', value: `${today?.readinessScore ?? 0}` },
            { icon: Brain, label: 'Mastered', value: `${today?.mastered ?? 0}` },
          ]}
        />

        {error && <InlineAlert variant="warning">{error}</InlineAlert>}

        <LearnerSurfaceSectionHeader
          eyebrow="Mode"
          title="Pick a quiz format"
          description="Modes anchored to clinical English. Listen-and-type uses British TTS only."
        />
        <QuizModePicker selected={mode} onChange={setMode} />

        <div className="rounded-2xl border border-border bg-background-light p-5">
          {loading ? (
            <Skeleton className="h-44 rounded-xl" />
          ) : sessionComplete ? (
            <div className="space-y-4 text-center">
              <h3 className="text-lg font-semibold text-navy">Session complete</h3>
              <p className="text-sm text-muted">
                You&apos;ve worked through every due card in this queue. Come back tomorrow for the next batch, or add
                more words to keep going.
              </p>
              <div className="flex flex-wrap justify-center gap-2">
                <Button
                  type="button"
                  variant="secondary"
                  onClick={() => {
                    setIndex(0);
                    analytics.track('recalls_cards_session_restart', { queueLength: queue?.length ?? 0 });
                  }}
                >
                  Run again
                </Button>
                <Link
                  href="/recalls/words#catalog"
                  className="inline-flex h-9 items-center justify-center rounded-xl bg-primary px-4 text-sm font-medium text-white shadow-sm transition-colors hover:bg-primary/90"
                >
                  Add more words
                </Link>
                <Link
                  href="/recalls"
                  className="inline-flex h-9 items-center justify-center rounded-xl border border-border bg-surface px-4 text-sm font-medium text-navy transition-colors hover:bg-background"
                >
                  Back to Recalls
                </Link>
              </div>
            </div>
          ) : !current ? (
            <div className="space-y-3 text-center text-sm text-muted">
              <p>Nothing due right now.</p>
              <Link
                href="/recalls/words#catalog"
                className="inline-flex h-9 items-center justify-center rounded-xl bg-primary px-4 text-sm font-medium text-white shadow-sm transition-colors hover:bg-primary/90"
              >
                Browse the vocabulary catalog
              </Link>
            </div>
          ) : mode === 'listen_and_type' && current.kind === 'vocab' ? (
            current.termId ? (
              <ListenAndType
                key={current.id}
                termId={current.termId}
                termHint={current.subtitle ?? undefined}
                onResult={() => {
                  setTimeout(() => setIndex((i) => i + 1), 1200);
                }}
              />
            ) : (
              <div className="space-y-3">
                <InlineAlert variant="warning">
                  This vocabulary card is missing its term reference. Refresh your Recalls queue or skip this card.
                </InlineAlert>
                <button
                  type="button"
                  onClick={() => setIndex((i) => i + 1)}
                  className="rounded-xl bg-primary px-4 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-primary/90"
                >
                  Skip card
                </button>
              </div>
            )
          ) : (
            <QuizRunner mode={mode} limit={10} />
          )}

          {queue && queue.length > 0 && mode === 'listen_and_type' && !sessionComplete && (
            <div className="mt-3 text-center text-xs text-muted">
              Card {index + 1} of {queue.length}
            </div>
          )}
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
