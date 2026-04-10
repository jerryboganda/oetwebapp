'use client';

import { useEffect, useState } from 'react';
import { AlertTriangle, CheckCircle2, RefreshCw, Calendar, TrendingDown } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';

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
    recommendation: string;
  };
  subtestDrift?: { subtestCode: string; total: number; completed: number; overdue: number; completionRate: number }[];
  overdueItems?: { id: string; title: string; subtestCode: string; dueDate: string; daysOverdue: number }[];
}

async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, { ...init, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', ...init?.headers } });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

const DRIFT_COLOR: Record<string, string> = {
  severe: 'text-red-600 bg-red-50 dark:bg-red-950',
  moderate: 'text-amber-600 bg-amber-50 dark:bg-amber-950',
  mild: 'text-yellow-600 bg-yellow-50 dark:bg-yellow-950',
  'on-track': 'text-emerald-600 bg-emerald-50 dark:bg-emerald-950',
};

export default function StudyPlanDriftPage() {
  const [data, setData] = useState<DriftData | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    analytics.track('study_plan_drift_viewed');
    apiRequest<DriftData>('/v1/learner/study-plan/drift').then(setData).catch(() => {}).finally(() => setLoading(false));
  }, []);

  return (
    <LearnerDashboardShell>
      <LearnerPageHero title="Study Plan Health" subtitle="Detect drift from your study plan and get recommendations to get back on track." />

      <MotionSection className="px-4 py-6 space-y-6 max-w-4xl mx-auto">
        {loading ? (
          <div className="space-y-4">{Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-32 rounded-xl" />)}</div>
        ) : !data?.hasPlan ? (
          <Card className="p-8 text-center text-muted-foreground"><Calendar className="w-8 h-8 mx-auto mb-3 opacity-50" /><p>No study plan found. Generate one from your dashboard.</p></Card>
        ) : data.drift ? (
          <>
            {/* Main drift card */}
            <MotionItem>
              <Card className={`p-6 ${DRIFT_COLOR[data.drift.level] ?? ''}`}>
                <div className="flex items-start gap-4">
                  {data.drift.level === 'on-track' ? <CheckCircle2 className="w-8 h-8 flex-shrink-0" /> : <AlertTriangle className="w-8 h-8 flex-shrink-0" />}
                  <div>
                    <h3 className="text-lg font-semibold capitalize">{data.drift.level === 'on-track' ? 'On Track!' : `${data.drift.level.charAt(0).toUpperCase() + data.drift.level.slice(1)} Drift Detected`}</h3>
                    <p className="text-sm mt-1">{data.drift.recommendation}</p>
                    <div className="flex gap-4 mt-3 flex-wrap">
                      <span className="text-sm">Completion: <strong>{data.drift.completionRate}%</strong></span>
                      <span className="text-sm">Overdue: <strong>{data.drift.overdueItems}</strong></span>
                      <span className="text-sm">Progress: <strong>{data.drift.actualCompleted}/{data.drift.totalItems}</strong></span>
                    </div>
                    {data.drift.shouldRegenerate && (
                      <Button variant="default" size="sm" className="mt-3"><RefreshCw className="w-4 h-4 mr-1" /> Regenerate Plan</Button>
                    )}
                  </div>
                </div>
              </Card>
            </MotionItem>

            {/* Subtest breakdown */}
            {data.subtestDrift && data.subtestDrift.length > 0 && (
              <>
                <LearnerSurfaceSectionHeader title="Per-Subtest Status" />
                <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                  {data.subtestDrift.map(s => (
                    <MotionItem key={s.subtestCode}>
                      <Card className="p-4">
                        <h4 className="font-medium capitalize text-sm">{s.subtestCode}</h4>
                        <div className="h-2 rounded-full bg-muted mt-2 overflow-hidden">
                          <div className="h-full rounded-full bg-primary" style={{ width: `${s.completionRate}%` }} />
                        </div>
                        <div className="flex justify-between mt-1 text-xs text-muted-foreground">
                          <span>{s.completed}/{s.total} done</span>
                          {s.overdue > 0 && <span className="text-red-500">{s.overdue} overdue</span>}
                        </div>
                      </Card>
                    </MotionItem>
                  ))}
                </div>
              </>
            )}

            {/* Overdue items */}
            {data.overdueItems && data.overdueItems.length > 0 && (
              <>
                <LearnerSurfaceSectionHeader title="Overdue Items" />
                <div className="space-y-2">
                  {data.overdueItems.map(item => (
                    <MotionItem key={item.id}>
                      <Card className="p-3 flex items-center justify-between">
                        <div>
                          <p className="text-sm font-medium">{item.title}</p>
                          <p className="text-xs text-muted-foreground capitalize">{item.subtestCode} • Due: {item.dueDate}</p>
                        </div>
                        <Badge variant="destructive">{item.daysOverdue}d overdue</Badge>
                      </Card>
                    </MotionItem>
                  ))}
                </div>
              </>
            )}
          </>
        ) : null}
      </MotionSection>
    </LearnerDashboardShell>
  );
}
