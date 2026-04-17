'use client';

import { useState, useEffect, useMemo } from 'react';
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
  const resultId = searchParams?.get('id') ?? 'wr-001';
  const taskId = searchParams?.get('taskId') ?? '';

  const [result, setResult] = useState<WritingResult | null>(null);
  const [model, setModel] = useState<ModelAnswer | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [viewMode, setViewMode] = useState<ViewMode>('side-by-side');

  useEffect(() => {
    analytics.track('content_view', { page: 'writing-compare', resultId });
    Promise.allSettled([fetchWritingResult(resultId), taskId ? fetchModelAnswer(taskId) : Promise.resolve(null)])
      .then(([resR, modelR]) => {
        if (resR.status === 'fulfilled') setResult(resR.value);
        if (modelR.status === 'fulfilled' && modelR.value) setModel(modelR.value);
        if (resR.status === 'rejected') setError('Failed to load your writing result.');
      })
      .finally(() => setLoading(false));
  }, [resultId, taskId]);

  const wordCount = useMemo(() => {
    const body = (result as Record<string, unknown> | null)?.letterBody as string | undefined;
    const modelText = model?.paragraphs?.map(p => p.text).join('\n\n');
    const learnerWords = body?.split(/\s+/).filter(Boolean).length ?? 0;
    const modelWords = modelText?.split(/\s+/).filter(Boolean).length ?? 0;
    return { learner: learnerWords, model: modelWords };
  }, [result, model]);

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
        <Link href={`/writing/result?id=${resultId}`} className="inline-flex items-center gap-1 text-sm text-indigo-600 dark:text-indigo-400 hover:underline">
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
            <div className="p-5 border-b border-gray-200 dark:border-gray-700 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <FileText className="w-5 h-5 text-blue-600 dark:text-blue-400" />
                <h3 className="font-semibold text-gray-900 dark:text-gray-100">Your Response</h3>
              </div>
              <span className="text-xs text-gray-500">{wordCount.learner} words</span>
            </div>
            <div className="p-5 text-sm text-gray-700 dark:text-gray-300 whitespace-pre-wrap leading-relaxed max-h-[600px] overflow-y-auto">
              {(result as Record<string, unknown> | null)?.letterBody as string ?? 'No response available.'}
            </div>
          </Card>
        </MotionSection>

        {/* Model Answer */}
        <MotionSection>
          <Card className="h-full">
            <div className="p-5 border-b border-gray-200 dark:border-gray-700 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <BookOpen className="w-5 h-5 text-green-600 dark:text-green-400" />
                <h3 className="font-semibold text-gray-900 dark:text-gray-100">Model Answer</h3>
              </div>
              {model && <span className="text-xs text-gray-500">{wordCount.model} words</span>}
            </div>
            <div className="p-5 text-sm text-gray-700 dark:text-gray-300 whitespace-pre-wrap leading-relaxed max-h-[600px] overflow-y-auto">
              {model?.paragraphs?.map(p => p.text).join('\n\n') ?? (
                <div className="flex flex-col items-center justify-center py-12 text-gray-400">
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
          <Card className="bg-indigo-50 dark:bg-indigo-900/20 border-indigo-200 dark:border-indigo-800">
            <div className="p-5">
              <h3 className="text-sm font-semibold text-indigo-800 dark:text-indigo-200 mb-2">Study Tips</h3>
              <ul className="list-disc list-inside text-sm text-indigo-700 dark:text-indigo-300 space-y-1">
                <li>Compare the opening and closing paragraphs for tone and formality.</li>
                <li>Note how the model answer organizes key clinical information.</li>
                <li>Look at transition phrases and cohesive devices used in the model.</li>
                <li>Pay attention to word count differences — the model aims for conciseness.</li>
              </ul>
            </div>
          </Card>
        </MotionSection>
      )}
    </LearnerDashboardShell>
  );
}
