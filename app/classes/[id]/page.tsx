'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import { CalendarDays, Clock, FileText, PlayCircle, Users, Video, X } from 'lucide-react';

import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button, buttonClassName } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import {
  cancelLiveClassEnrollment,
  enrollLiveClassSession,
  fetchLiveClassDetail,
  fetchLiveClassRecording,
  type LiveClassDetail,
  type LiveClassRecording,
  type LiveClassSessionSummary,
} from '@/lib/api';

function formatDate(value: string) {
  return new Intl.DateTimeFormat('en-AU', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value));
}

function statusVariant(status: string): 'success' | 'warning' | 'danger' | 'info' | 'muted' {
  if (status === 'Completed') return 'success';
  if (status === 'Cancelled') return 'danger';
  if (status === 'Live') return 'warning';
  if (status === 'Scheduled') return 'info';
  return 'muted';
}

function isJoinAvailable(session: LiveClassSessionSummary, now: number) {
  return session.isEnrolled
    && new Date(session.scheduledStartAt).getTime() <= now + 30 * 60 * 1000
    && new Date(session.scheduledEndAt).getTime() >= now - 15 * 60 * 1000;
}

export default function LiveClassDetailPage() {
  const params = useParams();
  const id = typeof params?.id === 'string' ? params.id : null;
  const [detail, setDetail] = useState<LiveClassDetail | null>(null);
  const [recording, setRecording] = useState<LiveClassRecording | null>(null);
  const [busySessionId, setBusySessionId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 60_000);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    if (!id) return;
    let cancelled = false;
    fetchLiveClassDetail(id)
      .then((data) => {
        if (!cancelled) setDetail(data);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load this class.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [id]);

  async function refresh() {
    if (!id) return;
    setDetail(await fetchLiveClassDetail(id));
  }

  async function handleEnroll(session: LiveClassSessionSummary) {
    setBusySessionId(session.id);
    setError(null);
    try {
      await enrollLiveClassSession(session.id, crypto.randomUUID());
      await refresh();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not reserve this seat.');
    } finally {
      setBusySessionId(null);
    }
  }

  async function handleCancel(session: LiveClassSessionSummary) {
    setBusySessionId(session.id);
    setError(null);
    try {
      await cancelLiveClassEnrollment(session.id, 'Learner cancelled from class detail page.');
      await refresh();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not cancel this reservation.');
    } finally {
      setBusySessionId(null);
    }
  }

  async function loadRecording(sessionId: string) {
    setError(null);
    try {
      setRecording(await fetchLiveClassRecording(sessionId));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Recording is not available yet.');
    }
  }

  if (loading) {
    return (
      <LearnerDashboardShell>
        <div className="space-y-4"><Skeleton className="h-24 rounded-xl" /><Skeleton className="h-72 rounded-xl" /></div>
      </LearnerDashboardShell>
    );
  }

  if (!detail) {
    return (
      <LearnerDashboardShell>
        <InlineAlert variant="warning">Live class not found.</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <div className="space-y-8">
        <LearnerPageHero title={detail.title} description={detail.description} icon={Video} />

        {error ? (
          <InlineAlert variant="warning" className="flex items-center justify-between gap-3">
            <span>{error}</span>
            <button type="button" onClick={() => setError(null)} className="rounded-full p-1 hover:bg-background-light"><X className="h-4 w-4" /></button>
          </InlineAlert>
        ) : null}

        <section className="grid gap-4 rounded-2xl border border-border bg-surface p-5 shadow-sm md:grid-cols-4">
          <div><p className="text-xs font-semibold uppercase text-muted">Track</p><p className="mt-1 text-sm font-semibold text-navy">{detail.professionTrack}</p></div>
          <div><p className="text-xs font-semibold uppercase text-muted">Level</p><p className="mt-1 text-sm font-semibold text-navy">{detail.level}</p></div>
          <div><p className="text-xs font-semibold uppercase text-muted">Tutor</p><p className="mt-1 text-sm font-semibold text-navy">{detail.tutorDisplayName ?? 'To confirm'}</p></div>
          <div><p className="text-xs font-semibold uppercase text-muted">Cost</p><p className="mt-1 text-sm font-semibold text-navy">{detail.creditCost} wallet credits</p></div>
        </section>

        <section className="space-y-4">
          <LearnerSurfaceSectionHeader eyebrow="Schedule" title="Sessions" description="Reserve, join, cancel, or open class recordings from one place." />
          <div className="space-y-3">
            {detail.sessions.map((session) => {
              const canJoin = isJoinAvailable(session, now);
              return (
              <article key={session.id} className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
                <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
                  <div className="space-y-2">
                    <div className="flex flex-wrap items-center gap-2">
                      <Badge variant={statusVariant(session.status)}>{session.status}</Badge>
                      {session.isEnrolled ? <Badge variant="success">Reserved</Badge> : null}
                    </div>
                    <p className="text-lg font-semibold text-navy">{formatDate(session.scheduledStartAt)}</p>
                    <div className="flex flex-wrap gap-4 text-sm text-muted">
                      <span className="flex items-center gap-1"><Clock className="h-4 w-4" /> Ends {formatDate(session.scheduledEndAt)}</span>
                      <span className="flex items-center gap-1"><Users className="h-4 w-4" /> {session.enrolledCount}/{session.capacity} enrolled</span>
                      <span className="flex items-center gap-1"><CalendarDays className="h-4 w-4" /> {session.creditCost} credits</span>
                    </div>
                  </div>
                  <div className="flex flex-col gap-2 sm:flex-row">
                    {session.isEnrolled ? (
                      <>
                        {canJoin ? (
                          <Link href={`/classes/${detail.slug}/sessions/${session.id}/join`} className={buttonClassName({ variant: 'primary', size: 'sm' })}>
                            <PlayCircle className="h-4 w-4" /> Join class
                          </Link>
                        ) : (
                          <Button type="button" variant="secondary" size="sm" disabled>
                            <PlayCircle className="h-4 w-4" /> Opens 30m before
                          </Button>
                        )}
                        <Button type="button" variant="outline" size="sm" onClick={() => loadRecording(session.id)}>
                          <FileText className="h-4 w-4" /> Recording
                        </Button>
                        <Button type="button" variant="ghost" size="sm" loading={busySessionId === session.id} onClick={() => handleCancel(session)}>
                          Cancel
                        </Button>
                      </>
                    ) : (
                      <Button type="button" size="sm" loading={busySessionId === session.id} onClick={() => handleEnroll(session)}>
                        Reserve seat
                      </Button>
                    )}
                  </div>
                </div>
              </article>
              );
            })}
          </div>
        </section>

        {recording ? (
          <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <LearnerSurfaceSectionHeader eyebrow="Replay" title="Recording notes" description={recording.status === 'Ready' ? 'Summary and transcript are ready.' : 'Recording is queued for processing.'} />
            <div className="mt-4 space-y-3 text-sm text-muted">
              {recording.aiSummary ? <p className="rounded-xl bg-background-light p-4 text-navy">{recording.aiSummary}</p> : null}
              {recording.transcriptText ? <p className="max-h-64 overflow-auto rounded-xl bg-background-light p-4 whitespace-pre-wrap">{recording.transcriptText}</p> : null}
              {!recording.aiSummary && !recording.transcriptText ? <p>Recording status: {recording.status}</p> : null}
            </div>
          </section>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}