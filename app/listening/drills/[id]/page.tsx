'use client';

import { useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, Headphones, Target } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { analytics } from '@/lib/analytics';
import { fetchListeningDrill } from '@/lib/api';
import type { ListeningDrill } from '@/lib/mock-data';

export default function ListeningDrillPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const drillId = params?.id;
  const [drill, setDrill] = useState<ListeningDrill | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!drillId) return;
    analytics.track('content_view', { page: 'listening-drill', drillId });
    fetchListeningDrill(drillId)
      .then(setDrill)
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load this listening drill.'))
      .finally(() => setLoading(false));
  }, [drillId]);

  return (
    <LearnerDashboardShell pageTitle="Listening Drill" subtitle="Focused error-type practice for listening accuracy." backHref="/listening">
      <div className="space-y-8">
        <Button variant="ghost" className="gap-2" onClick={() => router.push('/listening')}>
          <ArrowLeft className="h-4 w-4" />
          Back to listening
        </Button>

        {loading ? <Skeleton className="h-48 rounded-[24px]" /> : null}
        {!loading && error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {!loading && drill ? (
          <>
            <LearnerPageHero
              eyebrow="Listening Drill"
              icon={Headphones}
              accent="indigo"
              title={drill.title}
              description="Use this drill to isolate one listening error pattern before you return to a full task or transcript review."
              highlights={[
                { icon: Target, label: 'Focus', value: drill.focusLabel },
                { icon: Headphones, label: 'Duration', value: `${drill.estimatedMinutes} minutes` },
                { icon: Target, label: 'Review handoff', value: 'Transcript route ready' },
              ]}
            />

            <section className="rounded-[28px] border border-gray-200 bg-surface p-6 shadow-sm">
              <LearnerSurfaceSectionHeader
                eyebrow="How to use this drill"
                title="The drill tells the learner exactly what to practise"
                description="This keeps drill content aligned with OET listening error patterns instead of generic audio practice."
                className="mb-4"
              />
              <div className="space-y-3">
                {drill.highlights.map((highlight) => (
                  <div key={highlight} className="rounded-2xl border border-gray-100 bg-background-light p-4 text-sm text-navy">
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
