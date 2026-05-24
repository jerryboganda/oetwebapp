'use client';

import * as React from 'react';

import { cn } from '@/lib/utils';
import { Button } from '@/components/admin/ui/button';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { EmptyState } from '@/components/admin/ui/empty-state';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from './card';

/**
 * ChartCard — standard chrome around Recharts components per
 * docs/admin-redesign/axelit-study/16-CHARTS-COMPLETE.md §3.
 *
 * Owns: title/subtitle, optional time-range tab-group, action slot, and the
 * loading/error/empty states. The chart itself is rendered as children.
 */

export type ChartTimeRange = 'day' | 'week' | 'month' | 'year';

export type ChartCardProps = {
  title: string;
  subtitle?: string;
  timeRanges?: ChartTimeRange[];
  defaultRange?: ChartTimeRange;
  onRangeChange?: (range: ChartTimeRange) => void;
  actions?: React.ReactNode;
  loading?: boolean;
  error?: Error | null;
  empty?: boolean;
  onRetry?: () => void;
  /** Chart body min-height in pixels. Defaults to 300. */
  height?: number;
  children: React.ReactNode;
  className?: string;
};

const RANGE_LABELS: Record<ChartTimeRange, string> = {
  day: 'Day',
  week: 'Week',
  month: 'Month',
  year: 'Year',
};

function ChartSkeleton({ height }: { height: number }) {
  // Y-axis stub bars (5 tick marks) — suggest chart axes without faking data.
  const ticks = Array.from({ length: 5 }, (_, i) => i);
  return (
    <div
      className="flex w-full gap-3"
      style={{ minHeight: height }}
      role="status"
      aria-label="Loading chart"
    >
      <div className="flex w-8 shrink-0 flex-col justify-between py-1">
        {ticks.map((i) => (
          <Skeleton key={i} className="h-2 w-6" />
        ))}
      </div>
      <div className="flex-1 rounded-admin-md bg-admin-bg-subtle/40 motion-safe:animate-pulse" />
    </div>
  );
}

function RangeTabs({
  ranges,
  value,
  onChange,
}: {
  ranges: ChartTimeRange[];
  value: ChartTimeRange;
  onChange: (next: ChartTimeRange) => void;
}) {
  return (
    <div
      role="tablist"
      aria-label="Time range"
      className="inline-flex items-center gap-0.5 rounded-admin-md bg-admin-bg-subtle p-0.5"
    >
      {ranges.map((r) => {
        const active = r === value;
        return (
          <Button
            key={r}
            size="sm"
            variant="ghost"
            role="tab"
            aria-selected={active}
            onClick={() => onChange(r)}
            className={cn(
              'h-7 min-w-0 px-2.5 text-xs',
              active
                ? 'bg-admin-bg-surface text-admin-fg-strong shadow-admin-sm'
                : 'bg-transparent text-admin-fg-muted hover:text-admin-fg-default',
            )}
          >
            {RANGE_LABELS[r]}
          </Button>
        );
      })}
    </div>
  );
}

export const ChartCard = React.forwardRef<HTMLDivElement, ChartCardProps>(
  (
    {
      title,
      subtitle,
      timeRanges,
      defaultRange,
      onRangeChange,
      actions,
      loading,
      error,
      empty,
      onRetry,
      height = 300,
      children,
      className,
    },
    ref,
  ) => {
    const initialRange: ChartTimeRange = defaultRange ?? timeRanges?.[0] ?? 'month';
    const [range, setRange] = React.useState<ChartTimeRange>(initialRange);

    const handleRange = React.useCallback(
      (next: ChartTimeRange) => {
        setRange(next);
        onRangeChange?.(next);
      },
      [onRangeChange],
    );

    return (
      <Card ref={ref} className={className}>
        <CardHeader className="items-start gap-3 pb-3 sm:pb-3">
          <div className="min-w-0 flex-1">
            <CardTitle>{title}</CardTitle>
            {subtitle ? <CardDescription>{subtitle}</CardDescription> : null}
          </div>
          <div className="ml-auto flex shrink-0 items-center gap-2">
            {timeRanges && timeRanges.length > 0 ? (
              <RangeTabs ranges={timeRanges} value={range} onChange={handleRange} />
            ) : null}
            {actions}
          </div>
        </CardHeader>

        <CardContent className="pt-0">
          {loading ? (
            <ChartSkeleton height={height} />
          ) : error ? (
            <EmptyState
              variant="error"
              title="Couldn't load chart"
              description={error.message || 'An error occurred while loading the data.'}
              primaryAction={
                onRetry
                  ? { label: 'Retry', onClick: onRetry }
                  : undefined
              }
            />
          ) : empty ? (
            <EmptyState
              variant="default"
              title="No data yet"
              description="Data will appear here once it's available."
            />
          ) : (
            <div className="w-full" style={{ minHeight: height }}>
              {children}
            </div>
          )}
        </CardContent>
      </Card>
    );
  },
);
ChartCard.displayName = 'ChartCard';
