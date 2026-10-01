'use client';

import { useCallback, useEffect, useState } from 'react';
import { Activity, AlertTriangle, CheckCircle2, RefreshCw, Calendar } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { ErrorState } from '@/components/ui/empty-error';
import { ProgressBar } from '@/components/ui/progress';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { fetchStudyPlanDrift, regenerateStudyPlan } from '@/lib/api';

interface DriftData {
  hasPlan: boolean;
  planId?: string;
  drift?: {
    level: string;
    overdueItems: number;
    oldestOverdueDays: number;
    completionRate: number;
    expectedCompleted: number;
    actualCompleted: number;
    totalItems: number;
    shouldRegenerate: boolean;
    autoRegenQueued?: boolean;
    recommendation: string;
  };
  subtestDrift?: { subtestCode: string; total: number; completed: number; overdue: number; completionRate: number }[];
  overdueItems?: { id: string; title: string; subtestCode: string; dueDate: string; daysOverdue: number }[];
}

const DRIFT_COLOR: Record<string, string> = {
  severe: 'text-danger-strong bg-danger/10 border-danger/30',
  moderate: 'text-warning-strong bg-warning/10 border-warning/30',
  mild: 'text-warning-strong bg-warning/10 border-warning/30',
  'on-track': 'text-success-strong bg-success/10 border-success/30',
};

export default function StudyPlanDriftPage() {
  const [data, setData] = useState<DriftData | null>(null);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [regenerating, setRegenerating] = useState(false);
  const [regenError, setRegenError] = useState<string | null>(null);

  const load = useCallback(() => {
    setLoading(true);
    setFailed(false);
    fetchStudyPlanDrift().then((d) => setData(d as DriftData)).catch(() => setFailed(true)).finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    analytics.track('study_plan_drift_viewed');
    load();
  }, [load]);

  const handleRegenerate = async () => {
    setRegenerating(true);
    setRegenError(null);
    try {
      analytics.track('study_plan_regenerate_clicked');
      await regenerateStudyPlan();
      load();
    } catch (e: unknown) {
      const err = e as { userMessage?: string; message?: string };
      setRegenError(err.userMessage ?? err.message ?? 'Could not regenerate plan.');
    } finally {
      setRegenerating(false);
    }
  };

  return (
    <>
      <LearnerPageHero
        eyebrow="Study Plan"
        icon={Activity}
        title="Study Plan Health"
        description="Detect drift from your study plan and get recommendations to get back on track."
      />

      {loading ? (
        <div className="space-y-4" aria-hidden="true">{Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-32 rounded-2xl" />)}</div>
      ) : failed && !data ? (
        <ErrorState onRetry={load} />
      ) : !data?.hasPlan ? (
        <LearnerEmptyState
          icon={Calendar}
          title="No study plan found"
          description="Generate one from your dashboard."
          primaryAction={{ label: 'Open Study Plan', href: '/study-plan' }}
        />
      ) : data.drift ? (
        <>
          {/* Main drift card */}
          <MotionSection>
            <Card padding="lg" className={DRIFT_COLOR[data.drift.level] ?? ''}>
              <div className="flex items-start gap-4">
                {data.drift.level === 'on-track' ? <CheckCircle2 className="h-8 w-8 shrink-0" aria-hidden="true" /> : <AlertTriangle className="h-8 w-8 shrink-0" aria-hidden="true" />}
                <div className="min-w-0">
                  <h2 className="text-lg font-semibold capitalize">{data.drift.level === 'on-track' ? 'On Track!' : `${data.drift.level.charAt(0).toUpperCase() + data.drift.level.slice(1)} Drift Detected`}</h2>
                  <p className="mt-1 max-w-prose text-sm">{data.drift.recommendation}</p>
                  <div className="mt-3 flex flex-wrap gap-x-4 gap-y-1 tabular-nums">
                    <span className="text-sm">Completion: <strong>{data.drift.completionRate}%</strong></span>
                    <span className="text-sm">Overdue: <strong>{data.drift.overdueItems}</strong></span>
                    <span className="text-sm">Progress: <strong>{data.drift.actualCompleted}/{data.drift.totalItems}</strong></span>
                  </div>
                  {data.drift.autoRegenQueued && (
                    <p className="mt-2 text-xs italic">
                      Auto-recovery already queued. Your plan will refresh shortly.
                    </p>
                  )}
                  {data.drift.shouldRegenerate && !data.drift.autoRegenQueued && (
                    <>
                      <Button variant="primary" size="sm" className="mt-3" onClick={handleRegenerate} disabled={regenerating}>
                        <RefreshCw className={`h-4 w-4 ${regenerating ? 'animate-spin' : ''}`} aria-hidden="true" /> {regenerating ? 'Regenerating…' : 'Recover my plan'}
                      </Button>
                      {regenError && <p className="mt-2 text-xs text-danger-strong" role="alert">{regenError}</p>}
                    </>
                  )}
                </div>
              </div>
            </Card>
          </MotionSection>

          {/* Subtest breakdown */}
          {data.subtestDrift && data.subtestDrift.length > 0 && (
            <section>
              <LearnerSurfaceSectionHeader title="Per-Subtest Status" className="mb-4" />
              <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
                {data.subtestDrift.map((s, index) => (
                  <MotionItem key={s.subtestCode} delayIndex={Math.min(index, 5)} className="h-full">
                    <Card className="h-full">
                      <h3 className="text-sm font-medium capitalize text-navy">{s.subtestCode}</h3>
                      <ProgressBar value={s.completionRate} ariaLabel={`${s.subtestCode} completion ${s.completionRate}%`} className="mt-2" />
                      <div className="mt-1 flex flex-wrap justify-between gap-x-2 text-xs tabular-nums text-muted">
                        <span>{s.completed}/{s.total} done</span>
                        {s.overdue > 0 && <span className="text-danger-strong">{s.overdue} overdue</span>}
                      </div>
                    </Card>
                  </MotionItem>
                ))}
              </div>
            </section>
          )}

          {/* Overdue items */}
          {data.overdueItems && data.overdueItems.length > 0 && (
            <section>
              <LearnerSurfaceSectionHeader title="Overdue Items" className="mb-4" />
              <div className="space-y-2">
                {data.overdueItems.map((item, index) => (
                  <MotionItem key={item.id} delayIndex={Math.min(index, 5)}>
                    <Card padding="sm" className="flex items-center justify-between gap-3">
                      <div className="min-w-0">
                        <p className="text-sm font-medium text-navy">{item.title}</p>
                        <p className="text-xs capitalize text-muted">{item.subtestCode} • Due: {item.dueDate}</p>
                      </div>
                      <Badge variant="danger" className="shrink-0 tabular-nums">{item.daysOverdue}d overdue</Badge>
                    </Card>
                  </MotionItem>
                ))}
              </div>
            </section>
          )}
        </>
      ) : null}
    </>
  );
}
