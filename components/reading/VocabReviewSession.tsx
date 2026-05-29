'use client';

import { useState } from 'react';
import type { VocabItemDto } from '@/lib/reading-pathway-api';
import { submitVocabReview } from '@/lib/reading-pathway-api';
import VocabCard from './VocabCard';

interface VocabReviewSessionProps {
  items: VocabItemDto[];
  onComplete: () => void;
}

type Quality = 0 | 3 | 4 | 5;

const RATING_BUTTONS: Array<{ label: string; quality: Quality; className: string }> = [
  {
    label: 'Forgot',
    quality: 0,
    className:
      'flex-1 rounded-xl border border-danger/30 bg-danger/10 px-4 py-3 text-sm font-semibold text-danger transition-colors hover:bg-danger/20 disabled:opacity-50',
  },
  {
    label: 'Hard',
    quality: 3,
    className:
      'flex-1 rounded-xl border border-warning/30 bg-warning/10 px-4 py-3 text-sm font-semibold text-warning transition-colors hover:bg-warning/20 disabled:opacity-50',
  },
  {
    label: 'Good',
    quality: 4,
    className:
      'flex-1 rounded-xl border border-info/30 bg-info/10 px-4 py-3 text-sm font-semibold text-info transition-colors hover:bg-info/20 disabled:opacity-50',
  },
  {
    label: 'Easy',
    quality: 5,
    className:
      'flex-1 rounded-xl border border-success/30 bg-success/10 px-4 py-3 text-sm font-semibold text-success transition-colors hover:bg-success/20 disabled:opacity-50',
  },
];

export default function VocabReviewSession({ items, onComplete }: VocabReviewSessionProps) {
  const [currentIndex, setCurrentIndex] = useState(0);
  const [isFlipped, setIsFlipped] = useState(false);
  const [completed, setCompleted] = useState(0);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const total = items.length;
  const currentItem = items[currentIndex];
  const progressPct = total > 0 ? (completed / total) * 100 : 0;

  async function handleRate(quality: Quality) {
    if (isSubmitting || !currentItem) return;
    setIsSubmitting(true);
    try {
      await submitVocabReview(currentItem.id, quality);
    } catch {
      // fire-and-forget — do not block the learner on network errors
    } finally {
      setIsSubmitting(false);
    }

    const nextCompleted = completed + 1;
    setCompleted(nextCompleted);

    if (nextCompleted >= total) {
      onComplete();
      return;
    }

    setCurrentIndex((i) => i + 1);
    setIsFlipped(false);
  }

  if (!currentItem) return null;

  return (
    <div className="flex flex-col gap-6">
      {/* Progress bar */}
      <div className="space-y-1">
        <div className="flex justify-between text-xs font-medium text-muted">
          <span>{completed} of {total} reviewed</span>
          <span>{Math.round(progressPct)}%</span>
        </div>
        <div className="h-2 w-full overflow-hidden rounded-full bg-lavender dark:bg-primary/20">
          <div
            className="h-full rounded-full bg-primary transition-[width] duration-300"
            style={{ width: `${progressPct}%` }}
          />
        </div>
      </div>

      {/* Card — controlled flip state keeps review and card in sync */}
      <VocabCard
        item={currentItem}
        onFlip={() => setIsFlipped((prev) => !prev)}
      />

      {/* Actions */}
      <div className="flex gap-3">
        {!isFlipped ? (
          <button
            type="button"
            onClick={() => setIsFlipped(true)}
            className="w-full rounded-xl bg-primary px-6 py-3 text-sm font-semibold text-white transition-colors hover:bg-primary-dark dark:bg-violet-700 dark:hover:bg-violet-600 active:scale-95"
          >
            Reveal
          </button>
        ) : (
          RATING_BUTTONS.map(({ label, quality, className }) => (
            <button
              key={quality}
              type="button"
              disabled={isSubmitting}
              onClick={() => void handleRate(quality)}
              className={className}
            >
              {label}
            </button>
          ))
        )}
      </div>
    </div>
  );
}