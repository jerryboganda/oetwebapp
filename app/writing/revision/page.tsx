'use client';

import { useState, useEffect } from 'react';
import {
  ChevronLeft,
  ArrowRight,
  AlertCircle,
  TrendingUp,
  Minus,
} from 'lucide-react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { AppShell } from '@/components/layout/app-shell';
import { RevisionDiffViewer } from '@/components/domain/revision-diff-viewer';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchWritingRevisionData } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { CriteriaDelta } from '@/lib/mock-data';

export default function WritingRevisionMode() {
  const searchParams = useSearchParams();
  const resultId = searchParams?.get('id') ?? 'we-001';
  const [deltas, setDeltas] = useState<CriteriaDelta[]>([]);
  const [originalText, setOriginalText] = useState('');
  const [revisedText, setRevisedText] = useState('');
  const [unresolvedIssues, setUnresolvedIssues] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('content_view', { content: 'revision', resultId, subtest: 'writing' });
    fetchWritingRevisionData(resultId)
      .then((data) => {
        setDeltas(data.deltas);
        setOriginalText(data.originalText);
        setRevisedText(data.revisedText);
        setUnresolvedIssues(data.unresolvedIssues);
      })
      .catch(() => setError('Failed to load revision data. Please try again.'))
      .finally(() => setLoading(false));
  }, [resultId]);

  if (loading) {
    return (
      <AppShell pageTitle="Revision Mode">
        <div className="max-w-7xl mx-auto p-6 space-y-6">
          <Skeleton className="h-40 rounded-2xl" />
          <Skeleton className="h-[240px] rounded-2xl sm:h-[280px] lg:h-80" />
        </div>
      </AppShell>
    );
  }

  if (error) {
    return (
      <AppShell pageTitle="Revision Mode">
        <div className="max-w-3xl mx-auto p-6">
          <InlineAlert variant="error">{error}</InlineAlert>
        </div>
      </AppShell>
    );
  }

  return (
    <AppShell pageTitle="Revision Mode" distractionFree>
      {/* Toolbar */}
      <header className="bg-white border-b border-gray-200 shrink-0 px-4 sm:px-6 py-3 flex items-center justify-between z-10">
        <div className="flex items-center gap-4">
          <Link href={`/writing/feedback?id=${resultId}`} className="text-gray-500 hover:text-navy transition-colors"><ChevronLeft className="w-5 h-5" /></Link>
          <div>
            <h1 className="font-bold text-lg text-navy leading-tight">Revision Mode</h1>
            <div className="text-xs text-muted">Compare original vs. revised submission</div>
          </div>
        </div>
        <Link href="/writing"><Button variant="ghost" size="sm">Done</Button></Link>
      </header>

      <main className="flex-1 overflow-y-auto p-4 sm:p-6 lg:p-8">
        <div className="max-w-7xl mx-auto space-y-6">

          {/* Top Section: Delta Summary + Unresolved Issues */}
          <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
            {/* Criterion Delta Summary */}
            <MotionSection className="lg:col-span-2">
              <Card className="p-6">
                <h2 className="text-lg font-bold text-navy mb-4 flex items-center gap-2"><TrendingUp className="w-5 h-5 text-primary" /> Criterion Delta Summary</h2>
                <div className="grid grid-cols-2 sm:grid-cols-3 gap-4">
                  {deltas.map(delta => {
                    const diff = delta.revised - delta.original;
                    return (
                      <div key={delta.name} className="bg-gray-50 rounded-xl p-3 border border-gray-100">
                        <div className="text-xs font-bold text-muted uppercase tracking-wider mb-2 truncate" title={delta.name}>{delta.name}</div>
                        <div className="flex items-center justify-between">
                          <div className="flex items-center gap-2 text-sm font-medium text-gray-600">
                            <span>{delta.original}</span>
                            <ArrowRight className="w-3 h-3 text-gray-400" />
                            <span className={diff > 0 ? 'text-green-600 font-bold' : ''}>{delta.revised}</span>
                            <span className="text-xs text-gray-400">/ {delta.max}</span>
                          </div>
                          {diff > 0 ? (
                            <span className="inline-flex items-center justify-center w-6 h-6 rounded-full bg-green-100 text-green-700 text-xs font-bold">+{diff}</span>
                          ) : (
                            <span className="inline-flex items-center justify-center w-6 h-6 rounded-full bg-gray-100 text-gray-500 text-xs font-bold"><Minus className="w-3 h-3" /></span>
                          )}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </Card>
            </MotionSection>

            {/* Unresolved Issues */}
            <MotionSection delayIndex={1}>
              <div className="bg-amber-50 rounded-2xl border border-amber-200 p-6 shadow-sm flex flex-col h-full">
                <h2 className="text-lg font-bold text-amber-900 mb-4 flex items-center gap-2"><AlertCircle className="w-5 h-5" /> Unresolved Issues</h2>
                <ul className="space-y-3 flex-1">
                  {unresolvedIssues.map((issue, idx) => (
                    <li key={idx} className="flex items-start gap-2 text-sm text-amber-800">
                      <span className="w-1.5 h-1.5 rounded-full bg-amber-500 mt-1.5 shrink-0" />
                      <span className="leading-snug">{issue}</span>
                    </li>
                  ))}
                </ul>
              </div>
            </MotionSection>
          </div>

          {/* Diff Viewer */}
          <MotionSection delayIndex={2}>
            <RevisionDiffViewer original={originalText} revised={revisedText} />
          </MotionSection>
        </div>
      </main>
    </AppShell>
  );
}
