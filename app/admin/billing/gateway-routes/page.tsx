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
  listGatewayRoutes,
  upsertGatewayRoute,
  deleteGatewayRoute,
  type GatewayRouteDto,
} from '@/lib/api';

const REGIONS = ['UK', 'GULF', 'EGYPT', 'PK', 'ROW'];
const PRODUCT_TYPES = ['*', 'subscription', 'addon', 'wallet_topup', 'manual'];
const GATEWAYS = ['stripe', 'paypal', 'paytabs', 'paymob', 'checkoutcom'];

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

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows(await listGatewayRoutes());
      setError(null);
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
      toast.success('Route saved.');
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
      toast.success('Route deleted.');
      await load();
    } catch (err: any) {
      toast.error(err?.userMessage ?? err?.message ?? 'Delete failed.');
    }
  }

  const columns: ColumnDef<GatewayRouteDto>[] = [
    { id: 'region', accessorKey: 'region', header: 'Region' },
    { id: 'currency', accessorKey: 'currency', header: 'Currency' },
    { id: 'productType', accessorKey: 'productType', header: 'Product' },
    { id: 'gateway', accessorKey: 'gatewayName', header: 'Gateway' },
    {
      id: 'priority',
      header: 'Priority',
      cell: ({ row }) => row.original.priority.toString(),
    },
    {
      id: 'enabled',
      header: 'Enabled',
      cell: ({ row }) => (row.original.isEnabled ? 'Yes' : 'No'),
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
      title="Gateway routes"
      description="(region, currency, productType) → payment gateway routing with priority."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Billing', href: '/admin/billing' },
        { label: 'Gateway routes' },
      ]}
      banner={
        <Card>
          <CardHeader>
            <CardTitle>Add or update route</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4 pt-0">
            {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-3 lg:grid-cols-6">
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="route-region">Region</Label>
                <Select value={form.region} onValueChange={(v) => setForm({ ...form, region: v })}>
                  <SelectTrigger id="route-region">
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
                label="Currency or *"
                value={form.currency}
                onChange={(e) => setForm({ ...form, currency: e.target.value.toUpperCase() })}
              />
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="route-product">Product</Label>
                <Select
                  value={form.productType}
                  onValueChange={(v) => setForm({ ...form, productType: v })}
                >
                  <SelectTrigger id="route-product">
                    <SelectValue placeholder="Product" />
                  </SelectTrigger>
                  <SelectContent>
                    {PRODUCT_TYPES.map((p) => (
                      <SelectItem key={p} value={p}>
                        {p}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="route-gateway">Gateway</Label>
                <Select
                  value={form.gatewayName}
                  onValueChange={(v) => setForm({ ...form, gatewayName: v })}
                >
                  <SelectTrigger id="route-gateway">
                    <SelectValue placeholder="Gateway" />
                  </SelectTrigger>
                  <SelectContent>
                    {GATEWAYS.map((g) => (
                      <SelectItem key={g} value={g}>
                        {g}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <Input
                label="Priority"
                type="number"
                value={form.priority}
                onChange={(e) => setForm({ ...form, priority: e.target.value })}
              />
              <div className="flex items-end gap-2">
                <Switch
                  id="route-enabled"
                  checked={form.isEnabled}
                  onCheckedChange={(c) => setForm({ ...form, isEnabled: c })}
                />
                <Label htmlFor="route-enabled">Enabled</Label>
              </div>
            </div>
            <div className="flex justify-end">
              <Button onClick={handleAdd} disabled={saving} loading={saving} startIcon={<Plus className="h-4 w-4" />}>
                Add / update route
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
        emptyMessage="No gateway routes configured."
        searchPlaceholder="Search routes…"
      />
    </AdminTableLayout>
  );
}
