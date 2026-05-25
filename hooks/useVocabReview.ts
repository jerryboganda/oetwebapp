import { useState } from 'react';
import { submitVocabReview, type VocabItemDto } from '@/lib/reading-pathway-api';

export function useVocabReview(items: VocabItemDto[]) {
  const [queue, setQueue] = useState<VocabItemDto[]>(items);
  const [currentIndex, setCurrentIndex] = useState(0);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const current = queue[currentIndex] ?? null;
  const remaining = Math.max(0, queue.length - currentIndex);
  const isDone = currentIndex >= queue.length;

  async function rate(quality: number): Promise<void> {
    if (!current) return;
    setIsSubmitting(true);
    try {
      const updated = await submitVocabReview(current.id, quality);
      setQueue((prev) => {
        const next = [...prev];
        next[currentIndex] = updated;
        return next;
      });
    } catch {
      // best-effort — still advance
    } finally {
      setIsSubmitting(false);
      setCurrentIndex((prev) => prev + 1);
    }
  }

  return { current, remaining, rate, isDone, isSubmitting };
}
