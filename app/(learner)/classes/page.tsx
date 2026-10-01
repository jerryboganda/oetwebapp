'use client';

import Link from 'next/link';
import { useEffect, useMemo, useState } from 'react';
import { CalendarDays, Clock, GraduationCap, PlayCircle, RotateCcw, Users, Video } from 'lucide-react';

import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { cardClassName } from '@/components/ui/card';
import { CardLink } from '@/components/ui/card-link';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem } from '@/components/ui/motion-primitives';
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
    <>
      <LearnerPageHero
        title="Live classes"
        description="Join expert-led OET classes, reserve seats with wallet credits, and replay recordings after each session."
        icon={Video}
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

      {loading ? (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-3" role="status" aria-busy="true" aria-label="Loading">
          <Skeleton aria-hidden className="h-44 rounded-2xl" />
          <Skeleton aria-hidden className="h-44 rounded-2xl" />
          <Skeleton aria-hidden className="h-44 rounded-2xl" />
        </div>
      ) : (
        <>
          <section className="space-y-4">
            <LearnerSurfaceSectionHeader
              eyebrow="Your schedule"
              title="Upcoming reservations"
              description="Class links open 30 minutes before start time."
            />

            {upcoming.length === 0 ? (
              <EmptyState className="py-8" icon={<CalendarDays className="h-8 w-8" />} title="No live classes reserved yet." />
            ) : (
              <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
                {upcoming.slice(0, 3).map((item, index) => {
                  const session = nextSession(item);
                  if (!session) return null;
                  const canJoin = isJoinAvailable(session, now);
                  return (
                    <MotionItem key={`${item.id}-${session.id}`} delayIndex={Math.min(index, 5)}>
                      <CardLink href={`/classes/${item.slug}`} className="h-full">
                        <div className="flex items-start justify-between gap-3">
                          <div className="min-w-0">
                            <p className="break-words text-sm font-semibold text-navy">{item.title}</p>
                            <p className="mt-1 flex items-center gap-1 text-xs text-muted"><CalendarDays className="h-3.5 w-3.5" aria-hidden="true" /> {formatDate(session.scheduledStartAt)}</p>
                          </div>
                          <Badge variant={canJoin ? 'success' : 'info'} className="shrink-0">{canJoin ? 'Join now' : session.status}</Badge>
                        </div>
                      </CardLink>
                    </MotionItem>
                  );
                })}
              </div>
            )}
          </section>

          <section className="space-y-4">
            {/* The track filter narrows this catalog, so it lives on the catalog header. */}
            <LearnerSurfaceSectionHeader
              eyebrow="Catalog"
              title="Reserve a live class"
              description="Credits are charged at reservation. Cancel more than 24 hours before the class for a full credit refund."
              action={(
                <div className="flex flex-wrap gap-2">
                  {tracks.map((track) => (
                    <button
                      key={track}
                      type="button"
                      onClick={() => setFilter(track)}
                      aria-pressed={filter === track}
                      className={cn(
                        'pressable min-h-11 rounded-full border px-4 text-sm font-medium focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
                        filter === track
                          ? 'border-primary/30 bg-primary/10 text-primary'
                          : 'hover-primary border-border bg-surface text-muted',
                      )}
                    >
                      {track}
                    </button>
                  ))}
                </div>
              )}
            />

            {visibleClasses.length === 0 ? (
              <EmptyState className="py-8" icon={<Video className="h-8 w-8" />} title="No classes match this filter." />
            ) : (
              <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
                {visibleClasses.map((item, index) => {
                  const session = nextSession(item);
                  const canJoin = session ? isJoinAvailable(session, now) : false;
                  return (
                    <MotionItem key={item.id} delayIndex={Math.min(index, 5)}>
                      <article className={cn(cardClassName({ padding: 'lg' }), 'h-full')}>
                        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
                          <div className="min-w-0 space-y-2">
                            <div className="flex flex-wrap items-center gap-2">
                              <Badge variant="outline">{item.type}</Badge>
                              <Badge variant="default">{item.professionTrack}</Badge>
                              <span className="text-xs font-medium text-muted">{item.level}</span>
                            </div>
                            <h3 className="break-words text-xl font-semibold text-navy">{item.title}</h3>
                            <p className="line-clamp-2 text-sm leading-6 text-muted">{item.description}</p>
                          </div>
                          <div className="shrink-0 self-start rounded-xl border border-border bg-background-light px-3 py-2 text-center">
                            <p className="text-2xl font-semibold tabular-nums text-navy">{item.creditCost}</p>
                            <p className="text-xs text-muted">credits</p>
                          </div>
                        </div>

                        {session ? (
                          <div className="mt-5 grid grid-cols-1 gap-3 rounded-xl bg-background-light p-4 text-sm md:grid-cols-3">
                            <div className="flex items-center gap-2 text-muted"><Clock className="h-4 w-4 shrink-0" aria-hidden="true" /> {formatDate(session.scheduledStartAt)}</div>
                            <div className="flex items-center gap-2 text-muted"><Users className="h-4 w-4 shrink-0" aria-hidden="true" /> {seatsLabel(session)}</div>
                            <div className="flex items-center gap-2 text-muted"><GraduationCap className="h-4 w-4 shrink-0" aria-hidden="true" /> {item.tutorDisplayName ?? 'Tutor to confirm'}</div>
                          </div>
                        ) : null}

                        <div className="mt-5 flex flex-col gap-2 sm:flex-row sm:justify-end">
                          <Button asChild variant="outline" size="sm">
                            <Link href={`/classes/${item.slug}`}>
                              Details
                            </Link>
                          </Button>
                          {session?.isEnrolled ? (
                            canJoin ? (
                              <Button asChild size="sm">
                                <Link href={`/classes/${item.slug}/sessions/${session.id}/join`}>
                                  <PlayCircle className="h-4 w-4" aria-hidden="true" /> Join class
                                </Link>
                              </Button>
                            ) : (
                              <Button type="button" variant="secondary" size="sm" disabled>
                                <PlayCircle className="h-4 w-4" aria-hidden="true" /> Opens 30m before
                              </Button>
                            )
                          ) : session ? (
                            <Button type="button" size="sm" onClick={() => handleEnroll(session.id)} loading={enrollingSessionId === session.id}>
                              <RotateCcw className="h-4 w-4" aria-hidden="true" /> Reserve seat
                            </Button>
                          ) : null}
                        </div>
                      </article>
                    </MotionItem>
                  );
                })}
              </div>
            )}
          </section>
        </>
      )}
    </>
  );
}