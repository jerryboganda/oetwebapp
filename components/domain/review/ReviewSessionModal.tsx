'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  CheckCircle2,
  ChevronRight,
  PauseCircle,
  RotateCcw,
  Sparkles,
  Undo2,
} from 'lucide-react';
import { Modal } from '@/components/ui/modal';
import { Button } from '@/components/ui/button';
import { ProgressBar } from '@/components/ui/progress';
import { InlineAlert } from '@/components/ui/alert';
import { cn } from '@/lib/utils';
import { triggerImpactHaptic } from '@/lib/mobile/haptics';
import { analytics } from '@/lib/analytics';
import {
  QUALITY_OPTIONS,
  type ReviewItem,
  type ReviewSubmissionResponse,
  SOURCE_TYPE_LABELS,
} from '@/lib/types/review';
import { resumeReviewItem, submitReview, suspendReviewItem, undoLastReview } from '@/lib/api';
import { ReviewItemRenderer } from './ReviewItemRenderer';

interface ReviewSessionModalProps {
  open: boolean;
  onClose: () => void;
  items: ReviewItem[];
  onSessionComplete?: (summary: SessionSummary) => void;
}

export interface SessionSummary {
  reviewed: number;
  correct: number;
  masteredJustNow: number;
  durationSeconds: number;
}

interface HistoryEntry {
  itemId: string;
  quality: number;
  masteredJustNow: boolean;
}

const QUALITY_TONE_CLASSES: Record<'danger' | 'warning' | 'info' | 'success', string> = {
  danger:
    'border-danger/30 bg-danger/10 text-danger hover:bg-danger/20 focus-visible:ring-danger/50',
  warning:
    'border-warning/30 bg-warning/10 text-warning hover:bg-warning/20 focus-visible:ring-warning/50',
  info:
    'border-info/30 bg-info/10 text-info hover:bg-info/20 focus-visible:ring-info/50',
  success:
    'border-success/30 bg-success/10 text-success hover:bg-success/20 focus-visible:ring-success/50',
};

