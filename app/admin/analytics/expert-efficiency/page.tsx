'use client';

import { useEffect, useState } from 'react';
import { UserCheck, Clock, BarChart3, Zap } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';

interface ExpertReport { expertId: string; expertName: string; period: number; assignmentsReceived: number; reviewsCompleted: number; averageReviewTimeMinutes: number | null; reviewsPerDay: number; aiAlignmentScore: number | null; efficiency: string }
interface EfficiencyData { period: number; experts: ExpertReport[]; summary: { totalExperts: number; activeExperts: number; totalReviewsCompleted: number; averageReviewsPerExpertPerDay: number }; generatedAt: string }

async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, { ...init, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', ...init?.headers } });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

const EFF_BADGE: Record<string, { label: string; color: string }> = { high: { label: 'High', color: 'bg-emerald-100 text-emerald-700' }, medium: { label: 'Medium', color: 'bg-amber-100 text-amber-700' }, low: { label: 'Low', color: 'bg-red-100 text-red-700' }, 'no-data': { label: 'No Data', color: 'bg-muted text-muted-foreground' } };

export default function ExpertEfficiencyPage() {
  const [data, setData] = useState<EfficiencyData | null>(null);
  const [loading, setLoading] = useState(true);
  const [days, setDays] = useState(30);

  const load = (d: number) => {
    setLoading(true); setDays(d);
    apiRequest<EfficiencyData>(`/v1/admin/analytics/expert-efficiency?days=${d}`).then(setData).catch(() => {}).finally(() => setLoading(false));
  };

  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { analytics.track('admin_expert_efficiency_viewed'); load(30); }, []);

  return (
    <div className="min-h-screen bg-background">
      <div className="max-w-5xl mx-auto px-4 py-8">
        <h1 className="text-2xl font-bold mb-1">Expert Efficiency Report</h1>
        <p className="text-muted-foreground mb-6">Review throughput, quality alignment, and operational efficiency per expert.</p>

        <div className="flex gap-2 mb-6">
          {[7, 14, 30, 60, 90].map(d => (
            <button key={d} onClick={() => load(d)} className={`px-4 py-2 rounded-lg text-sm font-medium ${days === d ? 'bg-primary text-primary-foreground' : 'bg-muted'}`}>{d}d</button>
          ))}
        </div>

        {loading ? <div className="space-y-3">{Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className="h-20 rounded-xl" />)}</div> : data ? (
          <MotionSection className="space-y-6">
            {/* Summary */}
            <div className="grid grid-cols-4 gap-4">
              <Card className="p-4 text-center"><p className="text-2xl font-bold">{data.summary.totalExperts}</p><p className="text-xs text-muted-foreground">Total Experts</p></Card>
              <Card className="p-4 text-center"><p className="text-2xl font-bold">{data.summary.activeExperts}</p><p className="text-xs text-muted-foreground">Active</p></Card>
              <Card className="p-4 text-center"><p className="text-2xl font-bold">{data.summary.totalReviewsCompleted}</p><p className="text-xs text-muted-foreground">Reviews Done</p></Card>
              <Card className="p-4 text-center"><p className="text-2xl font-bold">{data.summary.averageReviewsPerExpertPerDay}</p><p className="text-xs text-muted-foreground">Avg/Expert/Day</p></Card>
            </div>

            {/* Expert list */}
            <div className="space-y-3">
              {data.experts.map(e => {
                const eff = EFF_BADGE[e.efficiency] ?? EFF_BADGE['no-data'];
                return (
                  <MotionItem key={e.expertId}>
                    <Card className="p-4">
                      <div className="flex items-center justify-between mb-2">
                        <div className="flex items-center gap-2"><UserCheck className="w-4 h-4 text-blue-500" /><h3 className="font-semibold">{e.expertName}</h3></div>
                        <Badge className={eff.color}>{eff.label} Efficiency</Badge>
                      </div>
                      <div className="grid grid-cols-5 gap-3 text-center text-sm">
                        <div><p className="font-bold">{e.assignmentsReceived}</p><p className="text-[10px] text-muted-foreground">Assigned</p></div>
                        <div><p className="font-bold">{e.reviewsCompleted}</p><p className="text-[10px] text-muted-foreground">Completed</p></div>
                        <div><p className="font-bold">{e.averageReviewTimeMinutes ?? '--'}m</p><p className="text-[10px] text-muted-foreground">Avg Time</p></div>
                        <div><p className="font-bold">{e.reviewsPerDay}/day</p><p className="text-[10px] text-muted-foreground">Throughput</p></div>
                        <div><p className="font-bold">{e.aiAlignmentScore ?? '--'}</p><p className="text-[10px] text-muted-foreground">AI Align</p></div>
                      </div>
                    </Card>
                  </MotionItem>
                );
              })}
            </div>
          </MotionSection>
        ) : <Card className="p-8 text-center text-muted-foreground"><p>No data available.</p></Card>}
      </div>
    </div>
  );
}
