'use client';

import { useCallback, useEffect, useState } from 'react';
import { Play, Square, Plus, Trash2, Beaker } from 'lucide-react';
import { AdminRouteWorkspace, AdminRoutePanel, AdminRouteSectionHeader } from '@/components/domain/admin-route-surface';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Button } from '@/components/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Badge } from '@/components/ui/badge';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';
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

const STATUS_OPTIONS = [
  { value: '', label: 'all' },
  { value: 'draft', label: 'draft' },
  { value: 'running', label: 'running' },
  { value: 'paused', label: 'paused' },
  { value: 'completed', label: 'completed' },
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
  variantsJson: '[\n  {"code":"control","weight":50,"priceMultiplier":1.0},\n  {"code":"discount_10","weight":50,"priceMultiplier":0.9}\n]',
};

function statusVariant(status: string) {
  if (status === 'running') return 'success' as const;
  if (status === 'paused') return 'warning' as const;
  if (status === 'completed') return 'muted' as const;
  return 'default' as const;
}

export default function AdminPricingExperimentsPage() {
  const [statusFilter, setStatusFilter] = useState('');
  const [rows, setRows] = useState<PricingExperimentDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const [editing, setEditing] = useState<PricingExperimentUpsertRequest | null>(null);
  const [resultsFor, setResultsFor] = useState<{ id: string; rows: VariantResultsDto[]; significance: ZTestResultDto[] } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows(await listPricingExperiments(statusFilter || undefined));
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to load.');
    } finally {
      setLoading(false);
    }
  }, [statusFilter]);

  useEffect(() => { void load(); }, [load]);

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
    try { await startPricingExperiment(id); await load(); }
    catch (err: any) { setToast({ variant: 'error', message: err?.message ?? 'Start failed.' }); }
  }
  async function handleStop(id: string) {
    try { await stopPricingExperiment(id); await load(); }
    catch (err: any) { setToast({ variant: 'error', message: err?.message ?? 'Stop failed.' }); }
  }
  async function handleDelete(id: string) {
    if (!confirm('Delete this experiment?')) return;
    try { await deletePricingExperiment(id); await load(); }
    catch (err: any) { setToast({ variant: 'error', message: err?.message ?? 'Delete failed.' }); }
  }
  async function viewResults(id: string) {
    try {
      const [r, sig] = await Promise.all([
        fetchExperimentResults(id),
        fetchExperimentSignificance(id),
      ]);
      setResultsFor({ id, rows: r.variants, significance: sig });
    }
    catch (err: any) { setToast({ variant: 'error', message: err?.message ?? 'Failed.' }); }
  }

  const columns: Column<PricingExperimentDto>[] = [
    { key: 'code', header: 'Code', render: (r) => r.code },
    { key: 'name', header: 'Name', render: (r) => r.name },
    { key: 'target', header: 'Target', render: (r) => `${r.targetType}:${r.targetId}` },
    { key: 'region', header: 'Region', render: (r) => r.region },
    { key: 'rollout', header: 'Rollout', render: (r) => `${r.rolloutPercent}%` },
    { key: 'status', header: 'Status', render: (r) => <Badge variant={statusVariant(r.status)}>{r.status}</Badge> },
    {
      key: 'actions',
      header: '',
      render: (r) => (
        <div className="flex gap-1">
          {r.status === 'draft' || r.status === 'paused' ? (
            <Button variant="ghost" size="sm" onClick={() => handleStart(r.id)} aria-label="Start">
              <Play className="h-4 w-4 text-emerald-600" />
            </Button>
          ) : null}
          {r.status === 'running' ? (
            <Button variant="ghost" size="sm" onClick={() => handleStop(r.id)} aria-label="Stop">
              <Square className="h-4 w-4 text-rose-600" />
            </Button>
          ) : null}
          <Button variant="ghost" size="sm" onClick={() => viewResults(r.id)} aria-label="Results">
            <Beaker className="h-4 w-4" />
          </Button>
          <Button variant="ghost" size="sm" onClick={() => setEditing({
            code: r.code,
            name: r.name,
            targetType: r.targetType,
            targetId: r.targetId,
            region: r.region,
            rolloutPercent: r.rolloutPercent,
            variantsJson: r.variantsJson,
          })}>Edit</Button>
          <Button variant="ghost" size="sm" onClick={() => handleDelete(r.id)} aria-label="Delete">
            <Trash2 className="h-4 w-4" />
          </Button>
        </div>
      ),
    },
  ];

  function update<K extends keyof PricingExperimentUpsertRequest>(k: K, v: PricingExperimentUpsertRequest[K]) {
    if (!editing) return;
    setEditing({ ...editing, [k]: v });
  }

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader title="Pricing experiments" description="A/B price tests with FX-aware variants. Deterministic per-user assignment by SHA-256 hash." />

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <AdminRoutePanel>
        {error && <InlineAlert variant="error">{error}</InlineAlert>}
        <div className="mb-4 flex items-end gap-3">
          <Select label="Status" value={statusFilter} options={STATUS_OPTIONS} onChange={(e) => setStatusFilter(e.target.value)} />
          <Button onClick={() => setEditing({ ...EMPTY })}>
            <Plus className="mr-2 h-4 w-4" />
            New experiment
          </Button>
        </div>
        {loading ? (
          <p className="text-sm text-muted-foreground">Loading…</p>
        ) : (
          <DataTable data={rows} columns={columns} keyExtractor={(r) => r.id} emptyMessage="No experiments." />
        )}
      </AdminRoutePanel>

      {editing && (
        <Modal open onClose={() => setEditing(null)} title="Pricing experiment" size="lg">
          <div className="space-y-3 p-4">
            <div className="grid grid-cols-2 gap-3">
              <Input label="Code" value={editing.code} onChange={(e) => update('code', e.target.value)} placeholder="premium_uk_summer25" />
              <Input label="Name" value={editing.name} onChange={(e) => update('name', e.target.value)} placeholder="UK summer pricing test" />
              <Select label="Target type" value={editing.targetType} options={TARGET_TYPES} onChange={(e) => update('targetType', e.target.value)} />
              <Input label="Target id" value={editing.targetId} onChange={(e) => update('targetId', e.target.value)} placeholder="premium" />
              <Select label="Region" value={editing.region ?? '*'} options={REGIONS} onChange={(e) => update('region', e.target.value)} />
              <Input label="Rollout %" type="number" min={0} max={100} value={editing.rolloutPercent} onChange={(e) => update('rolloutPercent', Number(e.target.value))} />
            </div>
            <Textarea
              value={editing.variantsJson ?? ''}
              onChange={(e) => update('variantsJson', e.target.value)}
              placeholder='[{"code":"control","weight":50,"priceMultiplier":1.0}]'
            />
            <p className="text-xs text-muted-foreground">
              Each variant: <code>{`{ code, weight, priceMultiplier, currency? }`}</code>. Weight determines split; priceMultiplier scales base price; optional currency triggers FX conversion.
            </p>
            <div className="flex justify-end gap-2">
              <Button variant="ghost" onClick={() => setEditing(null)}>Cancel</Button>
              <Button onClick={handleSave}>Save</Button>
            </div>
          </div>
        </Modal>
      )}

      {resultsFor && (
        <Modal open onClose={() => setResultsFor(null)} title="Experiment results" size="lg">
          <div className="space-y-4 p-4">
            {resultsFor.rows.length === 0 ? (
              <p className="text-sm text-muted-foreground">No assignments yet.</p>
            ) : (
              <>
                <table className="w-full text-sm">
                  <thead className="bg-muted/50">
                    <tr>
                      <th className="px-3 py-2 text-left">Variant</th>
                      <th className="px-3 py-2 text-right">Assignments</th>
                      <th className="px-3 py-2 text-right">Conversions</th>
                      <th className="px-3 py-2 text-right">Conv. rate</th>
                      <th className="px-3 py-2 text-right">Revenue</th>
                    </tr>
                  </thead>
                  <tbody>
                    {resultsFor.rows.map((v) => {
                      const rate = v.assignments > 0 ? (v.conversions / v.assignments) * 100 : 0;
                      return (
                        <tr key={v.variantCode} className="border-t border-border">
                          <td className="px-3 py-2 font-mono">{v.variantCode}</td>
                          <td className="px-3 py-2 text-right">{v.assignments}</td>
                          <td className="px-3 py-2 text-right">{v.conversions}</td>
                          <td className="px-3 py-2 text-right">{rate.toFixed(1)}%</td>
                          <td className="px-3 py-2 text-right">${v.conversionRevenue.toFixed(2)}</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>

                {resultsFor.significance.length > 0 && (
                  <div>
                    <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">Statistical significance (vs control)</p>
                    <table className="w-full text-sm">
                      <thead className="bg-muted/50">
                        <tr>
                          <th className="px-3 py-2 text-left">Variant</th>
                          <th className="px-3 py-2 text-right">Δ rate</th>
                          <th className="px-3 py-2 text-right">95% CI</th>
                          <th className="px-3 py-2 text-right">z</th>
                          <th className="px-3 py-2 text-right">p-value</th>
                          <th className="px-3 py-2 text-center">Sig.</th>
                        </tr>
                      </thead>
                      <tbody>
                        {resultsFor.significance.map((s) => (
                          <tr key={s.variantCode} className="border-t border-border">
                            <td className="px-3 py-2 font-mono">{s.variantCode}</td>
                            <td className="px-3 py-2 text-right">
                              <span className={s.difference > 0 ? 'text-emerald-600' : s.difference < 0 ? 'text-rose-600' : ''}>
                                {s.difference >= 0 ? '+' : ''}{(s.difference * 100).toFixed(2)}pp
                              </span>
                            </td>
                            <td className="px-3 py-2 text-right text-muted-foreground">
                              [{(s.ciLower95 * 100).toFixed(2)}, {(s.ciUpper95 * 100).toFixed(2)}]
                            </td>
                            <td className="px-3 py-2 text-right">{s.z.toFixed(3)}</td>
                            <td className="px-3 py-2 text-right">{s.pValueTwoTailed.toFixed(4)}</td>
                            <td className="px-3 py-2 text-center">
                              {s.significantAt95
                                ? <Badge variant="success">p&lt;0.05</Badge>
                                : <Badge variant="muted">n.s.</Badge>}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </>
            )}
          </div>
        </Modal>
      )}
    </AdminRouteWorkspace>
  );
}
