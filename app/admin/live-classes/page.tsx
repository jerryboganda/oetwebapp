'use client';

/* Hallmark · pre-emit critique: P4 H4 E4 S4 R5 V4 */

import { useEffect, useMemo, useState } from 'react';
import { CalendarPlus, Radio, RotateCw, Users, Video, XCircle } from 'lucide-react';

import { AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { PageHeader } from '@/components/admin/ui/page-header';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  cancelAdminLiveClassSession,
  createAdminLiveClass,
  fetchAdminLiveClassAnalytics,
  fetchAdminLiveClasses,
  fetchAdminPrivateSpeakingTutors,
  publishAdminLiveClass,
  type AdminLiveClassUpsertPayload,
  type LiveClassListItem,
  type LiveClassSessionSummary,
} from '@/lib/api';

type TutorProfile = {
  id: string;
  displayName: string;
  expertUserId: string;
  isActive: boolean;
};

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

const defaultDraft: AdminLiveClassUpsertPayload = {
  title: '',
  description: '',
  type: 'GroupClass',
  professionTrack: 'All',
  level: 'All',
  tutorProfileId: null,
  scheduledStartAt: '',
  durationMinutes: 60,
  capacity: 100,
  creditCost: 5,
  tags: [],
  autoPublish: false,
};

function formatDate(value: string) {
  return new Intl.DateTimeFormat('en-AU', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value));
}

function badgeVariant(status: string): 'default' | 'success' | 'warning' | 'danger' | 'info' {
  if (status === 'Published' || status === 'Completed') return 'success';
  if (status === 'Draft' || status === 'Scheduled') return 'info';
  if (status === 'Live') return 'warning';
  if (status === 'Cancelled' || status === 'Failed') return 'danger';
  return 'default';
}

function toLocalInputValue(date: Date) {
  const copy = new Date(date.getTime() - date.getTimezoneOffset() * 60_000);
  return copy.toISOString().slice(0, 16);
}

