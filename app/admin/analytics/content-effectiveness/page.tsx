'use client';

import { useEffect, useMemo, useState } from 'react';
import { Trophy, Download } from 'lucide-react';
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

interface ContentItem {
  contentId: string;
  title: string;
  subtestCode: string;
  difficulty: string;
  totalAttempts: number;
  completionRate: number;
  averageScore: number | null;
  avgTimeSeconds: number | null;
  effectivenessScore: number | null;
}
interface EffectivenessData {
  subtestFilter: string | null;
  items: ContentItem[];
  generatedAt: string;
}

type SubtestFilter = '' | 'writing' | 'speaking' | 'reading' | 'listening';
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

export default function ContentEffectivenessPage() {
  const [data, setData] = useState<EffectivenessData | null>(null);
  const [status, setStatus] = useState<Status>('loading');
  const [subtest, setSubtest] = useState<SubtestFilter>('');

  const load = (s: SubtestFilter) => {
    setStatus('loading');
    setSubtest(s);
    const q = s ? `?subtestCode=${s}&top=50` : '?top=50';
    apiRequest<EffectivenessData>(`/v1/admin/analytics/content-effectiveness${q}`)
      .then((res) => {
        setData(res);
        setStatus(res.items.length === 0 ? 'empty' : 'success');
      })
      .catch(() => setStatus('error'));
  };

  useEffect(() => {
    analytics.track('admin_content_effectiveness_viewed');
    // eslint-disable-next-line react-hooks/set-state-in-effect -- data-fetch on mount
    load('');
  }, []);

  const columns = useMemo<Column<ContentItem>[]>(
    () => [
      {
        key: 'rank',
        header: '#',
        render: (_row, idx) => <span className="font-semibold text-muted">{idx + 1}</span>,
        className: 'w-10',
      },
      {
        key: 'title',
        header: 'Content',
        render: (row) => (
          <div className="min-w-0">
            <p className="truncate font-semibold text-navy">{row.title}</p>
            <div className="mt-1 flex flex-wrap items-center gap-1.5">
              <Badge variant="outline" className="capitalize">{row.subtestCode}</Badge>
              <Badge variant="muted">{row.difficulty}</Badge>
            </div>
          </div>
        ),
      },
      {
        key: 'attempts',
        header: 'Attempts',
        render: (row) => row.totalAttempts.toLocaleString(),
        hideOnMobile: true,
      },
      {
        key: 'completion',
        header: 'Complete',
        render: (row) => `${row.completionRate}%`,
      },
      {
        key: 'score',
        header: 'Avg score',
        render: (row) => <span className="font-semibold text-navy">{row.averageScore ?? '—'}</span>,
      },
      {
        key: 'effectiveness',
        header: 'Effectiveness',
        render: (row) => (
          <span className="font-semibold text-primary">{row.effectivenessScore ?? '—'}</span>
        ),
      },
    ],
    [],
  );

  const handleExport = () => {
    if (!data) return;
    const rows = data.items.map((item) => ({
      title: item.title,
      subtest: item.subtestCode,
      difficulty: item.difficulty,
      totalAttempts: item.totalAttempts,
      completionRate: item.completionRate,
      averageScore: item.averageScore,
      avgTimeSeconds: item.avgTimeSeconds,
      effectivenessScore: item.effectivenessScore,
    }));
    exportToCsv(rows, `content-effectiveness-${formatDateForExport(new Date())}.csv`);
  };

  const totals = useMemo(() => {
    if (!data) return null;
    const attempts = data.items.reduce((sum, i) => sum + i.totalAttempts, 0);
    const avgComplete = data.items.length
      ? Math.round(data.items.reduce((sum, i) => sum + i.completionRate, 0) / data.items.length)
      : 0;
    return { attempts, avgComplete };
  }, [data]);

  return (
    <AdminRouteWorkspace role="main" aria-label="Content effectiveness analytics">
      <AdminRouteHero
        eyebrow="Analytics · Content"
        icon={Trophy}
        accent="navy"
        title="Content Effectiveness"
        description="Which content produces the most improvement and engagement? Use this to prioritise library investment."
        highlights={
          data && totals
            ? [
                { label: 'Ranked items', value: String(data.items.length) },
                { label: 'Attempts', value: totals.attempts.toLocaleString() },
                { label: 'Avg completion', value: `${totals.avgComplete}%` },
              ]
            : undefined
        }
      />

      <AdminRoutePanel
        eyebrow="Filter"
        title="Subtest filter"
        description="Restrict the ranking to a single productive or receptive skill."
        actions={
          data && data.items.length > 0 ? (
            <Button variant="outline" size="sm" onClick={handleExport}>
              <Download className="h-4 w-4" /> Export CSV
            </Button>
          ) : undefined
        }
      >
        <SegmentedControl
          value={subtest}
          onChange={(next) => load(next)}
          namespace="admin-content-effectiveness"
          options={[
            { value: '', label: 'All' },
            { value: 'writing', label: 'Writing' },
            { value: 'speaking', label: 'Speaking' },
            { value: 'reading', label: 'Reading' },
            { value: 'listening', label: 'Listening' },
          ]}
          aria-label="Subtest filter"
        />
        {data && totals ? (
          <AdminRouteStatRow
            items={[
              { label: 'Ranked items', value: String(data.items.length) },
              { label: 'Attempts', value: totals.attempts.toLocaleString() },
              { label: 'Avg completion', value: `${totals.avgComplete}%` },
            ]}
          />
        ) : null}
      </AdminRoutePanel>

      <AdminRoutePanel eyebrow="Ranking" title="Top content by effectiveness" description="Sorted by effectiveness score, capped at 50.">
        <AsyncStateWrapper
          status={status}
          onRetry={() => load(subtest)}
          emptyContent={
            <EmptyState
              icon={<Trophy className="h-6 w-6" aria-hidden />}
              title="No content effectiveness data"
              description="There are no attempts recorded for the selected filters."
            />
          }
        >
          {data ? (
            <DataTable
              density="compact"
              data={data.items}
              columns={columns}
              keyExtractor={(row) => row.contentId}
              aria-label="Content effectiveness ranking"
            />
          ) : null}
        </AsyncStateWrapper>
        {data ? (
          <AdminRoutePanelFooter updatedAt={data.generatedAt} source="Attempt telemetry" />
        ) : null}
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
