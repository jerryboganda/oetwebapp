'use client';

import { useCallback, useEffect, useState } from 'react';
import { Play, Square, Plus, Trash2, Beaker } from 'lucide-react';
import type { ColumnDef } from '@tanstack/react-table';

import {
  listPricingExperiments,
  upsertPricingExperiment,
  startPricingExperiment,
  stopPricingExperiment,
  deletePricingExperiment,
  fetchExperimentResults,
  fetchExperimentSignificance,
  type PricingExperimentDto,
  type PricingExperimentUpsertRequest,
  type VariantResultsDto,
  type ZTestResultDto,
} from '@/lib/api';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { DataTable } from '@/components/admin/ui/data-table';
import { Button } from '@/components/admin/ui/button';
import { Input } from '@/components/admin/ui/input';
import { Textarea } from '@/components/admin/ui/textarea';
import { Badge } from '@/components/admin/ui/badge';
import { Card, CardContent } from '@/components/admin/ui/card';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/admin/ui/select';
import { Label } from '@/components/admin/ui/label';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@/components/admin/ui/dialog';

const STATUS_OPTIONS = [
  { value: '__all__', label: 'All' },
  { value: 'draft', label: 'Draft' },
  { value: 'running', label: 'Running' },
  { value: 'paused', label: 'Paused' },
  { value: 'completed', label: 'Completed' },
];

const TARGET_TYPES = [
  { value: 'plan', label: 'plan' },
  { value: 'addon', label: 'addon' },
  { value: 'wallet_topup_tier', label: 'wallet_topup_tier' },
];

const REGIONS = ['*', 'UK', 'GULF', 'EGYPT', 'PK', 'ROW'].map((r) => ({ value: r, label: r }));

const EMPTY: PricingExperimentUpsertRequest = {
  code: '',
  name: '',
  targetType: 'plan',
  targetId: '',
  region: '*',
  rolloutPercent: 50,
  variantsJson:
    '[\n  {"code":"control","weight":50,"priceMultiplier":1.0},\n  {"code":"discount_10","weight":50,"priceMultiplier":0.9}\n]',
};

function statusVariant(status: string): 'success' | 'warning' | 'default' | 'secondary' {
  if (status === 'running') return 'success';
  if (status === 'paused') return 'warning';
  if (status === 'completed') return 'secondary';
  return 'default';
}

