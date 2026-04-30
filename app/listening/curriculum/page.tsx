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
          eyebrow="Listening · Curriculum"
          title="Your 12-stage Listening pathway"
          description="From numbers and dosages through MCQ trap-spotting to a full one-play mock — work through each stage in order."
        />

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
                title: data.headline,
                description: `${data.completedStages} of ${data.totalStages} stages complete.`,
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
    <div className="rounded-2xl border border-slate-200 bg-white p-5 dark:border-slate-800 dark:bg-slate-900 h-full flex flex-col gap-2">
      <div className="flex items-center justify-between">
        <span className="text-xs font-medium uppercase tracking-wide text-slate-500">
          Stage {stage.order} · {stage.partHint}
        </span>
        {stateBadge}
      </div>
      <h3 className="font-semibold text-slate-900 dark:text-slate-100">{stage.title}</h3>
      <p className="text-sm text-slate-600 dark:text-slate-300">{stage.focus}</p>
      <span className="mt-auto text-xs text-slate-500">≈ {stage.estimatedMinutes} min</span>
    </div>
  );

  if (stage.locked || stage.completed) return body;
  // Available: link to the Listening hub for now (drill routing lands when
  // stages are tied to concrete drill ids in a follow-up).
  return <Link href="/listening" className="block">{body}</Link>;
}
