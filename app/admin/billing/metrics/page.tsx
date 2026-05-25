'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import type { ColumnDef } from '@tanstack/react-table';
import { Activity, Download, RefreshCw } from 'lucide-react';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { DataTable } from '@/components/admin/ui/data-table';
import { Input } from '@/components/admin/ui/input';
import { Label } from '@/components/admin/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/admin/ui/select';
import { toast } from '@/components/admin/ui/toaster';
import { InlineAlert } from '@/components/ui/alert';
import {
  readBillingMetrics,
  rollupBillingMetrics,
  type BillingMetricDto,
} from '@/lib/api';

const METRIC_OPTIONS = [
  { value: '__all', label: 'All metrics' },
  { value: 'mrr', label: 'mrr' },
  { value: 'arr', label: 'arr' },
  { value: 'active_subscriptions', label: 'active_subscriptions' },
  { value: 'new_subscriptions', label: 'new_subscriptions' },
  { value: 'cancelled_subscriptions', label: 'cancelled_subscriptions' },
  { value: 'paused_subscriptions', label: 'paused_subscriptions' },
  { value: 'churn_rate', label: 'churn_rate' },
  { value: 'refund_rate', label: 'refund_rate' },
  { value: 'arpu', label: 'arpu' },
  { value: 'gross_revenue', label: 'gross_revenue' },
  { value: 'net_revenue', label: 'net_revenue' },
  { value: 'credits_sold', label: 'credits_sold' },
];

export default function AdminMetricsPage() {
  const today = useMemo(() => new Date().toISOString().slice(0, 10), []);
  const monthAgo = useMemo(() => {
    const d = new Date();
    d.setDate(d.getDate() - 30);
    return d.toISOString().slice(0, 10);
  }, []);

  const [from, setFrom] = useState(monthAgo);
  const [to, setTo] = useState(today);
  const [code, setCode] = useState('mrr');
  const [rows, setRows] = useState<BillingMetricDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const metricCode = code === '__all' ? undefined : code;
      setRows(await readBillingMetrics({ from, to, code: metricCode }));
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to load metrics.');
    } finally {
      setLoading(false);
    }
  }, [from, to, code]);

  useEffect(() => { void load(); }, [load]);

  async function handleRollup() {
    try {
      await rollupBillingMetrics();
      toast.success('Rollup complete.');
      await load();
    } catch (err: any) {
      toast.error(err?.userMessage ?? err?.message ?? 'Rollup failed.');
    }
  }

  function handleExport() {
    const qs = new URLSearchParams({ from, to });
    if (code !== '__all') qs.set('code', code);
    window.location.href = `/api/backend/v1/admin/billing/metrics.csv?${qs.toString()}`;
  }

  const columns: ColumnDef<BillingMetricDto>[] = [
    { id: 'date', accessorKey: 'metricDate', header: 'Date' },
    { id: 'code', accessorKey: 'metricCode', header: 'Metric' },
    { id: 'region', accessorKey: 'region', header: 'Region' },
    { id: 'currency', accessorKey: 'currency', header: 'Currency' },
    {
      id: 'value',
      header: 'Value',
      cell: ({ row }) => row.original.value.toLocaleString(),
    },
    {
      id: 'computed',
      header: 'Computed',
      cell: ({ row }) => new Date(row.original.computedAt).toLocaleString(),
    },
  ];

  return (
    <AdminTableLayout
      title="Billing metrics"
      description="MRR / ARR / churn / refund / ARPU rolled up daily. Downloadable as CSV."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Billing', href: '/admin/billing' },
        { label: 'Metrics' },
      ]}
      actions={
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="secondary" onClick={handleExport} startIcon={<Download className="h-4 w-4" />}>
            Export CSV
          </Button>
          <Button onClick={handleRollup} startIcon={<Activity className="h-4 w-4" />}>
            Roll up today
          </Button>
        </div>
      }
      banner={
        <Card>
          <CardContent className="space-y-4 pt-6">
            {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
              <Input label="From" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
              <Input label="To" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="metric-code">Metric</Label>
                <Select value={code} onValueChange={setCode}>
                  <SelectTrigger id="metric-code">
                    <SelectValue placeholder="Metric" />
                  </SelectTrigger>
                  <SelectContent>
                    {METRIC_OPTIONS.map((opt) => (
                      <SelectItem key={opt.value} value={opt.value}>
                        {opt.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="flex items-end">
                <Button
                  variant="ghost"
                  onClick={() => void load()}
                  startIcon={<RefreshCw className="h-4 w-4" />}
                >
                  Refresh
                </Button>
              </div>
            </div>
          </CardContent>
        </Card>
      }
    >
      <DataTable
        columns={columns}
        data={rows}
        loading={loading}
        emptyMessage="No metric rows in this range. Click ‘Roll up today’ to compute."
        searchPlaceholder="Search metrics…"
      />
    </AdminTableLayout>
  );
}
