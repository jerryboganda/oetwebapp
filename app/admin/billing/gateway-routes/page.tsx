'use client';

import { useCallback, useEffect, useState } from 'react';
import { Trash2, Plus } from 'lucide-react';
import { AdminRouteWorkspace, AdminRoutePanel, AdminRouteSectionHeader } from '@/components/domain/admin-route-surface';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Button } from '@/components/ui/button';
import { Input, Select, Checkbox } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import {
  listGatewayRoutes,
  upsertGatewayRoute,
  deleteGatewayRoute,
  type GatewayRouteDto,
} from '@/lib/api';

const REGIONS = ['UK', 'GULF', 'EGYPT', 'PK', 'ROW'].map((r) => ({ value: r, label: r }));
const PRODUCT_TYPES = ['*', 'subscription', 'addon', 'wallet_topup', 'manual'].map((p) => ({ value: p, label: p }));
const GATEWAYS = ['stripe', 'paypal', 'paytabs', 'paymob', 'checkoutcom'].map((g) => ({ value: g, label: g }));

interface FormState {
  region: string;
  currency: string;
  productType: string;
  gatewayName: string;
  priority: string;
  isEnabled: boolean;
}

const EMPTY: FormState = {
  region: 'UK',
  currency: 'GBP',
  productType: '*',
  gatewayName: 'stripe',
  priority: '10',
  isEnabled: true,
};

export default function AdminGatewayRoutesPage() {
  const [rows, setRows] = useState<GatewayRouteDto[]>([]);
  const [form, setForm] = useState<FormState>(EMPTY);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows(await listGatewayRoutes());
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to load gateway routes.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function handleAdd() {
    const priority = Number(form.priority);
    if (Number.isNaN(priority)) {
      setError('Priority must be a number.');
      return;
    }
    setSaving(true);
    setError(null);
    try {
      await upsertGatewayRoute({ ...form, priority });
      setToast({ variant: 'success', message: 'Route saved.' });
      setForm(EMPTY);
      await load();
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to save route.');
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete(id: string) {
    if (!confirm('Delete this gateway route?')) return;
    try {
      await deleteGatewayRoute(id);
      setToast({ variant: 'success', message: 'Route deleted.' });
      await load();
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.userMessage ?? err?.message ?? 'Delete failed.' });
    }
  }

  const columns: Column<GatewayRouteDto>[] = [
    { key: 'region', header: 'Region', render: (r) => r.region },
    { key: 'currency', header: 'Currency', render: (r) => r.currency },
    { key: 'productType', header: 'Product', render: (r) => r.productType },
    { key: 'gateway', header: 'Gateway', render: (r) => r.gatewayName },
    { key: 'priority', header: 'Priority', render: (r) => r.priority.toString() },
    { key: 'enabled', header: 'Enabled', render: (r) => (r.isEnabled ? 'Yes' : 'No') },
    {
      key: 'actions',
      header: '',
      render: (r) => (
        <Button variant="ghost" onClick={() => handleDelete(r.id)} aria-label="Delete">
          <Trash2 className="h-4 w-4" />
        </Button>
      ),
    },
  ];

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader title="Gateway routes" description="(region, currency, productType) → payment gateway routing with priority." />

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <AdminRoutePanel>
        {error && <InlineAlert variant="error">{error}</InlineAlert>}
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-6">
          <Select label="Region" value={form.region} options={REGIONS} onChange={(e) => setForm({ ...form, region: e.target.value })} />
          <Input label="Currency or *" value={form.currency} onChange={(e) => setForm({ ...form, currency: e.target.value.toUpperCase() })} />
          <Select label="Product" value={form.productType} options={PRODUCT_TYPES} onChange={(e) => setForm({ ...form, productType: e.target.value })} />
          <Select label="Gateway" value={form.gatewayName} options={GATEWAYS} onChange={(e) => setForm({ ...form, gatewayName: e.target.value })} />
          <Input label="Priority" type="number" value={form.priority} onChange={(e) => setForm({ ...form, priority: e.target.value })} />
          <Checkbox label="Enabled" checked={form.isEnabled} onChange={(e) => setForm({ ...form, isEnabled: e.target.checked })} />
        </div>
        <div className="mt-3 flex justify-end">
          <Button onClick={handleAdd} disabled={saving}>
            <Plus className="mr-2 h-4 w-4" />
            {saving ? 'Saving…' : 'Add / update route'}
          </Button>
        </div>
      </AdminRoutePanel>

      <AdminRoutePanel>
        {loading ? (
          <p className="text-sm text-muted-foreground">Loading…</p>
        ) : (
          <DataTable
            data={rows}
            columns={columns}
            keyExtractor={(r) => r.id}
            emptyMessage="No gateway routes configured."
          />
        )}
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
