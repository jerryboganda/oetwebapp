'use client';

/* Hallmark · macrostructure: Workbench · tone: utilitarian · anchor hue: blue
 * pre-emit critique: P4 H4 E5 S4 R4 V4
 * Admin-only: follows Hallmark admin token system.
 */

import { use, useCallback, useEffect, useState } from 'react';
import {
  AlertCircle,
  CalendarPlus,
  CheckCircle2,
  ChevronDown,
  ChevronUp,
  Loader2,
  Radio,
  RefreshCw,
  Video,
  XCircle,
} from 'lucide-react';

import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { PageHeader } from '@/components/admin/ui/page-header';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/admin/ui/card';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Skeleton } from '@/components/admin/ui/skeleton';
import {
  Dialog,
  DialogContent,
  DialogHeader as DlgHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
  DialogClose,
} from '@/components/admin/ui/dialog';
import { AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  fetchAdminLiveClassDetail,
  publishAdminLiveClass,
  cancelAdminLiveClassSession,
  addAdminLiveClassSession,
  retryAdminLiveClassSessionZoom,
  type LiveClassDetail,
  type LiveClassSessionSummary,
} from '@/lib/api';

/* ─── helpers ──────────────────────────────────────────────────────────── */

function fmtDateTime(iso: string) {
  return new Intl.DateTimeFormat('en-AU', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    timeZone: 'UTC',
    timeZoneName: 'short',
  }).format(new Date(iso));
}

function toLocalInput(date: Date) {
  const copy = new Date(date.getTime() - date.getTimezoneOffset() * 60_000);
  return copy.toISOString().slice(0, 16);
}

type StatusTone = 'success' | 'info' | 'warning' | 'danger' | 'default';

function classBadge(status: string): StatusTone {
  if (status === 'Published') return 'success';
  if (status === 'Draft') return 'info';
  if (status === 'Archived') return 'default';
  return 'default';
}

function sessionBadge(status: string): StatusTone {
  if (status === 'Scheduled') return 'info';
  if (status === 'Live') return 'warning';
  if (status === 'Completed') return 'success';
  if (status === 'Cancelled' || status === 'NoShow') return 'danger';
  return 'default';
}

/* ─── sub-components ───────────────────────────────────────────────────── */

function ZoomCell({ session }: { session: LiveClassSessionSummary }) {
  if (session.zoomMeetingId) {
    return <span className="font-mono text-xs text-admin-fg-default">{session.zoomMeetingId}</span>;
  }
  return (
    <Badge variant="warning" size="sm">
      Not provisioned
    </Badge>
  );
}

/* ─── Cancel modal ─────────────────────────────────────────────────────── */

type CancelModalProps = {
  session: LiveClassSessionSummary | null;
  onClose: () => void;
  onConfirm: (sessionId: string, reason: string) => Promise<void>;
};

