'use client';

import { useCallback, useEffect, useState } from 'react';
import { Target, BarChart3, Users } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { ErrorState } from '@/components/ui/empty-error';
import { ProgressBar } from '@/components/ui/progress';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';

interface SubtestComparative {
  subtestCode: string;
  yourScore: number;
  percentile: number;
  cohortAverage: number;
  cohortMedian: number;
  cohortSize: number;
  targetScore: number | null;
  gapToTarget: number | null;
  tier: string;
}

interface ComparativeData {
  subtests: SubtestComparative[];
  generatedAt: string;
}

const apiRequest = apiClient.request;

const TIER_BADGE: Record<string, { label: string; variant: BadgeProps['variant'] }> = {
  top10: { label: 'Top 10%', variant: 'success' },
  top25: { label: 'Top 25%', variant: 'info' },
  aboveMedian: { label: 'Above Median', variant: 'warning' },
  belowMedian: { label: 'Below Median', variant: 'danger' },
};

export default function ComparativeAnalyticsPage() {
  const [data, setData] = useState<ComparativeData | null>(null);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);

  const load = useCallback(() => {
    setLoading(true);
    setFailed(false);
    apiRequest<ComparativeData>('/v1/learner/comparative-analytics')
      .then(setData)
      .catch(() => { setData(null); setFailed(true); })
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    analytics.track('comparative_analytics_viewed');
    load();
  }, [load]);

  return (
    <>
      <LearnerPageHero
        eyebrow="Progress"
        icon={Users}
        title="Comparative Analytics"
        description="See how your performance compares to the cohort. Percentile rankings and score gap analysis."
      />

      {loading ? (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2" aria-hidden="true">
          {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-48 rounded-2xl" />)}
        </div>
      ) : failed ? (
        <ErrorState onRetry={load} />
      ) : !data || data.subtests.length === 0 ? (
        <LearnerEmptyState
          icon={BarChart3}
          title="No comparative analytics yet"
          description="Complete some practice evaluations to see your comparative analytics."
        />
      ) : (
        <MotionSection>
          <LearnerSurfaceSectionHeader title="Per-Subtest Ranking" className="mb-4" />
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            {data.subtests.map((s, index) => {
              // Backend reports percentile 50 / avg 0 when the 90-day cohort is empty — don't show those as real.
              const hasCohort = s.cohortSize > 0;
              const tier = hasCohort ? TIER_BADGE[s.tier] : undefined;
              return (
                <MotionItem key={s.subtestCode} delayIndex={Math.min(index, 5)} className="h-full">
                  <Card className="h-full">
                    <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
                      <h3 className="text-lg font-bold capitalize text-navy">{s.subtestCode}</h3>
                      {tier && <Badge variant={tier.variant}>{tier.label}</Badge>}
                    </div>

                    <div className="mb-4 grid grid-cols-3 gap-3 text-center">
                      <div className="min-w-0">
                        <p className="text-2xl font-bold tabular-nums text-primary">{s.yourScore}</p>
                        <p className="tile-label text-muted">Your Score</p>
                      </div>
                      <div className="min-w-0">
                        <p className="text-2xl font-bold tabular-nums text-navy">{hasCohort ? s.cohortAverage : '—'}</p>
                        <p className="tile-label text-muted">Cohort Avg</p>
                      </div>
                      <div className="min-w-0">
                        <p className="text-2xl font-bold tabular-nums text-navy">{hasCohort ? `${s.percentile}%` : '—'}</p>
                        <p className="tile-label text-muted">Percentile</p>
                      </div>
                    </div>

                    {hasCohort && (
                      <div className="mb-3">
                        <ProgressBar value={s.percentile} ariaLabel={`${s.subtestCode} percentile ${s.percentile}%`} size="md" />
                        <div className="mt-1 flex justify-between text-3xs tabular-nums text-muted"><span>0%</span><span>50%</span><span>100%</span></div>
                      </div>
                    )}

                    {s.targetScore && s.gapToTarget !== null && (
                      <div className="flex flex-wrap items-center gap-2 text-sm text-navy">
                        <Target className="h-4 w-4 text-muted" aria-hidden="true" />
                        <span className="tabular-nums">Target: {s.targetScore}</span>
                        <Badge variant={s.gapToTarget <= 0 ? 'default' : 'danger'}>
                          {s.gapToTarget <= 0 ? 'Target reached!' : `${s.gapToTarget} pts to go`}
                        </Badge>
                      </div>
                    )}

                    <p className="mt-2 text-xs text-muted">{hasCohort ? `Based on ${s.cohortSize} scored evaluation${s.cohortSize === 1 ? '' : 's'} in the last 90 days` : 'Not enough cohort data in the last 90 days yet.'}</p>
                  </Card>
                </MotionItem>
              );
            })}
          </div>
        </MotionSection>
      )}
    </>
  );
}
