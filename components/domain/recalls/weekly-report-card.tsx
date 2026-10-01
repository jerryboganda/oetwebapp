'use client';

import { useEffect, useState, type ReactNode } from 'react';
import { TrendingUp } from 'lucide-react';
import { Skeleton } from '@/components/ui/skeleton';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { CountUp } from '@/components/ui/count-up';
import { fetchRecallsWeeklyReport, type RecallsWeeklyReport } from '@/lib/api';

/**
 * Candidate weekly report card (spec §14). Pure SQL aggregation — no AI.
 * Shows: practised count, mastered count, spelling accuracy %, weakest
 * topic, most common error, average reviews per card.
 */
export function WeeklyReportCard() {
  const [report, setReport] = useState<RecallsWeeklyReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    fetchRecallsWeeklyReport()
      .then((r) => {
        if (!cancelled) setReport(r);
      })
      .catch(() => {
        if (!cancelled) setError('Could not load weekly report.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  if (!loading && !error && !report) return null;

  const stats: { label: string; value: ReactNode }[] = report
    ? [
        { label: 'Practised', value: <CountUp value={report.practisedCount} /> },
        { label: 'Mastered', value: <CountUp value={report.masteredCount} /> },
        { label: 'Spelling accuracy', value: <CountUp value={report.spellingAccuracyPct} suffix="%" /> },
        { label: 'Avg reviews / card', value: report.averageReviewsPerCard.toFixed(1) },
      ]
    : [];

  return (
    <Card padding="md">
      <div className="flex items-center gap-2">
        <TrendingUp className="h-4 w-4 text-primary" aria-hidden="true" />
        <h3 className="text-sm font-semibold text-navy">This week</h3>
      </div>
      {loading ? (
        <Skeleton className="mt-3 h-24 rounded-xl" />
      ) : error || !report ? (
        <p className="mt-3 text-xs text-warning-strong" role="alert">{error}</p>
      ) : (
        <>
          <div className="mt-3 grid grid-cols-2 gap-3 sm:grid-cols-4">
            {stats.map((s) => (
              <div key={s.label} className="min-w-0 rounded-xl border border-border bg-background-light p-3">
                <div className="tile-label text-muted">{s.label}</div>
                <div className="mt-1 text-lg font-bold tabular-nums text-navy">{s.value}</div>
              </div>
            ))}
          </div>
          <div className="mt-3 flex flex-wrap items-center gap-2 text-xs text-muted">
            {report.weakestTopic ? (
              <>
                <span>Weakest topic:</span>
                <Badge variant="warning">{report.weakestTopic}</Badge>
              </>
            ) : (
              <span>No weak topic this week.</span>
            )}
            {report.mostCommonErrorLabel && (
              <>
                <span className="ms-2">Top error:</span>
                <Badge variant="info">{report.mostCommonErrorLabel}</Badge>
              </>
            )}
          </div>
        </>
      )}
    </Card>
  );
}
