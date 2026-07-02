'use client';

/**
 * Video Library — library-wide analytics dashboard.
 * KPI row, views-per-day trend, top-10 videos and views-by-category bars,
 * fed by `GET /v1/admin/video-library/analytics/summary?days=30`.
 */

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { BarChart3, CheckCircle2, Clapperboard, Clock, Eye } from 'lucide-react';
import { AdminOperationsLayout, KpiStrip } from '@/components/admin/layout/admin-operations-layout';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { ChartCard } from '@/components/admin/ui/chart-card';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Skeleton } from '@/components/admin/ui/skeleton';
import {
  adminGetVideoLibraryAnalytics,
  type VideoLibraryAnalyticsSummary,
} from '@/lib/api/video-library';
import { buildVideoStepHref } from '@/components/domain/video-library/video-wizard-config';

type PageStatus = 'loading' | 'success' | 'error';

function ViewsPerDayBars({ data }: { data: Array<{ date: string; views: number }> }) {
  if (data.length === 0) {
    return <p className="text-sm text-admin-fg-muted">No views recorded in this window yet.</p>;
  }
  const max = Math.max(1, ...data.map((d) => d.views));
  return (
    <div className="flex h-full min-h-[220px] items-end gap-1" role="img" aria-label="Views per day">
      {data.map((d) => (
        <div key={d.date} className="group flex flex-1 flex-col items-center justify-end gap-1">
          <div
            className="w-full rounded-t bg-[var(--admin-primary)] opacity-80 transition-opacity group-hover:opacity-100"
            style={{ height: `${Math.max(2, (d.views / max) * 200)}px` }}
            title={`${d.date}: ${d.views} view${d.views === 1 ? '' : 's'}`}
          />
        </div>
      ))}
    </div>
  );
}

function ViewsByCategoryBars({
  rows,
}: {
  rows: Array<{ categoryId: string; title: string; views: number }>;
}) {
  if (rows.length === 0) {
    return <p className="text-sm text-admin-fg-muted">No category-level views yet.</p>;
  }
  const max = Math.max(1, ...rows.map((r) => r.views));
  return (
    <ul className="space-y-2">
      {rows.map((row) => (
        <li key={row.categoryId} className="space-y-1">
          <div className="flex items-center justify-between text-xs text-admin-fg-muted">
            <span className="truncate">{row.title}</span>
            <span className="tabular-nums">{row.views}</span>
          </div>
          <div className="h-1.5 w-full overflow-hidden rounded-full bg-admin-bg-subtle">
            <div
              className="h-full bg-[var(--admin-info)]"
              style={{ width: `${Math.max(2, (row.views / max) * 100)}%` }}
              aria-hidden
            />
          </div>
        </li>
      ))}
    </ul>
  );
}

