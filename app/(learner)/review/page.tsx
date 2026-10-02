'use client';

import { useEffect, useState } from 'react';
import { Brain, CheckCircle2, RotateCcw, ChevronRight } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-error';
import { CountUp } from '@/components/ui/count-up';
import { ProgressBar } from '@/components/ui/progress';
import { MotionFadeSwitch, MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { fetchReviewSummary, fetchDueReviewItems, submitReview } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';

type ReviewSummary = { due: number; total: number; dueToday: number; mastered: number; upcoming?: number };
type ReviewItem = {
  id: string;
  examTypeCode: string;
  subtestCode: string | null;
  questionJson: string;
  answerJson: string;
  easeFactor: number;
  intervalDays: number;
  reviewCount: number;
};

const QUALITY_LABELS = ['Again', 'Hard', 'Okay', 'Good', 'Easy', 'Perfect'];
const QUALITY_COLORS = [
  'bg-danger/10 text-danger-strong hover:bg-danger/20 border border-danger/20',
  'bg-warning/10 text-warning-strong hover:bg-warning/20 border border-warning/20',
  'bg-warning/5 text-warning-strong hover:bg-warning/15 border border-warning/15',
  'bg-info/10 text-info hover:bg-info/20 border border-info/20',
  'bg-success/10 text-success-strong hover:bg-success/20 border border-success/20',
  'bg-success/15 text-success-strong hover:bg-success/25 border border-success/25',
];

function parseReviewText(payload: string): string | null {
  try {
    const parsed = JSON.parse(payload) as unknown;
    if (
      parsed &&
      typeof parsed === 'object' &&
      'text' in parsed &&
      typeof (parsed as { text?: unknown }).text === 'string' &&
      (parsed as { text: string }).text.trim().length > 0
    ) {
      return (parsed as { text: string }).text;
    }
  } catch {
    return null;
  }

  return null;
}

export default function ReviewPage() {
  const [summary, setSummary] = useState<ReviewSummary | null>(null);
  const [items, setItems] = useState<ReviewItem[]>([]);
  const [current, setCurrent] = useState(0);
  const [revealed, setRevealed] = useState(false);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sessionStats, setSessionStats] = useState({ reviewed: 0, correct: 0 });
  const [started, setStarted] = useState(false);

  useEffect(() => {
    analytics.track('review_page_viewed');
    Promise.allSettled([fetchReviewSummary(), fetchDueReviewItems(20)]).then(([summaryR, itemsR]) => {
      if (summaryR.status === 'fulfilled') setSummary(summaryR.value as ReviewSummary);
      if (itemsR.status === 'fulfilled') {
        const loadedItems = Array.isArray(itemsR.value) ? itemsR.value : (itemsR.value?.items ?? []);
        setItems(loadedItems as ReviewItem[]);
      }
      if (summaryR.status === 'rejected' && itemsR.status === 'rejected') setError('Could not load review items.');
      setLoading(false);
    });
  }, []);

  const currentItem = items[current];
  const currentQuestionText = currentItem ? parseReviewText(currentItem.questionJson) : null;
  const currentAnswerText = currentItem ? parseReviewText(currentItem.answerJson) : null;

  async function handleRate(quality: number) {
    if (!currentItem || submitting) return;
    setSubmitting(true);
    try {
      await submitReview(currentItem.id, quality);
      setSessionStats(s => ({ reviewed: s.reviewed + 1, correct: s.correct + (quality >= 3 ? 1 : 0) }));
      if (current + 1 >= items.length) {
        setDone(true);
      } else {
        setCurrent(c => c + 1);
        setRevealed(false);
      }
    } catch {
      setError('Failed to submit review.');
    } finally {
      setSubmitting(false);
    }
  }

  // The overview tiles below carry these counts, so the hero no longer repeats them.
  const hero = (
    <LearnerPageHero
      eyebrow="Daily Review"
      title="Lock in what you've already learned"
      description="Each card comes back exactly when you're about to forget it: the fastest way to keep weak areas from slipping back."
      icon={Brain}
    />
  );

  if (loading) {
    return (
      <>
        {hero}
        <div className="space-y-4" role="status" aria-busy="true" aria-label="Loading">
          {Array.from({ length: 3 }).map((_, i) => <Skeleton aria-hidden key={i} className="h-24 rounded-2xl" />)}
        </div>
      </>
    );
  }

  return (
    <>
      {hero}

      {error && <InlineAlert variant="warning">{error}</InlineAlert>}

      {/* Summary cards */}
      {!started && (
        <section className="space-y-4">
          <LearnerSurfaceSectionHeader eyebrow="Session overview" title="Review at a glance" />
          <div className="grid grid-cols-[repeat(auto-fit,minmax(min(100%,8.5rem),1fr))] gap-4">
            {[
              { label: 'Due Today', value: summary?.dueToday ?? 0, color: 'text-danger-strong' },
              { label: 'Total Due', value: summary?.due ?? 0, color: 'text-warning-strong' },
              { label: 'Total Items', value: summary?.total ?? 0, color: 'text-info' },
              { label: 'Mastered', value: summary?.mastered ?? 0, color: 'text-success-strong' },
            ].map((stat, index) => (
              <MotionItem key={stat.label} delayIndex={index}>
                <Card padding="md" className="h-full text-center">
                  <div className={`text-3xl font-bold ${stat.color}`}><CountUp value={stat.value} /></div>
                  <div className="mt-1 text-sm text-muted">{stat.label}</div>
                </Card>
              </MotionItem>
            ))}
          </div>
        </section>
      )}

      {!started && !done && items.length === 0 && (
        <EmptyState
          icon={<CheckCircle2 className="h-8 w-8 text-success-strong" />}
          title="No items due for review"
          description="You're all caught up. New items will appear here as you complete more practice activities."
          action={{
            label: 'Refresh',
            onClick: () => { setLoading(true); setError(null); Promise.allSettled([fetchReviewSummary(), fetchDueReviewItems(20)]).then(([summaryR, itemsR]) => { if (summaryR.status === 'fulfilled') setSummary(summaryR.value as ReviewSummary); if (itemsR.status === 'fulfilled') { const loadedItems = Array.isArray(itemsR.value) ? itemsR.value : (itemsR.value?.items ?? []); setItems(loadedItems as ReviewItem[]); } setLoading(false); }); },
          }}
        />
      )}

      {!started && !done && items.length > 0 && (
        <div className="flex justify-center">
          <Button size="lg" onClick={() => setStarted(true)}>
            Start Review ({items.length} items) <ChevronRight className="h-5 w-5 rtl:rotate-180" aria-hidden="true" />
          </Button>
        </div>
      )}

      {started && !done && currentItem && (
        <section className="space-y-4">
          <LearnerSurfaceSectionHeader eyebrow="Active session" title="Review one item at a time" />
          <div className="space-y-2">
            <div className="flex items-center justify-between text-sm text-muted tabular-nums">
              <span>{current + 1} / {items.length}</span>
              <span>{sessionStats.correct} correct so far</span>
            </div>
            <ProgressBar value={current + 1} max={items.length} ariaLabel={`Item ${current + 1} of ${items.length}`} />
          </div>

          {/* One card at a time: the shared fade switch, not a hand-written slide. */}
          <MotionFadeSwitch activeKey={currentItem.id}>
            <Card padding="lg">
              <div className="eyebrow mb-3 text-primary">{currentItem.examTypeCode} · {currentItem.subtestCode || 'General'}</div>
              <div className="mb-4 max-w-3xl text-lg font-medium text-navy">
                {currentQuestionText ?? 'Review item unavailable.'}
              </div>

              {!revealed ? (
                <button
                  type="button"
                  onClick={() => setRevealed(true)}
                  className="min-h-11 w-full rounded-2xl border-2 border-dashed border-border py-3 font-medium text-muted transition-colors hover:border-primary hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                >
                  Tap to reveal answer
                </button>
              ) : (
                <MotionItem>
                  <div className="mb-5 max-w-3xl rounded-xl border border-border bg-background-light p-4 text-navy/80">
                    {currentAnswerText ?? 'Review item unavailable.'}
                  </div>
                  <div className="mb-3 text-center text-sm text-muted">How well did you recall this?</div>
                  <div className="grid grid-cols-3 gap-2 sm:grid-cols-6">
                    {QUALITY_LABELS.map((label, q) => (
                      <button
                        key={q}
                        onClick={() => handleRate(q)}
                        disabled={submitting}
                        type="button"
                        className={`pressable min-h-11 rounded-control py-2 text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${QUALITY_COLORS[q]} disabled:opacity-50`}
                      >
                        {label}
                      </button>
                    ))}
                  </div>
                </MotionItem>
              )}
            </Card>
          </MotionFadeSwitch>
        </section>
      )}

      {done && (
        <MotionSection>
          <Card padding="lg" className="flex flex-col items-center py-10 text-center sm:py-12">
            {/* One-shot pop for a real event: the session was just finished. */}
            <CheckCircle2 className="pop-in mb-4 h-16 w-16 text-success-strong" aria-hidden="true" />
            <h2 className="mb-2 text-2xl font-bold text-navy">Session Complete</h2>
            <p className="mb-6 text-muted tabular-nums">{sessionStats.reviewed} items reviewed · {sessionStats.correct} correct ({sessionStats.reviewed > 0 ? Math.round((sessionStats.correct / sessionStats.reviewed) * 100) : 0}%)</p>
            <Button
              onClick={() => { setStarted(false); setDone(false); setCurrent(0); setRevealed(false); setSessionStats({ reviewed: 0, correct: 0 }); }}
            >
              <RotateCcw className="h-4 w-4" aria-hidden="true" /> Review Again
            </Button>
          </Card>
        </MotionSection>
      )}
    </>
  );
}
