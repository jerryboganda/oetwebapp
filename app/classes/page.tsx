'use client';

import Link from 'next/link';
import { useEffect, useMemo, useState } from 'react';
import { CalendarDays, Clock, GraduationCap, PlayCircle, RotateCcw, Users, Video } from 'lucide-react';

import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button, buttonClassName } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import {
  enrollLiveClassSession,
  fetchLiveClasses,
  fetchMyUpcomingLiveClasses,
  type LiveClassListItem,
  type LiveClassSessionSummary,
} from '@/lib/api';
import { cn } from '@/lib/utils';

function formatDate(value: string) {
  return new Intl.DateTimeFormat('en-AU', {
    weekday: 'short',
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value));
}

function nextSession(item: LiveClassListItem) {
  return item.sessions.find((session) => session.status !== 'Cancelled') ?? item.sessions[0] ?? null;
}

function seatsLabel(session: LiveClassSessionSummary) {
  const remaining = Math.max(0, session.capacity - session.enrolledCount);
  return remaining === 0 ? 'Waitlist' : `${remaining} seats left`;
}

function isJoinAvailable(session: LiveClassSessionSummary, now: number) {
  return session.isEnrolled
    && new Date(session.scheduledStartAt).getTime() <= now + 30 * 60 * 1000
    && new Date(session.scheduledEndAt).getTime() >= now - 15 * 60 * 1000;
}

