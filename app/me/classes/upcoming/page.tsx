'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { CalendarDays, Clock, PlayCircle, Users, Video } from 'lucide-react';

import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button, buttonClassName } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
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

  return (
    <LearnerDashboardShell>
      <div className="space-y-5 sm:space-y-8">
        <LearnerPageHero
          title="My Upcoming Classes"
          description="Live sessions you have reserved. Class links open 30 minutes before start time."
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

        <div className="flex gap-3 border-b border-border pb-1 text-sm">
          <Link
            href="/me/classes/upcoming"
            className="border-b-2 border-primary pb-2 font-semibold text-primary"
          >
            Upcoming
          </Link>
          <Link
            href="/me/classes/past"
            className="pb-2 text-muted hover:text-navy"
          >
            Past
          </Link>
        </div>

        {loading ? (
          <div className="space-y-4">
            <Skeleton className="h-36 rounded-xl" />
            <Skeleton className="h-36 rounded-xl" />
            <Skeleton className="h-36 rounded-xl" />
          </div>
        ) : classes.length === 0 ? (
          <div className="rounded-2xl border border-dashed border-border bg-surface p-10 text-center">
            <Video className="mx-auto mb-3 h-8 w-8 text-muted/50" />
            <p className="text-sm font-medium text-navy">No upcoming classes</p>
            <p className="mt-1 text-sm text-muted">
              Browse the class catalog to enroll.
            </p>
            <Link
              href="/classes"
              className={buttonClassName({ variant: 'primary', size: 'sm' }) + ' mt-4 inline-flex'}
            >
              Browse catalog
            </Link>
          </div>
        ) : (
          <div className="space-y-4">
            {classes.map((item) => {
              const session = nextSession(item);
              if (!session) return null;
              return (
                <article
                  key={`${item.id}-${session.id}`}
                  className="rounded-2xl border border-border bg-surface p-5 shadow-sm"
                >
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
                      <div className="flex flex-wrap gap-4 text-sm text-muted">
                        <span className="flex items-center gap-1.5">
                          <CalendarDays className="h-4 w-4" />
                          {formatDate(session.scheduledStartAt)}
                        </span>
                        <span className="flex items-center gap-1.5">
                          <Clock className="h-4 w-4" />
                          Ends {formatDate(session.scheduledEndAt)}
                        </span>
                        <span className="flex items-center gap-1.5">
                          <Users className="h-4 w-4" />
                          {seatsLabel(session)}
                        </span>
                      </div>
                    </div>

                    <div className="flex shrink-0 flex-col gap-2 sm:flex-row lg:flex-col xl:flex-row">
                      {session.isJoinAvailable ? (
                        <Link
                          href={`/classes/${item.slug}/sessions/${session.id}/join`}
                          className={buttonClassName({ variant: 'primary', size: 'sm' })}
                        >
                          <PlayCircle className="h-4 w-4" />
                          Join class
                        </Link>
                      ) : (
                        <Link
                          href={`/classes/${item.slug}/sessions/${session.id}/join`}
                          className={buttonClassName({ variant: 'secondary', size: 'sm' })}
                        >
                          <PlayCircle className="h-4 w-4" />
                          Open join page
                        </Link>
                      )}
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
              );
            })}
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
