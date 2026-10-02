'use client';

import { LearnerNavActions } from "@/components/layout/learner-dashboard-shell";
import { LearnerPageHero } from '@/components/domain';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card, cardClassName } from '@/components/ui/card';
import { ErrorState } from '@/components/ui/empty-error';
import { MotionFadeSwitch } from '@/components/ui/motion-primitives';
import { ProgressBar } from '@/components/ui/progress';
import { PageSkeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { fetchPhrasingData } from '@/lib/api';
import type { PhrasingSegment } from '@/lib/mock-data';
import { cn } from '@/lib/utils';
import {
    AlertCircle, ChevronLeft, ChevronRight, MessageSquare, Volume2, Zap
} from 'lucide-react';
import { useParams, useRouter } from 'next/navigation';
import { Suspense, useEffect, useState } from 'react';

function BetterPhrasingContent() {
  const params = useParams();
  const router = useRouter();
  const rawId = params?.id;
  const id = Array.isArray(rawId) ? rawId[0] ?? '' : rawId ?? '';

  // --- Data State ---
  const [title, setTitle] = useState('');
  const [segments, setSegments] = useState<PhrasingSegment[]>([]);
  const [disclaimer, setDisclaimer] = useState<string | undefined>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);

  // --- UI State ---
  const [currentIndex, setCurrentIndex] = useState(0);

  useEffect(() => {
    fetchPhrasingData(id)
      .then((result) => {
        setTitle(result.title);
        setSegments(result.segments);
        setDisclaimer(result.disclaimer);
        analytics.track('content_view', { contentId: id, subtest: 'speaking', type: 'phrasing' });
      })
      .catch(() => setError(true))
      .finally(() => setLoading(false));
  }, [id]);

  const currentSegment = segments[currentIndex];
  const progress = segments.length > 0 ? ((currentIndex + 1) / segments.length) * 100 : 0;

  const handleNext = () => {
    if (currentIndex < segments.length - 1) {
      setCurrentIndex(prev => prev + 1);
    } else {
      router.push(`/speaking/results/${id}`);
    }
  };

  const handlePrev = () => {
    if (currentIndex > 0) {
      setCurrentIndex(prev => prev - 1);
    }
  };

  if (loading) {
    return (
      <>
        <LearnerSkeleton variant="hero" />
        <LearnerSkeleton variant="card-grid" />
      </>
    );
  }

  if (error || segments.length === 0) {
    return <ErrorState message="Could not load phrasing data. Please try again." />;
  }

  return (
    <>
      <LearnerNavActions>
        <div className="flex items-center gap-3">
          <span className="text-xs font-bold tabular-nums text-muted">
            Segment {currentIndex + 1} of {segments.length}
          </span>
          <ProgressBar value={progress} ariaLabel="Phrasing review progress" className="hidden w-32 md:block" />
        </div>
      </LearnerNavActions>

      <LearnerPageHero
        eyebrow="Phrasing review"
        icon={MessageSquare}
        accent="speaking"
        title={title}
      />

      {disclaimer ? (
        <InlineAlert variant="info">{disclaimer}</InlineAlert>
      ) : null}

      <MotionFadeSwitch activeKey={currentSegment.id} className="space-y-6">
        {/* Original Phrase Card */}
        <Card padding="lg">
          <div className="mb-4 flex items-center gap-3">
            <span className="flex h-8 w-8 items-center justify-center rounded-lg bg-background-light">
              <MessageSquare className="h-4 w-4 text-muted" aria-hidden="true" />
            </span>
            <h2 className="eyebrow text-muted">Your Original Phrase</h2>
          </div>
          <p className="text-xl font-medium italic leading-relaxed text-navy">
            &quot;{currentSegment.originalPhrase}&quot;
          </p>

          <div className="mt-8 border-t border-border pt-8">
            <div className="flex items-start gap-3">
              <AlertCircle className="mt-0.5 h-5 w-5 shrink-0 text-warning-strong" aria-hidden />
              <div>
                <h3 className="eyebrow mb-1 text-warning-strong">Issue Explanation</h3>
                <p className="text-sm leading-relaxed text-muted">{currentSegment.issueExplanation}</p>
              </div>
            </div>
          </div>
        </Card>

        {/* Stronger Alternative Card: a tinted surface, not a second dark hero. */}
        <section className={cn(cardClassName({ padding: 'lg' }), 'border-primary/30 bg-primary/5')}>
          <div className="mb-6 flex items-center gap-3">
            <Zap className="h-5 w-5 shrink-0 text-primary" aria-hidden />
            <h2 className="eyebrow text-primary">Stronger Alternative</h2>
          </div>
          <p className="mb-8 text-2xl font-bold leading-tight tracking-tight text-navy">
            {currentSegment.strongerAlternative}
          </p>
          <div className="border-t border-primary/20 pt-6">
            <div className="mb-3 flex items-center gap-3">
              <Volume2 className="h-4 w-4 text-primary" aria-hidden="true" />
              <h3 className="eyebrow text-muted">Drill Prompt</h3>
            </div>
            <p className="text-sm leading-relaxed text-navy/80">{currentSegment.drillPrompt}</p>
          </div>
        </section>
      </MotionFadeSwitch>

      {/* Segment controls */}
      <nav aria-label="Phrasing segments" className={cn(cardClassName({ padding: 'md' }), 'flex items-center justify-between gap-3')}>
        <Button variant="ghost" onClick={handlePrev} disabled={currentIndex === 0}>
          <ChevronLeft className="h-5 w-5 rtl:rotate-180" aria-hidden="true" /> Previous
        </Button>

        <div className="hidden items-center gap-2 sm:flex" aria-hidden="true">
          {segments.map((_, i) => (
            <span
              key={i}
              className={`h-2 rounded-full transition-colors duration-300 ${i === currentIndex ? 'w-6 bg-primary' : 'w-2 bg-border'}`}
            />
          ))}
        </div>

        <Button variant="ghost" onClick={handleNext}>
          {currentIndex === segments.length - 1 ? 'Finish Review' : 'Next Segment'} <ChevronRight className="h-5 w-5 rtl:rotate-180" aria-hidden="true" />
        </Button>
      </nav>
    </>
  );
}

export default function BetterPhrasingView() {
  return (
    <Suspense fallback={<PageSkeleton />}>
      <BetterPhrasingContent />
    </Suspense>
  );
}
