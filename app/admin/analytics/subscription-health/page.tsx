'use client';

import { useEffect, useState } from 'react';
import { DollarSign, TrendingUp, TrendingDown, Users, CreditCard, BarChart3, Download } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { BarMeter } from '@/components/ui/bar-meter';
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

interface RevenuePlan {
  planId: string;
  planName: string;
  subscribers: number;
  monthlyRevenue: number;
}
interface MonthlyTrend {
  month: string;
  newSubscriptions: number;
  cancellations: number;
}
interface SubHealthData {
  mrr: number;
  activeSubscriptions: number;
  churnRate: number;
  newSubscriptionsThisMonth: number;
  trialConversionRate: number;
  arpu: number;
  revenueByPlan: RevenuePlan[];
  monthlyTrend: MonthlyTrend[];
  generatedAt: string;
}

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

export default function SubscriptionHealthPage() {
  const [data, setData] = useState<SubHealthData | null>(null);
  const [status, setStatus] = useState<Status>('loading');

  useEffect(() => {
    analytics.track('admin_subscription_health_viewed');
    apiRequest<SubHealthData>('/v1/admin/analytics/subscription-health')
      .then((res) => {
        setData(res);
        setStatus('success');
      })
      .catch(() => setStatus('error'));
  }, []);

  const handleExport = () => {
    if (!data) return;
    const rows: Record<string, unknown>[] = [
      { metric: 'MRR', value: data.mrr },
      { metric: 'Active Subscriptions', value: data.activeSubscriptions },
      { metric: 'Churn Rate (%)', value: data.churnRate },
      { metric: 'New This Month', value: data.newSubscriptionsThisMonth },
      { metric: 'ARPU', value: data.arpu },
      { metric: 'Trial Conversion (%)', value: data.trialConversionRate },
      ...data.revenueByPlan.map((p) => ({
        metric: `Plan: ${p.planName}`,
        value: p.monthlyRevenue,
        subscribers: p.subscribers,
      })),
      ...data.monthlyTrend.map((m) => ({
        metric: `Trend: ${m.month}`,
        value: m.newSubscriptions,
        cancellations: m.cancellations,
      })),
    ];
    exportToCsv(rows, `subscription-health-${formatDateForExport(new Date())}.csv`);
  };

  return (
    <AdminRouteWorkspace role="main" aria-label="Subscription health">
      <AdminRouteHero
        eyebrow="Analytics · Billing"
        icon={DollarSign}
        accent="emerald"
        title="Subscription Health"
        description="MRR, churn, ARPU, trial conversion, and revenue breakdown across the subscription book."
        highlights={
          data
            ? [
                { label: 'MRR', value: `$${data.mrr.toLocaleString()}` },
                { label: 'Active subs', value: data.activeSubscriptions.toLocaleString() },
                { label: 'Churn (30d)', value: `${data.churnRate}%` },
              ]
            : undefined
        }
        aside={
          data ? (
            <div className="rounded-2xl border border-border bg-background-light p-4 shadow-sm">
              <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">Quick export</p>
              <Button variant="outline" size="sm" className="mt-3" onClick={handleExport}>
                <Download className="h-4 w-4" /> Download CSV
              </Button>
            </div>
          ) : undefined
        }
      />

      <AsyncStateWrapper
        status={status}
        onRetry={() => window.location.reload()}
        emptyContent={
          <EmptyState
            icon={<DollarSign className="h-6 w-6" aria-hidden />}
            title="No subscription data"
            description="We could not load subscription health. Try again or check the billing pipeline."
          />
        }
      >
        {data ? (
          <>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              <AdminRouteSummaryCard
                label="Monthly Recurring Revenue"
                value={`$${data.mrr.toLocaleString()}`}
                hint="Total MRR across active subscriptions"
                icon={<DollarSign className="h-5 w-5" />}
                tone="success"
              />
              <AdminRouteSummaryCard
                label="Active Subscriptions"
                value={data.activeSubscriptions}
                hint="Currently-paying subscribers"
                icon={<Users className="h-5 w-5" />}
              />
              <AdminRouteSummaryCard
                label="Churn Rate"
                value={`${data.churnRate}%`}
                hint="Monthly churn over the last 30 days"
                icon={<TrendingDown className="h-5 w-5" />}
                tone={data.churnRate > 5 ? 'danger' : 'warning'}
              />
              <AdminRouteSummaryCard
                label="New This Month"
                value={data.newSubscriptionsThisMonth}
                hint="Gross new subs this calendar month"
                icon={<TrendingUp className="h-5 w-5" />}
                tone="success"
              />
              <AdminRouteSummaryCard
                label="ARPU"
                value={`$${data.arpu}`}
                hint="Average revenue per user"
                icon={<CreditCard className="h-5 w-5" />}
                tone="info"
              />
              <AdminRouteSummaryCard
                label="Trial Conversion"
                value={`${data.trialConversionRate}%`}
                hint="Trial → paid conversion rate"
                icon={<BarChart3 className="h-5 w-5" />}
                tone="warning"
              />
            </div>

            {data.revenueByPlan.length > 0 ? (
              <AdminRoutePanel
                eyebrow="Revenue"
                title="Revenue by plan"
                description="How MRR is distributed across the plan lineup."
              >
                <div className="space-y-4">
                  {data.revenueByPlan.map((p) => (
                    <BarMeter
                      key={p.planId}
                      label={p.planName}
                      value={p.monthlyRevenue}
                      max={Math.max(data.mrr, 1)}
                      showValue={false}
                      hint={`$${p.monthlyRevenue.toLocaleString()} · ${p.subscribers.toLocaleString()} subs`}
                      showLegend={false}
                    />
                  ))}
                </div>
                <AdminRoutePanelFooter updatedAt={data.generatedAt} source="Billing ledger" />
              </AdminRoutePanel>
            ) : null}

            {data.monthlyTrend.length > 0 ? (
              <AdminRoutePanel
                eyebrow="Trend"
                title="Monthly trend (6 months)"
                description="New subscriptions vs cancellations month-over-month."
              >
                <div className="space-y-3">
                  {data.monthlyTrend.map((m) => {
                    const total = Math.max(1, m.newSubscriptions + m.cancellations);
                    return (
                      <BarMeter
                        key={m.month}
                        label={m.month}
                        max={total}
                        segments={[
                          { label: 'New', value: m.newSubscriptions, tone: 'success' },
                          { label: 'Cancellations', value: m.cancellations, tone: 'danger' },
                        ]}
                        showValue={false}
                        hint={`+${m.newSubscriptions} · -${m.cancellations}`}
                      />
                    );
                  })}
                </div>
                <AdminRoutePanelFooter
                  updatedAt={data.generatedAt}
                  window="6mo"
                  source="Subscription events"
                />
              </AdminRoutePanel>
            ) : null}
          </>
        ) : null}
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
