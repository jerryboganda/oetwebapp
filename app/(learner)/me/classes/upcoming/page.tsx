'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { CalendarDays, Clock, PlayCircle, Users, Video } from 'lucide-react';

import { LearnerPageHero } from '@/components/domain/learner-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { cardClassName } from '@/components/ui/card';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { ClassesTabs } from '../classes-tabs';
import {
  cancelLiveClassEnrollment,
  fetchMyUpcomingLiveClasses,
  type LiveClassListItem,
  type LiveClassSessionSummary,
} from '@/lib/api';

function formatDate(value: string) {
  return new Intl.DateTimeFormat('en-AU', {
    weekday: 'short',
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value));
}

function nextSession(item: LiveClassListItem): LiveClassSessionSummary | null {
  return item.sessions.find((s) => s.status !== 'Cancelled') ?? item.sessions[0] ?? null;
}

function seatsLabel(session: LiveClassSessionSummary) {
  const remaining = Math.max(0, session.capacity - session.enrolledCount);
  return remaining === 0 ? 'Waitlist only' : `${remaining} seats left`;
}

export default function MyUpcomingClassesPage() {
  const [classes, setClasses] = useState<LiveClassListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [cancellingSessionId, setCancellingSessionId] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      const data = await fetchMyUpcomingLiveClasses();
      setClasses(data);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not load upcoming classes.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    let cancelled = false;
    fetchMyUpcomingLiveClasses()
      .then((data) => { if (!cancelled) setClasses(data); })
      .catch((err: unknown) => { if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load upcoming classes.'); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, []);

  async function handleCancel(sessionId: string) {
    setCancellingSessionId(sessionId);
    setError(null);
    try {
      await cancelLiveClassEnrollment(sessionId, 'Learner cancelled from my classes page.');
      await load();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not cancel this reservation.');
    } finally {
      setCancellingSessionId(null);
    }
  }

  // The list itself failed to load; an empty list here would read as "no classes".
  const loadFailed = Boolean(error) && !loading && classes.length === 0;

  return (
    <>
      <LearnerPageHero
        title="My Upcoming Classes"
        description="Live sessions you have reserved. Class links open 30 minutes before start time."
        icon={Video}
      />

      {error && !loadFailed ? (
        <InlineAlert
          variant="warning"
          action={(
            <div className="flex flex-wrap gap-2">
              <Button type="button" variant="outline" size="sm" onClick={() => void load()}>
                Retry
              </Button>
              <Button type="button" variant="ghost" size="sm" onClick={() => setError(null)}>
                Dismiss
              </Button>
            </div>
          )}
        >
          {error}
        </InlineAlert>
      ) : null}

      <ClassesTabs active="upcoming" />

      {loading ? (
        <div className="space-y-4">
          <Skeleton className="h-36 rounded-2xl" />
          <Skeleton className="h-36 rounded-2xl" />
          <Skeleton className="h-36 rounded-2xl" />
        </div>
      ) : loadFailed ? (
        <ErrorState message={error ?? undefined} onRetry={() => void load()} retryLabel="Retry" />
      ) : classes.length === 0 ? (
        <EmptyState
          icon={<Video className="h-8 w-8" />}
          title="No upcoming classes"
          description="Browse the class catalog to enroll."
          action={{ label: 'Browse catalog', href: '/classes' }}
        />
      ) : (
        <div className="space-y-4">
          {classes.map((item, index) => {
            const session = nextSession(item);
            if (!session) return null;
            return (
              <MotionItem key={`${item.id}-${session.id}`} delayIndex={Math.min(index, 5)}>
                <article className={cardClassName({})}>
                  <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
                    <div className="min-w-0 space-y-2">
                      <div className="flex flex-wrap items-center gap-2">
                        <Badge variant="info">{item.type}</Badge>
                        <Badge variant="default">{item.professionTrack}</Badge>
                        {session.isJoinAvailable ? (
                          <Badge variant="success">Join now</Badge>
                        ) : null}
                      </div>
                      <h2 className="text-lg font-semibold text-navy">{item.title}</h2>
                      <div className="flex flex-wrap gap-x-4 gap-y-1.5 text-sm tabular-nums text-muted">
                        <span className="flex items-center gap-1.5">
                          <CalendarDays className="h-4 w-4" aria-hidden="true" />
                          {formatDate(session.scheduledStartAt)}
                        </span>
                        <span className="flex items-center gap-1.5">
                          <Clock className="h-4 w-4" aria-hidden="true" />
                          Ends {formatDate(session.scheduledEndAt)}
                        </span>
                        <span className="flex items-center gap-1.5">
                          <Users className="h-4 w-4" aria-hidden="true" />
                          {seatsLabel(session)}
                        </span>
                      </div>
                    </div>

                    <div className="flex shrink-0 flex-col gap-2 sm:flex-row lg:flex-col xl:flex-row">
                      <Button asChild variant={session.isJoinAvailable ? 'primary' : 'secondary'} size="sm">
                        <Link href={`/classes/${item.slug}/sessions/${session.id}/join`}>
                          <PlayCircle className="h-4 w-4" aria-hidden="true" />
                          {session.isJoinAvailable ? 'Join class' : 'Open join page'}
                        </Link>
                      </Button>
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        loading={cancellingSessionId === session.id}
                        onClick={() => handleCancel(session.id)}
                      >
                        Cancel
                      </Button>
                    </div>
                  </div>
                </article>
              </MotionItem>
            );
          })}
        </div>
      )}
    </>
  );
}
