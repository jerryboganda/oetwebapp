'use client';

import { cn } from '@/lib/utils';
import { motion, useReducedMotion } from 'motion/react';
import { motionTokens } from '@/lib/motion';
import { type ReactNode } from 'react';

// ─────────────────────────────────────────────────────────────────────────────
// BarMeter — labelled horizontal bar with optional stacked series.
//
// Design contract: DESIGN.md §5.16. Replaces raw:
//
//   <div className="h-4 bg-muted rounded-full">
//     <div className="h-full bg-primary" style={{ width: '62%' }} />
//   </div>
//
// Supports a single-value mode and a multi-segment stacked mode.
// ─────────────────────────────────────────────────────────────────────────────

export type BarMeterTone = 'primary' | 'success' | 'warning' | 'danger' | 'info' | 'navy' | 'slate';

const toneStyles: Record<BarMeterTone, string> = {
  primary: 'bg-primary',
  success: 'bg-emerald-500',
  warning: 'bg-amber-500',
  danger: 'bg-red-500',
  info: 'bg-blue-500',
  navy: 'bg-navy',
  slate: 'bg-slate-400',
};

export interface BarMeterSegment {
  value: number;
  label?: string;
  tone?: BarMeterTone;
  hint?: string;
}

export interface BarMeterProps {
  label?: string;
  value?: number;
  /** Total used for percent calculations. When omitted, max(sum(segments), value, 100) is used. */
  max?: number;
  tone?: BarMeterTone;
  hint?: string;
  segments?: BarMeterSegment[];
  size?: 'sm' | 'md' | 'lg';
  /** Display the numeric value to the right of the label. */
  showValue?: boolean;
  formatValue?: (value: number, max: number) => string;
  /** Show a small dotted legend under the bar when segments are provided. */
  showLegend?: boolean;
  className?: string;
}

const barHeights = { sm: 'h-2', md: 'h-3', lg: 'h-4' } as const;

function defaultFormatter(value: number, max: number) {
  if (max === 100) return `${Math.round(value)}%`;
  return `${Math.round((value / max) * 100)}%`;
}

export function BarMeter({
  label,
  value,
  max,
  tone = 'primary',
  hint,
  segments,
  size = 'md',
  showValue = true,
  formatValue,
  showLegend = true,
  className,
}: BarMeterProps) {
  const reducedMotion = useReducedMotion() ?? false;

  const hasSegments = segments && segments.length > 0;
  const totalFromSegments = hasSegments ? segments.reduce((sum, s) => sum + Math.max(0, s.value), 0) : 0;
  const effectiveMax = Math.max(1, max ?? Math.max(totalFromSegments, value ?? 0, 100));
  const singleValue = value ?? 0;
  const fmt = formatValue ?? defaultFormatter;

  return (
    <div className={cn('flex flex-col gap-1.5', className)}>
      {(label || (showValue && !hasSegments)) && (
        <div className="flex items-baseline justify-between gap-3">
          {label && <span className="text-sm font-semibold text-navy">{label}</span>}
          {showValue && !hasSegments && (
            <span className="text-sm font-semibold text-muted">{fmt(singleValue, effectiveMax)}</span>
          )}
        </div>
      )}

      <div
        className={cn(
          'relative w-full overflow-hidden rounded-full bg-background-light ring-1 ring-inset ring-border/60',
          barHeights[size],
        )}
        role="progressbar"
        aria-valuenow={hasSegments ? totalFromSegments : singleValue}
        aria-valuemin={0}
        aria-valuemax={effectiveMax}
        aria-label={label}
      >
        {hasSegments ? (
          <div className="absolute inset-0 flex">
            {segments.map((seg, index) => {
              const pct = Math.max(0, (seg.value / effectiveMax) * 100);
              return (
                <motion.div
                  key={`${seg.label ?? index}-${index}`}
                  initial={reducedMotion ? { width: `${pct}%` } : { width: 0 }}
                  animate={{ width: `${pct}%` }}
                  transition={reducedMotion ? { duration: motionTokens.duration.instant } : { type: 'spring', stiffness: 120, damping: 22 }}
                  className={cn('h-full', toneStyles[seg.tone ?? 'primary'])}
                  title={seg.label ? `${seg.label}: ${seg.value}` : undefined}
                />
              );
            })}
          </div>
        ) : (
          <motion.div
            initial={reducedMotion ? { width: `${(singleValue / effectiveMax) * 100}%` } : { width: 0 }}
            animate={{ width: `${Math.min(100, (singleValue / effectiveMax) * 100)}%` }}
            transition={reducedMotion ? { duration: motionTokens.duration.instant } : { type: 'spring', stiffness: 120, damping: 22 }}
            className={cn('h-full rounded-full', toneStyles[tone])}
          />
        )}
      </div>

      {hint && <span className="text-xs leading-5 text-muted">{hint}</span>}

      {showLegend && hasSegments && (
        <div className="mt-1 flex flex-wrap items-center gap-x-4 gap-y-1.5 text-xs text-muted">
          {segments.map((seg, index) => (
            <span key={`legend-${seg.label ?? index}-${index}`} className="inline-flex items-center gap-1.5">
              <span className={cn('inline-block h-2 w-2 rounded-full', toneStyles[seg.tone ?? 'primary'])} aria-hidden />
              <span className="text-muted">{seg.label ?? `Series ${index + 1}`}</span>
              {typeof seg.value === 'number' && (
                <span className="font-semibold text-navy">{seg.value.toLocaleString()}</span>
              )}
            </span>
          ))}
        </div>
      )}
    </div>
  );
}

export function BarMeterList({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cn('space-y-4', className)}>{children}</div>;
}
