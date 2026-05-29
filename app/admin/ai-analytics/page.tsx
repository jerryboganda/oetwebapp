'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Activity, AlertTriangle, RefreshCw, Users } from 'lucide-react';

import { AdminOperationsLayout, KpiStrip } from '@/components/admin/layout/admin-operations-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Input, Select } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import {
  fetchAdminAiUsage,
  fetchAdminChurnList,
  fetchAdminForecastList,
  recomputeAdminChurn,
  recomputeAdminForecast,
  type AdminUsageSummaryDto,
  type ChurnRiskSnapshotDto,
  type UsageForecastSnapshotDto,
  type FeatureBreakdownDto,
  type ProviderBreakdownDto,
  type TopUserUsageDto,
} from '@/lib/api';

const BAND_OPTIONS = [
  { value: '', label: 'all bands' },
  { value: 'high', label: 'high' },
  { value: 'medium', label: 'medium' },
  { value: 'low', label: 'low' },
];

function bandVariant(band: string) {
  if (band === 'high') return 'danger' as const;
  if (band === 'medium') return 'warning' as const;
  return 'success' as const;
}

export default function AdminAiAnalyticsPage() {
  const today = useMemo(() => new Date().toISOString().slice(0, 10), []);
  const monthAgo = useMemo(() => {
    const d = new Date();
    d.setDate(d.getDate() - 30);
    return d.toISOString().slice(0, 10);
  }, []);

  const [from, setFrom] = useState(monthAgo);
  const [to, setTo] = useState(today);
  const [featureFilter, setFeatureFilter] = useState('');
  const [providerFilter, setProviderFilter] = useState('');
  const [bandFilter, setBandFilter] = useState('high');

  const [summary, setSummary] = useState<AdminUsageSummaryDto | null>(null);
  const [churn, setChurn] = useState<ChurnRiskSnapshotDto[]>([]);
  const [forecast, setForecast] = useState<UsageForecastSnapshotDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [s, c, f] = await Promise.all([
        fetchAdminAiUsage({ from, to, feature: featureFilter, provider: providerFilter }),
        fetchAdminChurnList(bandFilter, 50),
        fetchAdminForecastList(50),
      ]);
      setSummary(s);
      setChurn(c);
      setForecast(f);
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to load.');
    } finally {
      setLoading(false);
    }
  }, [from, to, featureFilter, providerFilter, bandFilter]);

  useEffect(() => { void load(); }, [load]);

  async function handleRecomputeChurn() {
    try {
      const msg = await recomputeAdminChurn();
      setToast({ variant: 'success', message: msg });
      await load();
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.userMessage ?? err?.message ?? 'Failed.' });
    }
  }

  async function handleRecomputeForecast() {
    try {
      const msg = await recomputeAdminForecast();
      setToast({ variant: 'success', message: msg });
      await load();
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.userMessage ?? err?.message ?? 'Failed.' });
    }
  }

  const featureColumns: Column<FeatureBreakdownDto>[] = [
    { key: 'code', header: 'Feature', render: (r) => r.featureCode },
    { key: 'calls', header: 'Calls', render: (r) => r.calls.toLocaleString() },
    { key: 'tokens', header: 'Tokens', render: (r) => r.totalTokens.toLocaleString() },
    { key: 'cost', header: 'Cost USD', render: (r) => `$${r.costUsd.toFixed(2)}` },
  ];

  const providerColumns: Column<ProviderBreakdownDto>[] = [
    { key: 'p', header: 'Provider', render: (r) => r.provider },
    { key: 'calls', header: 'Calls', render: (r) => r.calls.toLocaleString() },
    { key: 'tokens', header: 'Tokens', render: (r) => r.totalTokens.toLocaleString() },
    { key: 'cost', header: 'Cost USD', render: (r) => `$${r.costUsd.toFixed(2)}` },
    { key: 'sr', header: 'Success', render: (r) => `${r.successRate}%` },
    { key: 'lat', header: 'Avg latency', render: (r) => `${r.avgLatencyMs}ms` },
  ];

  const topUserColumns: Column<TopUserUsageDto>[] = [
    { key: 'u', header: 'User', render: (r) => r.userId },
    { key: 'calls', header: 'Calls', render: (r) => r.calls.toLocaleString() },
    { key: 'tokens', header: 'Tokens', render: (r) => r.totalTokens.toLocaleString() },
    { key: 'cost', header: 'Cost USD', render: (r) => `$${r.costUsd.toFixed(2)}` },
  ];

  const churnColumns: Column<ChurnRiskSnapshotDto>[] = [
    { key: 'date', header: 'Date', render: (r) => r.snapshotDate },
    { key: 'u', header: 'User', render: (r) => r.userId },
    { key: 'score', header: 'Score', render: (r) => `${(r.riskScore * 100).toFixed(0)}%` },
    {
      key: 'band',
      header: 'Band',
      render: (r) => (
        <Badge variant={bandVariant(r.riskBand)} intensity="tinted">
          {r.riskBand}
        </Badge>
      ),
    },
    { key: 'action', header: 'Action', render: (r) => r.recommendedAction ?? '—' },
    { key: 'disp', header: 'Dispatched', render: (r) => (r.actionDispatched ? 'Yes' : 'No') },
  ];

  const forecastColumns: Column<UsageForecastSnapshotDto>[] = [
    { key: 'u', header: 'User', render: (r) => r.userId },
    { key: 'calls', header: 'Calls/30d', render: (r) => r.forecastCalls.toLocaleString() },
    { key: 'credits', header: 'Credits/30d', render: (r) => r.forecastCredits.toLocaleString() },
    { key: 'cost', header: 'Cost USD/30d', render: (r) => `$${r.forecastCostUsd.toFixed(2)}` },
    { key: 'ema', header: 'EMA daily calls', render: (r) => r.ema30DailyCalls.toFixed(2) },
    { key: 'topup', header: 'Top-up suggested', render: (r) => (r.suggestedTopUpCredits ? `${r.suggestedTopUpCredits} cr` : '—') },
  ];

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'AI Analytics' },
  ];

  return (
    <AdminOperationsLayout
      title="AI analytics"
      description="Usage, churn risk, and 30-day forecast across the learner base."
      eyebrow="AI"
      breadcrumbs={breadcrumbs}
      actions={
        <>
          <Button variant="ghost" onClick={() => void load()}>
            <RefreshCw className="mr-2 h-4 w-4" /> Refresh
          </Button>
          <Button variant="secondary" onClick={handleRecomputeChurn}>
            <AlertTriangle className="mr-2 h-4 w-4" /> Recompute churn
          </Button>
          <Button onClick={handleRecomputeForecast}>
            <Activity className="mr-2 h-4 w-4" /> Recompute forecast
          </Button>
        </>
      }
      kpis={
        summary ? (
          <KpiStrip>
            <KpiTile label="Total calls" value={summary.totalCalls.toLocaleString()} tone="primary" />
            <KpiTile label="Tokens" value={summary.totalTokens.toLocaleString()} tone="info" />
            <KpiTile label="Cost USD" value={`$${summary.totalCostUsd.toFixed(2)}`} tone="warning" />
            <KpiTile label="Unique users" value={summary.uniqueUsers.toLocaleString()} tone="default" />
            <KpiTile label="Success rate" value={`${summary.successRate}%`} tone="success" />
          </KpiStrip>
        ) : null
      }
      primaryGrid={
        <div className="space-y-6">
          {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

          <Card>
            <CardHeader>
              <CardTitle>Filters</CardTitle>
            </CardHeader>
            <CardContent>
              {error && <InlineAlert variant="error">{error}</InlineAlert>}
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-5">
                <Input label="From" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
                <Input label="To" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
                <Input label="Feature filter" value={featureFilter} onChange={(e) => setFeatureFilter(e.target.value)} placeholder="writing.grade" />
                <Input label="Provider filter" value={providerFilter} onChange={(e) => setProviderFilter(e.target.value)} placeholder="openai-platform" />
                <Select label="Churn band" value={bandFilter} options={BAND_OPTIONS} onChange={(e) => setBandFilter(e.target.value)} />
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>By feature</CardTitle>
            </CardHeader>
            <CardContent>
              {loading ? (
                <p className="text-sm text-admin-fg-muted">Loading…</p>
              ) : (
                <DataTable
                  data={summary?.byFeature ?? []}
                  columns={featureColumns}
                  keyExtractor={(r) => r.featureCode}
                  emptyMessage="No usage."
                />
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>By provider</CardTitle>
            </CardHeader>
            <CardContent>
              <DataTable
                data={summary?.byProvider ?? []}
                columns={providerColumns}
                keyExtractor={(r) => r.provider}
                emptyMessage="No usage."
              />
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Top users by spend</CardTitle>
            </CardHeader>
            <CardContent>
              <DataTable
                data={summary?.topUsers ?? []}
                columns={topUserColumns}
                keyExtractor={(r) => r.userId}
                emptyMessage="No users."
              />
            </CardContent>
          </Card>
        </div>
      }
      secondaryGrid={
        <div className="space-y-6">
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <AlertTriangle className="h-4 w-4 text-[var(--admin-danger)]" aria-hidden="true" />
                Churn risk by band: {bandFilter || 'all'}
              </CardTitle>
            </CardHeader>
            <CardContent>
              <DataTable
                data={churn}
                columns={churnColumns}
                keyExtractor={(r) => r.id}
                emptyMessage="No risk snapshots."
              />
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <Users className="h-4 w-4 text-admin-fg-muted" aria-hidden="true" />
                Usage forecast: top 50 by predicted cost
              </CardTitle>
            </CardHeader>
            <CardContent>
              <DataTable
                data={forecast}
                columns={forecastColumns}
                keyExtractor={(r) => r.id}
                emptyMessage="No forecast snapshots."
              />
            </CardContent>
          </Card>
        </div>
      }
    />
  );
}
