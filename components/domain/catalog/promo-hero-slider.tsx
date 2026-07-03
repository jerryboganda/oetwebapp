'use client';

import { useCallback, useEffect, useState, type KeyboardEvent } from 'react';
import { ChevronLeft, ChevronRight, Zap } from 'lucide-react';
import { cn } from '@/lib/utils';

const SLIDE_COUNT = 20;
const AUTOPLAY_MS = 3000;

const SLIDES = Array.from({ length: SLIDE_COUNT }, (_, i) => {
  const n = String(i + 1).padStart(2, '0');
  return `/img/promotional-banners/promo-${n}.webp`;
});

/**
 * Auto-advancing promotional banner carousel that mirrors the OET website pricing
 * page hero slider. Dependency-free: local state + interval + CSS cross-fade.
 * Pauses on hover/focus, honours prefers-reduced-motion, and is keyboard navigable.
 */
export function PromoHeroSlider({ className }: { className?: string }) {
  const [index, setIndex] = useState(0);
  const [paused, setPaused] = useState(false);
  const [reducedMotion, setReducedMotion] = useState(false);

  const goTo = useCallback((next: number) => {
    setIndex(((next % SLIDE_COUNT) + SLIDE_COUNT) % SLIDE_COUNT);
  }, []);
  const next = useCallback(() => goTo(index + 1), [goTo, index]);
  const prev = useCallback(() => goTo(index - 1), [goTo, index]);

  // Track the reduced-motion preference (client-only).
  useEffect(() => {
    if (typeof window === 'undefined' || !window.matchMedia) return;
    const mq = window.matchMedia('(prefers-reduced-motion: reduce)');
    const update = () => setReducedMotion(mq.matches);
    update();
    mq.addEventListener?.('change', update);
    return () => mq.removeEventListener?.('change', update);
  }, []);

  // Autoplay — suspended while paused or when reduced motion is requested.
  useEffect(() => {
    if (paused || reducedMotion) return;
    const timer = window.setInterval(() => {
      setIndex((current) => (current + 1) % SLIDE_COUNT);
    }, AUTOPLAY_MS);
    return () => window.clearInterval(timer);
  }, [paused, reducedMotion]);

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'ArrowRight') {
      event.preventDefault();
      next();
    } else if (event.key === 'ArrowLeft') {
      event.preventDefault();
      prev();
    }
  };

  return (
    <div
      role="region"
      aria-roledescription="carousel"
      aria-label="OET promotional offers"
      onMouseEnter={() => setPaused(true)}
      onMouseLeave={() => setPaused(false)}
      onFocus={() => setPaused(true)}
      onBlur={() => setPaused(false)}
      onKeyDown={onKeyDown}
      className={cn('rounded-2xl border border-border bg-surface p-3 shadow-sm sm:p-4', className)}
    >
      <div className="mb-2.5 flex items-center justify-between gap-3 px-1">
        <span className="inline-flex items-center gap-1.5 rounded-full bg-primary/10 px-3 py-1 text-xs font-bold text-primary">
          <Zap className="h-3.5 w-3.5" aria-hidden="true" /> Latest OET offers
        </span>
        <span className="hidden text-xs font-semibold text-muted sm:inline">
          Auto-scrolls every 3 seconds. Hover to pause.
        </span>
      </div>

      <div className="relative h-[420px] overflow-hidden rounded-xl bg-background-light sm:h-[520px] lg:h-[600px]">
        {SLIDES.map((src, i) => (
          // Decorative full-bleed promo banners cross-fading in a fixed box — a plain
          // <img> is the right tool here (next/image fill + optimization fights the stack).
          // eslint-disable-next-line @next/next/no-img-element
          <img
            key={src}
            src={src}
            alt={`OET promotional offer ${i + 1}`}
            loading={i === 0 ? 'eager' : 'lazy'}
            decoding="async"
            aria-hidden={i !== index}
            className={cn(
              'absolute inset-0 h-full w-full object-contain transition-opacity duration-700 ease-in-out',
              i === index ? 'opacity-100' : 'pointer-events-none opacity-0',
            )}
          />
        ))}

        <button
          type="button"
          onClick={prev}
          aria-label="Previous offer"
          className="absolute left-2 top-1/2 flex h-10 w-10 -translate-y-1/2 items-center justify-center rounded-full border border-border bg-surface/90 text-navy shadow-sm backdrop-blur transition hover:bg-surface focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        >
          <ChevronLeft className="h-5 w-5" />
        </button>
        <button
          type="button"
          onClick={next}
          aria-label="Next offer"
          className="absolute right-2 top-1/2 flex h-10 w-10 -translate-y-1/2 items-center justify-center rounded-full border border-border bg-surface/90 text-navy shadow-sm backdrop-blur transition hover:bg-surface focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        >
          <ChevronRight className="h-5 w-5" />
        </button>
      </div>

      <div className="mt-3 flex flex-wrap items-center justify-center gap-1.5">
        {SLIDES.map((src, i) => {
          const active = i === index;
          return (
            <button
              key={src}
              type="button"
              onClick={() => goTo(i)}
              aria-label={`Go to slide ${i + 1}`}
              aria-current={active ? 'true' : undefined}
              className={cn(
                'h-2 rounded-full transition-all',
                active ? 'w-5 bg-primary' : 'w-2 bg-border hover:bg-muted',
              )}
            />
          );
        })}
      </div>
    </div>
  );
}
