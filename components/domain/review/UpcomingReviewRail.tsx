'use client';

import { useMemo } from 'react';
import { CalendarCheck, Sparkles } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import type { ReviewItem } from '@/lib/types/review';

interface UpcomingReviewRailProps {
  items: ReviewItem[];
  loading: boolean;
}

/**
 * Condensed rail of the next items the learner will see. Listed under the
 * heatmap so users can peek into what's coming without starting a session.
 */
export function UpcomingReviewRail({ items, loading }: UpcomingReviewRailProps) {
  const top = useMemo(() => items.slice(0, 6), [items]);

  return (
    <Card className="rounded-3xl border border-border bg-surface p-1 shadow-sm">
      <CardHeader className="pb-2">
        <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-primary">
          Next up
        </p>
        <CardTitle className="mt-1 text-lg text-navy">In this session</CardTitle>
        <p className="mt-1 text-xs text-muted">
          A peek at the items waiting in your queue.
        </p>
      </CardHeader>
      <CardContent>
        {loading ? (
          <div className="space-y-2">
            {Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} className="h-12 rounded-xl" />
            ))}
          </div>
        ) : top.length === 0 ? (
          <div className="flex flex-col items-center justify-center gap-2 rounded-2xl border border-dashed border-border bg-background-light/70 py-8 text-center">
            <CalendarCheck className="h-5 w-5 text-muted" />
            <p className="text-sm font-semibold text-navy">No items due</p>
            <p className="text-xs text-muted">Great work — check back tomorrow.</p>
          </div>
        ) : (
          <ul className="space-y-2">
            {top.map((item) => (
              <li
                key={item.id}
                className="flex items-center justify-between gap-3 rounded-2xl border border-border bg-background-light/60 px-3 py-2"
              >
                <div className="min-w-0">
                  <p className="text-[11px] font-semibold uppercase tracking-[0.14em] text-muted">
                    {item.sourceType.replace(/_/g, ' ')}
                  </p>
                  <p className="truncate text-sm font-medium text-navy">
                    {item.title ?? 'Review item'}
                  </p>
                </div>
                <Sparkles className="h-4 w-4 flex-none text-primary" aria-hidden="true" />
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}
