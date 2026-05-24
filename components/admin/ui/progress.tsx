'use client';

import * as React from 'react';
import * as ProgressPrimitive from '@radix-ui/react-progress';
import { cva, type VariantProps } from 'class-variance-authority';
import { cn } from '@/lib/utils';

/* ─────────────────────────────────────────────────────────────────────
 * Variants
 * ───────────────────────────────────────────────────────────────────── */

const trackVariants = cva(
  'relative w-full overflow-hidden rounded-full bg-[var(--admin-bg-subtle)]',
  {
    variants: {
      size: {
        sm: 'h-1',
        md: 'h-1.5',
        lg: 'h-2.5',
      },
    },
    defaultVariants: { size: 'md' },
  },
);

const indicatorColor = {
  default: 'bg-[var(--admin-fg-muted)]',
  primary: 'bg-[var(--admin-primary)]',
  success: 'bg-[var(--admin-success)]',
  warning: 'bg-[var(--admin-warning)]',
  danger: 'bg-[var(--admin-danger)]',
} as const;

export type ProgressVariant = keyof typeof indicatorColor;

/* ─────────────────────────────────────────────────────────────────────
 * Props
 * ───────────────────────────────────────────────────────────────────── */

export interface ProgressProps
  extends Omit<
      React.ComponentPropsWithoutRef<typeof ProgressPrimitive.Root>,
      'value' | 'children'
    >,
    VariantProps<typeof trackVariants> {
  /** 0–100. Ignored when `indeterminate` is true */
  value?: number;
  variant?: ProgressVariant;
  /** Show right-aligned percentage label above the track */
  showLabel?: boolean;
  /** Animate an indefinite back-and-forth bar (e.g. for unknown progress) */
  indeterminate?: boolean;
}

/* ─────────────────────────────────────────────────────────────────────
 * Component
 * ───────────────────────────────────────────────────────────────────── */

const Progress = React.forwardRef<
  React.ElementRef<typeof ProgressPrimitive.Root>,
  ProgressProps
>(
  (
    {
      className,
      value = 0,
      variant = 'primary',
      size,
      showLabel = false,
      indeterminate = false,
      ...props
    },
    ref,
  ) => {
    const clamped = Math.min(100, Math.max(0, value));
    const colorClass = indicatorColor[variant];

    return (
      <div className={cn('w-full', className)}>
        {showLabel ? (
          <div className="mb-1 flex justify-end">
            <span className="text-xs font-medium text-[var(--admin-fg-muted)] tabular-nums">
              {indeterminate ? '…' : `${Math.round(clamped)}%`}
            </span>
          </div>
        ) : null}

        <ProgressPrimitive.Root
          ref={ref}
          value={indeterminate ? null : clamped}
          className={cn(trackVariants({ size }))}
          {...props}
        >
          {indeterminate ? (
            <div
              className={cn(
                'absolute inset-y-0 left-0 w-1/3 rounded-full',
                colorClass,
                'admin-progress-indeterminate',
              )}
              aria-hidden
            />
          ) : (
            <ProgressPrimitive.Indicator
              className={cn(
                'h-full w-full rounded-full transition-transform duration-300 ease-out',
                colorClass,
              )}
              style={{ transform: `translateX(-${100 - clamped}%)` }}
            />
          )}
        </ProgressPrimitive.Root>

        <style>{`
          @keyframes admin-progress-indeterminate {
            0%   { transform: translateX(-100%); }
            50%  { transform: translateX(150%); }
            100% { transform: translateX(-100%); }
          }
          .admin-progress-indeterminate {
            animation: admin-progress-indeterminate 1.6s ease-in-out infinite;
          }
        `}</style>
      </div>
    );
  },
);
Progress.displayName = 'Progress';

export { Progress };
