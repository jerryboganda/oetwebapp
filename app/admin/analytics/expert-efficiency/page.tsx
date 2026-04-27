'use client';

import { useEffect, useState } from 'react';
import { UserCheck, Download, Users, Activity, Gauge, BarChart3 } from 'lucide-react';
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

interface ExpertReport { expertId: string; expertName: string; period: number; assignmentsReceived: number; reviewsCompleted: number; averageReviewTimeMinutes: number | null; reviewsPerDay: number; aiAlignmentScore: number | null; efficiency: string }
interface EfficiencyData { period: number; experts: ExpertReport[]; summary: { totalExperts: number; activeExperts: number; totalReviewsCompleted: number; averageReviewsPerExpertPerDay: number }; generatedAt: string }

const apiRequest = apiClient.request;

const EFF_BADGE: Record<string, { label: string; color: string }> = { high: { label: 'High', color: 'bg-emerald-100 text-emerald-700' }, medium: { label: 'Medium', color: 'bg-amber-100 text-amber-700' }, low: { label: 'Low', color: 'bg-danger/15 text-danger' }, 'no-data': { label: 'No Data', color: 'bg-muted text-muted' } };

export default function ExpertEfficiencyPage() {
  const [data, setData] = useState<EfficiencyData | null>(null);
  const [loading, setLoading] = useState(true);
  const [days, setDays] = useState(30);

  const load = (d: number) => {
    setLoading(true); setDays(d);
    apiRequest<EfficiencyData>(`/v1/admin/analytics/expert-efficiency?days=${d}`).then(setData).catch(() => {}).finally(() => setLoading(false));
  };

  // eslint-disable-next-line react-hooks/set-state-in-effect -- data-fetch on mount: setState from API response is the correct pattern
  useEffect(() => { analytics.track('admin_expert_efficiency_viewed'); load(30); }, []);

  return (
    <AdminRouteWorkspace role="main" aria-label="Expert Efficiency Report">
      <AdminRouteHero
        eyebrow="Analytics"
        icon={UserCheck}
        accent="navy"
        title="Expert Efficiency Report"
        description="Review throughput, quality alignment, and operational efficiency per expert."
        aside={data && data.experts.length > 0 ? (
          <div className="rounded-2xl border border-border bg-background-light p-4 shadow-sm">
            <Button variant="outline" size="sm" className="gap-2" onClick={() => {
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
            }}>
              <Download className="w-4 h-4" />
              Export CSV
            </Button>
          </div>
        ) : undefined}
      />

      <div className="flex gap-2">
        {[7, 14, 30, 60, 90].map(d => (
          <button key={d} onClick={() => load(d)} className={`px-4 py-2 rounded-lg text-sm font-medium ${days === d ? 'bg-primary text-primary-foreground' : 'bg-muted'}`}>{d}d</button>
        ))}
      </div>

      {loading ? (
        <div className="space-y-3">{Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className="h-20 rounded-xl" />)}</div>
      ) : data ? (
        <MotionSection className="space-y-6">
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
            <AdminRouteSummaryCard label="Total Experts" value={data.summary.totalExperts} icon={<Users className="h-5 w-5" />} />
            <AdminRouteSummaryCard label="Active" value={data.summary.activeExperts} icon={<Activity className="h-5 w-5" />} tone="success" />
            <AdminRouteSummaryCard label="Reviews Done" value={data.summary.totalReviewsCompleted} icon={<BarChart3 className="h-5 w-5" />} />
            <AdminRouteSummaryCard label="Avg/Expert/Day" value={data.summary.averageReviewsPerExpertPerDay} icon={<Gauge className="h-5 w-5" />} />
          </div>

          <AdminRoutePanel title="Expert breakdown">
            <div className="space-y-3">
              {data.experts.map(e => {
                const eff = EFF_BADGE[e.efficiency] ?? EFF_BADGE['no-data'];
                return (
                  <MotionItem key={e.expertId}>
                    <Card className="p-4">
                      <div className="flex items-center justify-between mb-2">
                        <div className="flex items-center gap-2"><UserCheck className="w-4 h-4 text-primary" /><h3 className="font-semibold">{e.expertName}</h3></div>
                        <Badge className={eff.color}>{eff.label} Efficiency</Badge>
                      </div>
                      <div className="grid grid-cols-5 gap-3 text-center text-sm">
                        <div><p className="font-bold">{e.assignmentsReceived}</p><p className="text-[10px] text-muted">Assigned</p></div>
                        <div><p className="font-bold">{e.reviewsCompleted}</p><p className="text-[10px] text-muted">Completed</p></div>
                        <div><p className="font-bold">{e.averageReviewTimeMinutes ?? '--'}m</p><p className="text-[10px] text-muted">Avg Time</p></div>
                        <div><p className="font-bold">{e.reviewsPerDay}/day</p><p className="text-[10px] text-muted">Throughput</p></div>
                        <div><p className="font-bold">{e.aiAlignmentScore ?? '--'}</p><p className="text-[10px] text-muted">AI Align</p></div>
                      </div>
                    </Card>
                  </MotionItem>
                );
              })}
            </div>
          </AdminRoutePanel>
        </MotionSection>
      ) : (
        <AdminRoutePanel><p className="text-center text-sm text-muted">No data available.</p></AdminRoutePanel>
      )}
    </AdminRouteWorkspace>
  );
}
