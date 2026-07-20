'use client';

import { useEffect, useState } from 'react';
import { TrendingUp } from 'lucide-react';
import { AnimatePresence, motion } from 'motion/react';
import { apiClient } from '@/lib/api';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';

// Field names mirror the backend LearnerProgressTrendResponse contract
// (Contracts/LearnerActionResponses.cs) as serialized in camelCase.
interface ProgressPoint {
  period: string;
  averageScore: number;
  attemptCount: number;
}

interface LearnerProgressTrendResponse {
  points: ProgressPoint[];
  projectedScore: number | null;
  projectedAt: string | null;
  generatedAt: string;
}

export function ProgressTimeline() {
  const [data, setData] = useState<LearnerProgressTrendResponse | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    apiClient
      .get<LearnerProgressTrendResponse>('/v1/learner/actions/progress/trend')
      .then((res) => {
        if (!cancelled) setData(res);
      })
      .catch(() => {
        if (!cancelled) setData(null);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => { cancelled = true; };
  }, []);

  if (loading) {
    return (
      <Card padding="md">
        <Skeleton className="mb-3 h-5 w-40" />
        <div className="flex items-end gap-1.5">
          {Array.from({ length: 8 }).map((_, i) => (
            <Skeleton
              key={i}
              className="w-full rounded-t"
              height={20 + Math.random() * 40}
            />
          ))}
        </div>
        <Skeleton className="mt-3 h-4 w-48" />
      </Card>
    );
  }

  if (!data || data.points.length === 0) {
    return (
      <Card padding="md">
        <div className="flex flex-col items-center py-4 text-center">
          <TrendingUp className="mb-2 h-8 w-8 text-muted" />
          <p className="text-sm text-muted">No activity data yet. Complete some practice to see your trend.</p>
        </div>
      </Card>
    );
  }

  const maxAttempts = Math.max(...data.points.map((p) => p.attemptCount), 1);

  return (
    <AnimatePresence mode="wait">
      <motion.div
        initial={{ opacity: 0, y: 8 }}
        animate={{ opacity: 1, y: 0 }}
        exit={{ opacity: 0, y: -8 }}
        transition={{ duration: 0.2 }}
      >
        <Card padding="md">
          <h3 className="mb-4 text-base font-semibold text-navy">Weekly Activity</h3>

          <div className="flex items-end gap-1.5" style={{ height: 80 }}>
            {data.points.map((point, i) => {
              const heightPercent = (point.attemptCount / maxAttempts) * 100;
              return (
                <motion.div
                  key={point.period}
                  className="group relative flex flex-1 flex-col items-center"
                  style={{ height: '100%' }}
                  initial={{ scaleY: 0 }}
                  animate={{ scaleY: 1 }}
                  transition={{ delay: i * 0.05, duration: 0.3 }}
                >
                  <div className="flex w-full flex-1 items-end">
                    <div
                      className="w-full rounded-t bg-primary/80 transition-colors group-hover:bg-primary"
                      style={{ height: `${Math.max(heightPercent, 4)}%` }}
                    />
                  </div>
                  <span className="mt-1 text-[10px] text-muted truncate w-full text-center">
                    {point.period}
                  </span>

                  {/* Tooltip on hover */}
                  <div className="pointer-events-none absolute -top-8 left-1/2 -translate-x-1/2 rounded bg-navy px-2 py-1 text-[10px] text-white opacity-0 transition-opacity group-hover:opacity-100 whitespace-nowrap">
                    {point.attemptCount} attempts
                  </div>
                </motion.div>
              );
            })}
          </div>

          {data.projectedScore != null && data.projectedAt && (
            <p className="mt-3 flex items-center gap-1.5 text-sm text-muted">
              <TrendingUp className="h-4 w-4 text-emerald-500" />
              Projected: ~{data.projectedScore} by{' '}
              {new Date(data.projectedAt).toLocaleDateString(undefined, {
                month: 'short',
                day: 'numeric',
              })}
            </p>
          )}
        </Card>
      </motion.div>
    </AnimatePresence>
  );
}
