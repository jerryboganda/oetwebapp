'use client';

import { cn } from '@/lib/utils';
import { motion, useReducedMotion } from 'motion/react';
import { getSharedLayoutId, motionTokens } from '@/lib/motion';
import { type ReactNode } from 'react';

// ─────────────────────────────────────────────────────────────────────────────
// SegmentedControl — animated pill group for time-range / type filters.
//
// Design contract: DESIGN.md §5.14. Replaces the pattern:
//
//   <div className="flex gap-2">
//     {ranges.map(r => (
//       <button className={active ? 'bg-primary text-white' : 'bg-muted'}>…</button>
//     ))}
//   </div>
//
// which was repeated across 6 admin pages inconsistently.
// ─────────────────────────────────────────────────────────────────────────────

export interface SegmentedOption<T extends string = string> {
  value: T;
  label: string;
  icon?: ReactNode;
  description?: string;
}

interface SegmentedControlProps<T extends string = string> {
  value: T;
  onChange: (value: T) => void;
  options: SegmentedOption<T>[];
  className?: string;
  size?: 'sm' | 'md';
  'aria-label'?: string;
  /** Unique id for the shared-layout pill (needed if multiple instances render). */
  namespace?: string;
  fullWidth?: boolean;
}

export function SegmentedControl<T extends string = string>({
  value,
  onChange,
  options,
  className,
  size = 'md',
  'aria-label': ariaLabel,
  namespace = 'default',
  fullWidth = false,
}: SegmentedControlProps<T>) {
  const reducedMotion = useReducedMotion() ?? false;

  return (
    <div
      role="radiogroup"
      aria-label={ariaLabel}
      className={cn(
        'inline-flex items-center gap-1 rounded-full border border-border bg-background-light p-1',
        fullWidth && 'w-full',
        className,
      )}
    >
      {options.map((option) => {
        const active = option.value === value;
        return (
          <button
            key={option.value}
            type="button"
            role="radio"
            aria-checked={active}
            onClick={() => onChange(option.value)}
            title={option.description}
            className={cn(
              'relative inline-flex items-center justify-center gap-1.5 rounded-full font-semibold whitespace-nowrap transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
              size === 'sm' ? 'min-h-8 px-3 text-xs' : 'min-h-10 px-4 text-sm',
              active ? 'text-white' : 'text-muted hover:text-navy',
              fullWidth && 'flex-1',
            )}
          >
            {active && (
              <motion.span
                aria-hidden="true"
                layoutId={getSharedLayoutId('segmented-active', namespace)}
                className="absolute inset-0 rounded-full bg-primary shadow-sm"
                transition={reducedMotion ? { duration: motionTokens.duration.instant } : motionTokens.spring.item}
              />
            )}
            <span className="relative z-10 inline-flex items-center gap-1.5">
              {option.icon}
              {option.label}
            </span>
          </button>
        );
      })}
    </div>
  );
}
