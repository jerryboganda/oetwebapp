'use client';

import { Suspense, useState, useEffect } from 'react';
import { FileText, BookOpen, ArrowLeftRight, ChevronLeft } from 'lucide-react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { EmptyState } from '@/components/ui/empty-error';
import { PageSkeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchWritingResult, fetchModelAnswer } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { WritingResult, ModelAnswer } from '@/lib/mock-data';

type ViewMode = 'side-by-side' | 'overlay';

function WritingCompareContent() {
  const searchParams = useSearchParams();
  const resultId = searchParams?.get('id') ?? '';
  const taskId = searchParams?.get('taskId') ?? '';

  const [result, setResult] = useState<WritingResult | null>(null);
  const [model, setModel] = useState<ModelAnswer | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [viewMode, setViewMode] = useState<ViewMode>('side-by-side');

  useEffect(() => {
    analytics.track('content_view', { page: 'writing-compare', resultId });
    if (!resultId) {
      return;
    }
    Promise.allSettled([fetchWritingResult(resultId), taskId ? fetchModelAnswer(taskId) : Promise.resolve(null)])
      .then(([resR, modelR]) => {
        if (resR.status === 'fulfilled') setResult(resR.value);
        if (modelR.status === 'fulfilled' && modelR.value) setModel(modelR.value);
        if (resR.status === 'rejected') setError('Failed to load your writing result.');
      })
      .finally(() => setLoading(false));
  }, [resultId, taskId]);

  // Word counting decommissioned per Writing Module Spec v1.0
  // (Dr. Ahmed Hesham). The compare page no longer surfaces or computes counts.

  if (!resultId) {
    return <EmptyState icon={<ArrowLeftRight className="h-8 w-8" />} title="Open compare from a completed writing result." />;
  }

  if (loading) {
    return (
      <>
        <LearnerSkeleton variant="hero" />
        <LearnerSkeleton variant="card-grid" />
      </>
    );
  }

  return (
    <>
      <LearnerPageHero
        title="Writing Comparison"
        description="Compare your response with the model answer side by side."
        icon={ArrowLeftRight}
        aside={
          <Button asChild variant="outline" size="sm">
            <Link href={`/writing/result?id=${resultId}`}>
              <ChevronLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" /> Back to result
            </Link>
          </Button>
        }
      />

      {error && <InlineAlert variant="error" title="Error">{error}</InlineAlert>}

      {/* View Mode Toggle */}
      <div className="flex flex-wrap items-center gap-2">
        <Button
          size="sm"
          variant={viewMode === 'side-by-side' ? 'primary' : 'outline'}
          aria-pressed={viewMode === 'side-by-side'}
          onClick={() => setViewMode('side-by-side')}
        >
          Side by Side
        </Button>
        <Button
          size="sm"
          variant={viewMode === 'overlay' ? 'primary' : 'outline'}
          aria-pressed={viewMode === 'overlay'}
          onClick={() => setViewMode('overlay')}
        >
          Stacked View
        </Button>
      </div>

      {/* Criteria Scores */}
      {result?.criteria && (
        <MotionSection className="space-y-3">
          <LearnerSurfaceSectionHeader icon={FileText} title="Criteria Scores" />
          <div className="flex flex-wrap gap-3">
            {result.criteria.map((c, index) => (
              <MotionItem key={c.name} delayIndex={Math.min(index, 5)}>
                <Badge variant="muted" size="md" className="tabular-nums">
                  {c.name}: {c.score}/{c.maxScore}
                </Badge>
              </MotionItem>
            ))}
          </div>
        </MotionSection>
      )}

      {/* Comparison View */}
      <div className={viewMode === 'side-by-side' ? 'grid grid-cols-1 gap-6 lg:grid-cols-2' : 'space-y-6'}>
        {/* Learner's Response */}
        <MotionItem delayIndex={0} className="min-w-0">
          <Card padding="none" className="h-full">
            <div className="flex items-center gap-2 border-b border-border p-4 sm:p-5">
              <FileText className="h-5 w-5 text-info" aria-hidden="true" />
              <h2 className="font-semibold text-navy">Your Response</h2>
            </div>
            <div className="max-h-[600px] overflow-y-auto whitespace-pre-wrap p-4 text-sm leading-relaxed text-navy sm:p-5" dir="ltr">
              {(result as Record<string, unknown> | null)?.letterBody as string ?? 'No response available.'}
            </div>
          </Card>
        </MotionItem>

        {/* Model Answer */}
        <MotionItem delayIndex={1} className="min-w-0">
          <Card padding="none" className="h-full">
            <div className="flex items-center gap-2 border-b border-border p-4 sm:p-5">
              <BookOpen className="h-5 w-5 text-success-strong" aria-hidden="true" />
              <h2 className="font-semibold text-navy">Model Answer</h2>
            </div>
            <div className="max-h-[600px] overflow-y-auto whitespace-pre-wrap p-4 text-sm leading-relaxed text-navy sm:p-5" dir="ltr">
              {model?.paragraphs?.map(p => p.text).join('\n\n') ?? (
                <div className="flex flex-col items-center justify-center py-12 text-muted">
                  <BookOpen className="mb-3 h-10 w-10 opacity-60" aria-hidden="true" />
                  <p className="text-sm">Model answer not available for this task.</p>
                </div>
              )}
            </div>
          </Card>
        </MotionItem>
      </div>

      {/* Key Differences callout */}
      {model && result && (
        <MotionSection>
          <Card padding="lg" className="border-primary/30 bg-primary/10">
            <h2 className="mb-2 text-sm font-semibold text-primary">Study Tips</h2>
            <ul className="list-inside list-disc space-y-1 text-sm text-primary">
              <li>Compare the opening and closing paragraphs for tone and formality.</li>
              <li>Note how the model answer organizes key clinical information.</li>
              <li>Look at transition phrases and cohesive devices used in the model.</li>
              <li>Notice how the model letter favours conciseness and reader-aware structure (target body length is 180-200 words, guidance only).</li>
            </ul>
          </Card>
        </MotionSection>
      )}
    </>
  );
}

export default function WritingComparePage() {
  return (
    <Suspense fallback={<PageSkeleton />}>
      <WritingCompareContent />
    </Suspense>
  );
}
