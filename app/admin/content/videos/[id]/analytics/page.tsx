'use client';

/**
 * Video Library — per-video analytics.
 * KPI row (views / unique viewers / watch hours / completion), views-per-day
 * trend, 10-bucket retention curve, and a paged viewers table
 * (X-Total-Count-backed).
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { ArrowLeft, CheckCircle2, Clock, Eye, Users } from 'lucide-react';
import { AdminOperationsLayout, KpiStrip } from '@/components/admin/layout/admin-operations-layout';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { ChartCard } from '@/components/admin/ui/chart-card';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Badge } from '@/components/admin/ui/badge';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Pagination } from '@/components/ui/pagination';
import {
  adminGetVideo,
  adminGetVideoAnalytics,
  adminListVideoViewers,
  type AdminVideoAnalyticsSummary,
  type AdminVideoViewerRow,
} from '@/lib/api/video-library';

type PageStatus = 'loading' | 'success' | 'error';

function ViewsPerDayBars({ data }: { data: Array<{ date: string; views: number }> }) {
  if (data.length === 0) {
    return <p className="text-sm text-admin-fg-muted">No views recorded in this window yet.</p>;
  }
  const max = Math.max(1, ...data.map((d) => d.views));
  return (
    <div className="flex h-full min-h-[200px] items-end gap-1" role="img" aria-label="Views per day">
      {data.map((d) => (
        <div key={d.date} className="group flex flex-1 flex-col items-center justify-end">
          <div
            className="w-full rounded-t bg-[var(--admin-primary)] opacity-80 transition-opacity group-hover:opacity-100"
            style={{ height: `${Math.max(2, (d.views / max) * 180)}px` }}
            title={`${d.date}: ${d.views} view${d.views === 1 ? '' : 's'}`}
          />
        </div>
      ))}
    </div>
  );
}

function RetentionBars({ buckets }: { buckets: number[] }) {
  if (buckets.length === 0) {
    return <p className="text-sm text-admin-fg-muted">No retention data yet.</p>;
  }
  const max = Math.max(1, ...buckets);
  return (
    <div>
      <div className="flex items-end gap-1.5" role="img" aria-label="Audience retention by position">
        {buckets.map((value, index) => (
          <div key={index} className="flex flex-1 flex-col items-center gap-1">
            <div
              className="w-full rounded-t bg-[var(--admin-success)] opacity-80"
              style={{ height: `${Math.max(2, (value / max) * 120)}px` }}
              title={`${index * 10}–${index * 10 + 10}%: ${value} viewer${value === 1 ? '' : 's'}`}
            />
            <span className="text-[10px] tabular-nums text-admin-fg-muted">{index * 10}</span>
          </div>
        ))}
      </div>
      <p className="mt-1 text-center text-[10px] uppercase tracking-widest text-admin-fg-muted">
        % of video watched
      </p>
    </div>
  );
}

export default function AdminVideoAnalyticsPage() {
  const params = useParams<{ id: string }>();
  const videoId = params?.id ?? '';

  const [status, setStatus] = useState<PageStatus>('loading');
  const [title, setTitle] = useState<string>('');
  const [summary, setSummary] = useState<AdminVideoAnalyticsSummary | null>(null);

  const [viewers, setViewers] = useState<AdminVideoViewerRow[]>([]);
  const [viewersTotal, setViewersTotal] = useState(0);
  const [viewersLoading, setViewersLoading] = useState(true);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);

  useEffect(() => {
    if (!videoId) return;
    let cancelled = false;
    setStatus('loading');
    Promise.all([adminGetVideoAnalytics(videoId, 30), adminGetVideo(videoId)])
      .then(([analytics, video]) => {
        if (cancelled) return;
        setSummary(analytics);
        setTitle(video.title);
        setStatus('success');
      })
      .catch(() => {
        if (!cancelled) setStatus('error');
      });
    return () => {
      cancelled = true;
    };
  }, [videoId]);

  const loadViewers = useCallback(async () => {
    if (!videoId) return;
    setViewersLoading(true);
    try {
      const response = await adminListVideoViewers(videoId, { page, pageSize });
      setViewers(response.items);
      setViewersTotal(response.total);
    } catch {
      setViewers([]);
      setViewersTotal(0);
    } finally {
      setViewersLoading(false);
    }
  }, [videoId, page, pageSize]);

  useEffect(() => {
    void loadViewers();
  }, [loadViewers]);

  const viewerColumns = useMemo<Column<AdminVideoViewerRow>[]>(() => [
    {
      key: 'viewer',
      header: 'Learner',
      render: (row) => (
        <div className="min-w-0">
          <p className="font-semibold text-admin-fg-strong line-clamp-1">{row.name || '—'}</p>
          <p className="mt-0.5 text-xs text-admin-fg-muted line-clamp-1">{row.email}</p>
        </div>
      ),
    },
    {
      key: 'position',
      header: 'Position',
      render: (row) => {
        const s = Math.max(0, Math.round(row.positionSeconds));
        return (
          <span className="text-sm tabular-nums text-admin-fg-default">
            {Math.floor(s / 60)}:{String(s % 60).padStart(2, '0')}
          </span>
        );
      },
      hideOnMobile: true,
    },
    {
      key: 'progress',
      header: 'Progress',
      render: (row) => (
        <span className="text-sm tabular-nums text-admin-fg-default">{Math.round(row.percentComplete)}%</span>
      ),
    },
    {
      key: 'completed',
      header: 'Completed',
      render: (row) => (
        <Badge variant={row.completed ? 'success' : 'muted'}>{row.completed ? 'Yes' : 'No'}</Badge>
      ),
      hideOnMobile: true,
    },
    {
      key: 'lastWatched',
      header: 'Last watched',
      render: (row) => (
        <span className="text-sm text-admin-fg-muted">{new Date(row.lastWatchedAt).toLocaleString()}</span>
      ),
      hideOnMobile: true,
    },
  ], []);

  return (
    <AdminOperationsLayout
      eyebrow="Content"
      title={title ? `Analytics — ${title}` : 'Video Analytics'}
      description="Engagement for this video over the last 30 days: plays, unique viewers, retention and per-learner progress."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Content', href: '/admin/content' },
        { label: 'Video Library', href: '/admin/content/videos' },
        { label: 'Analytics' },
      ]}
      kpis={
        status === 'success' && summary ? (
          <KpiStrip>
            <KpiTile label="Views (30d)" value={summary.views} icon={<Eye className="h-4 w-4" />} />
            <KpiTile
              label="Unique viewers"
              value={summary.uniqueViewers}
              icon={<Users className="h-4 w-4" />}
            />
            <KpiTile
              label="Watch hours"
              value={summary.watchHours.toFixed(1)}
              icon={<Clock className="h-4 w-4" />}
            />
            <KpiTile
              label="Avg completion"
              value={`${Math.round(summary.avgCompletionPercent)}%`}
              icon={<CheckCircle2 className="h-4 w-4" />}
              tone={summary.avgCompletionPercent < 50 ? 'warning' : 'success'}
            />
          </KpiStrip>
        ) : null
      }
      primaryGrid={
        <div className="space-y-6">
          <div>
            <Link
              href="/admin/content/videos"
              className="inline-flex items-center gap-1 text-sm font-bold text-admin-fg-strong hover:underline"
            >
              <ArrowLeft className="h-4 w-4" /> Back to Video Library
            </Link>
          </div>

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
                  Analytics for this video could not be loaded. Try again after the API is available.
                </p>
              </CardContent>
            </Card>
          ) : null}

          {status === 'success' && summary ? (
            <>
              <ChartCard
                title="Views per day"
                subtitle="Daily plays of this video (last 30 days)."
                empty={summary.viewsPerDay.length === 0}
                height={220}
              >
                <ViewsPerDayBars data={summary.viewsPerDay} />
              </ChartCard>

              <Card>
                <CardHeader>
                  <CardTitle>Audience retention</CardTitle>
                  <CardDescription>How far into the video learners are still watching.</CardDescription>
                </CardHeader>
                <CardContent>
                  <RetentionBars buckets={summary.retentionBuckets ?? []} />
                </CardContent>
              </Card>

              <Card>
                <CardHeader>
                  <CardTitle>Viewers</CardTitle>
                  <CardDescription>Per-learner watch position and completion.</CardDescription>
                </CardHeader>
                <CardContent className="space-y-4">
                  {viewersLoading ? (
                    <Skeleton className="h-40 rounded-admin" />
                  ) : viewers.length === 0 ? (
                    <p className="text-sm text-admin-fg-muted">No learners have watched this video yet.</p>
                  ) : (
                    <DataTable columns={viewerColumns} data={viewers} keyExtractor={(row) => row.userId} />
                  )}
                  <Pagination
                    page={page}
                    pageSize={pageSize}
                    total={viewersTotal}
                    onPageChange={setPage}
                    onPageSizeChange={(s) => {
                      setPageSize(s);
                      setPage(1);
                    }}
                    pageSizeOptions={[10, 25, 50]}
                    itemLabel="viewer"
                    itemLabelPlural="viewers"
                  />
                </CardContent>
              </Card>
            </>
          ) : null}
        </div>
      }
    />
  );
}
