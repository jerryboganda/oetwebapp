'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import { BarChart3, Clock, FileText, GraduationCap, PlayCircle, Users, Video, Wallet } from 'lucide-react';

import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, cardClassName } from '@/components/ui/card';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
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
      <div className="space-y-4" role="status" aria-busy="true" aria-label="Loading"><Skeleton aria-hidden className="h-24 rounded-2xl" /><Skeleton aria-hidden className="h-72 rounded-2xl" /></div>
    );
  }

  if (!detail) {
    return <InlineAlert variant="warning">Live class not found.</InlineAlert>;
  }

  return (
    <>
      {/* Track, level, tutor and cost are the class's key facts, so they ride in the hero. */}
      <LearnerPageHero
        title={detail.title}
        description={detail.description}
        icon={Video}
        highlights={[
          { icon: Users, label: 'Track', value: detail.professionTrack },
          { icon: BarChart3, label: 'Level', value: detail.level },
          { icon: GraduationCap, label: 'Tutor', value: detail.tutorDisplayName ?? 'To confirm' },
          { icon: Wallet, label: 'Cost', value: `${detail.creditCost} wallet credits` },
        ]}
      />

      {error ? (
        <InlineAlert
          variant="warning"
          action={(
            <Button type="button" variant="ghost" size="sm" onClick={() => setError(null)}>
              Dismiss
            </Button>
          )}
        >
          {error}
        </InlineAlert>
      ) : null}

      <section className="space-y-4">
        <LearnerSurfaceSectionHeader eyebrow="Schedule" title="Sessions" description="Reserve, join, cancel, or open class recordings from one place." />
        <div className="space-y-3">
          {detail.sessions.map((session, index) => {
            const canJoin = isJoinAvailable(session, now);
            return (
              <MotionItem key={session.id} delayIndex={Math.min(index, 5)}>
                <article className={cardClassName({ padding: 'lg' })}>
                  <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
                    <div className="min-w-0 space-y-2">
                      <div className="flex flex-wrap items-center gap-2">
                        <Badge variant={statusVariant(session.status)}>{session.status}</Badge>
                        {session.isEnrolled ? <Badge variant="success">Reserved</Badge> : null}
                      </div>
                      <h3 className="text-lg font-semibold text-navy">{formatDate(session.scheduledStartAt)}</h3>
                      <div className="flex flex-wrap gap-x-4 gap-y-1 text-sm text-muted tabular-nums">
                        <span className="flex items-center gap-1"><Clock className="h-4 w-4 shrink-0" aria-hidden="true" /> Ends {formatDate(session.scheduledEndAt)}</span>
                        <span className="flex items-center gap-1"><Users className="h-4 w-4 shrink-0" aria-hidden="true" /> {session.enrolledCount}/{session.capacity} enrolled</span>
                        <span className="flex items-center gap-1"><Wallet className="h-4 w-4 shrink-0" aria-hidden="true" /> {session.creditCost} credits</span>
                      </div>
                    </div>
                    <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap">
                      {session.isEnrolled ? (
                        <>
                          {canJoin ? (
                            <Button asChild size="sm">
                              <Link href={`/classes/${detail.slug}/sessions/${session.id}/join`}>
                                <PlayCircle className="h-4 w-4" aria-hidden="true" /> Join class
                              </Link>
                            </Button>
                          ) : (
                            <Button type="button" variant="secondary" size="sm" disabled>
                              <PlayCircle className="h-4 w-4" aria-hidden="true" /> Opens 30m before
                            </Button>
                          )}
                          <Button type="button" variant="outline" size="sm" onClick={() => loadRecording(session.id)}>
                            <FileText className="h-4 w-4" aria-hidden="true" /> Recording
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
              </MotionItem>
            );
          })}
        </div>
      </section>

      {recording ? (
        <MotionSection>
          <Card padding="lg">
            <LearnerSurfaceSectionHeader eyebrow="Replay" title="Recording notes" description={recording.status === 'Ready' ? 'Summary and transcript are ready.' : 'Recording is queued for processing.'} />
            <div className="mt-4 space-y-3 text-sm text-muted">
              {recording.aiSummary ? <p className="max-w-3xl rounded-xl bg-background-light p-4 text-navy">{recording.aiSummary}</p> : null}
              {recording.transcriptText ? <p className="max-h-64 overflow-auto whitespace-pre-wrap break-words rounded-xl bg-background-light p-4">{recording.transcriptText}</p> : null}
              {!recording.aiSummary && !recording.transcriptText ? <p>Recording status: {recording.status}</p> : null}
            </div>
          </Card>
        </MotionSection>
      ) : null}
    </>
  );
}