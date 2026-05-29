'use client';

/* Hallmark · macrostructure: Workbench · tone: utilitarian · anchor hue: primary-blue */

import Link from 'next/link';
import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Archive,
  CalendarClock,
  CheckCircle2,
  Globe2,
  Pencil,
  Plus,
  Radio,
  RotateCw,
  Search,
  Users,
  Video,
} from 'lucide-react';

import { AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { PageHeader } from '@/components/admin/ui/page-header';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Card, CardContent } from '@/components/admin/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  fetchAdminLiveClasses,
  fetchAdminLiveClassAnalytics,
  publishAdminLiveClass,
  type LiveClassListItem,
} from '@/lib/api';

/* ------------------------------------------------------------------ */
/* Types                                                               */
/* ------------------------------------------------------------------ */

type Analytics = {
  totalClasses: number;
  upcomingSessions: number;
  liveSessions: number;
  completedSessions: number;
  totalEnrollments: number;
  totalAttended: number;
  attendanceRate: number;
  recordingFailures: number;
};

/* ------------------------------------------------------------------ */
/* Helpers                                                             */
/* ------------------------------------------------------------------ */

const PROFESSION_TRACKS = ['All', 'Medicine', 'Pharmacy', 'Nursing', 'Dentistry'] as const;
const CLASS_STATUSES = ['All', 'Draft', 'Published', 'Archived'] as const;

function statusVariant(status: string): 'default' | 'success' | 'warning' | 'danger' | 'info' {
  switch (status.toLowerCase()) {
    case 'published': return 'success';
    case 'draft': return 'info';
    case 'archived': return 'warning';
    case 'cancelled': return 'danger';
    case 'live': return 'danger';
    default: return 'default';
  }
}

function typeVariant(type: string): 'default' | 'primary' | 'info' | 'warning' {
  switch (type) {
    case 'Masterclass': return 'primary';
    case 'MockReview': return 'warning';
    case 'OfficeHours': return 'info';
    default: return 'default';
  }
}

function totalEnrolled(item: LiveClassListItem): number {
  return item.sessions.reduce((sum, s) => sum + s.enrolledCount, 0);
}

function upcomingCount(item: LiveClassListItem): number {
  const now = Date.now();
  return item.sessions.filter((s) => new Date(s.scheduledStartAt).getTime() > now).length;
}

/* ------------------------------------------------------------------ */
/* Skeleton rows                                                       */
/* ------------------------------------------------------------------ */

function TableSkeleton() {
  return (
    <div className="space-y-2" aria-hidden="true">
      {Array.from({ length: 5 }).map((_, i) => (
        <div key={i} className="flex items-center gap-4 px-4 py-3 border-b border-admin-border">
          <Skeleton className="h-4 w-48 rounded" />
          <Skeleton className="h-5 w-20 rounded-full" />
          <Skeleton className="h-4 w-24 rounded" />
          <Skeleton className="h-4 w-12 rounded" />
          <Skeleton className="h-4 w-12 rounded" />
          <Skeleton className="h-8 w-24 rounded ml-auto" />
        </div>
      ))}
    </div>
  );
}

/* ------------------------------------------------------------------ */
/* Page                                                                */
/* ------------------------------------------------------------------ */