export default function LiveClassesPage() {
  const [classes, setClasses] = useState<LiveClassListItem[]>([]);
  const [upcoming, setUpcoming] = useState<LiveClassListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [enrollingSessionId, setEnrollingSessionId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState('All');
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 60_000);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    let cancelled = false;
    Promise.all([fetchLiveClasses(), fetchMyUpcomingLiveClasses()])
      .then(([catalog, upcomingClasses]) => {
        if (cancelled) return;
        setClasses(catalog);
        setUpcoming(upcomingClasses);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load live classes.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const tracks = useMemo(() => ['All', ...Array.from(new Set(classes.map((item) => item.professionTrack).filter(Boolean)))], [classes]);
  const visibleClasses = filter === 'All' ? classes : classes.filter((item) => item.professionTrack === filter || item.professionTrack === 'All');

  async function handleEnroll(sessionId: string) {
    setEnrollingSessionId(sessionId);
    setError(null);
    try {
      await enrollLiveClassSession(sessionId, crypto.randomUUID());
      const [catalog, upcomingClasses] = await Promise.all([fetchLiveClasses(), fetchMyUpcomingLiveClasses()]);
      setClasses(catalog);
      setUpcoming(upcomingClasses);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not enroll in this class.');
    } finally {
      setEnrollingSessionId(null);
    }
  }

  return (
    <LearnerDashboardShell>
      <div className="space-y-8">
        <LearnerPageHero
          title="Live classes"
          description="Join expert-led OET classes, reserve seats with wallet credits, and replay recordings after each session."
          icon={Video}
        />

        {error ? (
          <InlineAlert variant="warning" className="flex items-center justify-between gap-3">
            <span>{error}</span>
            <Button type="button" variant="ghost" size="sm" onClick={() => setError(null)}>
              Dismiss
            </Button>
          </InlineAlert>
        ) : null}

        {loading ? (
          <div className="grid gap-4 lg:grid-cols-3">
            <Skeleton className="h-44 rounded-xl" />
            <Skeleton className="h-44 rounded-xl" />
            <Skeleton className="h-44 rounded-xl" />
          </div>
        ) : (
          <>
            <section className="rounded-2xl border border-border bg-surface p-4 shadow-sm">
              <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
                <LearnerSurfaceSectionHeader
                  eyebrow="Your schedule"
                  title="Upcoming reservations"
                  description="Class links open 30 minutes before start time."
                />
                <div className="flex flex-wrap gap-2">
                  {tracks.map((track) => (
                    <button
                      key={track}
                      type="button"
                      onClick={() => setFilter(track)}
                      className={cn(
                        'min-h-10 rounded-full border px-4 text-sm font-medium transition-colors',
                        filter === track
                          ? 'border-primary bg-primary text-white dark:bg-violet-700'
                          : 'border-border bg-background text-muted hover:border-primary/50 hover:text-navy',
                      )}
                    >
                      {track}
                    </button>
                  ))}
                </div>
              </div>

              <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-3">
                {upcoming.length === 0 ? (
                  <div className="rounded-xl border border-dashed border-border bg-background-light p-4 text-sm text-muted md:col-span-2 xl:col-span-3">
                    No live classes reserved yet.
                  </div>
                ) : upcoming.slice(0, 3).map((item) => {
                  const session = nextSession(item);
                  if (!session) return null;
                  const canJoin = isJoinAvailable(session, now);
                  return (
                    <Link key={`${item.id}-${session.id}`} href={`/classes/${item.slug}`} className="rounded-xl border border-border bg-background p-4 transition-colors hover:border-primary/50">
                      <div className="flex items-start justify-between gap-3">
                        <div>
                          <p className="text-sm font-semibold text-navy">{item.title}</p>
                          <p className="mt-1 flex items-center gap-1 text-xs text-muted"><CalendarDays className="h-3.5 w-3.5" /> {formatDate(session.scheduledStartAt)}</p>
                        </div>
                        <Badge variant={canJoin ? 'success' : 'info'}>{canJoin ? 'Join now' : session.status}</Badge>
                      </div>
                    </Link>
                  );
                })}
              </div>
            </section>

            <section className="space-y-4">
              <LearnerSurfaceSectionHeader
                eyebrow="Catalog"
                title="Reserve a live class"
                description="Credits are charged at reservation. Cancel more than 24 hours before the class for a full credit refund."
              />

              <div className="grid gap-4 xl:grid-cols-2">
                {visibleClasses.length === 0 ? (
                  <div className="rounded-2xl border border-dashed border-border bg-surface p-6 text-sm text-muted">
                    No classes match this filter.
                  </div>
                ) : visibleClasses.map((item) => {
                  const session = nextSession(item);
                  const canJoin = session ? isJoinAvailable(session, now) : false;
                  return (
                    <article key={item.id} className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
                      <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
                        <div className="min-w-0 space-y-2">
                          <div className="flex flex-wrap items-center gap-2">
                            <Badge variant="info">{item.type}</Badge>
                            <Badge variant="default">{item.professionTrack}</Badge>
                            <span className="text-xs font-medium text-muted">{item.level}</span>
                          </div>
                          <h2 className="text-xl font-semibold tracking-normal text-navy">{item.title}</h2>
                          <p className="line-clamp-2 text-sm leading-6 text-muted">{item.description}</p>
                        </div>
                        <div className="rounded-xl border border-border bg-background px-3 py-2 text-center">
                          <p className="text-2xl font-semibold text-navy">{item.creditCost}</p>
                          <p className="text-xs text-muted">credits</p>
                        </div>
                      </div>

                      {session ? (
                        <div className="mt-5 grid gap-3 rounded-xl bg-background-light p-4 text-sm md:grid-cols-3">
                          <div className="flex items-center gap-2 text-muted"><Clock className="h-4 w-4" /> {formatDate(session.scheduledStartAt)}</div>
                          <div className="flex items-center gap-2 text-muted"><Users className="h-4 w-4" /> {seatsLabel(session)}</div>
                          <div className="flex items-center gap-2 text-muted"><GraduationCap className="h-4 w-4" /> {item.tutorDisplayName ?? 'Tutor to confirm'}</div>
                        </div>
                      ) : null}

                      <div className="mt-5 flex flex-col gap-2 sm:flex-row sm:justify-end">
                        <Link href={`/classes/${item.slug}`} className={buttonClassName({ variant: 'outline', size: 'sm' })}>
                          Details
                        </Link>
                        {session?.isEnrolled ? (
                          canJoin ? (
                            <Link href={`/classes/${item.slug}/sessions/${session.id}/join`} className={buttonClassName({ variant: 'primary', size: 'sm' })}>
                              <PlayCircle className="h-4 w-4" /> Join class
                            </Link>
                          ) : (
                            <Button type="button" variant="secondary" size="sm" disabled>
                              <PlayCircle className="h-4 w-4" /> Opens 30m before
                            </Button>
                          )
                        ) : session ? (
                          <Button type="button" size="sm" onClick={() => handleEnroll(session.id)} loading={enrollingSessionId === session.id}>
                            <RotateCcw className="h-4 w-4" /> Reserve seat
                          </Button>
                        ) : null}
                      </div>
                    </article>
                  );
                })}
              </div>
            </section>
          </>
        )}
      </div>
    </LearnerDashboardShell>
  );
}