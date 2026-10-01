'use client';

import { Suspense, useEffect, useState } from 'react';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { Headphones, Target } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { ErrorState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { PageSkeleton, Skeleton } from '@/components/ui/skeleton';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { analytics } from '@/lib/analytics';
import { getListeningDrill, type ListeningDrillDto } from '@/lib/listening-api';

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function ListeningDrillContent() {
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

  // The breadcrumb's "Listening" crumb is the way back, so no back button above the header.
  return (
    <>
      {loading ? <Skeleton className="h-48 rounded-2xl" /> : null}
      {!loading && error ? <ErrorState message={error} /> : null}

      {!loading && drill ? (
        <>
          <LearnerPageHero
            eyebrow="Listening Drill"
            icon={Headphones}
            accent="listening"
            title={drill.title}
            description={drill.description}
            highlights={[
              { icon: Target, label: 'Focus', value: drill.focusLabel },
              { icon: Headphones, label: 'Duration', value: `${drill.estimatedMinutes} minutes` },
              { icon: Target, label: 'Review handoff', value: drill.reviewRoute === '/listening' ? 'After submit' : 'Transcript route ready' },
            ]}
          />

          <MotionSection>
            <Card padding="lg">
              <LearnerSurfaceSectionHeader
                eyebrow="How to use this drill"
                title="The drill tells the learner exactly what to practise"
                description="This keeps drill content aligned with OET listening error patterns instead of generic audio practice."
                className="mb-4"
              />
              {/* Plain rows on the card: no cards inside the card. */}
              <ul className="divide-y divide-border">
                {drill.highlights.map((highlight, index) => (
                  <li key={highlight}>
                    <MotionItem delayIndex={Math.min(index, 5)} className="py-3 text-sm text-navy">
                      {highlight}
                    </MotionItem>
                  </li>
                ))}
              </ul>
            </Card>
          </MotionSection>

          <section className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <Button fullWidth onClick={() => router.push(drill.launchRoute)}>
              <Target className="h-4 w-4" aria-hidden />
              Launch drill audio
            </Button>
            <Button variant="outline" fullWidth onClick={() => router.push(drill.reviewRoute)}>
              Open transcript-backed review
            </Button>
          </section>
        </>
      ) : null}
    </>
  );
}

export default function ListeningDrillPage() {
  return (
    <Suspense fallback={<PageSkeleton />}>
      <ListeningDrillContent />
    </Suspense>
  );
}
