'use client';

import { LearnerPageHero, LearnerSurfaceSectionHeader } from "@/components/domain/learner-surface";
import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { fetchSubmissionComparison } from '@/lib/api';
import type { SubmissionComparison } from '@/lib/mock-data';
import { ArrowLeft, GitCompare, TrendingUp } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { useEffect, useState } from 'react';

export default function SubmissionComparisonPage() {
  const searchParams = useSearchParams();
  const router = useRouter();
  const leftId = searchParams?.get('leftId') ?? undefined;
  const rightId = searchParams?.get('rightId') ?? undefined;

  const [comparison, setComparison] = useState<SubmissionComparison | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('content_view', { page: 'submission-compare', leftId, rightId });
    fetchSubmissionComparison(leftId, rightId)
      .then(setComparison)
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not compare submissions.'))
      .finally(() => setLoading(false));
  }, [leftId, rightId]);

  return (
    <LearnerDashboardShell pageTitle="Compare Attempts" subtitle="Compare learner evidence by submission lineage instead of raw history order." backHref="/submissions">
      <div className="space-y-8">
        <Button variant="ghost" className="gap-2" onClick={() => router.push('/submissions')}>
          <ArrowLeft className="h-4 w-4" />
          Back to history
        </Button>

        {loading ? (
          <div className="space-y-4">
            {[1, 2].map((item) => <Skeleton key={item} className="h-40 rounded-[24px]" />)}
          </div>
        ) : null}

        {!loading && error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {!loading && comparison ? (
          <>
            <LearnerPageHero
              eyebrow="Attempt Comparison"
              icon={GitCompare}
              accent="slate"
              title="See what changed between related attempts"
              description="Use this comparison to spot score movement quickly before you reopen the underlying evidence."
              highlights={[
                { icon: GitCompare, label: 'Sub-test', value: comparison.left?.subtest ?? comparison.right?.subtest ?? 'Unknown' },
                { icon: TrendingUp, label: 'Baseline', value: comparison.left?.scoreRange ?? 'Pending' },
                { icon: TrendingUp, label: 'Comparison', value: comparison.right?.scoreRange ?? 'Pending' },
              ]}
            />

            {!comparison.canCompare ? (
              <InlineAlert variant="info">{comparison.reason ?? 'There are not enough related attempts to compare yet.'}</InlineAlert>
            ) : (
              <>
                <section className="grid gap-6 md:grid-cols-2">
                  {[comparison.left, comparison.right].map((side, index) => (
                    <div key={side?.attemptId ?? index} className="rounded-[28px] border border-border bg-surface p-6 shadow-sm">
                      <p className="text-xs font-black uppercase tracking-widest text-muted">{index === 0 ? 'Baseline attempt' : 'Comparison attempt'}</p>
                      <h2 className="mt-3 text-xl font-black text-navy">{side?.subtest ?? 'Unknown subtest'}</h2>
                      <p className="mt-2 text-sm text-muted">Attempt id: {side?.attemptId}</p>
                      <p className="mt-4 text-3xl font-black text-primary">{side?.scoreRange || 'Pending'}</p>
                    </div>
                  ))}
                </section>

                <section className="rounded-[28px] border border-border bg-surface p-6 shadow-sm">
                  <LearnerSurfaceSectionHeader
                    eyebrow="What changed"
                    title="Keep the progress narrative short and explicit"
                    description="Comparison copy should tell the learner what improved without making them infer the story from raw numbers alone."
                    className="mb-4"
                  />
                  <div className="rounded-2xl border border-success/30 bg-success/10 p-5">
                    <div className="flex items-start gap-3">
                      <TrendingUp className="mt-0.5 h-5 w-5 text-success" />
                      <p className="text-sm leading-6 text-success">{comparison.summary}</p>
                    </div>
                  </div>
                </section>
              </>
            )}
          </>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
