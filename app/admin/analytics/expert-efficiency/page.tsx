'use client';

import { useEffect, useMemo, useState } from 'react';
import { UserCheck, Download } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { SegmentedControl } from '@/components/ui/segmented-control';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { EmptyState } from '@/components/ui/empty-error';
import { exportToCsv, formatDateForExport } from '@/lib/csv-export';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRoutePanelFooter,
  AdminRouteSummaryCard,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { analytics } from '@/lib/analytics';

interface ExpertReport {
  expertId: string;
  expertName: string;
  period: number;
  assignmentsReceived: number;
  reviewsCompleted: number;
  averageReviewTimeMinutes: number | null;
  reviewsPerDay: number;
  aiAlignmentScore: number | null;
  efficiency: string;
}
interface EfficiencyData {
  period: number;
  experts: ExpertReport[];
  summary: {
    totalExperts: number;
    activeExperts: number;
    totalReviewsCompleted: number;
    averageReviewsPerExpertPerDay: number;
  };
  generatedAt: string;
}

type DaysOption = 7 | 14 | 30 | 60 | 90;
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

const EFFICIENCY_VARIANT: Record<string, { label: string; variant: BadgeProps['variant'] }> = {
  high: { label: 'High', variant: 'success' },
  medium: { label: 'Medium', variant: 'warning' },
  low: { label: 'Low', variant: 'danger' },
  'no-data': { label: 'No data', variant: 'muted' },
};

export default function ExpertEfficiencyPage() {
  const [data, setData] = useState<EfficiencyData | null>(null);
  const [status, setStatus] = useState<Status>('loading');
  const [days, setDays] = useState<DaysOption>(30);

  const load = (d: DaysOption) => {
    setStatus('loading');
    setDays(d);
    apiRequest<EfficiencyData>(`/v1/admin/analytics/expert-efficiency?days=${d}`)
      .then((res) => {
        setData(res);
        setStatus(res.experts.length === 0 ? 'empty' : 'success');
      })
      .catch(() => setStatus('error'));
  };

  useEffect(() => {
    analytics.track('admin_expert_efficiency_viewed');
    // eslint-disable-next-line react-hooks/set-state-in-effect -- data-fetch on mount
    load(30);
  }, []);

  const columns = useMemo<Column<ExpertReport>[]>(
    () => [
      {
        key: 'expert',
        header: 'Expert',
        render: (row) => (
          <div className="flex items-center gap-2 min-w-0">
            <UserCheck className="h-4 w-4 shrink-0 text-primary" aria-hidden />
            <span className="truncate font-semibold text-navy">{row.expertName}</span>
          </div>
        ),
      },
      {
        key: 'efficiency',
        header: 'Efficiency',
        render: (row) => {
          const eff = EFFICIENCY_VARIANT[row.efficiency] ?? EFFICIENCY_VARIANT['no-data'];
          return <Badge variant={eff.variant}>{eff.label}</Badge>;
        },
      },
      { key: 'assigned', header: 'Assigned', render: (row) => row.assignmentsReceived.toLocaleString(), hideOnMobile: true },
      { key: 'completed', header: 'Completed', render: (row) => row.reviewsCompleted.toLocaleString() },
      {
        key: 'avgTime',
        header: 'Avg time',
        render: (row) => (row.averageReviewTimeMinutes ? `${row.averageReviewTimeMinutes}m` : '—'),
        hideOnMobile: true,
      },
      {
        key: 'throughput',
        header: 'Per day',
        render: (row) => `${row.reviewsPerDay}/day`,
      },
      {
        key: 'alignment',
        header: 'AI align',
        render: (row) => row.aiAlignmentScore ?? '—',
        hideOnMobile: true,
      },
    ],
    [],
  );

  const handleExport = () => {
    if (!data) return;
    const rows = data.experts.map((e) => ({
      expertName: e.expertName,
      assignmentsReceived: e.assignmentsReceived,
      reviewsCompleted: e.reviewsCompleted,
      averageReviewTimeMinutes: e.averageReviewTimeMinutes,
      reviewsPerDay: e.reviewsPerDay,
      aiAlignmentScore: e.aiAlignmentScore,
      efficiency: e.efficiency,
    }));
    exportToCsv(rows, `expert-efficiency-${days}d-${formatDateForExport(new Date())}.csv`);
  };

  return (
    <AdminRouteWorkspace role="main" aria-label="Expert efficiency report">
      <AdminRouteHero
        eyebrow="Analytics · Experts"
        icon={UserCheck}
        accent="navy"
        title="Expert Efficiency Report"
        description="Review throughput, quality alignment, and operational efficiency per expert."
        highlights={
          data
            ? [
                { label: 'Total experts', value: String(data.summary.totalExperts) },
                { label: 'Active experts', value: String(data.summary.activeExperts) },
                { label: `Reviews (${days}d)`, value: data.summary.totalReviewsCompleted.toLocaleString() },
              ]
            : undefined
        }
      />

      <AdminRoutePanel
        eyebrow="Window"
        title="Reporting window"
        description="Rolling window across the active expert pool."
        actions={
          data && data.experts.length > 0 ? (
            <Button variant="outline" size="sm" onClick={handleExport}>
              <Download className="h-4 w-4" /> Export CSV
            </Button>
          ) : undefined
        }
      >
        <SegmentedControl
          value={String(days)}
          onChange={(next) => load(Number(next) as DaysOption)}
          namespace="admin-expert-efficiency"
          options={[
            { value: '7', label: '7d' },
            { value: '14', label: '14d' },
            { value: '30', label: '30d' },
            { value: '60', label: '60d' },
            { value: '90', label: '90d' },
          ]}
          aria-label="Reporting window"
        />
      </AdminRoutePanel>

      {data ? (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          <AdminRouteSummaryCard label="Total experts" value={data.summary.totalExperts} />
          <AdminRouteSummaryCard label="Active" value={data.summary.activeExperts} tone="success" />
          <AdminRouteSummaryCard label="Reviews done" value={data.summary.totalReviewsCompleted} />
          <AdminRouteSummaryCard label="Avg/expert/day" value={data.summary.averageReviewsPerExpertPerDay} tone="info" />
        </div>
      ) : null}

      <AdminRoutePanel eyebrow="Breakdown" title="Per-expert efficiency" description="Each row is an expert over the selected window.">
        <AsyncStateWrapper
          status={status}
          onRetry={() => load(days)}
          emptyContent={
            <EmptyState
              icon={<UserCheck className="h-6 w-6" aria-hidden />}
              title="No expert activity"
              description="No experts received assignments in this window."
            />
          }
        >
          {data ? (
            <DataTable
              density="compact"
              data={data.experts}
              columns={columns}
              keyExtractor={(row) => row.expertId}
              aria-label="Expert efficiency breakdown"
            />
          ) : null}
        </AsyncStateWrapper>
        {data ? (
          <AdminRoutePanelFooter
            updatedAt={data.generatedAt}
            window={`${days}d`}
            source="Review pipeline"
          />
        ) : null}
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
