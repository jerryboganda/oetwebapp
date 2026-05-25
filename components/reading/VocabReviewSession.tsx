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
      'flex-1 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm font-semibold text-red-700 transition hover:bg-red-100 disabled:opacity-50 dark:border-red-900/50 dark:bg-red-950/30 dark:text-red-400 dark:hover:bg-red-900/40',
  },
  {
    label: 'Hard',
    quality: 3,
    className:
      'flex-1 rounded-xl border border-orange-200 bg-orange-50 px-4 py-3 text-sm font-semibold text-orange-700 transition hover:bg-orange-100 disabled:opacity-50 dark:border-orange-900/50 dark:bg-orange-950/30 dark:text-orange-400 dark:hover:bg-orange-900/40',
  },
  {
    label: 'Good',
    quality: 4,
    className:
      'flex-1 rounded-xl border border-blue-200 bg-blue-50 px-4 py-3 text-sm font-semibold text-blue-700 transition hover:bg-blue-100 disabled:opacity-50 dark:border-blue-900/50 dark:bg-blue-950/30 dark:text-blue-400 dark:hover:bg-blue-900/40',
  },
  {
    label: 'Easy',
    quality: 5,
    className:
      'flex-1 rounded-xl border border-green-200 bg-green-50 px-4 py-3 text-sm font-semibold text-green-700 transition hover:bg-green-100 disabled:opacity-50 dark:border-green-900/50 dark:bg-green-950/30 dark:text-green-400 dark:hover:bg-green-900/40',
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
        <div className="flex justify-between text-xs font-medium text-neutral-500 dark:text-neutral-400">
          <span>{completed} of {total} reviewed</span>
          <span>{Math.round(progressPct)}%</span>
        </div>
        <div className="h-2 w-full overflow-hidden rounded-full bg-violet-100 dark:bg-violet-900/40">
          <div
            className="h-full rounded-full bg-violet-600 transition-all duration-300"
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
            className="w-full rounded-xl bg-violet-600 px-6 py-3 text-sm font-semibold text-white transition hover:bg-violet-700 active:scale-95"
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