'use client';

import { useEffect, useState } from 'react';
import { Download, BarChart3 } from 'lucide-react';
import { exportToCsv, formatDateForExport } from '@/lib/csv-export';
import { AdminOperationsLayout, KpiStrip, BentoGrid, BentoCell } from '@/components/admin/layout/admin-operations-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';

interface ContentItem { contentId: string; title: string; subtestCode: string; difficulty: string; totalAttempts: number; completionRate: number; averageScore: number | null; avgTimeSeconds: number | null; effectivenessScore: number | null }
interface EffectivenessData { subtestFilter: string | null; items: ContentItem[]; generatedAt: string }

const apiRequest = apiClient.request;

export default function ContentEffectivenessPage() {
  const [data, setData] = useState<EffectivenessData | null>(null);
  const [loading, setLoading] = useState(true);
  const [subtest, setSubtest] = useState('');

  const load = (s: string) => {
    setLoading(true); setSubtest(s);
    const q = s ? `?subtestCode=${s}&top=50` : '?top=50';
    apiRequest<EffectivenessData>(`/v1/admin/analytics/content-effectiveness${q}`).then(setData).catch(() => {}).finally(() => setLoading(false));
  };

  // eslint-disable-next-line react-hooks/set-state-in-effect -- data-fetch on mount
  useEffect(() => { analytics.track('admin_content_effectiveness_viewed'); load(''); }, []);

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Analytics', href: '/admin' },
    { label: 'Content effectiveness' },
  ];

  const totalAttempts = data?.items.reduce((acc, item) => acc + item.totalAttempts, 0) ?? 0;
  const avgCompletion = data && data.items.length > 0
    ? Math.round(data.items.reduce((acc, item) => acc + item.completionRate, 0) / data.items.length)
    : 0;

  return (
    <AdminOperationsLayout
      title="Content effectiveness"
      description="Which content produces the most improvement and engagement?"
      eyebrow="Analytics"
      breadcrumbs={breadcrumbs}
      actions={
        data && data.items.length > 0 ? (
          <Button
            variant="secondary"
            size="sm"
            onClick={() => {
              const rows = data.items.map(item => ({
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
            <KpiTile label="Total items" value={data.items.length} icon={<BarChart3 className="h-4 w-4" />} tone="primary" />
            <KpiTile label="Total attempts" value={totalAttempts.toLocaleString()} tone="info" />
            <KpiTile label="Avg completion" value={`${avgCompletion}%`} tone="success" />
            <KpiTile label="Filter" value={subtest || 'All'} tone="default" />
          </KpiStrip>
        ) : null
      }
      primaryGrid={
        <div className="space-y-6">
          <div className="flex flex-wrap gap-2">
            {['', 'writing', 'speaking', 'reading', 'listening'].map(s => (
              <Button
                key={s}
                size="sm"
                variant={subtest === s ? 'primary' : 'secondary'}
                onClick={() => load(s)}
                className="capitalize"
              >
                {s || 'All'}
              </Button>
            ))}
          </div>

          {loading ? (
            <div className="space-y-3">{Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className="h-20 rounded-admin" />)}</div>
          ) : data ? (
            <BentoGrid>
              {data.items.map((item, idx) => (
                <BentoCell key={item.contentId} span={{ default: 12, md: 6 }}>
                  <Card>
                    <CardContent className="p-4">
                      <div className="flex items-start gap-3">
                        <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-admin-bg-subtle text-sm font-bold text-admin-fg-strong">{idx + 1}</div>
                        <div className="min-w-0 flex-1">
                          <h3 className="truncate text-sm font-semibold text-admin-fg-strong">{item.title}</h3>
                          <div className="mt-1 flex flex-wrap gap-2">
                            <Badge variant="primary" intensity="tinted" size="sm" className="capitalize">{item.subtestCode}</Badge>
                            <Badge variant="default" intensity="tinted" size="sm">{item.difficulty}</Badge>
                          </div>
                        </div>
                      </div>
                      <div className="mt-3 grid grid-cols-4 gap-2 text-center">
                        <div>
                          <p className="text-sm font-bold tabular-nums text-admin-fg-strong">{item.totalAttempts}</p>
                          <p className="text-[10px] text-admin-fg-muted">Attempts</p>
                        </div>
                        <div>
                          <p className="text-sm font-bold tabular-nums text-admin-fg-strong">{item.completionRate}%</p>
                          <p className="text-[10px] text-admin-fg-muted">Complete</p>
                        </div>
                        <div>
                          <p className="text-sm font-bold tabular-nums text-admin-fg-strong">{item.averageScore ?? '--'}</p>
                          <p className="text-[10px] text-admin-fg-muted">Avg Score</p>
                        </div>
                        <div>
                          <p className="text-sm font-bold tabular-nums text-admin-fg-strong">{item.effectivenessScore ?? '--'}</p>
                          <p className="text-[10px] text-admin-fg-muted">Score</p>
                        </div>
                      </div>
                    </CardContent>
                  </Card>
                </BentoCell>
              ))}
            </BentoGrid>
          ) : (
            <Card><CardContent><p className="py-8 text-center text-sm text-admin-fg-muted">No data available.</p></CardContent></Card>
          )}
        </div>
      }
    />
  );
}
