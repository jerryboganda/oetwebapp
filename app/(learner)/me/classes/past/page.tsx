'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { CalendarDays, PlayCircle, Video } from 'lucide-react';

import { LearnerPageHero } from '@/components/domain/learner-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { cardClassName } from '@/components/ui/card';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { ClassesTabs } from '../classes-tabs';
import {
  fetchMyPastLiveClasses,
  type LiveClassListItem,
  type LiveClassSessionSummary,
} from '@/lib/api';

function formatDate(value: string) {
  return new Intl.DateTimeFormat('en-AU', {
    weekday: 'short',
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value));
}

function lastCompletedSession(item: LiveClassListItem): LiveClassSessionSummary | null {
  const completed = item.sessions.filter((s) => s.status === 'Completed');
  if (completed.length > 0) return completed[completed.length - 1];
  return item.sessions[item.sessions.length - 1] ?? null;
}

export default function MyPastClassesPage() {
  const [classes, setClasses] = useState<LiveClassListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    fetchMyPastLiveClasses()
      .then((data) => { if (!cancelled) setClasses(data); })
      .catch((err: unknown) => { if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load past classes.'); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, []);

  return (
    <>
      <LearnerPageHero
        title="My Past Classes"
        description="Replay recordings and review AI summaries from completed sessions."
        icon={Video}
      />

      <ClassesTabs active="past" />

      {loading ? (
        <div className="space-y-4">
          <Skeleton className="h-32 rounded-2xl" />
          <Skeleton className="h-32 rounded-2xl" />
          <Skeleton className="h-32 rounded-2xl" />
        </div>
      ) : error ? (
        <ErrorState message={error} />
      ) : classes.length === 0 ? (
        <EmptyState
          icon={<Video className="h-8 w-8" />}
          title="No past classes yet"
          description="Completed classes and their recordings will appear here."
          action={{ label: 'View upcoming classes', href: '/me/classes/upcoming' }}
        />
      ) : (
        <div className="space-y-4">
          {classes.map((item, index) => {
            const session = lastCompletedSession(item);
            if (!session) return null;

            const recordingReady = session.recordingReady === true;

            return (
              <MotionItem key={`${item.id}-${session.id}`} delayIndex={Math.min(index, 5)}>
                <article className={cardClassName({})}>
                  <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
                    <div className="min-w-0 space-y-2">
                      <div className="flex flex-wrap items-center gap-2">
                        <Badge variant="info">{item.type}</Badge>
                        <Badge variant="default">{item.professionTrack}</Badge>
                        <Badge variant="muted">{item.level}</Badge>
                      </div>
                      <h2 className="text-lg font-semibold text-navy">{item.title}</h2>
                      <p className="flex flex-wrap items-center gap-1.5 text-sm tabular-nums text-muted">
                        <CalendarDays className="h-4 w-4" aria-hidden="true" />
                        {formatDate(session.scheduledStartAt)}
                        {item.tutorDisplayName ? ` · ${item.tutorDisplayName}` : null}
                      </p>
                    </div>

                    <div className="shrink-0">
                      {recordingReady ? (
                        <Button asChild variant="secondary" size="sm">
                          <Link href={`/me/classes/recordings/${session.id}`}>
                            <PlayCircle className="h-4 w-4" aria-hidden="true" />
                            Watch recording
                          </Link>
                        </Button>
                      ) : (
                        <Button type="button" variant="ghost" size="sm" disabled>
                          <PlayCircle className="h-4 w-4" aria-hidden="true" />
                          No recording yet
                        </Button>
                      )}
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
