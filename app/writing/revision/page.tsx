'use client';

import { LearnerPageHero, LearnerSurfaceSectionHeader } from "@/components/domain/learner-surface";
import { RevisionDiffViewer } from '@/components/domain/revision-diff-viewer';
import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { fetchWritingRevisionData } from '@/lib/api';
import type { CriteriaDelta } from '@/lib/mock-data';
import {
    AlertCircle, ArrowRight, ChevronLeft, Minus, TrendingUp
} from 'lucide-react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { useEffect, useState } from 'react';

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
      <LearnerDashboardShell pageTitle="Revision Mode">
        <div className="space-y-6">
          <Skeleton className="h-40 rounded-2xl" />
          <Skeleton className="h-[240px] rounded-2xl sm:h-[280px] lg:h-80" />
        </div>
      </LearnerDashboardShell>
    );
  }

  if (error) {
    return (
      <LearnerDashboardShell pageTitle="Revision Mode">
        <div>
          <InlineAlert variant="error">{error}</InlineAlert>
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell pageTitle="Revision Mode">
      <main className="space-y-8">
        <LearnerPageHero
          eyebrow="Revision Workspace"
          icon={TrendingUp}
          accent="amber"
          title="Revision Mode"
          description="Compare original vs. revised submission and see exactly which criteria changed."
          highlights={[
            { icon: TrendingUp, label: 'Delta summary', value: `${deltas.length} criteria` },
            { icon: AlertCircle, label: 'Open issues', value: `${unresolvedIssues.length} items` },
            { icon: Minus, label: 'Mode', value: 'Side-by-side review' },
          ]}
          aside={
            <div className="flex flex-col gap-3">
              <Link href={`/writing/feedback?id=${resultId}`} className="inline-flex items-center justify-center gap-2 rounded-xl border border-border bg-surface px-4 py-2 text-sm font-medium text-navy shadow-sm transition-colors hover:border-primary/30 hover:bg-background-light">
                <ChevronLeft className="w-4 h-4" /> Back to feedback
              </Link>
              <Link href="/writing" className="inline-flex items-center justify-center rounded-xl bg-primary px-4 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-primary/90">
                Done
              </Link>
            </div>
          }
        />

        <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
          <MotionSection className="lg:col-span-2">
            <Card className="border-border bg-surface p-6">
              <LearnerSurfaceSectionHeader
                eyebrow="Criterion Delta"
                title="What changed"
                description="Track score movement across each criterion and spot the biggest gains."
                className="mb-5"
              />
              <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
                {deltas.map((delta) => {
                  const diff = delta.revised - delta.original;
                  return (
                    <div key={delta.name} className="rounded-2xl border border-border bg-background-light p-3">
                      <div className="mb-2 truncate text-xs font-bold uppercase tracking-wider text-muted" title={delta.name}>{delta.name}</div>
                      <div className="flex items-center justify-between gap-3">
                        <div className="flex items-center gap-2 text-sm font-medium text-muted">
                          <span>{delta.original}</span>
                          <ArrowRight className="w-3 h-3 text-muted/60" />
                          <span className={diff > 0 ? 'font-bold text-success' : ''}>{delta.revised}</span>
                          <span className="text-xs text-muted/60">/ {delta.max}</span>
                        </div>
                        {diff > 0 ? (
                          <span className="inline-flex h-6 w-6 items-center justify-center rounded-full bg-success/10 text-xs font-bold text-success">+{diff}</span>
                        ) : (
                          <span className="inline-flex h-6 w-6 items-center justify-center rounded-full bg-background-light text-xs font-bold text-muted"><Minus className="w-3 h-3" /></span>
                        )}
                      </div>
                    </div>
                  );
                })}
              </div>
            </Card>
          </MotionSection>

          <MotionSection delayIndex={1}>
            <Card className="flex h-full flex-col border-warning/30 bg-warning/10 p-6">
              <LearnerSurfaceSectionHeader
                eyebrow="Open Issues"
                title="Still to fix"
                description="These are the gaps that remain after the revision pass."
                className="mb-4"
              />
              <ul className="flex-1 space-y-3">
                {unresolvedIssues.map((issue, idx) => (
                  <li key={idx} className="flex items-start gap-2 text-sm text-warning">
                    <span className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-warning" />
                    <span className="leading-snug">{issue}</span>
                  </li>
                ))}
              </ul>
            </Card>
          </MotionSection>
        </div>

        <MotionSection delayIndex={2}>
          <RevisionDiffViewer original={originalText} revised={revisedText} />
        </MotionSection>
      </main>
    </LearnerDashboardShell>
  );
}
