'use client';

import { useState, useEffect, useMemo } from 'react';
import {
  ChevronLeft,
  ArrowRight,
  AlertCircle,
  TrendingUp,
  Minus,
} from 'lucide-react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { RevisionDiffViewer } from '@/components/domain/revision-diff-viewer';
import { WritingImprovementBanner } from '@/components/domain/writing-improvement-banner';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchWritingRevisionData } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { CriteriaDelta } from '@/lib/mock-data';
import { computeImprovementScore } from '@/lib/writing-revision/improvement-score';

type RevisionState = {
  resultId: string;
  deltas: CriteriaDelta[];
  originalText: string;
  revisedText: string;
  unresolvedIssues: string[];
  loading: boolean;
  error: string | null;
};

const EMPTY_DELTAS: CriteriaDelta[] = [];
const EMPTY_UNRESOLVED_ISSUES: string[] = [];

export default function WritingRevisionMode() {
  const searchParams = useSearchParams();
  const resultId = searchParams?.get('id') ?? '';
  const missingRevisionMessage = 'Revision unavailable. Open a completed Writing result first.';
  const [revisionState, setRevisionState] = useState<RevisionState>({
    resultId,
    deltas: [],
    originalText: '',
    revisedText: '',
    unresolvedIssues: [],
    loading: resultId.length > 0,
    error: null,
  });
  const isCurrentResult = revisionState.resultId === resultId;
  const deltas = isCurrentResult ? revisionState.deltas : EMPTY_DELTAS;
  const originalText = isCurrentResult ? revisionState.originalText : '';
  const revisedText = isCurrentResult ? revisionState.revisedText : '';
  const unresolvedIssues = isCurrentResult ? revisionState.unresolvedIssues : EMPTY_UNRESOLVED_ISSUES;
  const loading = resultId.length > 0 && (!isCurrentResult || revisionState.loading);
  const error = !resultId ? missingRevisionMessage : isCurrentResult ? revisionState.error : null;

  const improvement = useMemo(
    () => computeImprovementScore({ deltas, unresolvedIssuesCount: unresolvedIssues.length }),
    [deltas, unresolvedIssues],
  );

  useEffect(() => {
    if (!resultId) {
      return;
    }
    let active = true;
    analytics.track('content_view', { content: 'revision', resultId, subtest: 'writing' });
    fetchWritingRevisionData(resultId)
      .then((data) => {
        if (!active) return;
        setRevisionState({
          resultId,
          deltas: data.deltas,
          originalText: data.originalText,
          revisedText: data.revisedText,
          unresolvedIssues: data.unresolvedIssues,
          loading: false,
          error: null,
        });
      })
      .catch(() => {
        if (!active) return;
        setRevisionState({
          resultId,
          deltas: [],
          originalText: '',
          revisedText: '',
          unresolvedIssues: [],
          loading: false,
          error: 'Failed to load revision data. Please try again.',
        });
      });
    return () => {
      active = false;
    };
  }, [resultId]);

  useEffect(() => {
    if (loading || error || deltas.length === 0) return;
    analytics.track('writing_revision_score_computed', {
      resultId,
      score: improvement.score,
      band: improvement.band,
      criteriaImproved: improvement.criteriaImproved,
      criteriaRegressed: improvement.criteriaRegressed,
      unresolvedIssuesCount: unresolvedIssues.length,
    });
  }, [loading, error, deltas.length, improvement.score, improvement.band, improvement.criteriaImproved, improvement.criteriaRegressed, unresolvedIssues.length, resultId]);

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

        <MotionSection>
          <WritingImprovementBanner result={improvement} />
        </MotionSection>

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