export default function AdminLiveClassesPage() {
  useAdminAuth();

  const [classes, setClasses] = useState<LiveClassListItem[]>([]);
  const [analytics, setAnalytics] = useState<Analytics | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [publishing, setPublishing] = useState<string | null>(null);

  // Filters (client-side)
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState('All');
  const [trackFilter, setTrackFilter] = useState('All');

  const loadData = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [classRows, analyticsRow] = await Promise.all([
        fetchAdminLiveClasses(),
        fetchAdminLiveClassAnalytics(),
      ]);
      setClasses(classRows);
      setAnalytics(analyticsRow as Analytics);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not load live classes.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void loadData(); }, [loadData]);

  const filtered = useMemo(() => {
    return classes.filter((c) => {
      if (search && !c.title.toLowerCase().includes(search.toLowerCase())) return false;
      if (statusFilter !== 'All' && c.status !== statusFilter) return false;
      if (trackFilter !== 'All' && c.professionTrack !== trackFilter) return false;
      return true;
    });
  }, [classes, search, statusFilter, trackFilter]);

  async function handlePublish(id: string) {
    setPublishing(id);
    try {
      await publishAdminLiveClass(id);
      await loadData();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not publish class.');
    } finally {
      setPublishing(null);
    }
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="Live classes operations">
      <AdminPageShell>
        <PageHeader
          title="Live Classes"
          description="Manage Zoom-backed group classes, control enrollment capacity, and track attendance."
          breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Live Classes' }]}
          actions={
            <div className="flex items-center gap-2">
              <Button
                variant="secondary"
                size="sm"
                onClick={() => void loadData()}
                disabled={loading}
              >
                <RotateCw className="h-4 w-4" aria-hidden="true" />
                Refresh
              </Button>
              <Button variant="primary" size="sm" asChild>
                <Link href="/admin/live-classes/new">
                  <Plus className="h-4 w-4" aria-hidden="true" />
                  New Class
                </Link>
              </Button>
            </div>
          }
        />

        {error ? (
          <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>
        ) : null}

        {/* ── KPI strip — 6 tiles ── */}
        <div
          role="group"
          aria-label="Key performance indicators"
          className="grid grid-cols-2 gap-3 sm:grid-cols-3 sm:gap-4 lg:grid-cols-6"
        >
          <KpiTile
            label="Total Classes"
            value={loading ? '-' : String(analytics?.totalClasses ?? 0)}
            icon={<Video className="h-4 w-4" aria-hidden="true" />}
            loading={loading}
            size="sm"
          />
          <KpiTile
            label="Upcoming"
            value={loading ? '-' : String(analytics?.upcomingSessions ?? 0)}
            icon={<CalendarClock className="h-4 w-4" aria-hidden="true" />}
            loading={loading}
            size="sm"
          />
          <KpiTile
            label="Live Now"
            value={loading ? '-' : String(analytics?.liveSessions ?? 0)}
            icon={<Radio className="h-4 w-4" aria-hidden="true" />}
            tone={(analytics?.liveSessions ?? 0) > 0 ? 'danger' : 'default'}
            loading={loading}
            size="sm"
          />
          <KpiTile
            label="Completed"
            value={loading ? '-' : String(analytics?.completedSessions ?? 0)}
            icon={<CheckCircle2 className="h-4 w-4" aria-hidden="true" />}
            tone="success"
            loading={loading}
            size="sm"
          />
          <KpiTile
            label="Enrollments"
            value={loading ? '-' : String(analytics?.totalEnrollments ?? 0)}
            icon={<Users className="h-4 w-4" aria-hidden="true" />}
            loading={loading}
            size="sm"
          />
          <KpiTile
            label="Attendance Rate"
            value={loading ? '-' : `${analytics?.attendanceRate ?? 0}%`}
            icon={<Globe2 className="h-4 w-4" aria-hidden="true" />}
            tone={(analytics?.attendanceRate ?? 0) < 60 ? 'warning' : 'success'}
            loading={loading}
            size="sm"
          />
        </div>

        {/* ── Filters ── */}
        <div className="flex flex-wrap items-center gap-3">
          <div className="relative flex-1 min-w-[180px] max-w-xs">
            <Search
              className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-admin-fg-muted"
              aria-hidden="true"
            />
            <input
              type="search"
              placeholder="Search classes…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="h-9 w-full rounded-admin-md border border-admin-border bg-admin-bg-surface pl-9 pr-3 text-sm text-admin-fg-strong placeholder:text-admin-fg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)] focus-visible:ring-offset-1"
              aria-label="Search classes by title"
            />
          </div>

          <select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            className="h-9 rounded-admin-md border border-admin-border bg-admin-bg-surface px-3 text-sm text-admin-fg-strong focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)]"
            aria-label="Filter by status"
          >
            {CLASS_STATUSES.map((s) => (
              <option key={s} value={s}>{s === 'All' ? 'All Statuses' : s}</option>
            ))}
          </select>

          <select
            value={trackFilter}
            onChange={(e) => setTrackFilter(e.target.value)}
            className="h-9 rounded-admin-md border border-admin-border bg-admin-bg-surface px-3 text-sm text-admin-fg-strong focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)]"
            aria-label="Filter by profession track"
          >
            {PROFESSION_TRACKS.map((t) => (
              <option key={t} value={t}>{t === 'All' ? 'All Tracks' : t}</option>
            ))}
          </select>

          {(search || statusFilter !== 'All' || trackFilter !== 'All') ? (
            <button
              onClick={() => { setSearch(''); setStatusFilter('All'); setTrackFilter('All'); }}
              className="text-xs text-admin-fg-muted hover:text-admin-fg-default underline underline-offset-2 focus-visible:outline-none"
            >
              Clear filters
            </button>
          ) : null}
        </div>

        {/* ── Table ── */}
        <Card>
          <CardContent className="overflow-x-auto p-0">
            {loading ? (
              <TableSkeleton />
            ) : (
              <table className="w-full min-w-[800px] text-left text-sm">
                <thead className="border-b border-admin-border bg-admin-bg-subtle text-xs uppercase tracking-wider text-admin-fg-muted">
                  <tr>
                    <th scope="col" className="px-4 py-3 font-semibold">Title</th>
                    <th scope="col" className="px-4 py-3 font-semibold">Status</th>
                    <th scope="col" className="px-4 py-3 font-semibold">Track</th>
                    <th scope="col" className="px-4 py-3 font-semibold text-right">Upcoming</th>
                    <th scope="col" className="px-4 py-3 font-semibold text-right">Enrolled</th>
                    <th scope="col" className="px-4 py-3 font-semibold text-right">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-admin-border">
                  {filtered.map((item) => (
                    <tr
                      key={item.id}
                      className="group align-middle transition-colors hover:bg-admin-bg-subtle/60"
                    >
                      <td className="px-4 py-3">
                        <div className="flex flex-col gap-1">
                          <span className="font-medium text-admin-fg-strong leading-snug">
                            {item.title}
                          </span>
                          <div className="flex items-center gap-1.5">
                            <Badge variant={typeVariant(item.type)} size="sm">
                              {item.type}
                            </Badge>
                            {item.tutorDisplayName ? (
                              <span className="text-xs text-admin-fg-muted truncate max-w-[160px]">
                                {item.tutorDisplayName}
                              </span>
                            ) : null}
                          </div>
                        </div>
                      </td>

                      <td className="px-4 py-3">
                        <Badge variant={statusVariant(item.status)} size="sm">
                          {item.status}
                        </Badge>
                      </td>

                      <td className="px-4 py-3 text-admin-fg-default">
                        {item.professionTrack}
                      </td>

                      <td className="px-4 py-3 text-right tabular-nums text-admin-fg-default">
                        {upcomingCount(item)}
                      </td>

                      <td className="px-4 py-3 text-right tabular-nums text-admin-fg-default">
                        {totalEnrolled(item)}
                      </td>

                      <td className="px-4 py-3">
                        <div className="flex items-center justify-end gap-2">
                          <Button variant="secondary" size="sm" asChild>
                            <Link href={`/admin/live-classes/${item.id}`}>
                              <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
                              View
                            </Link>
                          </Button>

                          {item.status === 'Draft' ? (
                            <Button
                              variant="primary"
                              size="sm"
                              loading={publishing === item.id}
                              disabled={publishing !== null}
                              onClick={() => void handlePublish(item.id)}
                            >
                              Publish
                            </Button>
                          ) : null}

                          {item.status === 'Published' ? (
                            <Button
                              variant="outline"
                              size="sm"
                              disabled
                              title="Archive coming soon"
                            >
                              <Archive className="h-3.5 w-3.5" aria-hidden="true" />
                              Archive
                            </Button>
                          ) : null}
                        </div>
                      </td>
                    </tr>
                  ))}

                  {filtered.length === 0 && !loading ? (
                    <tr>
                      <td
                        colSpan={6}
                        className="px-4 py-12 text-center text-sm text-admin-fg-muted"
                      >
                        {classes.length === 0
                          ? 'No live classes have been created yet.'
                          : 'No classes match the current filters.'}
                      </td>
                    </tr>
                  ) : null}
                </tbody>
              </table>
            )}
          </CardContent>
        </Card>
      </AdminPageShell>
    </AdminRouteWorkspace>
  );
}
