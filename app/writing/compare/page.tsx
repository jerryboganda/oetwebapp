'use client';

import { useState, useEffect } from 'react';
import { FileText, BookOpen, ArrowLeftRight, ChevronLeft } from 'lucide-react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchWritingResult, fetchModelAnswer } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { WritingResult, ModelAnswer } from '@/lib/mock-data';

type ViewMode = 'side-by-side' | 'overlay';

export default function WritingComparePage() {
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
    return (
      <LearnerDashboardShell pageTitle="Compare Attempts">
        <div className="p-6">
          <InlineAlert variant="warning">Open compare from a completed writing result.</InlineAlert>
        </div>
      </LearnerDashboardShell>
    );
  }

  if (loading) {
    return (
      <LearnerDashboardShell>
        <div className="space-y-4 p-6">
          <Skeleton className="h-10 w-60" />
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <Skeleton className="h-96 w-full rounded-xl" />
            <Skeleton className="h-96 w-full rounded-xl" />
          </div>
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <div className="mb-4">
        <Link href={`/writing/result?id=${resultId}`} className="inline-flex items-center gap-1 text-sm text-primary hover:underline">
          <ChevronLeft className="w-4 h-4" /> Back to result
        </Link>
      </div>

      <LearnerPageHero
        title="Writing Comparison"
        description="Compare your response with the model answer side by side."
        icon={<ArrowLeftRight className="w-7 h-7" />}
      />

      {error && <InlineAlert variant="error" title="Error">{error}</InlineAlert>}

      {/* View Mode Toggle */}
      <div className="flex items-center gap-2 mb-6">
        <Button
          size="sm"
          variant={viewMode === 'side-by-side' ? 'primary' : 'outline'}
          onClick={() => setViewMode('side-by-side')}
        >
          Side by Side
        </Button>
        <Button
          size="sm"
          variant={viewMode === 'overlay' ? 'primary' : 'outline'}
          onClick={() => setViewMode('overlay')}
        >
          Stacked View
        </Button>
      </div>

      {/* Criteria Scores */}
      {result?.criteria && (
        <MotionSection className="mb-6">
          <LearnerSurfaceSectionHeader
            icon={<FileText className="w-5 h-5" />}
            title="Criteria Scores"
          />
          <div className="flex flex-wrap gap-3">
            {result.criteria.map((c) => (
              <MotionItem key={c.name}>
                <Badge variant="default" className="text-sm px-3 py-1.5">
                  {c.name}: {c.score}/{c.maxScore}
                </Badge>
              </MotionItem>
            ))}
          </div>
        </MotionSection>
      )}

      {/* Comparison View */}
      <div className={viewMode === 'side-by-side' ? 'grid grid-cols-1 lg:grid-cols-2 gap-6' : 'space-y-6'}>
        {/* Learner's Response */}
        <MotionSection>
          <Card className="h-full">
            <div className="p-5 border-b border-border flex items-center justify-between">
              <div className="flex items-center gap-2">
                <FileText className="w-5 h-5 text-info" />
                <h3 className="font-semibold text-navy">Your Response</h3>
              </div>
            </div>
            <div className="p-5 text-sm text-navy whitespace-pre-wrap leading-relaxed max-h-[600px] overflow-y-auto">
              {(result as Record<string, unknown> | null)?.letterBody as string ?? 'No response available.'}
            </div>
          </Card>
        </MotionSection>

        {/* Model Answer */}
        <MotionSection>
          <Card className="h-full">
            <div className="p-5 border-b border-border flex items-center justify-between">
              <div className="flex items-center gap-2">
                <BookOpen className="w-5 h-5 text-success" />
                <h3 className="font-semibold text-navy">Model Answer</h3>
              </div>
            </div>
            <div className="p-5 text-sm text-navy whitespace-pre-wrap leading-relaxed max-h-[600px] overflow-y-auto">
              {model?.paragraphs?.map(p => p.text).join('\n\n') ?? (
                <div className="flex flex-col items-center justify-center py-12 text-muted/60">
                  <BookOpen className="w-10 h-10 mb-3 opacity-60" />
                  <p className="text-sm">Model answer not available for this task.</p>
                </div>
              )}
            </div>
          </Card>
        </MotionSection>
      </div>

      {/* Key Differences callout */}
      {model && result && (
        <MotionSection className="mt-6">
          <Card className="bg-primary/10 border-primary/30">
            <div className="p-5">
              <h3 className="text-sm font-semibold text-primary mb-2">Study Tips</h3>
              <ul className="list-disc list-inside text-sm text-primary space-y-1">
                <li>Compare the opening and closing paragraphs for tone and formality.</li>
                <li>Note how the model answer organizes key clinical information.</li>
                <li>Look at transition phrases and cohesive devices used in the model.</li>
                <li>Notice how the model letter favours conciseness and reader-aware structure (target body length is 180–200 words — guidance only).</li>
              </ul>
            </div>
          </Card>
        </MotionSection>
      )}
    </LearnerDashboardShell>
  );
}
