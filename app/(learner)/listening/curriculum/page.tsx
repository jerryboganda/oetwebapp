'use client';

// Phase 10 of LISTENING-MODULE-PLAN.md — learner-facing 12-stage Listening
// curriculum page. Backed by GET /v1/listening-papers/me/curriculum.

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowRight, CheckCircle2, Lock, Map as MapIcon, Play } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { CardLink } from '@/components/ui/card-link';
import { ErrorState } from '@/components/ui/empty-error';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { apiClient } from '@/lib/api';

interface CurriculumStage {
  order: number;
  code: string;
  title: string;
  focus: string;
  partHint: string;
  estimatedMinutes: number;
  locked: boolean;
  completed: boolean;
  nextActionLabel: string;
  nextActionRoute: string;
}

interface CurriculumDto {
  headline: string;
  completedStages: number;
  totalStages: number;
  stages: CurriculumStage[];
}

function getCurriculum(): Promise<CurriculumDto> {
  return apiClient.get<CurriculumDto>('/v1/listening-papers/me/curriculum');
}

export default function ListeningCurriculumPage() {
  const [data, setData] = useState<CurriculumDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const result = await getCurriculum();
        if (!cancelled) setData(result);
      } catch (e) {
        console.error(e);
        if (!cancelled) setError('Could not load Listening curriculum.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  return (
    <>
      <LearnerPageHero
        eyebrow="Listening · Skills Catalog"
        icon={MapIcon}
        title="The 12-stage Listening skills catalog"
        description="What each Listening skill trains and which drill to open next. For your live progression status, open the Pathway dashboard instead."
      />

      <InlineAlert variant="info" live="polite">
        Tracking actual progress lives on the{' '}
        <Link href="/listening/pathway" className="font-semibold underline">
          Listening Pathway
        </Link>{' '}
        page. This catalog is a skill reference. Every card jumps straight to the matching drill.
      </InlineAlert>

      {loading && (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-32 rounded-2xl" />)}
        </div>
      )}

      {error && <ErrorState message={error} />}

      {data && (
        <MotionSection>
          {/* A section header, not a header card: its sentence repeated the alert above. */}
          <LearnerSurfaceSectionHeader title="Twelve Listening skills to drill, in order" className="mb-4" />
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
            {data.stages.map((stage, index) => (
              <MotionItem key={stage.code} delayIndex={Math.min(index, 5)} className="h-full">
                <StageCard stage={stage} />
              </MotionItem>
            ))}
          </div>
        </MotionSection>
      )}
    </>
  );
}

function StageCard({ stage }: { stage: CurriculumStage }) {
  const stateBadge = stage.completed
    ? <Badge variant="success" className="gap-1"><CheckCircle2 className="h-3 w-3" aria-hidden /> Done</Badge>
    : stage.locked
      ? <Badge variant="muted" className="gap-1"><Lock className="h-3 w-3" aria-hidden /> Locked</Badge>
      : <Badge variant="info" className="gap-1"><Play className="h-3 w-3" aria-hidden /> Available</Badge>;

  const body = (
    <>
      <div className="flex items-center justify-between gap-2">
        <span className="eyebrow min-w-0 text-muted">
          Stage {stage.order} · {stage.partHint}
        </span>
        {stateBadge}
      </div>
      <h3 className="font-semibold text-navy">{stage.title}</h3>
      <p className="text-sm text-muted">{stage.focus}</p>
      <span className="mt-auto text-xs tabular-nums text-muted">≈ {stage.estimatedMinutes} min</span>
      {!stage.locked && !stage.completed && stage.nextActionLabel && (
        <span className="inline-flex items-center gap-1 text-xs font-medium text-primary">
          {stage.nextActionLabel} <ArrowRight className="h-3.5 w-3.5 rtl:rotate-180" aria-hidden />
        </span>
      )}
    </>
  );

  // A locked stage is not a link, so it gets no hover lift.
  if (stage.locked) return <Card className="flex h-full flex-col gap-2">{body}</Card>;
  const route = stage.nextActionRoute || '/listening';
  return <CardLink href={route} className="flex h-full flex-col gap-2">{body}</CardLink>;
}
