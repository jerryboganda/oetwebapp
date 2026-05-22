'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Activity, Download, RefreshCw } from 'lucide-react';
import { AdminRouteWorkspace, AdminRoutePanel, AdminRouteSectionHeader } from '@/components/domain/admin-route-surface';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import {
  readBillingMetrics,
  rollupBillingMetrics,
  type BillingMetricDto,
} from '@/lib/api';

const METRIC_OPTIONS = [
  { value: '', label: 'all' },
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
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setRows(await readBillingMetrics({ from, to, code: code || undefined }));
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
      setToast({ variant: 'success', message: 'Rollup complete.' });
      await load();
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.userMessage ?? err?.message ?? 'Rollup failed.' });
    }
  }

  function handleExport() {
    const qs = new URLSearchParams({ from, to });
    if (code) qs.set('code', code);
    window.location.href = `/api/backend/v1/admin/billing/metrics.csv?${qs.toString()}`;
  }

  const columns: Column<BillingMetricDto>[] = [
    { key: 'date', header: 'Date', render: (r) => r.metricDate },
    { key: 'code', header: 'Metric', render: (r) => r.metricCode },
    { key: 'region', header: 'Region', render: (r) => r.region },
    { key: 'currency', header: 'Currency', render: (r) => r.currency },
    { key: 'value', header: 'Value', render: (r) => r.value.toLocaleString() },
    { key: 'computed', header: 'Computed', render: (r) => new Date(r.computedAt).toLocaleString() },
  ];

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader title="Billing metrics" description="MRR / ARR / churn / refund / ARPU rolled up daily. Downloadable as CSV." />

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <AdminRoutePanel>
        {error && <InlineAlert variant="error">{error}</InlineAlert>}
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-5">
          <Input label="From" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
          <Input label="To" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
          <Select label="Metric" value={code} options={METRIC_OPTIONS} onChange={(e) => setCode(e.target.value)} />
          <div className="flex items-end gap-2">
            <Button variant="ghost" onClick={() => void load()}>
              <RefreshCw className="mr-2 h-4 w-4" />
              Refresh
            </Button>
            <Button onClick={handleRollup}>
              <Activity className="mr-2 h-4 w-4" />
              Roll up today
            </Button>
          </div>
          <div className="flex items-end">
            <Button variant="secondary" onClick={handleExport}>
              <Download className="mr-2 h-4 w-4" />
              Export CSV
            </Button>
          </div>
        </div>
      </AdminRoutePanel>

      <AdminRoutePanel>
        {loading ? (
          <p className="text-sm text-muted-foreground">Loading…</p>
        ) : (
          <DataTable data={rows} columns={columns} keyExtractor={(r) => r.id} emptyMessage="No metric rows in this range. Click ‘Roll up today’ to compute." />
        )}
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
