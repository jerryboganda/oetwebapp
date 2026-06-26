'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { CalendarDays, PlayCircle, Video } from 'lucide-react';

import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button, buttonClassName } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
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
    <LearnerDashboardShell>
      <div className="space-y-5 sm:space-y-8">
        <LearnerPageHero
          title="My Past Classes"
          description="Replay recordings and review AI summaries from completed sessions."
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
            className="pb-2 text-muted hover:text-navy"
          >
            Upcoming
          </Link>
          <Link
            href="/me/classes/past"
            className="border-b-2 border-primary pb-2 font-semibold text-primary"
          >
            Past
          </Link>
        </div>

        {loading ? (
          <div className="space-y-4">
            <Skeleton className="h-32 rounded-xl" />
            <Skeleton className="h-32 rounded-xl" />
            <Skeleton className="h-32 rounded-xl" />
          </div>
        ) : classes.length === 0 ? (
          <div className="rounded-2xl border border-dashed border-border bg-surface p-10 text-center">
            <Video className="mx-auto mb-3 h-8 w-8 text-muted/50" />
            <p className="text-sm font-medium text-navy">No past classes yet</p>
            <p className="mt-1 text-sm text-muted">
              Completed classes and their recordings will appear here.
            </p>
          </div>
        ) : (
          <div className="space-y-4">
            {classes.map((item) => {
              const session = lastCompletedSession(item);
              if (!session) return null;

              const hasRecording = session.status === 'Completed';
              const recordingReady = hasRecording; // recording availability comes from the recording endpoint

              return (
                <article
                  key={`${item.id}-${session.id}`}
                  className="rounded-2xl border border-border bg-surface p-5 shadow-sm"
                >
                  <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
                    <div className="min-w-0 space-y-2">
                      <div className="flex flex-wrap items-center gap-2">
                        <Badge variant="info">{item.type}</Badge>
                        <Badge variant="default">{item.professionTrack}</Badge>
                        <Badge variant="muted">{item.level}</Badge>
                      </div>
                      <h2 className="text-lg font-semibold text-navy">{item.title}</h2>
                      <p className="flex items-center gap-1.5 text-sm text-muted">
                        <CalendarDays className="h-4 w-4" />
                        {formatDate(session.scheduledStartAt)}
                        {item.tutorDisplayName ? ` · ${item.tutorDisplayName}` : null}
                      </p>
                    </div>

                    <div className="shrink-0">
                      {recordingReady ? (
                        <Link
                          href={`/me/classes/recordings/${session.id}`}
                          className={buttonClassName({ variant: 'secondary', size: 'sm' })}
                        >
                          <PlayCircle className="h-4 w-4" />
                          Watch recording
                        </Link>
                      ) : (
                        <button
                          type="button"
                          disabled
                          className={buttonClassName({ variant: 'ghost', size: 'sm' }) + ' cursor-not-allowed opacity-50'}
                        >
                          <PlayCircle className="h-4 w-4" />
                          Recording pending
                        </button>
                      )}
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
