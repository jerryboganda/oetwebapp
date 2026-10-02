'use client';

/**
 * OET Speaking — Phase 8 P8.3 — learner-facing course pathway view.
 *
 * Renders the 16-stage pathway with state per stage. Backend owns
 * the state machine (`/v1/speaking/course-pathway`).
 */
import { useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { Compass } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { CountUp } from '@/components/ui/count-up';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { ProgressBar } from '@/components/ui/progress';
import { cn } from '@/lib/utils';
import {
  type SpeakingPathwayStage,
  type SpeakingPathway as SpeakingPathwayResponse,
  fetchSpeakingCoursePathway,
} from '@/lib/api/speaking-course-pathway';
import { trackSpeaking } from '@/lib/analytics/speaking-events';

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

/** `OrientationVideo` → `Orientation Video`: the activity code, read as words. */
function activityLabel(kind: string) {
  return kind.replace(/_/g, ' ').replace(/([a-z])([A-Z])/g, '$1 $2');
}

export default function SpeakingPathwayPage() {
  const [data, setData] = useState<SpeakingPathwayResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const trackedViewRef = useRef(false);

  useEffect(() => {
    if (!trackedViewRef.current) {
      trackedViewRef.current = true;
      trackSpeaking('pathway_viewed', {});
    }

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

  const progressPercent = data ? Math.min(100, Math.max(0, data.progressPercent)) : 0;

  return (
    <>
      <LearnerPageHero
        eyebrow="Speaking"
        icon={Compass}
        accent="speaking"
        title={data?.title ?? 'Your Speaking pathway'}
        description="A guided sequence of warm-ups, drills, and full role-plays. Complete each stage to unlock the next."
      />

      {/* An error replaces the skeleton: a failed load never looks like one still in flight. */}
      {error ? (
        <InlineAlert variant="error">{error}</InlineAlert>
      ) : !data ? (
        <LearnerSkeleton variant="list" />
      ) : (
        <>
          <MotionSection>
            <Card padding="md">
              <div className="flex items-center justify-between gap-4">
                <div>
                  <p className="eyebrow text-muted">Progress</p>
                  <p className="mt-1 text-lg font-bold tabular-nums text-navy">
                    {data.completedStageCount} / {data.totalStages} stages
                  </p>
                </div>
                <p className="text-3xl font-bold text-primary">
                  <CountUp value={progressPercent} suffix="%" />
                </p>
              </div>
              <ProgressBar value={progressPercent} ariaLabel="Speaking pathway progress" className="mt-3" />
            </Card>
          </MotionSection>

          <ol className="space-y-3">
            {data.stages.map((stage, idx) => (
              <li key={stage.code}>
                <MotionItem delayIndex={Math.min(idx, 5)}>
                  <Card
                    padding="md"
                    className={cn(stage.state === 'in_progress' && 'border-primary/40 ring-1 ring-primary/20')}
                  >
                    <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                      <div className="min-w-0 space-y-1">
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="text-xs font-semibold tabular-nums text-muted">
                            {String(idx + 1).padStart(2, '0')}
                          </span>
                          <h2 className="text-base font-semibold text-navy">{stage.title}</h2>
                          {stateBadge(stage.state)}
                          <Badge variant="outline">{activityLabel(stage.activityKind)}</Badge>
                        </div>
                        <p className="text-sm text-muted">{stage.description}</p>
                      </div>
                      {stage.actionHref && stage.state !== 'locked' ? (
                        <Button asChild variant={stage.state === 'in_progress' ? 'primary' : 'outline'} size="sm" className="shrink-0 self-start sm:self-auto">
                          <Link href={stage.actionHref}>{stage.actionLabel ?? 'Continue'}</Link>
                        </Button>
                      ) : null}
                    </div>
                  </Card>
                </MotionItem>
              </li>
            ))}
          </ol>
        </>
      )}
    </>
  );
}