function CancelSessionModal({ session, onClose, onConfirm }: CancelModalProps) {
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);

  async function handleConfirm() {
    if (!session) return;
    setBusy(true);
    try {
      await onConfirm(session.id, reason);
      setReason('');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog open={!!session} onOpenChange={(open) => { if (!open) { onClose(); setReason(''); } }}>
      <DialogContent size="md">
        <DlgHeader>
          <DialogTitle>Cancel session</DialogTitle>
          <DialogDescription>
            {session ? `${fmtDateTime(session.scheduledStartAt)} — enrolled: ${session.enrolledCount}` : ''}
          </DialogDescription>
        </DlgHeader>

        <div className="space-y-2">
          <label className="block text-xs font-semibold text-admin-fg-muted">
            Reason (optional)
          </label>
          <textarea
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            rows={3}
            placeholder="Tutor unavailable / technical issues / …"
            className="w-full rounded-admin-md border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-strong focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)] focus-visible:ring-offset-1 resize-none"
          />
        </div>

        <DialogFooter>
          <DialogClose asChild>
            <Button variant="secondary" size="sm">Keep session</Button>
          </DialogClose>
          <Button
            variant="destructive"
            size="sm"
            onClick={handleConfirm}
            disabled={busy}
            loading={busy}
          >
            <XCircle className="h-4 w-4" />
            Cancel session
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/* ─── Add session modal ─────────────────────────────────────────────────── */

type AddSessionModalProps = {
  open: boolean;
  defaultDuration: number;
  defaultCapacity: number;
  onClose: () => void;
  onSubmit: (payload: { scheduledStartAt: string; durationMinutes?: number; capacity?: number }) => Promise<void>;
};

function AddSessionModal({ open, defaultDuration, defaultCapacity, onClose, onSubmit }: AddSessionModalProps) {
  const [startAt, setStartAt] = useState(() => toLocalInput(new Date(Date.now() + 24 * 60 * 60 * 1000)));
  const [duration, setDuration] = useState(defaultDuration);
  const [capacity, setCapacity] = useState(defaultCapacity);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  async function handleSubmit() {
    if (!startAt) return;
    setBusy(true);
    setErr(null);
    try {
      await onSubmit({
        scheduledStartAt: new Date(startAt).toISOString(),
        durationMinutes: duration,
        capacity,
      });
      onClose();
    } catch (e: unknown) {
      setErr(e instanceof Error ? e.message : 'Could not add session.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) onClose(); }}>
      <DialogContent size="md">
        <DlgHeader>
          <DialogTitle>Add session</DialogTitle>
          <DialogDescription>Schedule a new Zoom session for this class.</DialogDescription>
        </DlgHeader>

        {err ? <InlineAlert variant="warning">{err}</InlineAlert> : null}

        <div className="space-y-3">
          <label className="block text-xs font-semibold text-admin-fg-muted">
            Scheduled start
            <input
              type="datetime-local"
              value={startAt}
              onChange={(e) => setStartAt(e.target.value)}
              className="mt-1 h-10 w-full rounded-admin-md border border-admin-border bg-admin-bg-surface px-3 text-sm text-admin-fg-strong focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)]"
            />
          </label>

          <div className="grid grid-cols-2 gap-3">
            <label className="block text-xs font-semibold text-admin-fg-muted">
              Duration (min)
              <input
                type="number"
                min={15}
                max={360}
                value={duration}
                onChange={(e) => setDuration(Number(e.target.value))}
                className="mt-1 h-10 w-full rounded-admin-md border border-admin-border bg-admin-bg-surface px-3 text-sm text-admin-fg-strong focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)]"
              />
            </label>
            <label className="block text-xs font-semibold text-admin-fg-muted">
              Capacity
              <input
                type="number"
                min={1}
                value={capacity}
                onChange={(e) => setCapacity(Number(e.target.value))}
                className="mt-1 h-10 w-full rounded-admin-md border border-admin-border bg-admin-bg-surface px-3 text-sm text-admin-fg-strong focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)]"
              />
            </label>
          </div>
        </div>

        <DialogFooter>
          <DialogClose asChild>
            <Button variant="secondary" size="sm">Cancel</Button>
          </DialogClose>
          <Button
            variant="primary"
            size="sm"
            onClick={handleSubmit}
            disabled={busy || !startAt}
            loading={busy}
          >
            <CalendarPlus className="h-4 w-4" />
            Add session
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/* ─── Page ─────────────────────────────────────────────────────────────── */

export default function AdminLiveClassDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  useAdminAuth();

  const [detail, setDetail] = useState<LiveClassDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // cancel modal
  const [cancelTarget, setCancelTarget] = useState<LiveClassSessionSummary | null>(null);

  // add session modal
  const [addOpen, setAddOpen] = useState(false);

  // inline retry states per session
  const [retrying, setRetrying] = useState<Record<string, boolean>>({});

  // publish busy
  const [publishing, setPublishing] = useState(false);

  // Arabic description toggle
  const [showAr, setShowAr] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await fetchAdminLiveClassDetail(id);
      setDetail(data);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Could not load class detail.');
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => { void load(); }, [load]);

  async function handlePublish() {
    if (!detail) return;
    setPublishing(true);
    try {
      await publishAdminLiveClass(detail.id);
      await load();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Could not publish class.');
    } finally {
      setPublishing(false);
    }
  }

  async function handleCancelConfirm(sessionId: string, reason: string) {
    await cancelAdminLiveClassSession(sessionId, reason || undefined);
    setCancelTarget(null);
    await load();
  }

  async function handleRetryZoom(sessionId: string) {
    setRetrying((prev) => ({ ...prev, [sessionId]: true }));
    try {
      await retryAdminLiveClassSessionZoom(sessionId);
      await load();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Zoom retry failed.');
    } finally {
      setRetrying((prev) => ({ ...prev, [sessionId]: false }));
    }
  }

  async function handleAddSession(payload: { scheduledStartAt: string; durationMinutes?: number; capacity?: number }) {
    await addAdminLiveClassSession(id, payload);
    await load();
  }

  if (loading) {
    return (
      <AdminRouteWorkspace role="main" aria-label="Live class detail loading">
        <AdminPageShell>
          <PageHeader loading title="Loading…" />
          <div className="space-y-4">
            <Skeleton className="h-24 rounded-admin-lg" />
            <Skeleton className="h-72 rounded-admin-lg" />
          </div>
        </AdminPageShell>
      </AdminRouteWorkspace>
    );
  }

  if (error && !detail) {
    return (
      <AdminRouteWorkspace role="main" aria-label="Live class detail error">
        <AdminPageShell>
          <PageHeader
            title="Class detail"
            backHref="/admin/live-classes"
            breadcrumbs={[
              { label: 'Admin', href: '/admin' },
              { label: 'Live classes', href: '/admin/live-classes' },
              { label: 'Error' },
            ]}
          />
          <InlineAlert variant="warning">{error}</InlineAlert>
        </AdminPageShell>
      </AdminRouteWorkspace>
    );
  }

  if (!detail) return null;

  const sessions = detail.sessions ?? [];
  const isDraft = detail.status === 'Draft';
  const upcomingCount = sessions.filter((s) => s.status === 'Scheduled' || s.status === 'Live').length;
  const enrolledTotal = sessions.reduce((acc, s) => acc + s.enrolledCount, 0);

  return (
    <>
      <AdminRouteWorkspace role="main" aria-label={`Live class: ${detail.title}`}>
        <AdminPageShell>
          <PageHeader
            title={detail.title}
            backHref="/admin/live-classes"
            breadcrumbs={[
              { label: 'Admin', href: '/admin' },
              { label: 'Live classes', href: '/admin/live-classes' },
              { label: detail.title },
            ]}
            icon={<Video className="h-5 w-5" />}
            actions={
              <div className="flex items-center gap-2 flex-wrap">
                {isDraft ? (
                  <Button
                    variant="primary"
                    size="sm"
                    onClick={handlePublish}
                    disabled={publishing}
                    loading={publishing}
                  >
                    <CheckCircle2 className="h-4 w-4" />
                    Publish class
                  </Button>
                ) : null}
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => void load()}
                >
                  <RefreshCw className="h-4 w-4" />
                  Refresh
                </Button>
              </div>
            }
          />

          {error ? <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert> : null}

          {/* ── Header meta strip ── */}
          <div className="flex flex-wrap items-center gap-2 mb-5">
            <Badge variant={classBadge(detail.status)}>{detail.status}</Badge>
            <Badge variant="default">{detail.type}</Badge>
            <Badge variant="default">{detail.professionTrack}</Badge>
            <Badge variant="default">{detail.level}</Badge>
            {detail.tags?.map((tag) => (
              <Badge key={tag} variant="muted" size="sm">{tag}</Badge>
            ))}
          </div>

          {/* ── KPI row ── */}
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4 mb-5">
            <div className="rounded-admin-lg border border-admin-border bg-admin-bg-surface p-4">
              <p className="text-xs text-admin-fg-muted mb-1">Sessions</p>
              <p className="text-2xl font-bold text-admin-fg-strong tabular-nums">{sessions.length}</p>
            </div>
            <div className="rounded-admin-lg border border-admin-border bg-admin-bg-surface p-4">
              <p className="text-xs text-admin-fg-muted mb-1">Upcoming</p>
              <p className="text-2xl font-bold text-admin-fg-strong tabular-nums">{upcomingCount}</p>
            </div>
            <div className="rounded-admin-lg border border-admin-border bg-admin-bg-surface p-4">
              <p className="text-xs text-admin-fg-muted mb-1">Total enrolled</p>
              <p className="text-2xl font-bold text-admin-fg-strong tabular-nums">{enrolledTotal}</p>
            </div>
            <div className="rounded-admin-lg border border-admin-border bg-admin-bg-surface p-4">
              <p className="text-xs text-admin-fg-muted mb-1">Credit cost</p>
              <p className="text-2xl font-bold text-admin-fg-strong tabular-nums">{detail.creditCost}</p>
            </div>
          </div>

          {/* ── Info + sessions grid ── */}
          <div className="grid gap-5 xl:grid-cols-[320px_1fr]">
            {/* Class info card */}
            <Card>
              <CardHeader>
                <CardTitle>Class info</CardTitle>
                {detail.tutorDisplayName ? (
                  <CardDescription>Tutor: {detail.tutorDisplayName}</CardDescription>
                ) : (
                  <CardDescription className="text-admin-fg-muted/70 italic">Tutor: to confirm</CardDescription>
                )}
              </CardHeader>
              <CardContent className="space-y-4">
                <div>
                  <p className="text-xs font-semibold text-admin-fg-muted mb-1">Description (EN)</p>
                  <p className="text-sm text-admin-fg-default leading-relaxed">{detail.description}</p>
                </div>

                {detail.descriptionAr ? (
                  <div>
                    <button
                      type="button"
                      onClick={() => setShowAr((v) => !v)}
                      className="flex items-center gap-1 text-xs font-semibold text-[var(--admin-primary)] hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)] rounded-admin-sm"
                    >
                      {showAr ? <ChevronUp className="h-3.5 w-3.5" /> : <ChevronDown className="h-3.5 w-3.5" />}
                      {showAr ? 'Hide Arabic' : 'Show Arabic'}
                    </button>
                    {showAr ? (
                      <p className="mt-2 text-sm text-admin-fg-default leading-relaxed" dir="rtl">
                        {detail.descriptionAr}
                      </p>
                    ) : null}
                  </div>
                ) : null}

                <div className="border-t border-admin-border pt-4 grid grid-cols-2 gap-3 text-sm">
                  <div>
                    <p className="text-xs text-admin-fg-muted mb-0.5">Default duration</p>
                    <p className="font-medium text-admin-fg-strong">{detail.defaultDurationMinutes} min</p>
                  </div>
                  <div>
                    <p className="text-xs text-admin-fg-muted mb-0.5">Default capacity</p>
                    <p className="font-medium text-admin-fg-strong">{detail.defaultCapacity}</p>
                  </div>
                  <div>
                    <p className="text-xs text-admin-fg-muted mb-0.5">Track</p>
                    <p className="font-medium text-admin-fg-strong">{detail.professionTrack}</p>
                  </div>
                  <div>
                    <p className="text-xs text-admin-fg-muted mb-0.5">Level</p>
                    <p className="font-medium text-admin-fg-strong">{detail.level}</p>
                  </div>
                </div>

                {detail.coverImageUrl ? (
                  <div className="border-t border-admin-border pt-4">
                    <p className="text-xs text-admin-fg-muted mb-2">Cover image</p>
                    {/* eslint-disable-next-line @next/next/no-img-element */}
                    <img
                      src={detail.coverImageUrl}
                      alt="Class cover"
                      className="rounded-admin-md w-full object-cover aspect-video border border-admin-border"
                    />
                  </div>
                ) : null}
              </CardContent>
            </Card>

            {/* Sessions card */}
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2">
                  <Radio className="h-4 w-4 text-[var(--admin-primary)]" aria-hidden />
                  Sessions
                </CardTitle>
                <div className="ml-auto">
                  <Button
                    variant="primary"
                    size="sm"
                    onClick={() => setAddOpen(true)}
                  >
                    <CalendarPlus className="h-4 w-4" />
                    Add session
                  </Button>
                </div>
              </CardHeader>
              <CardContent className="overflow-x-auto p-0">
                <table className="w-full min-w-[820px] text-left text-sm">
                  <thead className="border-b border-admin-border bg-admin-bg-subtle text-xs uppercase tracking-wide text-admin-fg-muted">
                    <tr>
                      <th className="px-4 py-3">Date / Time</th>
                      <th className="px-4 py-3">Status</th>
                      <th className="px-4 py-3">Seats</th>
                      <th className="px-4 py-3">Zoom meeting</th>
                      <th className="px-4 py-3">Zoom error</th>
                      <th className="px-4 py-3 text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-admin-border">
                    {sessions.length === 0 ? (
                      <tr>
                        <td colSpan={6} className="px-4 py-10 text-center text-admin-fg-muted">
                          No sessions yet — add the first one.
                        </td>
                      </tr>
                    ) : null}
                    {sessions.map((session) => {
                      const isLive = session.status === 'Live';
                      const isCancelled = session.status === 'Cancelled';
                      const hasZoomError = Boolean(session.zoomError);
                      const isRetrying = retrying[session.id];

                      return (
                        <tr
                          key={session.id}
                          className="align-top"
                        >
                          {/* Date/time */}
                          <td className="px-4 py-3">
                            <p className="font-medium text-admin-fg-strong">{fmtDateTime(session.scheduledStartAt)}</p>
                            <p className="text-xs text-admin-fg-muted mt-0.5">
                              {session.scheduledStartAt !== session.scheduledEndAt
                                ? `→ ${fmtDateTime(session.scheduledEndAt)}`
                                : null}
                            </p>
                          </td>

                          {/* Status */}
                          <td className="px-4 py-3">
                            <Badge
                              variant={sessionBadge(session.status)}
                              startIcon={isLive ? (
                                <span className="inline-block h-1.5 w-1.5 rounded-full bg-current animate-pulse" aria-hidden />
                              ) : undefined}
                            >
                              {session.status}
                            </Badge>
                          </td>

                          {/* Seats */}
                          <td className="px-4 py-3 tabular-nums text-admin-fg-default">
                            {session.enrolledCount} / {session.capacity}
                          </td>

                          {/* Zoom meeting */}
                          <td className="px-4 py-3">
                            <ZoomCell session={session} />
                          </td>

                          {/* Zoom error */}
                          <td className="px-4 py-3 max-w-[200px]">
                            {hasZoomError ? (
                              <div className="space-y-1.5">
                                <p className="text-xs text-[var(--admin-danger)] line-clamp-2" title={session.zoomError ?? undefined}>
                                  <AlertCircle className="inline h-3.5 w-3.5 mr-1 shrink-0 align-text-bottom" aria-hidden />
                                  {session.zoomError}
                                </p>
                                <Button
                                  variant="outline"
                                  size="sm"
                                  onClick={() => void handleRetryZoom(session.id)}
                                  disabled={isRetrying || isCancelled}
                                >
                                  {isRetrying ? (
                                    <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden />
                                  ) : (
                                    <RefreshCw className="h-3.5 w-3.5" aria-hidden />
                                  )}
                                  Retry Zoom
                                </Button>
                              </div>
                            ) : (
                              <span className="text-xs text-admin-fg-muted">—</span>
                            )}
                          </td>

                          {/* Actions */}
                          <td className="px-4 py-3">
                            <div className="flex justify-end gap-2">
                              {!isCancelled ? (
                                <Button
                                  variant="destructive"
                                  size="sm"
                                  onClick={() => setCancelTarget(session)}
                                >
                                  <XCircle className="h-4 w-4" />
                                  Cancel
                                </Button>
                              ) : (
                                <span className="text-xs text-admin-fg-muted italic">Cancelled</span>
                              )}
                            </div>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </CardContent>
            </Card>
          </div>
        </AdminPageShell>
      </AdminRouteWorkspace>

      {/* Modals rendered outside the scroll container */}
      <CancelSessionModal
        session={cancelTarget}
        onClose={() => setCancelTarget(null)}
        onConfirm={handleCancelConfirm}
      />
      <AddSessionModal
        open={addOpen}
        defaultDuration={detail.defaultDurationMinutes}
        defaultCapacity={detail.defaultCapacity}
        onClose={() => setAddOpen(false)}
        onSubmit={handleAddSession}
      />
    </>
  );
}
