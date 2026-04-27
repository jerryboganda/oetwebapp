'use client';

import { useEffect, useState } from 'react';
import { DollarSign, TrendingUp, TrendingDown, Users, CreditCard, BarChart3, Download } from 'lucide-react';
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

interface RevenuePlan { planId: string; planName: string; subscribers: number; monthlyRevenue: number }
interface MonthlyTrend { month: string; newSubscriptions: number; cancellations: number }
interface SubHealthData {
  mrr: number; activeSubscriptions: number; churnRate: number; newSubscriptionsThisMonth: number;
  trialConversionRate: number; arpu: number; revenueByPlan: RevenuePlan[]; monthlyTrend: MonthlyTrend[]; generatedAt: string;
}

const apiRequest = apiClient.request;

export default function SubscriptionHealthPage() {
  const [data, setData] = useState<SubHealthData | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    analytics.track('admin_subscription_health_viewed');
    apiRequest<SubHealthData>('/v1/admin/analytics/subscription-health').then(setData).catch(() => {}).finally(() => setLoading(false));
  }, []);

  return (
    <AdminRouteWorkspace role="main" aria-label="Subscription Health">
      <AdminRouteHero
        eyebrow="Analytics"
        icon={DollarSign}
        accent="emerald"
        title="Subscription Health"
        description="MRR, churn, ARPU, trial conversion, and revenue breakdown."
        aside={data ? (
          <div className="rounded-2xl border border-border bg-background-light p-4 shadow-sm">
            <Button variant="outline" size="sm" className="gap-2" onClick={() => {
              const rows: Record<string, unknown>[] = [
                { metric: 'MRR', value: data.mrr },
                { metric: 'Active Subscriptions', value: data.activeSubscriptions },
                { metric: 'Churn Rate (%)', value: data.churnRate },
                { metric: 'New This Month', value: data.newSubscriptionsThisMonth },
                { metric: 'ARPU', value: data.arpu },
                { metric: 'Trial Conversion (%)', value: data.trialConversionRate },
                ...data.revenueByPlan.map(p => ({ metric: `Plan: ${p.planName}`, value: p.monthlyRevenue, subscribers: p.subscribers })),
                ...data.monthlyTrend.map(m => ({ metric: `Trend: ${m.month}`, value: m.newSubscriptions, cancellations: m.cancellations })),
              ];
              exportToCsv(rows, `subscription-health-${formatDateForExport(new Date())}.csv`);
            }}>
              <Download className="w-4 h-4" />
              Export CSV
            </Button>
          </div>
        ) : undefined}
      />

      {loading ? (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-3">{Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-xl" />)}</div>
      ) : data ? (
        <MotionSection className="space-y-6">
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
            <MotionItem><AdminRouteSummaryCard label="Monthly Recurring Revenue" value={`$${data.mrr.toLocaleString()}`} icon={<DollarSign className="h-5 w-5" />} tone="success" /></MotionItem>
            <MotionItem><AdminRouteSummaryCard label="Active Subscriptions" value={data.activeSubscriptions} icon={<Users className="h-5 w-5" />} /></MotionItem>
            <MotionItem><AdminRouteSummaryCard label="Churn Rate (30d)" value={`${data.churnRate}%`} icon={<TrendingDown className="h-5 w-5" />} tone={data.churnRate > 5 ? 'danger' : 'default'} /></MotionItem>
            <MotionItem><AdminRouteSummaryCard label="New This Month" value={data.newSubscriptionsThisMonth} icon={<TrendingUp className="h-5 w-5" />} tone="success" /></MotionItem>
            <MotionItem><AdminRouteSummaryCard label="ARPU" value={`$${data.arpu}`} icon={<CreditCard className="h-5 w-5" />} /></MotionItem>
            <MotionItem><AdminRouteSummaryCard label="Trial Conversion" value={`${data.trialConversionRate}%`} icon={<BarChart3 className="h-5 w-5" />} tone="warning" /></MotionItem>
          </div>

          {data.revenueByPlan.length > 0 && (
            <AdminRoutePanel title="Revenue by Plan">
              <div className="space-y-3">
                {data.revenueByPlan.map(p => (
                  <div key={p.planId} className="flex items-center gap-3">
                    <span className="text-sm font-medium w-32 truncate">{p.planName}</span>
                    <div className="flex-1 h-4 rounded-full bg-muted overflow-hidden"><div className="h-full rounded-full bg-primary" style={{ width: `${Math.min(100, data.mrr > 0 ? p.monthlyRevenue / data.mrr * 100 : 0)}%` }} /></div>
                    <span className="text-sm text-muted w-24 text-right">${p.monthlyRevenue}</span>
                    <Badge variant="outline" className="text-[10px]">{p.subscribers} subs</Badge>
                  </div>
                ))}
              </div>
            </AdminRoutePanel>
          )}

          {data.monthlyTrend.length > 0 && (
            <AdminRoutePanel title="Monthly Trend (6 months)">
              <div className="space-y-2">
                {data.monthlyTrend.map(m => (
                  <div key={m.month} className="flex items-center gap-3">
                    <span className="text-xs font-mono w-20 text-muted">{m.month}</span>
                    <div className="flex-1 flex gap-1">
                      <div className="h-4 rounded-l-full bg-success" style={{ width: `${m.newSubscriptions * 5}%` }} />
                      <div className="h-4 rounded-r-full bg-danger" style={{ width: `${m.cancellations * 5}%` }} />
                    </div>
                    <span className="text-xs text-success w-12 text-right">+{m.newSubscriptions}</span>
                    <span className="text-xs text-danger w-12 text-right">-{m.cancellations}</span>
                  </div>
                ))}
              </div>
              <div className="flex gap-4 mt-3">
                <span className="text-xs flex items-center gap-1"><span className="w-3 h-3 rounded-full bg-success" /> New</span>
                <span className="text-xs flex items-center gap-1"><span className="w-3 h-3 rounded-full bg-danger" /> Cancellations</span>
              </div>
            </AdminRoutePanel>
          )}
        </MotionSection>
      ) : (
        <AdminRoutePanel><p className="text-center text-sm text-muted">No data available.</p></AdminRoutePanel>
      )}
    </AdminRouteWorkspace>
  );
}
