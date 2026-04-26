'use client';

import { useEffect, useState } from 'react';
import { DollarSign, TrendingUp, TrendingDown, Users, CreditCard, BarChart3, Download } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { exportToCsv, formatDateForExport } from '@/lib/csv-export';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
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
    <div className="min-h-screen bg-background-light">
      <div className="max-w-5xl mx-auto px-4 py-8">
        <div className="flex items-center justify-between mb-1">
          <h1 className="text-2xl font-bold">Subscription Health</h1>
          {data && (
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
          )}
        </div>
        <p className="text-muted mb-6">MRR, churn, ARPU, trial conversion, and revenue breakdown.</p>

        {loading ? <div className="grid grid-cols-3 gap-4">{Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-xl" />)}</div> : data ? (
          <MotionSection className="space-y-6">
            {/* Key metrics */}
            <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
              <MotionItem><Card className="p-5 text-center"><DollarSign className="w-6 h-6 mx-auto mb-2 text-emerald-500" /><p className="text-3xl font-bold">${data.mrr.toLocaleString()}</p><p className="text-sm text-muted">Monthly Recurring Revenue</p></Card></MotionItem>
              <MotionItem><Card className="p-5 text-center"><Users className="w-6 h-6 mx-auto mb-2 text-primary" /><p className="text-3xl font-bold">{data.activeSubscriptions}</p><p className="text-sm text-muted">Active Subscriptions</p></Card></MotionItem>
              <MotionItem><Card className="p-5 text-center"><TrendingDown className="w-6 h-6 mx-auto mb-2 text-danger" /><p className="text-3xl font-bold">{data.churnRate}%</p><p className="text-sm text-muted">Churn Rate (30d)</p></Card></MotionItem>
              <MotionItem><Card className="p-5 text-center"><TrendingUp className="w-6 h-6 mx-auto mb-2 text-emerald-500" /><p className="text-3xl font-bold">{data.newSubscriptionsThisMonth}</p><p className="text-sm text-muted">New This Month</p></Card></MotionItem>
              <MotionItem><Card className="p-5 text-center"><CreditCard className="w-6 h-6 mx-auto mb-2 text-purple-500" /><p className="text-3xl font-bold">${data.arpu}</p><p className="text-sm text-muted">ARPU</p></Card></MotionItem>
              <MotionItem><Card className="p-5 text-center"><BarChart3 className="w-6 h-6 mx-auto mb-2 text-amber-500" /><p className="text-3xl font-bold">{data.trialConversionRate}%</p><p className="text-sm text-muted">Trial Conversion</p></Card></MotionItem>
            </div>

            {/* Revenue by plan */}
            {data.revenueByPlan.length > 0 && (
              <Card className="p-5">
                <h3 className="font-semibold mb-4">Revenue by Plan</h3>
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
              </Card>
            )}

            {/* Monthly trend */}
            {data.monthlyTrend.length > 0 && (
              <Card className="p-5">
                <h3 className="font-semibold mb-4">Monthly Trend (6 months)</h3>
                <div className="space-y-2">
                  {data.monthlyTrend.map(m => (
                    <div key={m.month} className="flex items-center gap-3">
                      <span className="text-xs font-mono w-20 text-muted">{m.month}</span>
                      <div className="flex-1 flex gap-1">
                        <div className="h-4 rounded-l-full bg-emerald-400" style={{ width: `${m.newSubscriptions * 5}%` }} />
                        <div className="h-4 rounded-r-full bg-danger" style={{ width: `${m.cancellations * 5}%` }} />
                      </div>
                      <span className="text-xs text-emerald-600 w-12 text-right">+{m.newSubscriptions}</span>
                      <span className="text-xs text-danger w-12 text-right">-{m.cancellations}</span>
                    </div>
                  ))}
                </div>
                <div className="flex gap-4 mt-3"><span className="text-xs flex items-center gap-1"><span className="w-3 h-3 rounded-full bg-emerald-400" /> New</span><span className="text-xs flex items-center gap-1"><span className="w-3 h-3 rounded-full bg-danger" /> Cancellations</span></div>
              </Card>
            )}
          </MotionSection>
        ) : <Card className="p-8 text-center text-muted"><p>No data available.</p></Card>}
      </div>
    </div>
  );
}
