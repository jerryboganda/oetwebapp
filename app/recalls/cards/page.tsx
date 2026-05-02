'use client';

import { useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import { Brain } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
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

  const current = queue && queue.length > 0 ? queue[Math.min(index, queue.length - 1)] : null;

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
          ) : !current ? (
            <div className="text-center text-sm text-muted">
              Nothing due. Visit <code className="rounded bg-background px-1 py-0.5">/recalls/words</code> to seed cards.
            </div>
          ) : mode === 'listen_and_type' && current.kind === 'vocab' ? (
            <ListenAndType
              key={current.id}
              termId={current.id}
              termHint={current.subtitle ?? undefined}
              audioUrl={current.audioUrl}
              onResult={() => {
                setTimeout(() => setIndex((i) => Math.min(i + 1, (queue?.length ?? 1) - 1)), 1200);
              }}
            />
          ) : (
            <QuizRunner mode={mode} limit={10} />
          )}

          {queue && queue.length > 0 && mode === 'listen_and_type' && (
            <div className="mt-3 text-center text-xs text-muted">
              Card {Math.min(index + 1, queue.length)} of {queue.length}
            </div>
          )}
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
