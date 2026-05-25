'use client';

import { useEffect, useState } from 'react';
import { UserCheck, Download, Users, Activity, Gauge, BarChart3 } from 'lucide-react';
import { exportToCsv, formatDateForExport } from '@/lib/csv-export';
import { AdminOperationsLayout, KpiStrip, BentoGrid, BentoCell } from '@/components/admin/layout/admin-operations-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';

interface ExpertReport { expertId: string; expertName: string; period: number; assignmentsReceived: number; reviewsCompleted: number; averageReviewTimeMinutes: number | null; reviewsPerDay: number; aiAlignmentScore: number | null; efficiency: string }
interface EfficiencyData { period: number; experts: ExpertReport[]; summary: { totalExperts: number; activeExperts: number; totalReviewsCompleted: number; averageReviewsPerExpertPerDay: number }; generatedAt: string }

const apiRequest = apiClient.request;

const EFF_VARIANT: Record<string, 'success' | 'warning' | 'danger' | 'default'> = {
  high: 'success',
  medium: 'warning',
  low: 'danger',
  'no-data': 'default',
};

const EFF_LABEL: Record<string, string> = {
  high: 'High',
  medium: 'Medium',
  low: 'Low',
  'no-data': 'No Data',
};

export default function ExpertEfficiencyPage() {
  const [data, setData] = useState<EfficiencyData | null>(null);
  const [loading, setLoading] = useState(true);
  const [days, setDays] = useState(30);

  const load = (d: number) => {
    setLoading(true); setDays(d);
    apiRequest<EfficiencyData>(`/v1/admin/analytics/expert-efficiency?days=${d}`).then(setData).catch(() => {}).finally(() => setLoading(false));
  };

  // eslint-disable-next-line react-hooks/set-state-in-effect -- data-fetch on mount
  useEffect(() => { analytics.track('admin_expert_efficiency_viewed'); load(30); }, []);

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Analytics', href: '/admin' },
    { label: 'Tutor efficiency' },
  ];

  return (
    <AdminOperationsLayout
      title="Tutor Efficiency Report"
      description="Review throughput, quality alignment, and operational efficiency per tutor."
      eyebrow="Analytics"
      breadcrumbs={breadcrumbs}
      actions={
        data && data.experts.length > 0 ? (
          <Button
            variant="secondary"
            size="sm"
            onClick={() => {
              const rows = data.experts.map(e => ({
                expertName: e.expertName,
                assignmentsReceived: e.assignmentsReceived,
                reviewsCompleted: e.reviewsCompleted,
                averageReviewTimeMinutes: e.averageReviewTimeMinutes,
                reviewsPerDay: e.reviewsPerDay,
                aiAlignmentScore: e.aiAlignmentScore,
                efficiency: e.efficiency,
              }));
              exportToCsv(rows, `expert-efficiency-${days}d-${formatDateForExport(new Date())}.csv`);
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
            <KpiTile label="Total Tutors" value={data.summary.totalExperts} icon={<Users className="h-4 w-4" />} tone="primary" />
            <KpiTile label="Active" value={data.summary.activeExperts} icon={<Activity className="h-4 w-4" />} tone="success" />
            <KpiTile label="Reviews Done" value={data.summary.totalReviewsCompleted} icon={<BarChart3 className="h-4 w-4" />} tone="info" />
            <KpiTile label="Avg/Tutor/Day" value={data.summary.averageReviewsPerExpertPerDay} icon={<Gauge className="h-4 w-4" />} tone="default" />
          </KpiStrip>
        ) : null
      }
      primaryGrid={
        <div className="space-y-6">
          <div className="flex gap-2">
            {[7, 14, 30, 60, 90].map(d => (
              <Button
                key={d}
                size="sm"
                variant={days === d ? 'primary' : 'secondary'}
                onClick={() => load(d)}
              >
                {d}d
              </Button>
            ))}
          </div>

          {loading ? (
            <div className="space-y-3">{Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className="h-20 rounded-admin" />)}</div>
          ) : data ? (
            <Card>
              <CardHeader>
                <CardTitle>Tutor breakdown</CardTitle>
              </CardHeader>
              <CardContent>
                <BentoGrid>
                  {data.experts.map(e => {
                    const variant = EFF_VARIANT[e.efficiency] ?? 'default';
                    const label = EFF_LABEL[e.efficiency] ?? 'No Data';
                    return (
                      <BentoCell key={e.expertId} span={12}>
                        <Card>
                          <CardContent className="p-4">
                            <div className="mb-3 flex items-center justify-between">
                              <div className="flex items-center gap-2">
                                <UserCheck className="h-4 w-4 text-[var(--admin-primary)]" />
                                <h3 className="font-semibold text-admin-fg-strong">{e.expertName}</h3>
                              </div>
                              <Badge variant={variant} intensity="tinted">{label} Efficiency</Badge>
                            </div>
                            <div className="grid grid-cols-5 gap-3 text-center text-sm">
                              <div><p className="font-bold tabular-nums text-admin-fg-strong">{e.assignmentsReceived}</p><p className="text-[10px] text-admin-fg-muted">Assigned</p></div>
                              <div><p className="font-bold tabular-nums text-admin-fg-strong">{e.reviewsCompleted}</p><p className="text-[10px] text-admin-fg-muted">Completed</p></div>
                              <div><p className="font-bold tabular-nums text-admin-fg-strong">{e.averageReviewTimeMinutes ?? '--'}m</p><p className="text-[10px] text-admin-fg-muted">Avg Time</p></div>
                              <div><p className="font-bold tabular-nums text-admin-fg-strong">{e.reviewsPerDay}/day</p><p className="text-[10px] text-admin-fg-muted">Throughput</p></div>
                              <div><p className="font-bold tabular-nums text-admin-fg-strong">{e.aiAlignmentScore ?? '--'}</p><p className="text-[10px] text-admin-fg-muted">AI Align</p></div>
                            </div>
                          </CardContent>
                        </Card>
                      </BentoCell>
                    );
                  })}
                </BentoGrid>
              </CardContent>
            </Card>
          ) : (
            <Card><CardContent><p className="py-8 text-center text-sm text-admin-fg-muted">No data available.</p></CardContent></Card>
          )}
        </div>
      }
    />
  );
}
