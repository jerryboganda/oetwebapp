'use client';

import { Badge } from '@/components/ui/badge';
import type { VocabItemDto } from '@/lib/reading-pathway-api';

interface VocabCardProps {
  item: VocabItemDto;
  /**
   * Which face shows. The review session owns it, so its Reveal button turns
   * the card and every new word starts on its front (the card used to keep a
   * private copy: Reveal never turned it, and a word flipped by tapping left
   * the next word showing its answer).
   */
  flipped: boolean;
  onFlip: () => void;
}

export default function VocabCard({ item, flipped, onFlip }: VocabCardProps) {
  return (
    <div
      role="button"
      tabIndex={0}
      aria-pressed={flipped}
      className="relative w-full cursor-pointer select-none rounded-2xl focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
      style={{ perspective: '1000px' }}
      onClick={onFlip}
      onKeyDown={(event) => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault();
          onFlip();
        }
      }}
    >
      {/* Card container — perspective transform root */}
      <div
        className="relative w-full transition-transform duration-500"
        style={{
          transformStyle: 'preserve-3d',
          transform: flipped ? 'rotateY(180deg)' : 'rotateY(0deg)',
          minHeight: '22rem',
        }}
      >
        {/* Front face */}
        <div
          className="absolute inset-0 flex flex-col items-center justify-center rounded-2xl border border-border bg-surface px-8 py-10 shadow-sm"
          style={{ backfaceVisibility: 'hidden' }}
        >
          <p className="mb-3 text-4xl font-bold tracking-tight text-navy">
            {item.word}
          </p>
          {item.pronunciationIpa ? (
            <p className="text-base italic text-muted">
              /{item.pronunciationIpa}/
            </p>
          ) : null}
          <p className="mt-auto pt-6 eyebrow text-primary">
            Tap to reveal
          </p>
        </div>

        {/* Back face */}
        <div
          className="absolute inset-0 flex flex-col gap-3 rounded-2xl border border-primary/20 bg-lavender px-8 py-7 shadow-sm"
          style={{
            backfaceVisibility: 'hidden',
            transform: 'rotateY(180deg)',
          }}
        >
          {/* Word + IPA compact header */}
          <div className="flex items-baseline gap-2">
            <span className="text-sm font-semibold text-navy">
              {item.word}
            </span>
            {item.pronunciationIpa ? (
              <span className="text-xs italic text-muted">
                /{item.pronunciationIpa}/
              </span>
            ) : null}
          </div>

          {/* English definition */}
          <p className="text-base font-bold text-navy">
            {item.definitionEn}
          </p>

          {/* Arabic definition */}
          {item.definitionAr ? (
            <p className="text-sm text-muted" dir="auto">
              {item.definitionAr}
            </p>
          ) : null}

          {/* English example */}
          {item.exampleEn ? (
            <p className="text-sm italic text-muted">
              &ldquo;{item.exampleEn}&rdquo;
            </p>
          ) : null}

          {/* Healthcare context badge */}
          {item.healthcareContext ? (
            <Badge className="w-fit font-medium">
              {item.healthcareContext}
            </Badge>
          ) : null}

          {/* Arabic example — RTL */}
          {item.exampleAr ? (
            <p
              className="mt-auto text-xs text-muted"
              dir="rtl"
              lang="ar"
            >
              {item.exampleAr}
            </p>
          ) : null}
        </div>
      </div>
    </div>
  );
}
