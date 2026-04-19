'use client';

import { useEffect, useMemo, useState } from 'react';
import { Users, Download } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { SegmentedControl } from '@/components/ui/segmented-control';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Badge } from '@/components/ui/badge';
import { EmptyState } from '@/components/ui/empty-error';
import { exportToCsv, formatDateForExport } from '@/lib/csv-export';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRoutePanelFooter,
  AdminRouteStatRow,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { analytics } from '@/lib/analytics';

interface Cohort {
  cohortKey: string;
  cohortName: string;
  learnerCount: number;
  averageScore: number | null;
  evaluationCount: number;
  activeLastMonth: number;
}
interface CohortData {
  groupBy: string;
  cohorts: Cohort[];
  totalLearners: number;
  generatedAt: string;
}

type GroupBy = 'profession' | 'plan';
type Status = 'loading' | 'error' | 'empty' | 'success';

async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, {
    ...init,
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
      ...init?.headers,
    },
  });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

export default function CohortAnalysisPage() {
  const [data, setData] = useState<CohortData | null>(null);
  const [status, setStatus] = useState<Status>('loading');
  const [groupBy, setGroupBy] = useState<GroupBy>('profession');

  const load = (g: GroupBy) => {
    setStatus('loading');
    setGroupBy(g);
    apiRequest<CohortData>(`/v1/admin/analytics/cohort?groupBy=${g}`)
      .then((res) => {
        setData(res);
        setStatus(res.cohorts.length === 0 ? 'empty' : 'success');
      })
      .catch(() => setStatus('error'));
  };

  useEffect(() => {
    analytics.track('admin_cohort_analysis_viewed');
    // eslint-disable-next-line react-hooks/set-state-in-effect -- data-fetch on mount
    load('profession');
  }, []);

  const columns = useMemo<Column<Cohort>[]>(
    () => [
      {
        key: 'cohort',
        header: 'Cohort',
        render: (row) => (
          <div>
            <p className="font-semibold text-navy">{row.cohortName}</p>
            <p className="mt-0.5 text-xs text-muted">{row.cohortKey}</p>
          </div>
        ),
      },
      {
        key: 'learners',
        header: 'Learners',
        render: (row) => <Badge variant="muted">{row.learnerCount.toLocaleString()}</Badge>,
      },
      {
        key: 'avg',
        header: 'Avg score',
        render: (row) => (
          <span className="font-semibold text-navy">{row.averageScore ?? '—'}</span>
        ),
      },
      {
        key: 'eval',
        header: 'Evaluations',
        render: (row) => row.evaluationCount.toLocaleString(),
        hideOnMobile: true,
      },
      {
        key: 'active',
        header: 'Active (30d)',
        render: (row) => row.activeLastMonth.toLocaleString(),
      },
    ],
    [],
  );

  const handleExport = () => {
    if (!data) return;
    const rows = data.cohorts.map((c) => ({
      cohort: c.cohortName,
      learnerCount: c.learnerCount,
      averageScore: c.averageScore,
      evaluationCount: c.evaluationCount,
      activeLastMonth: c.activeLastMonth,
    }));
    exportToCsv(rows, `cohort-analysis-${data.groupBy}-${formatDateForExport(new Date())}.csv`);
  };

  const totalEvaluations = data?.cohorts.reduce((sum, c) => sum + c.evaluationCount, 0) ?? 0;
  const totalActive = data?.cohorts.reduce((sum, c) => sum + c.activeLastMonth, 0) ?? 0;

  return (
    <AdminRouteWorkspace role="main" aria-label="Learner cohort analysis">
      <AdminRouteHero
        eyebrow="Analytics · Cohort"
        icon={Users}
        accent="navy"
        title="Learner Cohort Analysis"
        description="Compare outcomes across professions and subscription tiers to spot rollout and retention signals."
        highlights={
          data
            ? [
                { label: 'Total learners', value: data.totalLearners.toLocaleString() },
                { label: 'Cohorts', value: String(data.cohorts.length) },
                { label: 'Active (30d)', value: totalActive.toLocaleString() },
              ]
            : undefined
        }
      />

      <AdminRoutePanel
        eyebrow="Filter"
        title="Cohort grouping"
        description="Pick a dimension to pivot the cohort view."
        actions={
          data && data.cohorts.length > 0 ? (
            <Button variant="outline" size="sm" onClick={handleExport}>
              <Download className="h-4 w-4" /> Export CSV
            </Button>
          ) : undefined
        }
      >
        <SegmentedControl
          value={groupBy}
          onChange={(next) => load(next)}
          namespace="admin-cohort"
          options={[
            { value: 'profession', label: 'By profession' },
            { value: 'plan', label: 'By plan' },
          ]}
          aria-label="Cohort grouping"
        />
        {data ? (
          <AdminRouteStatRow
            items={[
              { label: 'Total learners', value: data.totalLearners.toLocaleString() },
              { label: 'Cohorts', value: String(data.cohorts.length) },
              { label: 'Evaluations', value: totalEvaluations.toLocaleString() },
              { label: 'Active (30d)', value: totalActive.toLocaleString() },
            ]}
          />
        ) : null}
      </AdminRoutePanel>

      <AdminRoutePanel
        eyebrow="Breakdown"
        title={groupBy === 'profession' ? 'Cohorts by profession' : 'Cohorts by plan'}
        description="Each row is a learner cohort; sort signals come from the anchor metrics."
      >
        <AsyncStateWrapper
          status={status}
          onRetry={() => load(groupBy)}
          emptyContent={
            <EmptyState
              icon={<Users className="h-6 w-6" aria-hidden />}
              title="No cohort data yet"
              description="There are no learners matching this grouping in the current window."
            />
          }
        >
          {data ? (
            <DataTable
              density="compact"
              data={data.cohorts}
              columns={columns}
              keyExtractor={(row) => row.cohortKey}
              aria-label="Cohort breakdown"
            />
          ) : null}
        </AsyncStateWrapper>
        {data ? (
          <AdminRoutePanelFooter
            updatedAt={data.generatedAt}
            source="Learner analytics pipeline"
          />
        ) : null}
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
