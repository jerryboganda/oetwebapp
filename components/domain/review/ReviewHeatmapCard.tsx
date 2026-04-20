'use client';

import { useMemo } from 'react';
import { Target } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { cn } from '@/lib/utils';
import {
  type ReviewHeatmapCell,
  type ReviewHeatmapResponse,
  SOURCE_TYPE_LABELS,
} from '@/lib/types/review';

interface ReviewHeatmapCardProps {
  data: ReviewHeatmapResponse | null;
  loading: boolean;
}

const SOURCE_ORDER = [
  'writing_issue',
  'speaking_issue',
  'reading_miss',
  'listening_miss',
  'grammar_error',
  'pronunciation_finding',
  'vocabulary',
  'mock_miss',
] as const;

const SOURCE_ACCENT: Record<string, string> = {
  writing_issue: 'bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-300',
  speaking_issue: 'bg-fuchsia-50 text-fuchsia-700 dark:bg-fuchsia-500/10 dark:text-fuchsia-300',
  reading_miss: 'bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-300',
  listening_miss: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/10 dark:text-indigo-300',
  grammar_error: 'bg-violet-50 text-violet-700 dark:bg-violet-500/10 dark:text-violet-300',
  pronunciation_finding: 'bg-purple-50 text-purple-700 dark:bg-purple-500/10 dark:text-purple-300',
  vocabulary: 'bg-lavender/40 text-primary dark:bg-violet-500/10 dark:text-violet-300',
  mock_miss: 'bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-300',
};

interface Totals {
  active: number;
  due: number;
  mastered: number;
}

export function ReviewHeatmapCard({ data, loading }: ReviewHeatmapCardProps) {
  const totals = useMemo(() => {
    const bucket: Record<string, Totals> = {};
    (data?.cells ?? []).forEach((cell: ReviewHeatmapCell) => {
      const key = cell.sourceType;
      bucket[key] ??= { active: 0, due: 0, mastered: 0 };
      bucket[key].active += cell.active;
      bucket[key].due += cell.due;
      bucket[key].mastered += cell.mastered;
    });
    return bucket;
  }, [data]);

  const totalActive = useMemo(
    () => Object.values(totals).reduce((sum, t) => sum + t.active, 0),
    [totals],
  );

  return (
    <Card className="rounded-3xl border border-border bg-surface p-1 shadow-sm">
      <CardHeader className="pb-2">
        <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-primary">
          Weak areas
        </p>
        <CardTitle className="mt-1 text-lg text-navy">By skill and source</CardTitle>
        <p className="mt-1 text-xs text-muted">
          Count of active review cards in each area. Focus first where the due count is highest.
        </p>
      </CardHeader>
      <CardContent>
        {loading ? (
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
            {Array.from({ length: 8 }).map((_, i) => (
              <Skeleton key={i} className="h-24 rounded-2xl" />
            ))}
          </div>
        ) : totalActive === 0 ? (
          <div className="flex flex-col items-center justify-center gap-2 rounded-2xl border border-dashed border-border bg-background-light/70 py-10 text-center">
            <Target className="h-6 w-6 text-muted" />
            <p className="text-sm font-semibold text-navy">No items yet</p>
            <p className="text-xs text-muted">
              Complete practice tasks and your weak areas will surface here automatically.
            </p>
          </div>
        ) : (
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
            {SOURCE_ORDER.map((source) => {
              const data = totals[source];
              const accent = SOURCE_ACCENT[source] ?? 'bg-background-light text-muted';
              const label = SOURCE_TYPE_LABELS[source as keyof typeof SOURCE_TYPE_LABELS] ?? source;
              return (
                <div
                  key={source}
                  className={cn(
                    'flex flex-col justify-between rounded-2xl border border-border bg-surface px-3 py-3 text-left transition-colors',
                  )}
                >
                  <div className={cn('inline-flex w-fit rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-[0.14em]', accent)}>
                    {label}
                  </div>
                  <div className="mt-2 flex items-baseline gap-1">
                    <span className="text-2xl font-bold text-navy">{data?.active ?? 0}</span>
                    <span className="text-xs text-muted">active</span>
                  </div>
                  <div className="mt-1 flex items-center justify-between text-[11px] text-muted">
                    <span>
                      <span className="font-semibold text-danger">{data?.due ?? 0}</span> due
                    </span>
                    <span>
                      <span className="font-semibold text-success">{data?.mastered ?? 0}</span> mastered
                    </span>
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
