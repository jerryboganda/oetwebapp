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

  let toneClass = 'text-muted';
  let labelHint = '';
  if (safeCount === 0) {
    toneClass = 'text-muted';
  } else if (safeCount >= min && safeCount <= max) {
    toneClass = 'text-success';
    labelHint = 'in target range';
  } else if (
    (safeCount >= amberLowStart && safeCount < min)
    || (safeCount > max && safeCount <= amberHighEnd)
  ) {
    toneClass = 'text-warning';
    labelHint = safeCount < min ? 'approaching target' : 'over target';
  } else if (safeCount > amberHighEnd) {
    toneClass = 'text-danger';
    labelHint = 'over-length';
  } else {
    toneClass = 'text-muted';
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
