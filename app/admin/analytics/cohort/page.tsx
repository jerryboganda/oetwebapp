'use client';

import { useEffect, useState } from 'react';
import { Download, Users } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { exportToCsv, formatDateForExport } from '@/lib/csv-export';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteSummaryCard,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';

interface Cohort { cohortKey: string; cohortName: string; learnerCount: number; averageScore: number | null; evaluationCount: number; activeLastMonth: number }
interface CohortData { groupBy: string; cohorts: Cohort[]; totalLearners: number; generatedAt: string }

const apiRequest = apiClient.request;

export default function CohortAnalysisPage() {
  const [data, setData] = useState<CohortData | null>(null);
  const [loading, setLoading] = useState(true);
  const [groupBy, setGroupBy] = useState('profession');

  const load = (g: string) => {
    setLoading(true); setGroupBy(g);
    apiRequest<CohortData>(`/v1/admin/analytics/cohort?groupBy=${g}`).then(setData).catch(() => {}).finally(() => setLoading(false));
  };

  // eslint-disable-next-line react-hooks/set-state-in-effect -- data-fetch on mount: setState from API response is the correct pattern
  useEffect(() => { analytics.track('admin_cohort_analysis_viewed'); load('profession'); }, []);

  return (
    <AdminRouteWorkspace role="main" aria-label="Cohort analysis">
      <AdminRouteHero
        eyebrow="Analytics"
        icon={Users}
        accent="navy"
        title="Learner cohort analysis"
        description="Compare outcomes across professions and subscription tiers."
        aside={data && data.cohorts.length > 0 ? (
          <div className="rounded-2xl border border-border bg-background-light p-4 shadow-sm">
            <Button variant="outline" size="sm" className="gap-2" onClick={() => {
              const rows = data.cohorts.map(c => ({
                cohort: c.cohortName,
                learnerCount: c.learnerCount,
                averageScore: c.averageScore,
                evaluationCount: c.evaluationCount,
                activeLastMonth: c.activeLastMonth,
              }));
              exportToCsv(rows, `cohort-analysis-${data.groupBy}-${formatDateForExport(new Date())}.csv`);
            }}>
              <Download className="w-4 h-4" />
              Export CSV
            </Button>
          </div>
        ) : undefined}
      />

      <div className="flex gap-2">
        <button onClick={() => load('profession')} className={`px-4 py-2 rounded-lg text-sm font-medium ${groupBy === 'profession' ? 'bg-primary text-primary-foreground' : 'bg-muted'}`}>By Profession</button>
        <button onClick={() => load('plan')} className={`px-4 py-2 rounded-lg text-sm font-medium ${groupBy === 'plan' ? 'bg-primary text-primary-foreground' : 'bg-muted'}`}>By Plan</button>
      </div>

      {loading ? <div className="space-y-3">{Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className="h-20 rounded-xl" />)}</div> : data ? (
        <MotionSection className="space-y-4">
          <AdminRoutePanel>
            <p className="text-sm text-muted">Total learners: <strong>{data.totalLearners}</strong> • Grouped by: <strong className="capitalize">{data.groupBy}</strong></p>
          </AdminRoutePanel>
          {data.cohorts.map(c => (
            <MotionItem key={c.cohortKey}>
              <AdminRoutePanel
                title={c.cohortName}
                actions={<Badge variant="outline">{c.learnerCount} learners</Badge>}
              >
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                  <AdminRouteSummaryCard label="Avg score" value={c.averageScore ?? '—'} hint="Across recent evaluations" />
                  <AdminRouteSummaryCard label="Evaluations" value={c.evaluationCount} hint="Lifetime total" />
                  <AdminRouteSummaryCard label="Active (30d)" value={c.activeLastMonth} hint="Logged in last 30 days" />
                </div>
              </AdminRoutePanel>
            </MotionItem>
          ))}
        </MotionSection>
      ) : <AdminRoutePanel><p className="text-center text-muted">No data available.</p></AdminRoutePanel>}
    </AdminRouteWorkspace>
  );
}
