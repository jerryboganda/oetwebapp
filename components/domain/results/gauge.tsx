import type { ReactNode } from 'react';
import { cn } from '@/lib/utils';

export interface ResultGaugeProps {
  /** 0–100 fill percentage of the ring. */
  value: number;
  size?: number;
  stroke?: number;
  /** CSS color for the progress arc (defaults to the primary token). */
  color?: string;
  /** Centre content — a score, band letter, percentage, etc. */
  children?: ReactNode;
  className?: string;
}

/**
 * A flexible circular score gauge with a free-form centre slot. Unlike the
 * generic {@link CircularProgress} primitive (whose centre is fixed to `%`),
 * this lets each results surface render a band letter, `x/500`, or accuracy in
 * the middle while keeping one consistent ring style across all modules.
 */
export function ResultGauge({
  value,
  size = 132,
  stroke = 10,
  color = 'var(--color-primary)',
  children,
  className,
}: ResultGaugeProps) {
  const v = Math.min(100, Math.max(0, Number.isFinite(value) ? value : 0));
  const radius = (size - stroke) / 2;
  const circumference = 2 * Math.PI * radius;
  const offset = circumference - (v / 100) * circumference;

  return (
    <div className={cn('relative shrink-0', className)} style={{ width: size, height: size }}>
      <svg className="h-full w-full -rotate-90" viewBox={`0 0 ${size} ${size}`} aria-hidden>
        <circle
          cx={size / 2}
          cy={size / 2}
          r={radius}
          fill="none"
          strokeWidth={stroke}
          className="stroke-border/70 dark:stroke-border/50"
        />
        <circle
          cx={size / 2}
          cy={size / 2}
          r={radius}
          fill="none"
          stroke={color}
          strokeWidth={stroke}
          strokeDasharray={circumference}
          strokeDashoffset={offset}
          strokeLinecap="round"
          className="transition-[stroke-dashoffset] duration-700 ease-out motion-reduce:transition-none"
        />
      </svg>
      <div className="absolute inset-0 flex flex-col items-center justify-center text-center leading-none">
        {children}
      </div>
    </div>
  );
}
