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
      className="relative w-full cursor-pointer select-none"
      style={{ perspective: '1000px' }}
      onClick={handleFlip}
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
          className="absolute inset-0 flex flex-col items-center justify-center rounded-2xl border border-violet-100 bg-white px-8 py-10 shadow-md dark:border-violet-900/40 dark:bg-neutral-900"
          style={{ backfaceVisibility: 'hidden' }}
        >
          <p className="mb-3 text-4xl font-bold tracking-tight text-neutral-900 dark:text-white">
            {item.word}
          </p>
          {item.pronunciationIpa ? (
            <p className="text-base italic text-neutral-400 dark:text-neutral-500">
              /{item.pronunciationIpa}/
            </p>
          ) : null}
          <p className="mt-auto pt-6 text-xs font-medium uppercase tracking-widest text-violet-400">
            Tap to reveal
          </p>
        </div>

        {/* Back face */}
        <div
          className="absolute inset-0 flex flex-col gap-3 rounded-2xl border border-violet-200 bg-violet-50 px-8 py-7 shadow-md dark:border-violet-800/50 dark:bg-violet-950/40"
          style={{
            backfaceVisibility: 'hidden',
            transform: 'rotateY(180deg)',
          }}
        >
          {/* Word + IPA compact header */}
          <div className="flex items-baseline gap-2">
            <span className="text-sm font-semibold text-neutral-700 dark:text-neutral-200">
              {item.word}
            </span>
            {item.pronunciationIpa ? (
              <span className="text-xs italic text-neutral-400">
                /{item.pronunciationIpa}/
              </span>
            ) : null}
          </div>

          {/* English definition */}
          <p className="text-base font-bold text-neutral-900 dark:text-white">
            {item.definitionEn}
          </p>

          {/* Arabic definition */}
          {item.definitionAr ? (
            <p className="text-sm text-neutral-500 dark:text-neutral-400" dir="auto">
              {item.definitionAr}
            </p>
          ) : null}

          {/* English example */}
          {item.exampleEn ? (
            <p className="text-sm italic text-neutral-600 dark:text-neutral-300">
              &ldquo;{item.exampleEn}&rdquo;
            </p>
          ) : null}

          {/* Healthcare context badge */}
          {item.healthcareContext ? (
            <span className="inline-flex w-fit items-center rounded-full bg-violet-100 px-2.5 py-0.5 text-xs font-medium text-violet-700 dark:bg-violet-900/60 dark:text-violet-300">
              {item.healthcareContext}
            </span>
          ) : null}

          {/* Arabic example — RTL */}
          {item.exampleAr ? (
            <p
              className="mt-auto text-xs text-neutral-400 dark:text-neutral-500"
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