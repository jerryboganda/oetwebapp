'use client';

import { useState } from 'react';
import type { VocabItemDto } from '@/lib/reading-pathway-api';

interface VocabCardProps {
  item: VocabItemDto;
  onFlip?: () => void;
}

export default function VocabCard({ item, onFlip }: VocabCardProps) {
  const [flipped, setFlipped] = useState(false);

  function handleFlip() {
    setFlipped((prev) => !prev);
    onFlip?.();
  }

  return (
    <div
      role="button"
      tabIndex={0}
      aria-pressed={flipped}
      className="relative w-full cursor-pointer select-none rounded-2xl focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
      style={{ perspective: '1000px' }}
      onClick={handleFlip}
      onKeyDown={(event) => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault();
          handleFlip();
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
          className="absolute inset-0 flex flex-col items-center justify-center rounded-2xl border border-primary-100 bg-surface px-8 py-10 shadow-md dark:border-primary-900/40"
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
          <p className="mt-auto pt-6 eyebrow text-primary-400">
            Tap to reveal
          </p>
        </div>

        {/* Back face */}
        <div
          className="absolute inset-0 flex flex-col gap-3 rounded-2xl border border-primary-200 bg-primary-50 px-8 py-7 shadow-md dark:border-primary-800/50 dark:bg-primary-950/40"
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
            <span className="inline-flex w-fit items-center rounded-full bg-primary-100 px-2.5 py-0.5 text-xs font-medium text-primary-700 dark:bg-primary-900/60 dark:text-primary-300">
              {item.healthcareContext}
            </span>
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