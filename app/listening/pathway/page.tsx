'use client';

import { useEffect, useMemo, useState } from 'react';
import { Activity, CheckCircle2, Headphones, Target } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { PathwayBoard, pathwayStageLabel, type PathwayStageView } from '@/components/domain/listening/PathwayBoard';
import { listeningV2Api } from '@/lib/listening/v2-api';

/**
 * Listening V2 pathway dashboard. Renders the 12-stage board from
 * `/v1/listening/v2/me/pathway`.
 */
export default function ListeningPathwayPage() {
  const [stages, setStages] = useState<PathwayStageView[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let alive = true;
    listeningV2Api
      .myPathway()
      .then((rows) => {
        if (!alive) return;
        setStages(
          rows.map((r) => ({
            stageKey: r.stage,
            status: r.status as PathwayStageView['status'],
            bestScaledScore: r.scaledScore ?? null,
            attemptsCount: r.completedAt ? 1 : 0,
            actionHref: r.actionHref ?? undefined,
          })),
        );
      })
      .catch((e) => {
        if (alive) setError(e instanceof Error ? e.message : String(e));
      });
    return () => {
      alive = false;
    };
  }, []);

  const summary = useMemo(() => {
    const rows = stages ?? [];
    const completed = rows.filter((stage) => stage.status === 'Completed').length;
    const active = rows.find((stage) => stage.status === 'InProgress')
      ?? rows.find((stage) => stage.status === 'Unlocked')
      ?? rows[0]
      ?? null;
    const bestScaled = rows
      .map((stage) => stage.bestScaledScore)
      .filter((score): score is number => typeof score === 'number')
      .sort((left, right) => right - left)[0] ?? null;

    return { completed, active, bestScaled };
  }, [stages]);

  const progressPercent = stages?.length ? Math.round((summary.completed / stages.length) * 100) : 0;

  return (
    <LearnerDashboardShell pageTitle="Listening Pathway" subtitle="Twelve-stage Listening progression." backHref="/listening">
      <div className="space-y-8 pb-24">
        <LearnerPageHero
          eyebrow="Listening · Pathway"
          title="Your 12-stage Listening pathway"
          description="Move from diagnostic placement through focused Part A/B/C foundations, mini-tests, full papers, and final OET@Home simulation."
          icon={Headphones}
          accent="indigo"
          highlights={[
            { icon: CheckCircle2, label: 'Completed', value: stages ? `${summary.completed}/${stages.length}` : '--' },
            { icon: Activity, label: 'Current', value: summary.active ? pathwayStageLabel(summary.active.stageKey) : 'Loading' },
            { icon: Target, label: 'Best score', value: summary.bestScaled != null ? `${summary.bestScaled}/500` : '--' },
          ]}
        />

        {error ? <InlineAlert variant="warning" title="Pathway unavailable">{error}</InlineAlert> : null}

        {!stages && !error ? (
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {Array.from({ length: 6 }).map((_, index) => <Skeleton key={index} className="h-48 rounded-2xl" />)}
          </div>
        ) : null}

        {stages ? (
          <>
            <Card className="space-y-4 p-5">
              <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
                <div>
                  <p className="text-xs font-black uppercase tracking-[0.16em] text-muted">Pathway progress</p>
                  <h2 className="mt-1 text-xl font-bold text-navy">{progressPercent}% complete</h2>
                </div>
                <p className="text-sm font-semibold text-muted">{summary.completed} of {stages.length} stages complete</p>
              </div>
              <div
                className="h-3 overflow-hidden rounded-full bg-lavender/70"
                role="progressbar"
                aria-label="Listening pathway progress"
                aria-valuemin={0}
                aria-valuemax={100}
                aria-valuenow={progressPercent}
              >
                <div className="h-full rounded-full bg-primary" style={{ width: `${progressPercent}%` }} />
              </div>
            </Card>

            <PathwayBoard stages={stages} />
          </>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}

