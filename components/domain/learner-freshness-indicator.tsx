'use client';

import { useState } from 'react';
import { Clock } from 'lucide-react';
import { cn } from '@/lib/utils';

type LearnerFreshnessIndicatorProps = {
  updatedAt?: string | Date | null;
  staleAfterMinutes?: number;
  source?: 'updated' | 'loaded';
  className?: string;
};

function toDate(value: string | Date | null | undefined) {
  if (!value) return null;
  const date = value instanceof Date ? value : new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
}

function freshnessLabel(date: Date | null, source: 'updated' | 'loaded', nowMs: number | null) {
  const verb = source === 'loaded' ? 'Loaded' : 'Updated';

  if (!date) {
    return `${verb} time unknown`;
  }

  if (nowMs === null) {
    return `${verb} recently`;
  }

  const elapsedMs = Math.max(0, nowMs - date.getTime());
  const elapsedMinutes = Math.floor(elapsedMs / 60000);

  if (elapsedMinutes < 1) {
    return `${verb} just now`;
  }

  if (elapsedMinutes < 60) {
    return `${verb} ${elapsedMinutes} min ago`;
  }

  const elapsedHours = Math.floor(elapsedMinutes / 60);
  if (elapsedHours < 24) {
    return `${verb} ${elapsedHours} hr ago`;
  }

  const elapsedDays = Math.floor(elapsedHours / 24);
  return `${verb} ${elapsedDays} day${elapsedDays === 1 ? '' : 's'} ago`;
}

export function LearnerFreshnessIndicator({
  updatedAt,
  staleAfterMinutes = 60,
  source = 'updated',
  className,
}: LearnerFreshnessIndicatorProps) {
  const [nowMs] = useState(() => Date.now());

  const date = toDate(updatedAt);
  const elapsedMinutes = date ? Math.floor(Math.max(0, nowMs - date.getTime()) / 60000) : null;
  const isStale = elapsedMinutes !== null && elapsedMinutes >= staleAfterMinutes;
  const label = freshnessLabel(date, source, nowMs);

  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-[11px] font-bold uppercase tracking-[0.14em]',
        date
          ? isStale
            ? 'border-amber-200 bg-amber-50 text-amber-700'
            : 'border-emerald-200 bg-emerald-50 text-emerald-700'
          : 'border-border bg-background-light text-muted',
        className,
      )}
      title={date ? date.toLocaleString() : 'No freshness timestamp available'}
    >
      <Clock className="h-3.5 w-3.5" aria-hidden="true" />
      <time dateTime={date?.toISOString()}>{label}</time>
    </span>
  );
}
