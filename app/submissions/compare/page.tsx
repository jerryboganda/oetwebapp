'use client';

import { useEffect, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { ArrowLeft, GitCompare, TrendingUp, TrendingDown, Minus } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { CompareSelector } from '@/components/domain/submissions';
import { analytics } from '@/lib/analytics';
import { fetchSubmissionComparison } from '@/lib/api';
import type { SubmissionComparison } from '@/lib/mock-data';
import { cn } from '@/lib/utils';

/**
 * Compare Attempts — `/submissions/compare?leftId=…&rightId=…`
 *
 * The page is driven entirely by URL state so it is shareable. Picking a
 * pair through the <CompareSelector/> updates the URL; the effect below
 * refetches the comparison whenever left/right change. Cross-subtest
 * comparison is forbidden by contract (server returns canCompare=false).
 */
export default function SubmissionComparisonPage() {
  const searchParams = useSearchParams();
  const router = useRouter();
  const leftId = searchParams?.get('leftId') ?? undefined;
  const rightId = searchParams?.get('rightId') ?? undefined;

  const [comparison, setComparison] = useState<SubmissionComparison | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!leftId && !rightId) {
      return;
    }
    let cancelled = false;
    // Defer state mutation until after commit to satisfy react-hooks/set-state-in-effect.
    queueMicrotask(() => { if (!cancelled) setLoading(true); });
    analytics.track('content_view', { page: 'submission-compare', leftId, rightId });
    fetchSubmissionComparison(leftId, rightId)
      .then((result) => { if (!cancelled) setComparison(result); })
      .catch((err) => { if (!cancelled) setError(err instanceof Error ? err.message : 'Could not compare submissions.'); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [leftId, rightId]);

  useEffect(() => {
    if (!leftId && !rightId) {
      queueMicrotask(() => setComparison(null));
    }
  }, [leftId, rightId]);

  function onPickerChange(next: { leftId?: string; rightId?: string }) {
    const p = new URLSearchParams();
    if (next.leftId) p.set('leftId', next.leftId);
    if (next.rightId) p.set('rightId', next.rightId);
    if (next.leftId && next.rightId) {
      analytics.track('submissions_compare_selected', { leftId: next.leftId, rightId: next.rightId });
    }
    router.replace(`/submissions/compare${p.toString() ? `?${p.toString()}` : ''}`, { scroll: false });
  }

  const delta = comparison?.scaledDelta ?? null;
  const deltaLabel = delta === null || delta === undefined
    ? '—'
    : delta > 0 ? `+${delta}` : String(delta);
  const DeltaIcon = delta === null || delta === undefined
    ? Minus
    : delta > 0 ? TrendingUp : delta < 0 ? TrendingDown : Minus;

  return (
    <LearnerDashboardShell pageTitle="Compare Attempts" subtitle="Compare learner evidence by submission lineage instead of raw history order." backHref="/submissions">
      <div className="space-y-6">
        <Button variant="ghost" className="gap-2" onClick={() => router.push('/submissions')}>
          <ArrowLeft className="h-4 w-4" />
          Back to history
        </Button>

        <LearnerPageHero
          eyebrow="Attempt Comparison"
          icon={GitCompare}
          accent="slate"
          title="See what changed between related attempts"
          description="Pick two attempts in the same sub-test to see scaled score movement, criterion deltas, and pass-state flips."
          highlights={[
            { icon: GitCompare, label: 'Sub-test', value: comparison?.left?.subtest ?? comparison?.right?.subtest ?? 'Unknown' },
            { icon: TrendingUp, label: 'Baseline', value: comparison?.left?.scoreRange ?? 'Pending' },
            { icon: TrendingUp, label: 'Comparison', value: comparison?.right?.scoreRange ?? 'Pending' },
          ]}
        />

        <CompareSelector
          leftId={leftId}
          rightId={rightId}
          onChange={onPickerChange}
        />

        {loading ? (
          <div className="space-y-4" aria-busy="true">
            {[1, 2].map((item) => <Skeleton key={item} className="h-40 rounded-[24px]" />)}
          </div>
        ) : null}

        {!loading && error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {!loading && !comparison && !error ? (
          <InlineAlert variant="info">Select two attempts above to generate a comparison.</InlineAlert>
        ) : null}

        {!loading && comparison ? (
          !comparison.canCompare ? (
            <InlineAlert variant="info">
              {comparison.reasonLabel ?? 'There are not enough related attempts to compare yet.'}
            </InlineAlert>
          ) : (
            <>
              <section className="grid gap-6 md:grid-cols-2">
                {[{ side: comparison.left, role: 'Baseline attempt' }, { side: comparison.right, role: 'Comparison attempt' }].map(({ side, role }) => (
                  <div key={side?.attemptId ?? role} className="rounded-[28px] border border-gray-200 bg-surface p-6 shadow-sm">
                    <p className="text-xs font-black uppercase tracking-widest text-muted">{role}</p>
                    <h2 className="mt-3 text-xl font-black text-navy">{side?.subtest ?? 'Unknown subtest'}</h2>
                    <p className="mt-2 text-sm text-muted">Attempt ID: {side?.attemptId}</p>
                    <p className="mt-4 text-3xl font-black text-primary">{side?.scoreRange || 'Pending'}</p>
                    {side?.passState ? (
                      <Badge
                        variant={side.passState === 'pass' ? 'success' : side.passState === 'fail' ? 'danger' : 'muted'}
                        className="mt-3"
                      >
                        {side.passState === 'pass' ? 'Pass' : side.passState === 'fail' ? 'Fail' : 'Pending'}
                        {side.grade ? ` · Grade ${side.grade}` : ''}
                      </Badge>
                    ) : null}
                  </div>
                ))}
              </section>

              <section className="rounded-[28px] border border-gray-200 bg-surface p-6 shadow-sm">
                <LearnerSurfaceSectionHeader
                  eyebrow="Scaled-score movement"
                  title="The direction of change, not an invented narrative"
                  description="This delta is computed from the two evaluations' scaled 0–500 scores. Criterion deltas below come straight from the evaluator output."
                  className="mb-4"
                />
                <div className={cn(
                  'rounded-2xl border p-5 flex items-center gap-4',
                  delta === null || delta === undefined
                    ? 'border-gray-200 bg-background-light text-navy'
                    : delta > 0
                      ? 'border-emerald-100 bg-emerald-50 text-emerald-900'
                      : delta < 0
                        ? 'border-rose-100 bg-rose-50 text-rose-900'
                        : 'border-gray-200 bg-background-light text-navy',
                )}>
                  <DeltaIcon className="w-8 h-8" aria-hidden />
                  <div>
                    <p className="text-xs font-black uppercase tracking-widest opacity-70">Scaled delta</p>
                    <p className="text-3xl font-black">{deltaLabel}</p>
                    {comparison.summary ? <p className="text-sm mt-2 opacity-90">{comparison.summary}</p> : null}
                  </div>
                </div>
              </section>

              {(comparison.criterionDeltas?.length ?? 0) > 0 ? (
                <section className="rounded-[28px] border border-gray-200 bg-surface p-6 shadow-sm">
                  <LearnerSurfaceSectionHeader
                    eyebrow="Criterion deltas"
                    title="Where the score moved, broken out by criterion"
                    description="Criterion scores are lifted verbatim from each evaluation — no interpretation."
                    className="mb-4"
                  />
                  <div className="grid gap-3 md:grid-cols-2">
                    {comparison.criterionDeltas!.map((d) => {
                      const direction = d.direction;
                      const Icon = direction === 'up' ? TrendingUp : direction === 'down' ? TrendingDown : Minus;
                      return (
                        <div
                          key={d.name}
                          className={cn(
                            'rounded-2xl border p-4 flex items-center gap-3',
                            direction === 'up'
                              ? 'border-emerald-100 bg-emerald-50'
                              : direction === 'down'
                                ? 'border-rose-100 bg-rose-50'
                                : 'border-gray-200 bg-background-light',
                          )}
                        >
                          <Icon className={cn(
                            'w-5 h-5 shrink-0',
                            direction === 'up' ? 'text-emerald-600' : direction === 'down' ? 'text-rose-600' : 'text-muted',
                          )} aria-hidden />
                          <div className="flex-1">
                            <p className="font-bold text-navy">{d.name}</p>
                            <p className="text-xs text-muted mt-1">
                              {d.leftScore}/{d.maxScore} → {d.rightScore}/{d.maxScore}
                            </p>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </section>
              ) : null}
            </>
          )
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
