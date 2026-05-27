'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { BarChart3, RefreshCw } from 'lucide-react';

import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { PageHeader } from '@/components/admin/ui/page-header';
import { ChartCard } from '@/components/admin/ui/chart-card';
import { Input } from '@/components/admin/ui/input';
import { InlineAlert } from '@/components/ui/alert';
import { NoBillingPermission } from '@/components/admin/billing/no-billing-permission';
import { useAuth } from '@/contexts/auth-context';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import {
  fetchAdminBillingAnalytics,
  type AdminBillingAnalyticsResponse,
  type AdminBillingAnalyticsSeriesPoint,
} from '@/lib/api';
import {
  Area,
  AreaChart,
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from '@/components/charts/dynamic-recharts';

/**
 * Admin billing analytics — MRR, churn, LTV trends. Wave B4 is expected
 * to publish the underlying `/v1/admin/billing/analytics` endpoint. If
 * the service is not ready, we surface a graceful "data not yet
 * available" alert and show empty charts.
 */
export default function AdminAnalyticsPage() {
  const { user } = useAuth();
  const canRead = hasPermission(user?.adminPermissions, AdminPermission.BillingRead, AdminPermission.BillingWrite);

  const today = useMemo(() => new Date().toISOString().slice(0, 10), []);
  const monthAgo = useMemo(() => {
    const d = new Date();
    d.setDate(d.getDate() - 30);
    return d.toISOString().slice(0, 10);
  }, []);
  const [from, setFrom] = useState(monthAgo);
  const [to, setTo] = useState(today);

  const [data, setData] = useState<AdminBillingAnalyticsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setData(await fetchAdminBillingAnalytics({ from, to }));
    } catch (err) {
      console.error(err);
      setError(err instanceof Error ? err.message : 'Failed to load analytics.');
    } finally {
      setLoading(false);
    }
  }, [from, to]);

  useEffect(() => {
    void load();
  }, [load]);

  if (!user) return null;
  if (!canRead) return <NoBillingPermission />;

  const available = Boolean(data?.available);

  return (
    <AdminPageShell>
      <PageHeader
        eyebrow="Billing"
        title="Analytics"
        description="MRR, churn rate, and lifetime value across the billing portfolio."
        icon={<BarChart3 aria-hidden className="h-5 w-5" />}
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Billing', href: '/admin/billing' },
          { label: 'Analytics' },
        ]}
        actions={
          <Button variant="secondary" onClick={() => void load()} startIcon={<RefreshCw className="h-4 w-4" />}>
            Refresh
          </Button>
        }
      />

      <Card>
        <CardContent className="space-y-3 pt-6">
          {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
          {!loading && !available ? (
            <InlineAlert variant="info" title="Analytics not yet available">
              Wave B4 is publishing the consolidated analytics service. Daily rollups are still
              visible on the Metrics page; this dashboard will populate as soon as the underlying
              endpoint ships.
            </InlineAlert>
          ) : null}
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
            <Input label="From" type="date" value={from} onChange={(event) => setFrom(event.target.value)} />
            <Input label="To" type="date" value={to} onChange={(event) => setTo(event.target.value)} />
          </div>
        </CardContent>
      </Card>

      <div className="grid gap-6 lg:grid-cols-2">
        <ChartCard
          title="Monthly recurring revenue"
          subtitle={`Trailing series in ${data?.currency ?? 'AUD'}.`}
          loading={loading}
          empty={!loading && (data?.mrr.length ?? 0) === 0}
        >
          <ResponsiveContainer width="100%" height={300}>
            <AreaChart data={normaliseSeries(data?.mrr ?? [])}>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--admin-border)" />
              <XAxis dataKey="date" tick={{ fontSize: 12 }} />
              <YAxis tick={{ fontSize: 12 }} />
              <Tooltip />
              <Area dataKey="value" stroke="var(--admin-primary)" fill="var(--admin-primary)" fillOpacity={0.15} />
            </AreaChart>
          </ResponsiveContainer>
        </ChartCard>

        <ChartCard
          title="Churn rate"
          subtitle="Percentage of active subscriptions lost over the period."
          loading={loading}
          empty={!loading && (data?.churnRate.length ?? 0) === 0}
        >
          <ResponsiveContainer width="100%" height={300}>
            <LineChart data={normaliseSeries(data?.churnRate ?? [])}>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--admin-border)" />
              <XAxis dataKey="date" tick={{ fontSize: 12 }} />
              <YAxis tick={{ fontSize: 12 }} />
              <Tooltip />
              <Legend />
              <Line type="monotone" dataKey="value" name="Churn %" stroke="#dc2626" dot={false} />
            </LineChart>
          </ResponsiveContainer>
        </ChartCard>

        <ChartCard
          title="Customer lifetime value"
          subtitle="Estimated LTV across active subscribers."
          loading={loading}
          empty={!loading && (data?.ltv.length ?? 0) === 0}
          className="lg:col-span-2"
        >
          <ResponsiveContainer width="100%" height={300}>
            <LineChart data={normaliseSeries(data?.ltv ?? [])}>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--admin-border)" />
              <XAxis dataKey="date" tick={{ fontSize: 12 }} />
              <YAxis tick={{ fontSize: 12 }} />
              <Tooltip />
              <Legend />
              <Line
                type="monotone"
                dataKey="value"
                name={`LTV (${data?.currency ?? 'AUD'})`}
                stroke="#0f766e"
                dot={false}
              />
            </LineChart>
          </ResponsiveContainer>
        </ChartCard>
      </div>
    </AdminPageShell>
  );
}

function normaliseSeries(series: AdminBillingAnalyticsSeriesPoint[]) {
  return series.map((point) => ({
    date: point.date.length > 10 ? point.date.slice(0, 10) : point.date,
    value: Number.isFinite(point.value) ? point.value : 0,
  }));
}