export default function AdminVideoLibraryAnalyticsPage() {
  const [status, setStatus] = useState<PageStatus>('loading');
  const [summary, setSummary] = useState<VideoLibraryAnalyticsSummary | null>(null);

  useEffect(() => {
    let cancelled = false;
    setStatus('loading');
    adminGetVideoLibraryAnalytics(30)
      .then((result) => {
        if (cancelled) return;
        setSummary(result);
        setStatus('success');
      })
      .catch(() => {
        if (!cancelled) setStatus('error');
      });
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <AdminOperationsLayout
      eyebrow="Content"
      title="Video Analytics"
      description="Library-wide engagement over the last 30 days: views, watch time, completion, top videos and category breakdown."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Content', href: '/admin/content' },
        { label: 'Video Library', href: '/admin/content/videos' },
        { label: 'Analytics' },
      ]}
      kpis={
        status === 'success' && summary ? (
          <KpiStrip>
            <KpiTile
              label="Published videos"
              value={summary.totals.publishedVideos}
              icon={<Clapperboard className="h-4 w-4" />}
              tone="primary"
            />
            <KpiTile label="Views (30d)" value={summary.totals.views} icon={<Eye className="h-4 w-4" />} />
            <KpiTile
              label="Watch hours (30d)"
              value={summary.totals.watchHours.toFixed(1)}
              icon={<Clock className="h-4 w-4" />}
            />
            <KpiTile
              label="Avg completion"
              value={`${Math.round(summary.totals.avgCompletionPercent)}%`}
              icon={<CheckCircle2 className="h-4 w-4" />}
              tone={summary.totals.avgCompletionPercent < 50 ? 'warning' : 'success'}
            />
          </KpiStrip>
        ) : null
      }
      primaryGrid={
        <div className="space-y-6">
          {status === 'loading' ? (
            <>
              <Skeleton className="h-64 rounded-admin" />
              <Skeleton className="h-48 rounded-admin" />
            </>
          ) : null}

          {status === 'error' ? (
            <Card>
              <CardHeader>
                <CardTitle>Video analytics unavailable</CardTitle>
              </CardHeader>
              <CardContent>
                <p className="text-sm text-admin-fg-muted">
                  The video analytics service could not be loaded. Try again after the API is available.
                </p>
              </CardContent>
            </Card>
          ) : null}

          {status === 'success' && summary ? (
            <>
              <ChartCard
                title="Views per day"
                subtitle="Daily plays across the whole library (last 30 days)."
                empty={summary.viewsPerDay.length === 0}
                height={240}
              >
                <ViewsPerDayBars data={summary.viewsPerDay} />
              </ChartCard>

              <Card>
                <CardHeader>
                  <CardTitle>Top videos</CardTitle>
                  <CardDescription>The ten most-watched videos in the window.</CardDescription>
                </CardHeader>
                <CardContent>
                  {summary.topVideos.length === 0 ? (
                    <p className="text-sm text-admin-fg-muted">No plays recorded yet.</p>
                  ) : (
                    <div className="overflow-x-auto">
                      <table className="min-w-full divide-y divide-admin-border text-sm">
                        <thead>
                          <tr className="text-left text-xs font-bold uppercase tracking-[0.14em] text-admin-fg-muted">
                            <th scope="col" className="py-3 pr-4">Video</th>
                            <th scope="col" className="px-4 py-3 text-right">Views</th>
                            <th scope="col" className="px-4 py-3 text-right">Watch hours</th>
                            <th scope="col" className="px-4 py-3 text-right">Completion</th>
                            <th scope="col" className="px-4 py-3 text-right" aria-label="Actions" />
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-admin-border/70">
                          {summary.topVideos.slice(0, 10).map((row) => (
                            <tr key={row.videoId}>
                              <td className="py-3 pr-4">
                                <p className="font-semibold text-admin-fg-strong line-clamp-1">{row.title}</p>
                              </td>
                              <td className="px-4 py-3 text-right tabular-nums">{row.views}</td>
                              <td className="px-4 py-3 text-right tabular-nums">{row.watchHours.toFixed(1)}</td>
                              <td className="px-4 py-3 text-right tabular-nums">
                                {Math.round(row.completionPercent)}%
                              </td>
                              <td className="px-4 py-3 text-right">
                                <span className="inline-flex items-center gap-3">
                                  <Link
                                    className="inline-flex items-center gap-1 text-xs font-bold text-admin-fg-strong hover:underline"
                                    href={`/admin/content/videos/${encodeURIComponent(row.videoId)}/analytics`}
                                  >
                                    <BarChart3 className="h-3.5 w-3.5" /> Analytics
                                  </Link>
                                  <Link
                                    className="text-xs font-bold text-admin-fg-strong hover:underline"
                                    href={buildVideoStepHref(row.videoId, 'details')}
                                  >
                                    Edit
                                  </Link>
                                </span>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </CardContent>
              </Card>

              <Card>
                <CardHeader>
                  <CardTitle>Views by category</CardTitle>
                  <CardDescription>Where learners are spending their attention.</CardDescription>
                </CardHeader>
                <CardContent>
                  <ViewsByCategoryBars rows={summary.viewsByCategory} />
                </CardContent>
              </Card>
            </>
          ) : null}
        </div>
      }
    />
  );
}