export function ReviewSessionModal({
  open,
  onClose,
  items,
  onSessionComplete,
}: ReviewSessionModalProps) {
  const [current, setCurrent] = useState(0);
  const [revealed, setRevealed] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [history, setHistory] = useState<HistoryEntry[]>([]);
  const [stats, setStats] = useState({ reviewed: 0, correct: 0, masteredJustNow: 0 });
  const [sessionStartedAt, setSessionStartedAt] = useState<number>(() => Date.now());
  const [startup, setStartup] = useState<'idle' | 'started'>('idle');

  const item = items[current];
  const hasItems = items.length > 0;

  useEffect(() => {
    if (!open) return;
    setCurrent(0);
    setRevealed(false);
    setSubmitting(false);
    setDone(false);
    setError(null);
    setHistory([]);
    setStats({ reviewed: 0, correct: 0, masteredJustNow: 0 });
    setSessionStartedAt(Date.now());
    setStartup('started');
    analytics.track('review_session_started', { itemCount: items.length });
  }, [open, items.length]);

  const progressPercent = useMemo(() => {
    if (!hasItems) return 0;
    return Math.min(100, ((stats.reviewed / items.length) * 100) | 0);
  }, [stats.reviewed, items.length, hasItems]);

  const handleReveal = useCallback(() => {
    if (!item || revealed) return;
    setRevealed(true);
    void triggerImpactHaptic('LIGHT');
  }, [item, revealed]);

  const finishSessionIfDone = useCallback(
    (nextStats: typeof stats) => {
      const totalNow = nextStats.reviewed;
      if (totalNow >= items.length) {
        setDone(true);
        const summary: SessionSummary = {
          reviewed: nextStats.reviewed,
          correct: nextStats.correct,
          masteredJustNow: nextStats.masteredJustNow,
          durationSeconds: Math.max(1, Math.round((Date.now() - sessionStartedAt) / 1000)),
        };
        analytics.track('review_session_completed', { ...summary });
        onSessionComplete?.(summary);
      }
    },
    [items.length, sessionStartedAt, onSessionComplete],
  );

  const handleRate = useCallback(
    async (quality: number) => {
      if (!item || submitting || !revealed) return;
      setSubmitting(true);
      setError(null);
      try {
        const response = (await submitReview(item.id, quality)) as ReviewSubmissionResponse;
        const masteredJustNow = Boolean(response?.masteredJustNow);
        const correctInc = quality >= 3 ? 1 : 0;
        const masteredInc = masteredJustNow ? 1 : 0;

        analytics.track('review_item_rated', {
          quality,
          promptKind: item.promptKind,
          sourceType: item.sourceType,
        });
        if (masteredJustNow) {
          analytics.track('review_item_mastered', {
            sourceType: item.sourceType,
            promptKind: item.promptKind,
          });
        }

        setHistory((h) => [...h, { itemId: item.id, quality, masteredJustNow }]);
        const nextStats = {
          reviewed: stats.reviewed + 1,
          correct: stats.correct + correctInc,
          masteredJustNow: stats.masteredJustNow + masteredInc,
        };
        setStats(nextStats);

        if (current + 1 >= items.length) {
          finishSessionIfDone(nextStats);
        } else {
          setCurrent((c) => c + 1);
          setRevealed(false);
        }
      } catch (err) {
        console.error('Failed to submit review', err);
        setError('Could not save your rating. Try again.');
      } finally {
        setSubmitting(false);
      }
    },
    [item, submitting, revealed, current, items.length, stats, finishSessionIfDone],
  );

  const handleUndo = useCallback(async () => {
    if (submitting) return;
    const last = history[history.length - 1];
    if (!last) return;
    setSubmitting(true);
    setError(null);
    try {
      await undoLastReview(last.itemId);
      setHistory((h) => h.slice(0, -1));
      setStats((s) => ({
        reviewed: Math.max(0, s.reviewed - 1),
        correct: last.quality >= 3 ? Math.max(0, s.correct - 1) : s.correct,
        masteredJustNow: last.masteredJustNow
          ? Math.max(0, s.masteredJustNow - 1)
          : s.masteredJustNow,
      }));
      // Step back one item if we weren't already on the undone item.
      if (current > 0) {
        setCurrent((c) => c - 1);
        setRevealed(true);
      } else {
        setRevealed(true);
      }
      if (done) setDone(false);
    } catch (err) {
      console.error('Undo failed', err);
      setError('Could not undo the last rating.');
    } finally {
      setSubmitting(false);
    }
  }, [history, submitting, current, done]);

  const handleSuspend = useCallback(async () => {
    if (!item || submitting) return;
    setSubmitting(true);
    setError(null);
    try {
      await suspendReviewItem(item.id, 'Suspended from session');
      analytics.track('review_item_suspended', {
        sourceType: item.sourceType,
      });
      const nextStats = {
        reviewed: stats.reviewed + 1,
        correct: stats.correct,
        masteredJustNow: stats.masteredJustNow,
      };
      setStats(nextStats);
      if (current + 1 >= items.length) {
        finishSessionIfDone(nextStats);
      } else {
        setCurrent((c) => c + 1);
        setRevealed(false);
      }
    } catch (err) {
      console.error('Suspend failed', err);
      setError('Could not suspend the item.');
    } finally {
      setSubmitting(false);
    }
  }, [item, submitting, current, items.length, stats, finishSessionIfDone]);

  // Keyboard shortcuts: Space reveal, 1-4 quality, U undo, S suspend.
  useEffect(() => {
    if (!open || !startup || done) return;
    const onKey = (ev: KeyboardEvent) => {
      if (ev.target && (ev.target as HTMLElement).tagName === 'INPUT') return;
      if (ev.key === ' ' || ev.key === 'Enter') {
        if (!revealed) {
          ev.preventDefault();
          handleReveal();
        }
        return;
      }
      if (!revealed) return;
      if (ev.key === '1') {
        ev.preventDefault();
        void handleRate(QUALITY_OPTIONS[0].quality);
      } else if (ev.key === '2') {
        ev.preventDefault();
        void handleRate(QUALITY_OPTIONS[1].quality);
      } else if (ev.key === '3') {
        ev.preventDefault();
        void handleRate(QUALITY_OPTIONS[2].quality);
      } else if (ev.key === '4') {
        ev.preventDefault();
        void handleRate(QUALITY_OPTIONS[3].quality);
      } else if (ev.key === 'u' || ev.key === 'U') {
        ev.preventDefault();
        void handleUndo();
      } else if (ev.key === 's' || ev.key === 'S') {
        ev.preventDefault();
        void handleSuspend();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, startup, done, revealed, handleReveal, handleRate, handleUndo, handleSuspend]);

  return (
    <Modal open={open} onClose={onClose} size="lg" title="Spaced repetition review">
      {done ? (
        <CompletionView
          stats={stats}
          total={items.length}
          onClose={onClose}
        />
      ) : !hasItems ? (
        <div className="py-6 text-center text-sm text-muted">No review items available.</div>
      ) : item ? (
        <div className="space-y-5">
          <header className="flex flex-wrap items-center justify-between gap-3">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted">
              Item {current + 1} of {items.length}
            </p>
            <p className="text-xs text-muted">
              {stats.reviewed} reviewed · {stats.correct} correct
            </p>
          </header>

          <ProgressBar value={progressPercent} max={100} ariaLabel="Session progress" />

          {error ? (
            <InlineAlert variant="warning">
              {error}
            </InlineAlert>
          ) : null}

          <AnimatePresence mode="wait">
            <motion.div
              key={item.id}
              initial={{ opacity: 0, y: 8 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: -8 }}
              transition={{ duration: 0.18 }}
            >
              <ReviewItemRenderer item={item} revealed={revealed} />
            </motion.div>
          </AnimatePresence>

          {!revealed ? (
            <Button
              onClick={handleReveal}
              variant="outline"
              size="lg"
              fullWidth
              aria-label="Reveal the correct answer"
            >
              <Sparkles className="mr-2 h-4 w-4" />
              Reveal answer (Space)
            </Button>
          ) : (
            <div className="space-y-4" aria-live="polite">
              <p className="text-center text-sm text-muted">How well did you recall this?</p>
              <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                {QUALITY_OPTIONS.map((opt) => (
                  <button
                    key={opt.quality}
                    type="button"
                    onClick={() => handleRate(opt.quality)}
                    disabled={submitting}
                    className={cn(
                      'flex min-h-12 flex-col items-center justify-center rounded-2xl border px-3 py-2 text-sm font-semibold transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-offset-2',
                      QUALITY_TONE_CLASSES[opt.tone],
                      submitting && 'opacity-60',
                    )}
                    aria-label={`${opt.label} — keyboard ${opt.key}`}
                  >
                    <span>{opt.label}</span>
                    <span className="text-[10px] font-medium uppercase tracking-[0.14em] opacity-70">
                      Key {opt.key}
                    </span>
                  </button>
                ))}
              </div>
              <div className="flex flex-wrap items-center justify-between gap-3 pt-2 text-xs text-muted">
                <div className="flex items-center gap-3">
                  <button
                    type="button"
                    onClick={handleUndo}
                    disabled={submitting || history.length === 0}
                    className="inline-flex items-center gap-1 rounded-full border border-border px-3 py-1 font-semibold text-navy transition-colors hover:border-primary hover:text-primary disabled:opacity-40"
                    aria-label="Undo last rating (U)"
                  >
                    <Undo2 className="h-3.5 w-3.5" />
                    Undo (U)
                  </button>
                  <button
                    type="button"
                    onClick={handleSuspend}
                    disabled={submitting}
                    className="inline-flex items-center gap-1 rounded-full border border-border px-3 py-1 font-semibold text-navy transition-colors hover:border-primary hover:text-primary disabled:opacity-40"
                    aria-label="Suspend this item (S)"
                  >
                    <PauseCircle className="h-3.5 w-3.5" />
                    Suspend (S)
                  </button>
                </div>
                <p className="text-muted">
                  {SOURCE_TYPE_LABELS[item.sourceType]}
                  {item.subtestCode ? ` · ${item.subtestCode}` : ''}
                </p>
              </div>
            </div>
          )}
        </div>
      ) : null}
    </Modal>
  );
}

function CompletionView({
  stats,
  total,
  onClose,
}: {
  stats: { reviewed: number; correct: number; masteredJustNow: number };
  total: number;
  onClose: () => void;
}) {
  const accuracy = stats.reviewed > 0 ? Math.round((stats.correct / stats.reviewed) * 100) : 0;
  return (
    <div className="space-y-5 py-4 text-center">
      <div className="mx-auto flex h-16 w-16 items-center justify-center rounded-full bg-success/10 text-success">
        <CheckCircle2 className="h-8 w-8" />
      </div>
      <div>
        <h3 className="text-xl font-semibold text-navy">Session complete</h3>
        <p className="mt-1 text-sm text-muted">
          {stats.reviewed} of {total} reviewed · {accuracy}% accuracy
        </p>
      </div>
      {stats.masteredJustNow > 0 ? (
        <p className="text-sm font-semibold text-success">
          +{stats.masteredJustNow} item{stats.masteredJustNow === 1 ? '' : 's'} mastered
        </p>
      ) : null}
      <div className="flex items-center justify-center gap-3 pt-2">
        <Button variant="outline" onClick={onClose}>
          Close
        </Button>
        <Button variant="primary" onClick={onClose}>
          <RotateCcw className="mr-1 h-4 w-4" />
          Back to Review
          <ChevronRight className="ml-1 h-4 w-4" />
        </Button>
      </div>
    </div>
  );
}
