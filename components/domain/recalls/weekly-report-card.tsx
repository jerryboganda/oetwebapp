'use client';

import { useEffect, useState } from 'react';
import { TrendingUp } from 'lucide-react';
import { Skeleton } from '@/components/ui/skeleton';
import { Badge } from '@/components/ui/badge';
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

  if (loading) return <Skeleton className="h-32 rounded-2xl" />;
  if (error) return <div className="text-xs text-warning">{error}</div>;
  if (!report) return null;

  const stats: { label: string; value: string }[] = [
    { label: 'Practised', value: `${report.practisedCount}` },
    { label: 'Mastered', value: `${report.masteredCount}` },
    { label: 'Spelling accuracy', value: `${report.spellingAccuracyPct}%` },
    { label: 'Avg reviews / card', value: report.averageReviewsPerCard.toFixed(1) },
  ];

  return (
    <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
      <div className="flex items-center gap-2">
        <TrendingUp className="h-4 w-4 text-primary" />
        <h3 className="text-sm font-semibold text-navy">This week</h3>
      </div>
      <div className="mt-3 grid grid-cols-2 gap-3 sm:grid-cols-4">
        {stats.map((s) => (
          <div key={s.label} className="rounded-xl border border-border bg-background-light p-3">
            <div className="text-xs uppercase tracking-wide text-muted">{s.label}</div>
            <div className="mt-1 font-mono text-lg font-semibold text-navy">{s.value}</div>
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
            <span className="ml-2">Top error:</span>
            <Badge variant="info">{report.mostCommonErrorLabel}</Badge>
          </>
        )}
      </div>
    </div>
  );
}
