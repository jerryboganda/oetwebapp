'use client';

import { useEffect, useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { Brain, CheckCircle2, RotateCcw, ChevronRight } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { MotionSection, MotionItem, MotionPage } from '@/components/ui/motion-primitives';
import { fetchReviewSummary, fetchDueReviewItems, submitReview } from '@/lib/api';
import { analytics } from '@/lib/analytics';

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
        <LearnerPageHero title="Spaced Repetition Review" description="Review your weak areas with smart scheduling" icon={Brain} />
        <div className="space-y-4">
          {Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-xl" />)}
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Spaced Repetition Review"
        description="Review your weak areas with smart scheduling"
        icon={Brain}
      />

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {/* Summary cards */}
      {!started && (
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-8">
          {[
            { label: 'Due Today', value: summary?.dueToday ?? 0, color: 'text-red-600 dark:text-red-400' },
            { label: 'Total Due', value: summary?.due ?? 0, color: 'text-orange-600 dark:text-orange-400' },
            { label: 'Total Items', value: summary?.total ?? 0, color: 'text-blue-600 dark:text-blue-400' },
            { label: 'Mastered', value: summary?.mastered ?? 0, color: 'text-green-600 dark:text-green-400' },
          ].map(stat => (
            <div key={stat.label} className="bg-white dark:bg-gray-800 rounded-xl p-4 border border-gray-200 dark:border-gray-700 text-center">
              <div className={`text-3xl font-bold ${stat.color}`}>{stat.value}</div>
              <div className="text-sm text-gray-500 mt-1">{stat.label}</div>
            </div>
          ))}
        </div>
      )}

      {!started && !done && (
        <div className="flex justify-center">
          <button
            onClick={() => setStarted(true)}
            disabled={items.length === 0}
            className="px-8 py-3 bg-indigo-600 hover:bg-indigo-700 disabled:opacity-50 text-white font-semibold rounded-xl flex items-center gap-2 transition-colors"
          >
            Start Review ({items.length} items) <ChevronRight className="w-5 h-5" />
          </button>
        </div>
      )}

      {started && !done && currentItem && (
        <div className="max-w-2xl mx-auto">
          {/* Progress */}
          <div className="flex items-center justify-between mb-4 text-sm text-gray-500">
            <span>{current + 1} / {items.length}</span>
            <span>{sessionStats.correct} correct so far</span>
          </div>
          <div className="w-full bg-gray-200 dark:bg-gray-700 rounded-full h-2 mb-6">
            <div
              className="bg-indigo-500 h-2 rounded-full transition-all"
              style={{ width: `${Math.min(100, ((current + 1) / items.length) * 100)}%` }}
            />
          </div>

          <AnimatePresence mode="wait">
            <motion.div
              key={currentItem.id}
              initial={{ opacity: 0, x: 30 }}
              animate={{ opacity: 1, x: 0 }}
              exit={{ opacity: 0, x: -30 }}
              className="bg-white dark:bg-gray-800 rounded-2xl border border-gray-200 dark:border-gray-700 p-6 mb-6"
            >
              <div className="text-xs font-medium text-indigo-500 uppercase mb-3">{currentItem.examTypeCode} · {currentItem.subtestCode || 'General'}</div>
              <div className="text-lg font-medium text-gray-900 dark:text-white mb-4">
                {(() => { try { const q = JSON.parse(currentItem.questionJson); return q.text ?? currentItem.questionJson; } catch { return currentItem.questionJson; } })()}
              </div>

              {!revealed ? (
                <button
                  onClick={() => setRevealed(true)}
                  className="w-full py-3 border-2 border-dashed border-gray-300 dark:border-gray-600 rounded-xl text-gray-500 dark:text-gray-400 hover:border-indigo-400 hover:text-indigo-500 transition-colors font-medium"
                >
                  Tap to reveal answer
                </button>
              ) : (
                <MotionItem>
                  <div className="p-4 bg-gray-50 dark:bg-gray-900 rounded-xl border border-gray-200 dark:border-gray-700 text-gray-800 dark:text-gray-200 mb-5">
                    {(() => { try { const a = JSON.parse(currentItem.answerJson); return a.text ?? currentItem.answerJson; } catch { return currentItem.answerJson; } })()}
                  </div>
                  <div className="text-sm text-gray-500 mb-3 text-center">How well did you recall this?</div>
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
        <MotionPage className="max-w-md mx-auto text-center py-12">
          <CheckCircle2 className="w-16 h-16 text-green-500 mx-auto mb-4" />
          <h2 className="text-2xl font-bold text-gray-900 dark:text-white mb-2">Session Complete!</h2>
          <p className="text-gray-500 mb-6">{sessionStats.reviewed} items reviewed · {sessionStats.correct} correct ({sessionStats.reviewed > 0 ? Math.round((sessionStats.correct / sessionStats.reviewed) * 100) : 0}%)</p>
          <button
            onClick={() => { setStarted(false); setDone(false); setCurrent(0); setRevealed(false); setSessionStats({ reviewed: 0, correct: 0 }); }}
            className="px-6 py-2.5 bg-indigo-600 hover:bg-indigo-700 text-white rounded-xl font-medium flex items-center gap-2 mx-auto"
          >
            <RotateCcw className="w-4 h-4" /> Review Again
          </button>
        </MotionPage>
      )}
    </LearnerDashboardShell>
  );
}
