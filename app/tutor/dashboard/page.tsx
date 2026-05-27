'use client';

import Link from 'next/link';
import { useEffect, useMemo, useState } from 'react';
import { CalendarPlus, LayoutDashboard, PlayCircle, Users, Video, DollarSign } from 'lucide-react';

import { TutorRouteHero, TutorRouteSectionHeader, TutorRouteWorkspace } from '@/components/domain/tutor-route-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Button, buttonClassName } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { DashboardCards } from '@/components/tutor/DashboardCards';
import {
  fetchTutorClasses,
  fetchTutorEarnings,
  type LiveClassListItem,
  type LiveClassSessionSummary,
  type TutorEarnings,
} from '@/lib/api';

type SessionRow = { classItem: LiveClassListItem; session: LiveClassSessionSummary };

function sessions(items: LiveClassListItem[]): SessionRow[] {
  return items
    .flatMap((classItem) => classItem.sessions.map((session) => ({ classItem, session })))
    .sort((a, b) => new Date(a.session.scheduledStartAt).getTime() - new Date(b.session.scheduledStartAt).getTime());
}

function hoursUntil(target: string): number {
  const ms = new Date(target).getTime() - Date.now();
  return Math.max(0, Math.round(ms / (60 * 60 * 1000)));
}

function withinThisWeek(date: string): boolean {
  const start = new Date();
  start.setHours(0, 0, 0, 0);
  const end = new Date(start);
  end.setDate(end.getDate() + 7);
  const t = new Date(date).getTime();
  return t >= start.getTime() && t < end.getTime();
}

export default function TutorDashboardPage() {
  const [classes, setClasses] = useState<LiveClassListItem[]>([]);
  const [earnings, setEarnings] = useState<TutorEarnings | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 60_000);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    Promise.all([
      fetchTutorClasses().catch((err: unknown) => {
        throw err instanceof Error ? err : new Error('Could not load classes.');
      }),
      fetchTutorEarnings().catch(() => null),
    ])
      .then(([list, e]) => {
        if (cancelled) return;
        setClasses(list);
        setEarnings(e);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load tutor dashboard.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const allSessions = useMemo(() => sessions(classes), [classes]);
  const nextSession = useMemo(
    () => allSessions.find(({ session }) => new Date(session.scheduledStartAt).getTime() > now && session.status !== 'Cancelled'),
    [allSessions, now],
  );

  const weekScheduled = allSessions.filter(({ session }) => withinThisWeek(session.scheduledStartAt) && session.status !== 'Cancelled').length;
  const weekEnrollments = allSessions
    .filter(({ session }) => withinThisWeek(session.scheduledStartAt))
    .reduce((total, { session }) => total + session.enrolledCount, 0);

  const totalNet = earnings?.netUsd ?? 0;

  return (
    <TutorRouteWorkspace>
      <TutorRouteHero
        title="Tutor Dashboard"
        description="Your upcoming classes, this week’s schedule, and quick links into the rest of the tutor console."
        icon={LayoutDashboard}
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
        <div className="grid gap-3 md:grid-cols-3">
          <Skeleton className="h-28 rounded-2xl" />
          <Skeleton className="h-28 rounded-2xl" />
          <Skeleton className="h-28 rounded-2xl" />
        </div>
      ) : (
        <DashboardCards
          nextSessionTitle={nextSession?.classItem.title ?? null}
          nextSessionStartsAt={nextSession?.session.scheduledStartAt ?? null}
          hoursToNext={nextSession ? hoursUntil(nextSession.session.scheduledStartAt) : null}
          weekClasses={weekScheduled}
          weekEnrollments={weekEnrollments}
          netUsd={totalNet}
        />
      )}

      <section className="space-y-4">
        <TutorRouteSectionHeader
          eyebrow="Quick actions"
          title="What would you like to do?"
          description="Schedule a new class, manage availability, or jump into past recordings."
        />
        <div className="grid gap-3 sm:grid-cols-3">
          <Link
            href="/tutor/classes/new"
            className={buttonClassName({ variant: 'primary' }) + ' justify-start gap-3'}
          >
            <CalendarPlus className="h-4 w-4" />
            Schedule class
          </Link>
          <Link
            href="/me/classes/past"
            className={buttonClassName({ variant: 'secondary' }) + ' justify-start gap-3'}
          >
            <PlayCircle className="h-4 w-4" />
            View past recordings
          </Link>
          <Link
            href="/tutor/earnings"
            className={buttonClassName({ variant: 'outline' }) + ' justify-start gap-3'}
          >
            <DollarSign className="h-4 w-4" />
            View earnings
          </Link>
        </div>
      </section>

      <section className="space-y-4">
        <TutorRouteSectionHeader
          eyebrow="This week"
          title="Upcoming sessions"
          description="Sessions scheduled in the next 7 days. Click a class to manage it."
          icon={Video}
        />
        {loading ? (
          <div className="space-y-3">
            <Skeleton className="h-24 rounded-2xl" />
            <Skeleton className="h-24 rounded-2xl" />
          </div>
        ) : allSessions.length === 0 ? (
          <div className="rounded-2xl border border-dashed border-border bg-surface p-10 text-center">
            <Video className="mx-auto mb-3 h-8 w-8 text-muted/50" />
            <p className="text-sm font-medium text-navy">No classes scheduled yet.</p>
            <p className="mt-1 text-sm text-muted">Create your first live class to start teaching.</p>
            <Link
              href="/tutor/classes/new"
              className={buttonClassName({ variant: 'primary', size: 'sm' }) + ' mt-4 inline-flex'}
            >
              <CalendarPlus className="h-4 w-4" /> Schedule class
            </Link>
          </div>
        ) : (
          <div className="space-y-3">
            {allSessions
              .filter(({ session }) => new Date(session.scheduledStartAt).getTime() > now)
              .slice(0, 5)
              .map(({ classItem, session }) => (
                <Link
                  key={`${classItem.id}-${session.id}`}
                  href={`/tutor/classes/${classItem.id}`}
                  className="block rounded-2xl border border-border bg-surface p-4 shadow-sm transition-colors hover:border-border-hover"
                >
                  <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
                    <div className="min-w-0">
                      <p className="truncate text-sm font-semibold text-navy">{classItem.title}</p>
                      <p className="mt-0.5 text-xs text-muted">
                        {new Intl.DateTimeFormat('en-AU', {
                          weekday: 'short',
                          day: 'numeric',
                          month: 'short',
                          hour: '2-digit',
                          minute: '2-digit',
                        }).format(new Date(session.scheduledStartAt))}
                      </p>
                    </div>
                    <span className="flex items-center gap-1.5 text-xs text-muted">
                      <Users className="h-4 w-4" />
                      {session.enrolledCount}/{session.capacity}
                    </span>
                  </div>
                </Link>
              ))}
          </div>
        )}
      </section>
    </TutorRouteWorkspace>
  );
}
