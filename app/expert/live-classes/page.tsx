'use client';

import { useEffect, useMemo, useState } from 'react';
import { CalendarDays, Clock, PlayCircle, RefreshCcw, Users, Video } from 'lucide-react';

import { ZoomMeetingEmbed } from '@/components/class/ZoomMeetingEmbed';
import { ExpertRouteHero, ExpertRouteSectionHeader, ExpertRouteWorkspace } from '@/components/domain/expert-route-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import {
  fetchExpertLiveClasses,
  fetchExpertLiveClassJoinToken,
  type LiveClassJoinToken,
  type LiveClassListItem,
  type LiveClassSessionSummary,
} from '@/lib/api';
import { safeZoomUrl } from '@/lib/zoom-url';

type ExpertLiveSession = {
  classItem: LiveClassListItem;
  session: LiveClassSessionSummary;
};

type ActiveMeeting = {
  token: LiveClassJoinToken;
  title: string;
  startsAt: string;
};

const JOIN_WINDOW_BEFORE_MS = 30 * 60 * 1000;
const JOIN_WINDOW_AFTER_MS = 15 * 60 * 1000;

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat('en-AU', {
    weekday: 'short',
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value));
}

function formatTime(value: string) {
  return new Intl.DateTimeFormat('en-AU', {
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value));
}

function displayStatus(status: string) {
  return status.replace(/([a-z])([A-Z])/g, '$1 $2');
}

function statusVariant(status: string): BadgeProps['variant'] {
  switch (status) {
    case 'Scheduled':
      return 'info';
    case 'Live':
      return 'success';
    case 'Completed':
      return 'muted';
    case 'Cancelled':
      return 'danger';
    default:
      return 'outline';
  }
}

function isJoinAvailable(session: LiveClassSessionSummary, now: number) {
  if (session.status === 'Cancelled') return false;
  const startsAt = new Date(session.scheduledStartAt).getTime();
  const endsAt = new Date(session.scheduledEndAt).getTime();
  return session.isJoinAvailable || (startsAt <= now + JOIN_WINDOW_BEFORE_MS && endsAt >= now - JOIN_WINDOW_AFTER_MS);
}

function sortSessions(items: LiveClassListItem[]): ExpertLiveSession[] {
  return items
    .flatMap((classItem) => classItem.sessions.map((session) => ({ classItem, session })))
    .sort((left, right) => new Date(left.session.scheduledStartAt).getTime() - new Date(right.session.scheduledStartAt).getTime());
}

