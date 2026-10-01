'use client';

import { useState } from 'react';
import type { VocabItemDto } from '@/lib/reading-pathway-api';
import { submitVocabReview } from '@/lib/reading-pathway-api';
import { Button } from '@/components/ui/button';
import { ProgressBar } from '@/components/ui/progress';
import { cn } from '@/lib/utils';
import VocabCard from './VocabCard';

interface VocabReviewSessionProps {
  items: VocabItemDto[];
  onComplete: () => void;
}

type Quality = 0 | 3 | 4 | 5;

// Recall ratings keep their colours (forgot → danger … easy → success).
const RATING_BASE =
  'pressable min-h-11 flex-1 rounded-control border px-3 py-3 text-sm font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:opacity-50';

const RATING_BUTTONS: Array<{ label: string; quality: Quality; className: string }> = [
  {
    label: 'Forgot',
    quality: 0,
    className: 'border-danger/30 bg-danger/10 text-danger-strong hover:bg-danger/20',
  },
  {
    label: 'Hard',
    quality: 3,
    className: 'border-warning/30 bg-warning/10 text-warning-strong hover:bg-warning/20',
  },
  {
    label: 'Good',
    quality: 4,
    className: 'border-info/30 bg-info/10 text-info hover:bg-info/20',
  },
  {
    label: 'Easy',
    quality: 5,
    className: 'border-success/30 bg-success/10 text-success-strong hover:bg-success/20',
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
      {/* Progress bar: the shared ProgressBar slides with a transform, not width. */}
      <div className="space-y-1">
        <div className="flex justify-between text-xs font-medium tabular-nums text-muted">
          <span>{completed} of {total} reviewed</span>
          <span>{Math.round(progressPct)}%</span>
        </div>
        <ProgressBar value={progressPct} ariaLabel={`${completed} of ${total} reviewed`} />
      </div>

      {/* Card — controlled flip state keeps review and card in sync */}
      <VocabCard
        item={currentItem}
        flipped={isFlipped}
        onFlip={() => setIsFlipped((prev) => !prev)}
      />

      {/* Actions */}
      <div className="flex flex-wrap gap-2 sm:flex-nowrap sm:gap-3">
        {!isFlipped ? (
          <Button fullWidth size="lg" onClick={() => setIsFlipped(true)}>
            Reveal
          </Button>
        ) : (
          RATING_BUTTONS.map(({ label, quality, className }) => (
            <button
              key={quality}
              type="button"
              disabled={isSubmitting}
              onClick={() => void handleRate(quality)}
              className={cn(RATING_BASE, className)}
            >
              {label}
            </button>
          ))
        )}
      </div>
    </div>
  );
}