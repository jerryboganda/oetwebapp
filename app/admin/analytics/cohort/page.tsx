'use client';

import { useEffect, useState } from 'react';
import { Download, Users } from 'lucide-react';
import { exportToCsv, formatDateForExport } from '@/lib/csv-export';
import { AdminOperationsLayout, KpiStrip, BentoGrid, BentoCell } from '@/components/admin/layout/admin-operations-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Skeleton } from '@/components/admin/ui/skeleton';
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

  // eslint-disable-next-line react-hooks/set-state-in-effect -- data-fetch on mount
  useEffect(() => { analytics.track('admin_cohort_analysis_viewed'); load('profession'); }, []);

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Analytics', href: '/admin' },
    { label: 'Cohort' },
  ];

  return (
    <AdminOperationsLayout
      title="Learner cohort analysis"
      description="Compare outcomes across professions and subscription tiers."
      eyebrow="Analytics"
      breadcrumbs={breadcrumbs}
      actions={
        data && data.cohorts.length > 0 ? (
          <Button
            variant="secondary"
            size="sm"
            onClick={() => {
              const rows = data.cohorts.map(c => ({
                cohort: c.cohortName,
                learnerCount: c.learnerCount,
                averageScore: c.averageScore,
                evaluationCount: c.evaluationCount,
                activeLastMonth: c.activeLastMonth,
              }));
              exportToCsv(rows, `cohort-analysis-${data.groupBy}-${formatDateForExport(new Date())}.csv`);
            }}
          >
            <Download className="h-4 w-4" />
            Export CSV
          </Button>
        ) : null
      }
      kpis={
        data ? (
          <KpiStrip>
            <KpiTile label="Total learners" value={data.totalLearners} icon={<Users className="h-4 w-4" />} tone="primary" />
            <KpiTile label="Cohorts" value={data.cohorts.length} tone="info" />
            <KpiTile label="Grouped by" value={data.groupBy} tone="default" />
          </KpiStrip>
        ) : null
      }
      primaryGrid={
        <div className="space-y-6">
          <div className="flex gap-2">
            <Button variant={groupBy === 'profession' ? 'primary' : 'secondary'} size="sm" onClick={() => load('profession')}>By Profession</Button>
            <Button variant={groupBy === 'plan' ? 'primary' : 'secondary'} size="sm" onClick={() => load('plan')}>By Plan</Button>
          </div>

          {loading ? (
            <div className="space-y-3">{Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className="h-20 rounded-admin" />)}</div>
          ) : data ? (
            <BentoGrid>
              {data.cohorts.map(c => (
                <BentoCell key={c.cohortKey} span={{ default: 12, md: 6 }}>
                  <Card>
                    <CardHeader>
                      <div className="flex items-center justify-between">
                        <CardTitle>{c.cohortName}</CardTitle>
                        <Badge variant="primary" intensity="tinted">{c.learnerCount} learners</Badge>
                      </div>
                    </CardHeader>
                    <CardContent>
                      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                        <div>
                          <p className="text-xs uppercase tracking-wider text-admin-fg-muted">Avg score</p>
                          <p className="mt-1 text-xl font-bold tabular-nums text-admin-fg-strong">{c.averageScore ?? '-'}</p>
                        </div>
                        <div>
                          <p className="text-xs uppercase tracking-wider text-admin-fg-muted">Evaluations</p>
                          <p className="mt-1 text-xl font-bold tabular-nums text-admin-fg-strong">{c.evaluationCount}</p>
                        </div>
                        <div>
                          <p className="text-xs uppercase tracking-wider text-admin-fg-muted">Active (30d)</p>
                          <p className="mt-1 text-xl font-bold tabular-nums text-admin-fg-strong">{c.activeLastMonth}</p>
                        </div>
                      </div>
                    </CardContent>
                  </Card>
                </BentoCell>
              ))}
            </BentoGrid>
          ) : (
            <Card>
              <CardContent>
                <p className="py-8 text-center text-sm text-admin-fg-muted">No data available.</p>
              </CardContent>
            </Card>
          )}
        </div>
      }
    />
  );
}
