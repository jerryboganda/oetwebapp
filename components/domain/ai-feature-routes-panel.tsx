'use client';

import { useEffect, useMemo, useState } from 'react';
import { Trash2 } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Input, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import {
  bulkRouteFeaturesToCopilot,
  deleteAiFeatureRoute,
  fetchAiFeatureRoutes,
  fetchAiProviders,
  upsertAiFeatureRoute,
  type AiFeatureRouteRow,
  type AiProviderRow,
} from '@/lib/ai-management-api';

/**
 * Phase 7 — per-feature provider routing.
 *
 * Lets admins pin individual feature codes to a specific provider so that
 * (e.g.) `vocabulary.gloss` always uses Copilot regardless of the global
 * failover-priority order. Missing rows fall through to the registry default.
 *
 * The "Route bulk-route set to Copilot" button is grey when no Copilot row
 * is registered + active — the server also enforces this, but the UI guard
 * keeps admins from staring at a 400.
 */
type ToastState = { variant: 'success' | 'error'; message: string } | null;

export function AiFeatureRoutesPanel() {
  const [rows, setRows] = useState<AiFeatureRouteRow[]>([]);
  const [knownCodes, setKnownCodes] = useState<string[]>([]);
  const [bulkTargets, setBulkTargets] = useState<string[]>([]);
  const [providers, setProviders] = useState<AiProviderRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [bulkBusy, setBulkBusy] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const [draftFeature, setDraftFeature] = useState('');
  const [draftProvider, setDraftProvider] = useState('');
  const [draftModel, setDraftModel] = useState('');

  const reload = async () => {
    setLoading(true);
    try {
      const [r, p] = await Promise.all([fetchAiFeatureRoutes(), fetchAiProviders()]);
      setRows(r.rows);
      setKnownCodes(r.knownFeatureCodes);
      setBulkTargets(r.copilotBulkRouteTargets);
      setProviders(p);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to load feature routes.';
      setToast({ variant: 'error', message });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void reload();
  }, []);

  const copilotActive = useMemo(
    () => providers.some((p) => p.code === 'copilot' && p.isActive),
    [providers],
  );

  const activeProviderCodes = useMemo(
    () => providers.filter((p) => p.isActive).map((p) => p.code),
    [providers],
  );

  const handleBulkRoute = async () => {
    if (!copilotActive) return;
    setBulkBusy(true);
    try {
      const result = await bulkRouteFeaturesToCopilot();
      setToast({
        variant: 'success',
        message:
          result.changed.length === 0
            ? 'All bulk-route features are already on Copilot.'
            : `Routed to Copilot: ${result.changed.join(', ')}`,
      });
      await reload();
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Bulk-route failed.';
      setToast({ variant: 'error', message });
    } finally {
      setBulkBusy(false);
    }
  };

  const handleUpsert = async () => {
    if (!draftFeature || !draftProvider) {
      setToast({ variant: 'error', message: 'Pick a feature and a provider.' });
      return;
    }
    try {
      await upsertAiFeatureRoute({
        featureCode: draftFeature,
        providerCode: draftProvider,
        model: draftModel || null,
        isActive: true,
      });
      setToast({ variant: 'success', message: `Pinned ${draftFeature} → ${draftProvider}` });
      setDraftFeature('');
      setDraftProvider('');
      setDraftModel('');
      await reload();
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Save failed.';
      setToast({ variant: 'error', message });
    }
  };

  const handleToggle = async (row: AiFeatureRouteRow) => {
    try {
      await upsertAiFeatureRoute({
        featureCode: row.featureCode,
        providerCode: row.providerCode,
        model: row.model,
        isActive: !row.isActive,
      });
      await reload();
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Toggle failed.';
      setToast({ variant: 'error', message });
    }
  };

  const handleDelete = async (row: AiFeatureRouteRow) => {
    try {
      await deleteAiFeatureRoute(row.featureCode);
      setToast({ variant: 'success', message: `Removed pin for ${row.featureCode}` });
      await reload();
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Delete failed.';
      setToast({ variant: 'error', message });
    }
  };

  const columns: Column<AiFeatureRouteRow>[] = [
    { key: 'featureCode', header: 'Feature', render: (r) => <span className="font-mono text-xs">{r.featureCode}</span> },
    {
      key: 'providerCode',
      header: 'Provider',
      render: (r) => <Badge variant="info">{r.providerCode}</Badge>,
    },
    { key: 'model', header: 'Model', render: (r) => r.model ?? <span className="text-muted">default</span> },
    {
      key: 'isActive',
      header: 'Status',
      render: (r) => <Badge variant={r.isActive ? 'success' : 'muted'}>{r.isActive ? 'active' : 'paused'}</Badge>,
    },
    {
      key: 'actions',
      header: 'Actions',
      render: (r) => (
        <div className="flex items-center gap-2">
          <Button variant="ghost" size="sm" onClick={() => handleToggle(r)}>
            {r.isActive ? 'Pause' : 'Resume'}
          </Button>
          <Button variant="ghost" size="sm" onClick={() => handleDelete(r)} aria-label={`Delete ${r.featureCode}`}>
            <Trash2 className="h-4 w-4" />
          </Button>
        </div>
      ),
    },
  ];

  return (
    <section
      className="rounded-2xl border border-border bg-surface p-6 shadow-sm"
      aria-label="Per-feature AI provider routing"
    >
      <div className="mb-4 flex items-start justify-between gap-4">
        <div>
          <h2 className="text-lg font-semibold">Per-feature routing</h2>
          <p className="text-sm text-muted">
            Pin a specific feature code (e.g. <code className="font-mono">vocabulary.gloss</code>) to a
            provider. Unpinned features fall through to the global failover-priority order.
          </p>
        </div>
        <Button
          variant="primary"
          onClick={handleBulkRoute}
          disabled={!copilotActive || bulkBusy}
          aria-label="Route bulk-route feature set to Copilot"
        >
          {bulkBusy ? 'Routing…' : 'Route to Copilot'}
        </Button>
      </div>

      {!copilotActive && (
        <p className="mb-4 rounded-lg border border-warning/40 bg-warning/5 p-3 text-sm text-warning">
          Copilot provider is not registered or not active. Add a row in the Providers table above to
          enable bulk-route.
        </p>
      )}

      <div className="mb-4 flex flex-wrap gap-2">
        <span className="text-xs text-muted">Bulk-route targets:</span>
        {bulkTargets.map((code) => (
          <Badge key={code} variant="outline" className="font-mono">
            {code}
          </Badge>
        ))}
      </div>

      <div className="mb-4 grid gap-3 rounded-xl border border-border-muted bg-surface-muted p-4 md:grid-cols-4">
        <Select
          label="Feature"
          value={draftFeature}
          onChange={(e) => setDraftFeature(e.target.value)}
          placeholder="— pick feature —"
          options={knownCodes.map((c) => ({ value: c, label: c }))}
        />
        <Select
          label="Provider"
          value={draftProvider}
          onChange={(e) => setDraftProvider(e.target.value)}
          placeholder="— pick provider —"
          options={activeProviderCodes.map((c) => ({ value: c, label: c }))}
        />
        <Input
          label="Model (optional)"
          value={draftModel}
          onChange={(e) => setDraftModel(e.target.value)}
          placeholder="provider default"
        />
        <div className="flex items-end">
          <Button variant="primary" onClick={handleUpsert} disabled={!draftFeature || !draftProvider}>
            Pin route
          </Button>
        </div>
      </div>

      <DataTable
        data={loading ? [] : rows}
        columns={columns}
        keyExtractor={(r) => r.id}
        emptyMessage={loading ? 'Loading…' : 'No per-feature routes pinned. All features use the registry default.'}
      />

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </section>
  );
}
