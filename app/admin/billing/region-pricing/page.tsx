'use client';

import { useCallback, useEffect, useState } from 'react';
import type { ColumnDef } from '@tanstack/react-table';
import { Plus, Trash2 } from 'lucide-react';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
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
import { Switch } from '@/components/admin/ui/switch';
import { toast } from '@/components/admin/ui/toaster';
import { InlineAlert } from '@/components/ui/alert';
import {
  listRegionPricings,
  upsertRegionPricing,
  deleteRegionPricing,
  type RegionPricingDto,
} from '@/lib/api';

const REGIONS = ['UK', 'GULF', 'EGYPT', 'PK', 'ROW'];
const TARGET_TYPES = ['plan', 'addon', 'wallet_topup_tier'];

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

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows(await listRegionPricings());
      setError(null);
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
      toast.success('Region price saved.');
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
      toast.success('Row deleted.');
      await load();
    } catch (err: any) {
      toast.error(err?.userMessage ?? err?.message ?? 'Delete failed.');
    }
  }

  const columns: ColumnDef<RegionPricingDto>[] = [
    {
      id: 'target',
      header: 'Target',
      cell: ({ row }) => `${row.original.targetType}:${row.original.targetId}`,
    },
    { id: 'region', accessorKey: 'region', header: 'Region' },
    { id: 'currency', accessorKey: 'currency', header: 'Currency' },
    {
      id: 'price',
      header: 'Price',
      cell: ({ row }) => row.original.priceAmount.toString(),
    },
    {
      id: 'active',
      header: 'Active',
      cell: ({ row }) => (row.original.isActive ? 'Yes' : 'No'),
    },
    {
      id: 'actions',
      header: '',
      enableSorting: false,
      enableHiding: false,
      cell: ({ row }) => (
        <Button variant="ghost" size="sm" onClick={() => handleDelete(row.original.id)} aria-label="Delete">
          <Trash2 className="h-4 w-4" />
        </Button>
      ),
    },
  ];

  return (
    <AdminTableLayout
      title="Region pricing"
      description="Per-region price overrides for plans, add-ons, and wallet top-up tiers."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Billing', href: '/admin/billing' },
        { label: 'Region pricing' },
      ]}
      banner={
        <Card>
          <CardHeader>
            <CardTitle>Add or update row</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4 pt-0">
            {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-3 lg:grid-cols-6">
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="rp-target">Target</Label>
                <Select
                  value={form.targetType}
                  onValueChange={(v) => setForm({ ...form, targetType: v })}
                >
                  <SelectTrigger id="rp-target">
                    <SelectValue placeholder="Target" />
                  </SelectTrigger>
                  <SelectContent>
                    {TARGET_TYPES.map((t) => (
                      <SelectItem key={t} value={t}>
                        {t}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <Input
                label="Target id"
                value={form.targetId}
                onChange={(e) => setForm({ ...form, targetId: e.target.value })}
              />
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="rp-region">Region</Label>
                <Select value={form.region} onValueChange={(v) => setForm({ ...form, region: v })}>
                  <SelectTrigger id="rp-region">
                    <SelectValue placeholder="Region" />
                  </SelectTrigger>
                  <SelectContent>
                    {REGIONS.map((r) => (
                      <SelectItem key={r} value={r}>
                        {r}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <Input
                label="Currency"
                value={form.currency}
                onChange={(e) => setForm({ ...form, currency: e.target.value.toUpperCase() })}
              />
              <Input
                label="Price"
                type="number"
                value={form.priceAmount}
                onChange={(e) => setForm({ ...form, priceAmount: e.target.value })}
              />
              <div className="flex items-end gap-2">
                <Switch
                  id="rp-active"
                  checked={form.isActive}
                  onCheckedChange={(c) => setForm({ ...form, isActive: c })}
                />
                <Label htmlFor="rp-active">Active</Label>
              </div>
            </div>
            <div className="flex justify-end">
              <Button onClick={handleAdd} loading={saving} startIcon={<Plus className="h-4 w-4" />}>
                Add / update row
              </Button>
            </div>
          </CardContent>
        </Card>
      }
    >
      <DataTable
        columns={columns}
        data={rows}
        loading={loading}
        emptyMessage="No region pricing rows yet."
        searchPlaceholder="Search rows…"
      />
    </AdminTableLayout>
  );
}
