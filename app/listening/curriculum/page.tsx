'use client';

// Phase 10 of LISTENING-MODULE-PLAN.md — learner-facing 12-stage Listening
// curriculum page. Backed by GET /v1/listening-papers/me/curriculum.

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { CheckCircle2, Lock, Play } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceCard } from '@/components/domain';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { MotionItem } from '@/components/ui/motion-primitives';
import { ensureFreshAccessToken } from '@/lib/auth-client';
import { fetchWithTimeout } from '@/lib/network/fetch-with-timeout';
import { env } from '@/lib/env';

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

async function getCurriculum(): Promise<CurriculumDto> {
  const token = await ensureFreshAccessToken();
  const headers = new Headers({ Accept: 'application/json' });
  if (token) headers.set('Authorization', `Bearer ${token}`);
  const base = env.apiBaseUrl || '';
  const url = `${base.replace(/\/$/, '')}/v1/listening-papers/me/curriculum`;
  const res = await fetchWithTimeout(url, { headers, credentials: 'include' });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return (await res.json()) as CurriculumDto;
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
    <LearnerDashboardShell>
      <div className="space-y-8">
        <LearnerPageHero
          eyebrow="Listening · Skills Catalog"
          title="The 12-stage Listening skills catalog"
          description="What each Listening skill trains and which drill to open next. For your live progression status, open the Pathway dashboard instead."
        />

        <InlineAlert variant="info">
          Tracking actual progress lives on the{' '}
          <Link href="/listening/pathway" className="font-semibold underline">
            Listening Pathway
          </Link>{' '}
          page. This catalog is a skill reference. Every card jumps straight to the matching drill.
        </InlineAlert>

        {loading && (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-32" />)}
          </div>
        )}

        {error && <InlineAlert variant="warning">{error}</InlineAlert>}

        {data && (
          <>
            <LearnerSurfaceCard
              card={{
                kind: 'insight',
                sourceType: 'frontend_navigation',
                accent: 'indigo',
                title: 'Twelve Listening skills to drill, in order',
                description: 'Browse below and open any unlocked skill to launch its drill. Progression lives on the Pathway page.',
              }}
            />

            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
              {data.stages.map((stage) => (
                <MotionItem key={stage.code}>
                  <StageCard stage={stage} />
                </MotionItem>
              ))}
            </div>
          </>
        )}
      </div>
    </LearnerDashboardShell>
  );
}

function StageCard({ stage }: { stage: CurriculumStage }) {
  const stateBadge = stage.completed
    ? <Badge variant="success"><CheckCircle2 className="w-3 h-3 mr-1 inline" /> Done</Badge>
    : stage.locked
      ? <Badge variant="muted"><Lock className="w-3 h-3 mr-1 inline" /> Locked</Badge>
      : <Badge variant="info"><Play className="w-3 h-3 mr-1 inline" /> Available</Badge>;

  const body = (
    <div className="rounded-2xl border border-border bg-surface p-5 h-full flex flex-col gap-2">
      <div className="flex items-center justify-between">
        <span className="text-xs font-medium uppercase tracking-wide text-muted">
          Stage {stage.order} · {stage.partHint}
        </span>
        {stateBadge}
      </div>
      <h3 className="font-semibold text-navy">{stage.title}</h3>
      <p className="text-sm text-muted">{stage.focus}</p>
      <span className="mt-auto text-xs text-muted">≈ {stage.estimatedMinutes} min</span>
      {!stage.locked && !stage.completed && stage.nextActionLabel && (
        <span className="text-xs font-medium text-primary">
          {stage.nextActionLabel} →
        </span>
      )}
    </div>
  );

  if (stage.locked) return body;
  const route = stage.nextActionRoute || '/listening';
  return <Link href={route} className="block">{body}</Link>;
}
