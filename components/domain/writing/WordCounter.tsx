'use client';

import { cn } from '@/lib/utils';

export interface WordCounterProps {
  count: number;
  target?: { min: number; max: number };
  ariaLabelPrefix?: string;
  className?: string;
}

/**
 * Live word counter for the writing editor.
 *
 * Color thresholds (per spec §11.3):
 *   0-149   grey   (too short — keep writing)
 *   150-179 amber  (approaching target)
 *   180-220 green  (in target band)
 *   221+    red    (over-length — risk to C3 score)
 *
 * If a `target` prop is supplied, the green band is calibrated to
 * `[target.min, target.max]` and amber widens both sides by 30 words.
 */
export function WordCounter({ count, target, ariaLabelPrefix, className }: WordCounterProps) {
  const safeCount = Math.max(0, Math.floor(count));
  const min = target?.min ?? 180;
  const max = target?.max ?? 220;
  const amberLowStart = Math.max(0, min - 30);
  const amberHighEnd = max + 30;

  let toneClass = 'text-slate-500';
  let labelHint = '';
  if (safeCount === 0) {
    toneClass = 'text-slate-500';
  } else if (safeCount >= min && safeCount <= max) {
    toneClass = 'text-emerald-600 dark:text-emerald-400';
    labelHint = 'in target range';
  } else if (
    (safeCount >= amberLowStart && safeCount < min)
    || (safeCount > max && safeCount <= amberHighEnd)
  ) {
    toneClass = 'text-amber-600 dark:text-amber-400';
    labelHint = safeCount < min ? 'approaching target' : 'over target';
  } else if (safeCount > amberHighEnd) {
    toneClass = 'text-red-600 dark:text-red-400';
    labelHint = 'over-length';
  } else {
    toneClass = 'text-slate-500';
    labelHint = 'keep writing';
  }

  const ariaLabel = `${ariaLabelPrefix ? ariaLabelPrefix + ' ' : ''}${safeCount} words${labelHint ? ', ' + labelHint : ''}, target ${min} to ${max}`;

  return (
    <span
      className={cn('inline-flex items-baseline gap-1 font-bold tabular-nums', toneClass, className)}
      aria-live="polite"
      aria-atomic="true"
      aria-label={ariaLabel}
    >
      <span>{safeCount}</span>
      <span className="text-xs font-normal opacity-70">/ {min}-{max} words</span>
    </span>
  );
}
