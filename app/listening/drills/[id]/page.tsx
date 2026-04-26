'use client';

import { LearnerPageHero, LearnerSurfaceSectionHeader } from "@/components/domain/learner-surface";
import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { getListeningDrill, type ListeningDrillDto } from '@/lib/listening-api';
import { ArrowLeft, Headphones, Target } from 'lucide-react';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { useEffect, useState } from 'react';

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

export default function ListeningDrillPage() {
  const params = useParams<{ id?: string | string[] }>();
  const searchParams = useSearchParams();
  const router = useRouter();
  const drillId = firstParam(params?.id);
  const paperId = searchParams?.get('paperId') ?? undefined;
  const attemptId = searchParams?.get('attemptId') ?? undefined;
  const [drill, setDrill] = useState<ListeningDrillDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!drillId) return;
    analytics.track('content_view', { page: 'listening-drill', drillId });
    getListeningDrill(drillId, { paperId, attemptId })
      .then(setDrill)
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load this listening drill.'))
      .finally(() => setLoading(false));
  }, [attemptId, drillId, paperId]);

  return (
    <LearnerDashboardShell pageTitle="Listening Drill" subtitle="Focused error-type practice for listening accuracy." backHref="/listening">
      <div className="space-y-8">
        <Button variant="ghost" className="gap-2" onClick={() => router.push('/listening')}>
          <ArrowLeft className="h-4 w-4" />
          Back to listening
        </Button>

        {loading ? <Skeleton className="h-48 rounded-2xl" /> : null}
        {!loading && error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {!loading && drill ? (
          <>
            <LearnerPageHero
              eyebrow="Listening Drill"
              icon={Headphones}
              accent="indigo"
              title={drill.title}
              description={drill.description}
              highlights={[
                { icon: Target, label: 'Focus', value: drill.focusLabel },
                { icon: Headphones, label: 'Duration', value: `${drill.estimatedMinutes} minutes` },
                { icon: Target, label: 'Review handoff', value: drill.reviewRoute === '/listening' ? 'After submit' : 'Transcript route ready' },
              ]}
            />

            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <LearnerSurfaceSectionHeader
                eyebrow="How to use this drill"
                title="The drill tells the learner exactly what to practise"
                description="This keeps drill content aligned with OET listening error patterns instead of generic audio practice."
                className="mb-4"
              />
              <div className="space-y-3">
                {drill.highlights.map((highlight) => (
                  <div key={highlight} className="rounded-2xl border border-border bg-background-light p-4 text-sm text-navy">
                    {highlight}
                  </div>
                ))}
              </div>
            </section>

            <section className="grid gap-4 md:grid-cols-2">
              <Button fullWidth onClick={() => router.push(drill.launchRoute)}>
                <Target className="h-4 w-4" />
                Launch drill audio
              </Button>
              <Button variant="outline" fullWidth onClick={() => router.push(drill.reviewRoute)}>
                Open transcript-backed review
              </Button>
            </section>
          </>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
