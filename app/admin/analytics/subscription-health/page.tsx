'use client';

import { useEffect, useState } from 'react';
import { DollarSign, TrendingUp, TrendingDown, Users, CreditCard, BarChart3, Download } from 'lucide-react';
import { AdminOperationsLayout, KpiStrip, BentoGrid, BentoCell } from '@/components/admin/layout/admin-operations-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { exportToCsv, formatDateForExport } from '@/lib/csv-export';
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

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Analytics', href: '/admin' },
    { label: 'Subscription Health' },
  ];

  return (
    <AdminOperationsLayout
      title="Subscription Health"
      description="MRR, churn, ARPU, trial conversion, and revenue breakdown."
      eyebrow="Analytics"
      breadcrumbs={breadcrumbs}
      actions={
        data ? (
          <Button
            variant="secondary"
            size="sm"
            onClick={() => {
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
            <KpiTile label="Monthly Recurring Revenue" value={`$${data.mrr.toLocaleString()}`} icon={<DollarSign className="h-4 w-4" />} tone="success" />
            <KpiTile label="Active Subscriptions" value={data.activeSubscriptions} icon={<Users className="h-4 w-4" />} tone="primary" />
            <KpiTile label="Churn Rate (30d)" value={`${data.churnRate}%`} icon={<TrendingDown className="h-4 w-4" />} tone={data.churnRate > 5 ? 'danger' : 'default'} />
            <KpiTile label="New This Month" value={data.newSubscriptionsThisMonth} icon={<TrendingUp className="h-4 w-4" />} tone="success" />
            <KpiTile label="ARPU" value={`$${data.arpu}`} icon={<CreditCard className="h-4 w-4" />} tone="info" />
            <KpiTile label="Trial Conversion" value={`${data.trialConversionRate}%`} icon={<BarChart3 className="h-4 w-4" />} tone="warning" />
          </KpiStrip>
        ) : null
      }
      primaryGrid={
        loading ? (
          <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
            {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-admin" />)}
          </div>
        ) : data ? (
          <BentoGrid>
            {data.revenueByPlan.length > 0 && (
              <BentoCell span={{ default: 12, xl: 6 }}>
                <Card>
                  <CardHeader><CardTitle>Revenue by Plan</CardTitle></CardHeader>
                  <CardContent>
                    <div className="space-y-3">
                      {data.revenueByPlan.map(p => (
                        <div key={p.planId} className="flex items-center gap-3">
                          <span className="w-32 truncate text-sm font-medium text-admin-fg-default">{p.planName}</span>
                          <div className="h-4 flex-1 overflow-hidden rounded-full bg-admin-bg-subtle">
                            <div className="h-full rounded-full bg-[var(--admin-primary)]" style={{ width: `${Math.min(100, data.mrr > 0 ? (p.monthlyRevenue / data.mrr) * 100 : 0)}%` }} />
                          </div>
                          <span className="w-24 text-right text-sm tabular-nums text-admin-fg-muted">${p.monthlyRevenue}</span>
                          <Badge variant="default" intensity="tinted" size="sm">{p.subscribers} subs</Badge>
                        </div>
                      ))}
                    </div>
                  </CardContent>
                </Card>
              </BentoCell>
            )}

            {data.monthlyTrend.length > 0 && (
              <BentoCell span={{ default: 12, xl: 6 }}>
                <Card>
                  <CardHeader><CardTitle>Monthly Trend (6 months)</CardTitle></CardHeader>
                  <CardContent>
                    <div className="space-y-2">
                      {data.monthlyTrend.map(m => (
                        <div key={m.month} className="flex items-center gap-3">
                          <span className="w-20 font-mono text-xs text-admin-fg-muted">{m.month}</span>
                          <div className="flex flex-1 gap-1">
                            <div className="h-4 rounded-l-full bg-[var(--admin-success)]" style={{ width: `${m.newSubscriptions * 5}%` }} />
                            <div className="h-4 rounded-r-full bg-[var(--admin-danger)]" style={{ width: `${m.cancellations * 5}%` }} />
                          </div>
                          <span className="w-12 text-right text-xs text-[var(--admin-success)]">+{m.newSubscriptions}</span>
                          <span className="w-12 text-right text-xs text-[var(--admin-danger)]">-{m.cancellations}</span>
                        </div>
                      ))}
                    </div>
                    <div className="mt-3 flex gap-4">
                      <span className="flex items-center gap-1 text-xs"><span className="h-3 w-3 rounded-full bg-[var(--admin-success)]" /> New</span>
                      <span className="flex items-center gap-1 text-xs"><span className="h-3 w-3 rounded-full bg-[var(--admin-danger)]" /> Cancellations</span>
                    </div>
                  </CardContent>
                </Card>
              </BentoCell>
            )}
          </BentoGrid>
        ) : (
          <Card><CardContent><p className="py-8 text-center text-sm text-admin-fg-muted">No data available.</p></CardContent></Card>
        )
      }
    />
  );
}