export default function AdminPricingExperimentsPage() {
  const [statusFilter, setStatusFilter] = useState('__all__');
  const [rows, setRows] = useState<PricingExperimentDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const [editing, setEditing] = useState<PricingExperimentUpsertRequest | null>(null);
  const [resultsFor, setResultsFor] = useState<{
    id: string;
    rows: VariantResultsDto[];
    significance: ZTestResultDto[];
  } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const filter = statusFilter === '__all__' ? undefined : statusFilter;
      setRows(await listPricingExperiments(filter));
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to load.');
    } finally {
      setLoading(false);
    }
  }, [statusFilter]);

  useEffect(() => {
    void load();
  }, [load]);

  async function handleSave() {
    if (!editing) return;
    try {
      JSON.parse(editing.variantsJson ?? '[]'); // shape check
      await upsertPricingExperiment(editing);
      setToast({ variant: 'success', message: 'Experiment saved.' });
      setEditing(null);
      await load();
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Save failed (check variants JSON).');
    }
  }

  async function handleStart(id: string) {
    try {
      await startPricingExperiment(id);
      await load();
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.message ?? 'Start failed.' });
    }
  }
  async function handleStop(id: string) {
    try {
      await stopPricingExperiment(id);
      await load();
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.message ?? 'Stop failed.' });
    }
  }
  async function handleDelete(id: string) {
    if (!confirm('Delete this experiment?')) return;
    try {
      await deletePricingExperiment(id);
      await load();
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.message ?? 'Delete failed.' });
    }
  }
  async function viewResults(id: string) {
    try {
      const [r, sig] = await Promise.all([
        fetchExperimentResults(id),
        fetchExperimentSignificance(id),
      ]);
      setResultsFor({ id, rows: r.variants, significance: sig });
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.message ?? 'Failed.' });
    }
  }

  const columns: ColumnDef<PricingExperimentDto>[] = [
    {
      accessorKey: 'code',
      header: 'Code',
      cell: ({ row }) => <span className="font-mono text-sm">{row.original.code}</span>,
    },
    {
      accessorKey: 'name',
      header: 'Name',
      cell: ({ row }) => (
        <span className="font-medium text-admin-fg-strong">{row.original.name}</span>
      ),
    },
    {
      id: 'target',
      header: 'Target',
      cell: ({ row }) => (
        <span className="text-sm text-admin-fg-muted">
          {row.original.targetType}:{row.original.targetId}
        </span>
      ),
    },
    {
      accessorKey: 'region',
      header: 'Region',
      cell: ({ row }) => <span className="text-sm">{row.original.region}</span>,
    },
    {
      accessorKey: 'rolloutPercent',
      header: 'Rollout',
      cell: ({ row }) => (
        <span className="tabular-nums">{row.original.rolloutPercent}%</span>
      ),
    },
    {
      accessorKey: 'status',
      header: 'Status',
      cell: ({ row }) => (
        <Badge variant={statusVariant(row.original.status)} size="sm">
          {row.original.status}
        </Badge>
      ),
    },
    {
      id: 'actions',
      header: '',
      cell: ({ row }) => {
        const r = row.original;
        return (
          <div className="flex items-center justify-end gap-1">
            {r.status === 'draft' || r.status === 'paused' ? (
              <Button
                variant="ghost"
                size="sm"
                onClick={() => handleStart(r.id)}
                aria-label="Start"
                startIcon={<Play className="h-4 w-4 text-[var(--admin-success)]" />}
              >
                Start
              </Button>
            ) : null}
            {r.status === 'running' ? (
              <Button
                variant="ghost"
                size="sm"
                onClick={() => handleStop(r.id)}
                aria-label="Stop"
                startIcon={<Square className="h-4 w-4 text-[var(--admin-danger)]" />}
              >
                Stop
              </Button>
            ) : null}
            <Button
              variant="ghost"
              size="sm"
              onClick={() => viewResults(r.id)}
              aria-label="Results"
              startIcon={<Beaker className="h-4 w-4" />}
            >
              Results
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() =>
                setEditing({
                  code: r.code,
                  name: r.name,
                  targetType: r.targetType,
                  targetId: r.targetId,
                  region: r.region,
                  rolloutPercent: r.rolloutPercent,
                  variantsJson: r.variantsJson,
                })
              }
            >
              Edit
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => handleDelete(r.id)}
              aria-label="Delete"
              startIcon={<Trash2 className="h-4 w-4 text-[var(--admin-danger)]" />}
            >
              Delete
            </Button>
          </div>
        );
      },
    },
  ];

  function update<K extends keyof PricingExperimentUpsertRequest>(
    k: K,
    v: PricingExperimentUpsertRequest[K],
  ) {
    if (!editing) return;
    setEditing({ ...editing, [k]: v });
  }

  const banner = (
    <Card>
      <CardContent>
        {error && (
          <div className="mb-3 rounded-admin border border-[var(--admin-danger-tint-strong)] bg-[var(--admin-danger-tint)] px-3 py-2 text-sm text-[var(--admin-danger)]">
            {error}
          </div>
        )}
        {toast && (
          <div
            className={`mb-3 rounded-admin border px-3 py-2 text-sm ${
              toast.variant === 'success'
                ? 'border-[var(--admin-success-tint-strong)] bg-[var(--admin-success-tint)] text-[var(--admin-success)]'
                : 'border-[var(--admin-danger-tint-strong)] bg-[var(--admin-danger-tint)] text-[var(--admin-danger)]'
            }`}
          >
            {toast.message}
          </div>
        )}
        <div className="flex flex-wrap items-end gap-3">
          <div className="w-48">
            <Label htmlFor="status-filter">Status</Label>
            <Select value={statusFilter} onValueChange={setStatusFilter}>
              <SelectTrigger id="status-filter" className="mt-1.5">
                <SelectValue placeholder="Status" />
              </SelectTrigger>
              <SelectContent>
                {STATUS_OPTIONS.map((opt) => (
                  <SelectItem key={opt.value} value={opt.value}>
                    {opt.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <Button
            onClick={() => setEditing({ ...EMPTY })}
            startIcon={<Plus className="h-4 w-4" />}
          >
            New experiment
          </Button>
        </div>
      </CardContent>
    </Card>
  );

  return (
    <AdminTableLayout
      title="Pricing experiments"
      description="A/B price tests with FX-aware variants. Deterministic per-user assignment by SHA-256 hash."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Pricing experiments' },
      ]}
      banner={banner}
    >
      <DataTable
        columns={columns as ColumnDef<PricingExperimentDto, unknown>[]}
        data={rows}
        loading={loading}
        emptyMessage="No experiments."
        searchPlaceholder="Search experiments…"
      />

      {editing && (
        <Dialog open onOpenChange={(o) => !o && setEditing(null)}>
          <DialogContent size="lg">
            <DialogHeader>
              <DialogTitle>Pricing experiment</DialogTitle>
            </DialogHeader>
            <div className="space-y-3">
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                <Input
                  label="Code"
                  value={editing.code}
                  onChange={(e) => update('code', e.target.value)}
                  placeholder="premium_uk_summer25"
                />
                <Input
                  label="Name"
                  value={editing.name}
                  onChange={(e) => update('name', e.target.value)}
                  placeholder="UK summer pricing test"
                />
                <div>
                  <Label htmlFor="target-type">Target type</Label>
                  <Select
                    value={editing.targetType}
                    onValueChange={(v) => update('targetType', v)}
                  >
                    <SelectTrigger id="target-type" className="mt-1.5">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {TARGET_TYPES.map((t) => (
                        <SelectItem key={t.value} value={t.value}>
                          {t.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <Input
                  label="Target id"
                  value={editing.targetId}
                  onChange={(e) => update('targetId', e.target.value)}
                  placeholder="premium"
                />
                <div>
                  <Label htmlFor="region">Region</Label>
                  <Select
                    value={editing.region ?? '*'}
                    onValueChange={(v) => update('region', v)}
                  >
                    <SelectTrigger id="region" className="mt-1.5">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {REGIONS.map((r) => (
                        <SelectItem key={r.value} value={r.value}>
                          {r.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <Input
                  label="Rollout %"
                  type="number"
                  min={0}
                  max={100}
                  value={editing.rolloutPercent}
                  onChange={(e) => update('rolloutPercent', Number(e.target.value))}
                />
              </div>
              <Textarea
                label="Variants JSON"
                value={editing.variantsJson ?? ''}
                onChange={(e) => update('variantsJson', e.target.value)}
                rows={6}
                placeholder='[{"code":"control","weight":50,"priceMultiplier":1.0}]'
              />
              <p className="text-xs text-admin-fg-muted">
                Each variant: <code className="font-mono">{`{ code, weight, priceMultiplier, currency? }`}</code>. Weight determines split; priceMultiplier scales base price; optional currency triggers FX conversion.
              </p>
            </div>
            <DialogFooter>
              <Button variant="ghost" onClick={() => setEditing(null)}>
                Cancel
              </Button>
              <Button onClick={handleSave}>Save</Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      )}

      {resultsFor && (
        <Dialog open onOpenChange={(o) => !o && setResultsFor(null)}>
          <DialogContent size="lg">
            <DialogHeader>
              <DialogTitle>Experiment results</DialogTitle>
            </DialogHeader>
            <div className="space-y-4">
              {resultsFor.rows.length === 0 ? (
                <p className="text-sm text-admin-fg-muted">No assignments yet.</p>
              ) : (
                <>
                  <div className="overflow-hidden rounded-admin border border-admin-border">
                    <table className="w-full text-sm">
                      <thead className="bg-admin-bg-subtle">
                        <tr>
                          <th scope="col" className="px-3 py-2 text-left text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
                            Variant
                          </th>
                          <th scope="col" className="px-3 py-2 text-right text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
                            Assignments
                          </th>
                          <th scope="col" className="px-3 py-2 text-right text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
                            Conversions
                          </th>
                          <th scope="col" className="px-3 py-2 text-right text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
                            Conv. rate
                          </th>
                          <th scope="col" className="px-3 py-2 text-right text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
                            Revenue
                          </th>
                        </tr>
                      </thead>
                      <tbody>
                        {resultsFor.rows.map((v) => {
                          const rate =
                            v.assignments > 0 ? (v.conversions / v.assignments) * 100 : 0;
                          return (
                            <tr key={v.variantCode} className="border-t border-admin-border">
                              <td className="px-3 py-2 font-mono">{v.variantCode}</td>
                              <td className="px-3 py-2 text-right tabular-nums">
                                {v.assignments}
                              </td>
                              <td className="px-3 py-2 text-right tabular-nums">
                                {v.conversions}
                              </td>
                              <td className="px-3 py-2 text-right tabular-nums">
                                {rate.toFixed(1)}%
                              </td>
                              <td className="px-3 py-2 text-right tabular-nums">
                                ${v.conversionRevenue.toFixed(2)}
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>

                  {resultsFor.significance.length > 0 && (
                    <div>
                      <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
                        Statistical significance (vs control)
                      </p>
                      <div className="overflow-hidden rounded-admin border border-admin-border">
                        <table className="w-full text-sm">
                          <thead className="bg-admin-bg-subtle">
                            <tr>
                              <th scope="col" className="px-3 py-2 text-left text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
                                Variant
                              </th>
                              <th scope="col" className="px-3 py-2 text-right text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
                                Δ rate
                              </th>
                              <th scope="col" className="px-3 py-2 text-right text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
                                95% CI
                              </th>
                              <th scope="col" className="px-3 py-2 text-right text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
                                z
                              </th>
                              <th scope="col" className="px-3 py-2 text-right text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
                                p-value
                              </th>
                              <th scope="col" className="px-3 py-2 text-center text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
                                Sig.
                              </th>
                            </tr>
                          </thead>
                          <tbody>
                            {resultsFor.significance.map((s) => (
                              <tr key={s.variantCode} className="border-t border-admin-border">
                                <td className="px-3 py-2 font-mono">{s.variantCode}</td>
                                <td className="px-3 py-2 text-right tabular-nums">
                                  <span
                                    className={
                                      s.difference > 0
                                        ? 'text-[var(--admin-success)]'
                                        : s.difference < 0
                                          ? 'text-[var(--admin-danger)]'
                                          : ''
                                    }
                                  >
                                    {s.difference >= 0 ? '+' : ''}
                                    {(s.difference * 100).toFixed(2)}pp
                                  </span>
                                </td>
                                <td className="px-3 py-2 text-right text-admin-fg-muted tabular-nums">
                                  [{(s.ciLower95 * 100).toFixed(2)},{' '}
                                  {(s.ciUpper95 * 100).toFixed(2)}]
                                </td>
                                <td className="px-3 py-2 text-right tabular-nums">
                                  {s.z.toFixed(3)}
                                </td>
                                <td className="px-3 py-2 text-right tabular-nums">
                                  {s.pValueTwoTailed.toFixed(4)}
                                </td>
                                <td className="px-3 py-2 text-center">
                                  {s.significantAt95 ? (
                                    <Badge variant="success" size="sm">
                                      p&lt;0.05
                                    </Badge>
                                  ) : (
                                    <Badge variant="default" size="sm">
                                      n.s.
                                    </Badge>
                                  )}
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    </div>
                  )}
                </>
              )}
            </div>
          </DialogContent>
        </Dialog>
      )}
    </AdminTableLayout>
  );
}
