'use client';

import { useEffect, useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { Brain, CheckCircle2, RotateCcw, ChevronRight } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { MotionSection, MotionItem, MotionPage } from '@/components/ui/motion-primitives';
import { fetchReviewSummary, fetchDueReviewItems, submitReview } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { Card } from '@/components/ui/card';

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
  'bg-red-500 hover:bg-red-600',
  'bg-orange-500 hover:bg-orange-600',
  'bg-yellow-500 hover:bg-yellow-600',
  'bg-lime-500 hover:bg-lime-600',
  'bg-green-500 hover:bg-green-600',
  'bg-emerald-600 hover:bg-emerald-700',
];

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
  const heroHighlights = [
    { icon: Brain, label: 'Due today', value: `${summary?.dueToday ?? 0}` },
    { icon: CheckCircle2, label: 'Mastered', value: `${summary?.mastered ?? 0}` },
    { icon: RotateCcw, label: 'Mode', value: 'Spaced review' },
  ];

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

  if (loading) {
    return (
      <LearnerDashboardShell>
        <div className="space-y-6">
        <LearnerPageHero eyebrow="Learn" title="Spaced Repetition Review" description="Review your weak areas with smart scheduling." icon={Brain} highlights={heroHighlights} />
        <div className="space-y-4">
          {Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-3xl" />)}
        </div>
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
      <LearnerPageHero
        eyebrow="Learn"
        title="Spaced Repetition Review"
        description="Review your weak areas with smart scheduling."
        icon={Brain}
        highlights={heroHighlights}
      />

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {/* Summary cards */}
      {!started && (
        <div>
          <LearnerSurfaceSectionHeader eyebrow="Session overview" title="Review at a glance" description="The overview should feel like the dashboard summary row." className="mb-4" />
          <div className="grid grid-cols-2 gap-4 md:grid-cols-4 mb-8">
          {[
            { label: 'Due Today', value: summary?.dueToday ?? 0, color: 'text-red-600 dark:text-red-400' },
            { label: 'Total Due', value: summary?.due ?? 0, color: 'text-orange-600 dark:text-orange-400' },
            { label: 'Total Items', value: summary?.total ?? 0, color: 'text-blue-600 dark:text-blue-400' },
            { label: 'Mastered', value: summary?.mastered ?? 0, color: 'text-green-600 dark:text-green-400' },
          ].map(stat => (
            <Card key={stat.label} className="rounded-3xl p-4 text-center shadow-sm">
              <div className={`text-3xl font-bold ${stat.color}`}>{stat.value}</div>
              <div className="mt-1 text-sm text-muted">{stat.label}</div>
            </Card>
          ))}
        </div>
        </div>
      )}

      {!started && !done && (
        <div className="flex justify-center">
          <button
            onClick={() => setStarted(true)}
            disabled={items.length === 0}
            className="inline-flex items-center gap-2 rounded-2xl bg-primary px-8 py-3 font-semibold text-white transition-colors hover:bg-primary-dark disabled:opacity-50"
          >
            Start Review ({items.length} items) <ChevronRight className="w-5 h-5" />
          </button>
        </div>
      )}

      {started && !done && currentItem && (
        <div className="mx-auto max-w-3xl">
          <LearnerSurfaceSectionHeader eyebrow="Active session" title="Review one item at a time" description="The active card should feel like a focused surface within the same dashboard system." className="mb-4" />
          {/* Progress */}
          <div className="mb-4 flex items-center justify-between text-sm text-muted">
            <span>{current + 1} / {items.length}</span>
            <span>{sessionStats.correct} correct so far</span>
          </div>
          <div className="mb-6 h-2 w-full rounded-full bg-background-light">
            <div
              className="h-2 rounded-full bg-primary transition-all"
              style={{ width: `${Math.min(100, ((current + 1) / items.length) * 100)}%` }}
            />
          </div>

          <AnimatePresence mode="wait">
            <motion.div
              key={currentItem.id}
              initial={{ opacity: 0, x: 30 }}
              animate={{ opacity: 1, x: 0 }}
              exit={{ opacity: 0, x: -30 }}
              className="mb-6 rounded-3xl border border-border bg-surface p-6 shadow-sm"
            >
              <div className="mb-3 text-xs font-medium uppercase text-primary">{currentItem.examTypeCode} · {currentItem.subtestCode || 'General'}</div>
              <div className="mb-4 text-lg font-medium text-navy">
                {(() => { try { const q = JSON.parse(currentItem.questionJson); return q.text ?? currentItem.questionJson; } catch { return currentItem.questionJson; } })()}
              </div>

              {!revealed ? (
                <button
                  onClick={() => setRevealed(true)}
                  className="w-full rounded-2xl border-2 border-dashed border-border py-3 font-medium text-muted transition-colors hover:border-primary hover:text-primary"
                >
                  Tap to reveal answer
                </button>
              ) : (
                <MotionItem>
                  <div className="mb-5 rounded-2xl border border-border bg-background-light p-4 text-navy/80">
                    {(() => { try { const a = JSON.parse(currentItem.answerJson); return a.text ?? currentItem.answerJson; } catch { return currentItem.answerJson; } })()}
                  </div>
                  <div className="mb-3 text-center text-sm text-muted">How well did you recall this?</div>
                  <div className="grid grid-cols-3 gap-2 sm:grid-cols-6">
                    {QUALITY_LABELS.map((label, q) => (
                      <button
                        key={q}
                        onClick={() => handleRate(q)}
                        disabled={submitting}
                        className={`py-2 rounded-lg text-white text-sm font-medium transition-colors ${QUALITY_COLORS[q]} disabled:opacity-50`}
                      >
                        {label}
                      </button>
                    ))}
                  </div>
                </MotionItem>
              )}
            </motion.div>
          </AnimatePresence>
        </div>
      )}

      {done && (
        <MotionPage className="mx-auto max-w-md py-12 text-center">
          <CheckCircle2 className="w-16 h-16 text-green-500 mx-auto mb-4" />
          <h2 className="mb-2 text-2xl font-bold text-navy">Session Complete</h2>
          <p className="mb-6 text-muted">{sessionStats.reviewed} items reviewed · {sessionStats.correct} correct ({sessionStats.reviewed > 0 ? Math.round((sessionStats.correct / sessionStats.reviewed) * 100) : 0}%)</p>
          <button
            onClick={() => { setStarted(false); setDone(false); setCurrent(0); setRevealed(false); setSessionStats({ reviewed: 0, correct: 0 }); }}
            className="mx-auto inline-flex items-center gap-2 rounded-2xl bg-primary px-6 py-2.5 font-medium text-white hover:bg-primary-dark"
          >
            <RotateCcw className="w-4 h-4" /> Review Again
          </button>
        </MotionPage>
      )}
      </div>
    </LearnerDashboardShell>
  );
}