export default function AdminLiveClassesPage() {
  useAdminAuth();
  const [classes, setClasses] = useState<LiveClassListItem[]>([]);
  const [tutors, setTutors] = useState<TutorProfile[]>([]);
  const [analytics, setAnalytics] = useState<Analytics | null>(null);
  const [draft, setDraft] = useState<AdminLiveClassUpsertPayload>({
    ...defaultDraft,
    scheduledStartAt: toLocalInputValue(new Date(Date.now() + 24 * 60 * 60 * 1000)),
  });
  const [saving, setSaving] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const nextSessions = useMemo(() => classes.flatMap((item) => item.sessions.map((session) => ({ item, session }))).slice(0, 8), [classes]);

  async function loadData() {
    setLoading(true);
    try {
      const [classRows, tutorRows, analyticsRow] = await Promise.all([
        fetchAdminLiveClasses(),
        fetchAdminPrivateSpeakingTutors(true),
        fetchAdminLiveClassAnalytics(),
      ]);
      setClasses(classRows);
      setTutors(tutorRows as TutorProfile[]);
      setAnalytics(analyticsRow as Analytics);
      setError(null);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not load live-class operations.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void loadData();
  }, []);

  async function handleCreate() {
    setSaving(true);
    setError(null);
    try {
      await createAdminLiveClass({
        ...draft,
        tutorProfileId: draft.tutorProfileId || null,
        scheduledStartAt: new Date(draft.scheduledStartAt).toISOString(),
        tags: typeof draft.tags === 'string' ? String(draft.tags).split(',').map((tag) => tag.trim()).filter(Boolean) : draft.tags,
      });
      setDraft({ ...defaultDraft, scheduledStartAt: toLocalInputValue(new Date(Date.now() + 24 * 60 * 60 * 1000)) });
      await loadData();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not create live class.');
    } finally {
      setSaving(false);
    }
  }

  async function handlePublish(liveClassId: string) {
    await publishAdminLiveClass(liveClassId);
    await loadData();
  }

  async function handleCancel(session: LiveClassSessionSummary) {
    await cancelAdminLiveClassSession(session.id, 'Cancelled from admin live classes page.');
    await loadData();
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="Live class operations">
      <AdminPageShell>
        <PageHeader
          title="Live classes"
          description="Schedule Zoom-backed group classes, control enrollment capacity, and monitor attendance operations."
          breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Live classes' }]}
          actions={<Button onClick={() => void loadData()} variant="secondary" size="sm"><RotateCw className="h-4 w-4" /> Refresh</Button>}
        />

        {error ? <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert> : null}

        {loading ? (
          <div className="space-y-4"><Skeleton className="h-24 rounded-admin-lg" /><Skeleton className="h-72 rounded-admin-lg" /></div>
        ) : (
          <div className="space-y-5">
            <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
              <KpiTile label="Classes" value={String(analytics?.totalClasses ?? 0)} icon={<Video className="h-4 w-4" />} size="sm" />
              <KpiTile label="Upcoming" value={String(analytics?.upcomingSessions ?? 0)} icon={<CalendarPlus className="h-4 w-4" />} size="sm" />
              <KpiTile label="Enrollments" value={String(analytics?.totalEnrollments ?? 0)} icon={<Users className="h-4 w-4" />} size="sm" />
              <KpiTile label="Attendance" value={`${analytics?.attendanceRate ?? 0}%`} icon={<Radio className="h-4 w-4" />} tone={(analytics?.attendanceRate ?? 0) < 60 ? 'warning' : 'default'} size="sm" />
            </div>

            <div className="grid gap-5 xl:grid-cols-[minmax(320px,420px)_1fr]">
              <Card>
                <CardHeader><CardTitle>Create class</CardTitle></CardHeader>
                <CardContent className="space-y-3">
                  <label className="block text-xs font-semibold text-admin-fg-muted">Title<input value={draft.title} onChange={(event) => setDraft((prev) => ({ ...prev, title: event.target.value }))} className="mt-1 h-10 w-full rounded-admin-md border border-admin-border bg-admin-bg-surface px-3 text-sm text-admin-fg-strong" /></label>
                  <label className="block text-xs font-semibold text-admin-fg-muted">Description<textarea value={draft.description} onChange={(event) => setDraft((prev) => ({ ...prev, description: event.target.value }))} className="mt-1 min-h-24 w-full rounded-admin-md border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-strong" /></label>
                  <div className="grid grid-cols-2 gap-3">
                    <label className="block text-xs font-semibold text-admin-fg-muted">Type<select value={draft.type} onChange={(event) => setDraft((prev) => ({ ...prev, type: event.target.value }))} className="mt-1 h-10 w-full rounded-admin-md border border-admin-border bg-admin-bg-surface px-3 text-sm"><option>GroupClass</option><option>Masterclass</option><option>MockReview</option><option>OfficeHours</option></select></label>
                    <label className="block text-xs font-semibold text-admin-fg-muted">Tutor<select value={draft.tutorProfileId ?? ''} onChange={(event) => setDraft((prev) => ({ ...prev, tutorProfileId: event.target.value || null }))} className="mt-1 h-10 w-full rounded-admin-md border border-admin-border bg-admin-bg-surface px-3 text-sm"><option value="">To confirm</option>{tutors.map((tutor) => <option key={tutor.id} value={tutor.id}>{tutor.displayName}</option>)}</select></label>
                  </div>
                  <div className="grid grid-cols-2 gap-3">
                    <label className="block text-xs font-semibold text-admin-fg-muted">Track<input value={draft.professionTrack} onChange={(event) => setDraft((prev) => ({ ...prev, professionTrack: event.target.value }))} className="mt-1 h-10 w-full rounded-admin-md border border-admin-border bg-admin-bg-surface px-3 text-sm" /></label>
                    <label className="block text-xs font-semibold text-admin-fg-muted">Level<input value={draft.level} onChange={(event) => setDraft((prev) => ({ ...prev, level: event.target.value }))} className="mt-1 h-10 w-full rounded-admin-md border border-admin-border bg-admin-bg-surface px-3 text-sm" /></label>
                  </div>
                  <label className="block text-xs font-semibold text-admin-fg-muted">Start<input type="datetime-local" value={draft.scheduledStartAt} onChange={(event) => setDraft((prev) => ({ ...prev, scheduledStartAt: event.target.value }))} className="mt-1 h-10 w-full rounded-admin-md border border-admin-border bg-admin-bg-surface px-3 text-sm" /></label>
                  <div className="grid grid-cols-3 gap-3">
                    <label className="block text-xs font-semibold text-admin-fg-muted">Minutes<input type="number" min={15} value={draft.durationMinutes} onChange={(event) => setDraft((prev) => ({ ...prev, durationMinutes: Number(event.target.value) }))} className="mt-1 h-10 w-full rounded-admin-md border border-admin-border bg-admin-bg-surface px-3 text-sm" /></label>
                    <label className="block text-xs font-semibold text-admin-fg-muted">Capacity<input type="number" min={1} value={draft.capacity} onChange={(event) => setDraft((prev) => ({ ...prev, capacity: Number(event.target.value) }))} className="mt-1 h-10 w-full rounded-admin-md border border-admin-border bg-admin-bg-surface px-3 text-sm" /></label>
                    <label className="block text-xs font-semibold text-admin-fg-muted">Credits<input type="number" min={0} value={draft.creditCost} onChange={(event) => setDraft((prev) => ({ ...prev, creditCost: Number(event.target.value) }))} className="mt-1 h-10 w-full rounded-admin-md border border-admin-border bg-admin-bg-surface px-3 text-sm" /></label>
                  </div>
                  <label className="flex items-center gap-2 text-sm text-admin-fg-strong"><input type="checkbox" checked={Boolean(draft.autoPublish)} onChange={(event) => setDraft((prev) => ({ ...prev, autoPublish: event.target.checked }))} /> Publish immediately</label>
                  <Button onClick={handleCreate} loading={saving} disabled={!draft.title || !draft.description || !draft.scheduledStartAt} className="w-full"><CalendarPlus className="h-4 w-4" /> Create class</Button>
                </CardContent>
              </Card>

              <Card>
                <CardHeader><CardTitle>Sessions</CardTitle></CardHeader>
                <CardContent className="overflow-x-auto p-0">
                  <table className="w-full min-w-[760px] text-left text-sm">
                    <thead className="border-b border-admin-border bg-admin-bg-subtle text-xs uppercase text-admin-fg-muted">
                      <tr><th className="px-4 py-3">Class</th><th className="px-4 py-3">Start</th><th className="px-4 py-3">Seats</th><th className="px-4 py-3">Status</th><th className="px-4 py-3 text-right">Actions</th></tr>
                    </thead>
                    <tbody className="divide-y divide-admin-border">
                      {nextSessions.map(({ item, session }) => (
                        <tr key={`${item.id}-${session.id}`} className="align-top">
                          <td className="px-4 py-3"><p className="font-medium text-admin-fg-strong">{item.title}</p><p className="text-xs text-admin-fg-muted">{item.professionTrack} · {item.tutorDisplayName ?? 'Tutor to confirm'}</p></td>
                          <td className="px-4 py-3 text-admin-fg-default">{formatDate(session.scheduledStartAt)}</td>
                          <td className="px-4 py-3 text-admin-fg-default">{session.enrolledCount}/{session.capacity}</td>
                          <td className="px-4 py-3"><div className="flex flex-wrap gap-1"><Badge variant={badgeVariant(item.status)}>{item.status}</Badge><Badge variant={badgeVariant(session.status)}>{session.status}</Badge></div></td>
                          <td className="px-4 py-3">
                            <div className="flex justify-end gap-2">
                              {item.status === 'Draft' ? <Button variant="secondary" size="sm" onClick={() => handlePublish(item.id)}>Publish</Button> : null}
                              {session.status !== 'Cancelled' ? <Button variant="destructive" size="sm" onClick={() => handleCancel(session)}><XCircle className="h-4 w-4" /> Cancel</Button> : null}
                            </div>
                          </td>
                        </tr>
                      ))}
                      {nextSessions.length === 0 ? <tr><td colSpan={5} className="px-4 py-8 text-center text-admin-fg-muted">No live classes have been created yet.</td></tr> : null}
                    </tbody>
                  </table>
                </CardContent>
              </Card>
            </div>
          </div>
        )}
      </AdminPageShell>
    </AdminRouteWorkspace>
  );
}