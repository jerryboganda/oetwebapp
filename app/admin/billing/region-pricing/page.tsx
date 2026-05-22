'use client';

import { useCallback, useEffect, useState } from 'react';
import { Trash2, Plus } from 'lucide-react';
import { AdminRouteWorkspace, AdminRoutePanel, AdminRouteSectionHeader } from '@/components/domain/admin-route-surface';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Button } from '@/components/ui/button';
import { Input, Select, Checkbox } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import {
  listRegionPricings,
  upsertRegionPricing,
  deleteRegionPricing,
  type RegionPricingDto,
} from '@/lib/api';

const REGIONS = ['UK', 'GULF', 'EGYPT', 'PK', 'ROW'].map((r) => ({ value: r, label: r }));
const TARGET_TYPES = [
  { value: 'plan', label: 'plan' },
  { value: 'addon', label: 'addon' },
  { value: 'wallet_topup_tier', label: 'wallet_topup_tier' },
];

interface FormState {
  targetType: string;
  targetId: string;
  region: string;
  currency: string;
  priceAmount: string;
  isActive: boolean;
}

const EMPTY: FormState = {
  targetType: 'plan',
  targetId: '',
  region: 'UK',
  currency: 'GBP',
  priceAmount: '',
  isActive: true,
};

export default function AdminRegionPricingPage() {
  const [rows, setRows] = useState<RegionPricingDto[]>([]);
  const [form, setForm] = useState<FormState>(EMPTY);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows(await listRegionPricings());
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to load region pricings.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function handleAdd() {
    const priceAmount = Number(form.priceAmount);
    if (!form.targetId || Number.isNaN(priceAmount) || priceAmount < 0) {
      setError('Provide a valid target id and non-negative price.');
      return;
    }
    setSaving(true);
    setError(null);
    try {
      await upsertRegionPricing({
        targetType: form.targetType,
        targetId: form.targetId,
        region: form.region,
        currency: form.currency,
        priceAmount,
        isActive: form.isActive,
      });
      setToast({ variant: 'success', message: 'Region price saved.' });
      setForm(EMPTY);
      await load();
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to save region price.');
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete(id: string) {
    if (!confirm('Delete this region-pricing row?')) return;
    try {
      await deleteRegionPricing(id);
      setToast({ variant: 'success', message: 'Row deleted.' });
      await load();
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.userMessage ?? err?.message ?? 'Delete failed.' });
    }
  }

  const columns: Column<RegionPricingDto>[] = [
    { key: 'target', header: 'Target', render: (r) => `${r.targetType}:${r.targetId}` },
    { key: 'region', header: 'Region', render: (r) => r.region },
    { key: 'currency', header: 'Currency', render: (r) => r.currency },
    { key: 'price', header: 'Price', render: (r) => r.priceAmount.toString() },
    { key: 'active', header: 'Active', render: (r) => (r.isActive ? 'Yes' : 'No') },
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
      <AdminRouteSectionHeader title="Region pricing" description="Per-region price overrides for plans, add-ons, and wallet top-up tiers." />

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <AdminRoutePanel>
        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        <div className="grid grid-cols-1 gap-3 sm:grid-cols-6">
          <Select label="Target" value={form.targetType} options={TARGET_TYPES} onChange={(e) => setForm({ ...form, targetType: e.target.value })} />
          <Input label="Target id" value={form.targetId} onChange={(e) => setForm({ ...form, targetId: e.target.value })} />
          <Select label="Region" value={form.region} options={REGIONS} onChange={(e) => setForm({ ...form, region: e.target.value })} />
          <Input label="Currency" value={form.currency} onChange={(e) => setForm({ ...form, currency: e.target.value.toUpperCase() })} />
          <Input label="Price" type="number" value={form.priceAmount} onChange={(e) => setForm({ ...form, priceAmount: e.target.value })} />
          <Checkbox label="Active" checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} />
        </div>
        <div className="mt-3 flex justify-end">
          <Button onClick={handleAdd} disabled={saving}>
            <Plus className="mr-2 h-4 w-4" />
            {saving ? 'Saving…' : 'Add / update row'}
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
            emptyMessage="No region pricing rows yet."
          />
        )}
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
