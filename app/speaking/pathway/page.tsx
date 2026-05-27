'use client';

/**
 * OET Speaking — Phase 8 P8.3 — learner-facing course pathway view.
 *
 * Renders the 16-stage pathway with state per stage. Backend owns
 * the state machine (`/v1/speaking/course-pathway`).
 */
import { useEffect, useState } from 'react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import {
  type SpeakingPathwayStage,
  type SpeakingPathway as SpeakingPathwayResponse,
  fetchSpeakingCoursePathway,
} from '@/lib/api/speaking-course-pathway';

function stateBadge(state: SpeakingPathwayStage['state']) {
  switch (state) {
    case 'completed':
      return <Badge variant="success">completed</Badge>;
    case 'in_progress':
      return <Badge variant="info">in progress</Badge>;
    default:
      return <Badge variant="muted">locked</Badge>;
  }
}

export default function SpeakingPathwayPage() {
  const [data, setData] = useState<SpeakingPathwayResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const res = await fetchSpeakingCoursePathway();
        if (!cancelled) setData(res);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Could not load pathway.');
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <LearnerDashboardShell>
      <div className="mx-auto max-w-4xl space-y-6 py-8">
        <header className="space-y-2">
          <h1 className="text-2xl font-semibold text-foreground">
            {data?.title ?? 'Your Speaking pathway'}
          </h1>
          <p className="text-muted-foreground">
            A guided sequence of warm-ups, drills, and full role-plays. Complete each stage to
            unlock the next.
          </p>
        </header>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        {!data ? (
          <Skeleton className="h-64 w-full rounded-xl" />
        ) : (
          <>
            <Card className="p-4">
              <div className="flex items-center justify-between gap-4">
                <div>
                  <div className="text-sm text-muted-foreground">Progress</div>
                  <div className="text-lg font-semibold text-foreground">
                    {data.completedStageCount} / {data.totalStages} stages
                  </div>
                </div>
                <div className="text-3xl font-mono font-semibold text-blue-600">
                  {Math.round(data.progressPercent)}%
                </div>
              </div>
              <div className="mt-3 h-2 overflow-hidden rounded-full bg-slate-200">
                <div
                  className="h-full bg-blue-500 transition-all"
                  style={{ width: `${Math.min(100, Math.max(0, data.progressPercent))}%` }}
                />
              </div>
            </Card>

            <ol className="space-y-3">
              {data.stages.map((stage, idx) => (
                <li key={stage.code}>
                  <Card className="p-4">
                    <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                      <div className="space-y-1">
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="text-xs font-mono text-muted-foreground">
                            {String(idx + 1).padStart(2, '0')}
                          </span>
                          <span className="font-medium text-foreground">{stage.title}</span>
                          {stateBadge(stage.state)}
                          <Badge variant="outline">{stage.activityKind}</Badge>
                        </div>
                        <p className="text-sm text-muted-foreground">{stage.description}</p>
                      </div>
                      {stage.actionHref && stage.state !== 'locked' ? (
                        <Button asChild variant={stage.state === 'in_progress' ? 'primary' : 'outline'} size="sm">
                          <Link href={stage.actionHref}>{stage.actionLabel ?? 'Continue'}</Link>
                        </Button>
                      ) : null}
                    </div>
                  </Card>
                </li>
              ))}
            </ol>
          </>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