export default function ExpertLiveClassesPage() {
  const [classes, setClasses] = useState<LiveClassListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [startingSessionId, setStartingSessionId] = useState<string | null>(null);
  const [activeMeeting, setActiveMeeting] = useState<ActiveMeeting | null>(null);
  const [now, setNow] = useState(() => Date.now());

  const sessions = useMemo(() => sortSessions(classes), [classes]);
  const liveCount = sessions.filter(({ session }) => isJoinAvailable(session, now)).length;
  const upcomingCount = sessions.filter(({ session }) => new Date(session.scheduledStartAt).getTime() > now && session.status !== 'Cancelled').length;
  const enrolledCount = sessions.reduce((total, { session }) => total + session.enrolledCount, 0);

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 60_000);
    return () => window.clearInterval(timer);
  }, []);

  async function loadClasses() {
    setLoading(true);
    setError(null);
    try {
      const data = await fetchExpertLiveClasses();
      setClasses(data);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not load assigned live classes.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    fetchExpertLiveClasses()
      .then((data) => {
        if (!cancelled) setClasses(data);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load assigned live classes.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  async function handleStart(item: ExpertLiveSession) {
    setStartingSessionId(item.session.id);
    setError(null);
    try {
      const token = await fetchExpertLiveClassJoinToken(item.session.id);
      if (token.sdkKey && token.signature && (token.role === 0 || token.zak)) {
        setActiveMeeting({ token, title: item.classItem.title, startsAt: item.session.scheduledStartAt });
        return;
      }

      const joinUrl = safeZoomUrl(token.joinUrl);
      if (joinUrl) {
        window.open(joinUrl, '_blank', 'noopener,noreferrer');
        return;
      }

      setError('Zoom host details are unavailable for this session.');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not prepare the Zoom host room.');
    } finally {
      setStartingSessionId(null);
    }
  }

  if (activeMeeting && activeMeeting.token.sdkKey && activeMeeting.token.signature) {
    return (
      <ExpertRouteWorkspace>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <p className="text-xs font-semibold uppercase tracking-wide text-primary">Live Classes</p>
            <h1 className="text-xl font-semibold text-navy">{activeMeeting.title}</h1>
            <p className="text-sm text-muted">{formatDateTime(activeMeeting.startsAt)}</p>
          </div>
          <Button type="button" variant="outline" onClick={() => setActiveMeeting(null)}>
            Close meeting
          </Button>
        </div>
        <ZoomMeetingEmbed joinToken={activeMeeting.token} onLeave={() => setActiveMeeting(null)} />
      </ExpertRouteWorkspace>
    );
  }

  return (
    <ExpertRouteWorkspace>
      <ExpertRouteHero
        title="Live Classes"
        description="Assigned group sessions, learner counts, and secure Zoom host rooms."
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

      <div className="grid gap-3 md:grid-cols-3">
        <section className="rounded-lg border border-border bg-surface p-4">
          <p className="text-xs font-semibold uppercase tracking-wide text-muted">Ready</p>
          <p className="mt-2 text-3xl font-semibold text-navy">{liveCount}</p>
        </section>
        <section className="rounded-lg border border-border bg-surface p-4">
          <p className="text-xs font-semibold uppercase tracking-wide text-muted">Upcoming</p>
          <p className="mt-2 text-3xl font-semibold text-navy">{upcomingCount}</p>
        </section>
        <section className="rounded-lg border border-border bg-surface p-4">
          <p className="text-xs font-semibold uppercase tracking-wide text-muted">Learners</p>
          <p className="mt-2 text-3xl font-semibold text-navy">{enrolledCount}</p>
        </section>
      </div>

      <section className="space-y-4">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <ExpertRouteSectionHeader
            eyebrow="Schedule"
            title="Assigned Sessions"
            description="Host controls appear when a session enters its join window."
            icon={CalendarDays}
          />
          <Button type="button" variant="outline" size="sm" onClick={loadClasses} loading={loading}>
            <RefreshCcw className="h-4 w-4" /> Refresh
          </Button>
        </div>

        {loading ? (
          <div className="space-y-3">
            <Skeleton className="h-36 rounded-lg" />
            <Skeleton className="h-36 rounded-lg" />
            <Skeleton className="h-36 rounded-lg" />
          </div>
        ) : sessions.length === 0 ? (
          <div className="rounded-lg border border-dashed border-border bg-surface p-8 text-center">
            <Video className="mx-auto mb-3 h-8 w-8 text-muted/60" />
            <p className="text-sm font-semibold text-navy">No assigned live classes</p>
            <p className="mt-1 text-sm text-muted">Published classes assigned to your tutor profile will appear here.</p>
          </div>
        ) : (
          <div className="space-y-3">
            {sessions.map((item) => {
              const canStart = isJoinAvailable(item.session, now);
              return (
                <article key={`${item.classItem.id}-${item.session.id}`} className="rounded-lg border border-border bg-surface p-5 shadow-sm">
                  <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
                    <div className="min-w-0 space-y-3">
                      <div className="flex flex-wrap items-center gap-2">
                        <Badge variant={canStart ? 'success' : statusVariant(item.session.status)}>
                          {canStart ? 'Ready' : displayStatus(item.session.status)}
                        </Badge>
                        <Badge variant="info">{item.classItem.type}</Badge>
                        <Badge variant="outline">{item.classItem.professionTrack}</Badge>
                        <span className="text-xs font-medium text-muted">{item.classItem.level}</span>
                      </div>
                      <div>
                        <h2 className="text-lg font-semibold text-navy">{item.classItem.title}</h2>
                        <p className="mt-1 line-clamp-2 text-sm leading-6 text-muted">{item.classItem.description}</p>
                      </div>
                      <div className="flex flex-wrap gap-4 text-sm text-muted">
                        <span className="flex items-center gap-1.5">
                          <CalendarDays className="h-4 w-4" />
                          {formatDateTime(item.session.scheduledStartAt)}
                        </span>
                        <span className="flex items-center gap-1.5">
                          <Clock className="h-4 w-4" />
                          Ends {formatTime(item.session.scheduledEndAt)}
                        </span>
                        <span className="flex items-center gap-1.5">
                          <Users className="h-4 w-4" />
                          {item.session.enrolledCount}/{item.session.capacity}
                        </span>
                      </div>
                    </div>

                    <div className="flex shrink-0 flex-col gap-2 sm:flex-row lg:flex-col xl:flex-row">
                      {canStart ? (
                        <Button
                          type="button"
                          size="sm"
                          onClick={() => handleStart(item)}
                          loading={startingSessionId === item.session.id}
                        >
                          <PlayCircle className="h-4 w-4" /> Start
                        </Button>
                      ) : (
                        <Button type="button" variant="secondary" size="sm" disabled>
                          <Clock className="h-4 w-4" /> Opens later
                        </Button>
                      )}
                    </div>
                  </div>
                </article>
              );
            })}
          </div>
        )}
      </section>
    </ExpertRouteWorkspace>
  );
}