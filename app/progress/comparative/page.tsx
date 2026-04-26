'use client';

import { LearnerPageHero, LearnerSurfaceSectionHeader } from "@/components/domain/learner-surface";
import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';
import { BarChart3, Target } from 'lucide-react';
import { useEffect, useState } from 'react';

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

const TIER_BADGE: Record<string, { label: string; color: string }> = {
  top10: { label: 'Top 10%', color: 'bg-success/10 text-success' },
  top25: { label: 'Top 25%', color: 'bg-info/10 text-info' },
  aboveMedian: { label: 'Above Median', color: 'bg-warning/10 text-warning' },
  belowMedian: { label: 'Below Median', color: 'bg-danger/10 text-danger' },
};

export default function ComparativeAnalyticsPage() {
  const [data, setData] = useState<ComparativeData | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    analytics.track('comparative_analytics_viewed');
    apiRequest<ComparativeData>('/v1/learner/comparative-analytics')
      .then(setData)
      .catch(() => setData(null))
      .finally(() => setLoading(false));
  }, []);

  return (
    <LearnerDashboardShell>
      <LearnerPageHero title="Comparative Analytics" description="See how your performance compares to the cohort. Percentile rankings and score gap analysis." />

      <MotionSection className="space-y-6 max-w-5xl mx-auto">
        {loading ? (
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">{Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-48 rounded-xl" />)}</div>
        ) : !data || data.subtests.length === 0 ? (
          <Card className="p-8 text-center text-muted-foreground"><BarChart3 className="w-8 h-8 mx-auto mb-3 opacity-50" /><p>Complete some practice evaluations to see your comparative analytics.</p></Card>
        ) : (
          <>
            <LearnerSurfaceSectionHeader title="Per-Subtest Ranking" />
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {data.subtests.map(s => {
                const tier = TIER_BADGE[s.tier] ?? TIER_BADGE.belowMedian;
                return (
                  <MotionItem key={s.subtestCode}>
                    <Card className="p-5">
                      <div className="flex items-center justify-between mb-4">
                        <h3 className="text-lg font-semibold capitalize">{s.subtestCode}</h3>
                        <Badge className={tier.color}>{tier.label}</Badge>
                      </div>

                      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 mb-4">
                        <div className="text-center"><p className="text-2xl font-bold text-primary">{s.yourScore}</p><p className="text-xs text-muted-foreground">Your Score</p></div>
                        <div className="text-center"><p className="text-2xl font-bold">{s.cohortAverage}</p><p className="text-xs text-muted-foreground">Cohort Avg</p></div>
                        <div className="text-center"><p className="text-2xl font-bold">{s.percentile}%</p><p className="text-xs text-muted-foreground">Percentile</p></div>
                      </div>

                      {/* Percentile bar */}
                      <div className="mb-3">
                        <div className="h-3 rounded-full bg-muted overflow-hidden relative">
                          <div className="h-full rounded-full bg-gradient-to-r from-red-400 via-amber-400 to-emerald-400" style={{ width: `${s.percentile}%` }} />
                        </div>
                        <div className="flex justify-between mt-1"><span className="text-[10px] text-muted-foreground">0%</span><span className="text-[10px] text-muted-foreground">50%</span><span className="text-[10px] text-muted-foreground">100%</span></div>
                      </div>

                      {s.targetScore && s.gapToTarget !== null && (
                        <div className="flex items-center gap-2 text-sm">
                          <Target className="w-4 h-4 text-muted-foreground" />
                          <span>Target: {s.targetScore}</span>
                          <Badge variant={s.gapToTarget <= 0 ? 'default' : 'danger'} className="text-xs">
                            {s.gapToTarget <= 0 ? 'Target reached!' : `${s.gapToTarget} pts to go`}
                          </Badge>
                        </div>
                      )}

                      <p className="text-xs text-muted-foreground mt-2">Based on {s.cohortSize} learners in the last 90 days</p>
                    </Card>
                  </MotionItem>
                );
              })}
            </div>
          </>
        )}
      </MotionSection>
    </LearnerDashboardShell>
  );
}
