'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import type { ColumnDef } from '@tanstack/react-table';
import { Edit, Plus, RefreshCw, Ticket } from 'lucide-react';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { DataTable } from '@/components/admin/ui/data-table';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/admin/ui/select';
import { Label } from '@/components/admin/ui/label';
import { InlineAlert } from '@/components/ui/alert';
import { NoBillingPermission } from '@/components/admin/billing/no-billing-permission';
import { useAuth } from '@/contexts/auth-context';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { fetchAdminBillingCoupons } from '@/lib/api';
import type { AdminBillingCoupon } from '@/lib/types/admin';

/**
 * Admin coupons index — lists every coupon with quick filters by
 * status. Detailed CRUD lives on `[code]/page.tsx`. The "Create" CTA
 * deep-links to the editor with `code=new`.
 */
export default function AdminCouponsPage() {
  const { user } = useAuth();
  const canRead = hasPermission(user?.adminPermissions, AdminPermission.BillingRead, AdminPermission.BillingWrite);
  const canWrite = hasPermission(user?.adminPermissions, AdminPermission.BillingWrite, AdminPermission.BillingCatalogWrite);

  const [status, setStatus] = useState<string>('__all');
  const [coupons, setCoupons] = useState<AdminBillingCoupon[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const filter = status === '__all' ? undefined : { status };
      const result = await fetchAdminBillingCoupons(filter);
      const items = Array.isArray(result) ? result : (result as { items?: AdminBillingCoupon[] }).items ?? [];
      setCoupons(items as AdminBillingCoupon[]);
    } catch (err) {
      console.error(err);
      setError(err instanceof Error ? err.message : 'Failed to load coupons.');
    } finally {
      setLoading(false);
    }
  }, [status]);

  useEffect(() => {
    void load();
  }, [load]);

  const columns: ColumnDef<AdminBillingCoupon>[] = useMemo(() => [
    {
      id: 'code',
      header: 'Code',
      cell: ({ row }) => (
        <div>
          <p className="font-mono text-sm font-semibold text-admin-fg-default">{row.original.code}</p>
          <p className="text-xs text-admin-fg-muted">{row.original.name}</p>
        </div>
      ),
    },
    {
      id: 'discount',
      header: 'Discount',
      cell: ({ row }) => {
        const c = row.original;
        const value =
          c.discountType === 'percentage'
            ? `${c.discountValue}%`
            : `${c.currency} ${c.discountValue.toFixed(2)}`;
        return <span className="text-sm text-admin-fg-default">{value}</span>;
      },
    },
    {
      id: 'window',
      header: 'Window',
      cell: ({ row }) => (
        <div className="text-xs text-admin-fg-muted">
          <p>{row.original.startsAt ? new Date(row.original.startsAt).toLocaleDateString() : '-'}</p>
          <p>{row.original.endsAt ? new Date(row.original.endsAt).toLocaleDateString() : 'No expiry'}</p>
        </div>
      ),
    },
    {
      id: 'usage',
      header: 'Usage',
      cell: ({ row }) => (
        <span className="text-sm text-admin-fg-muted">
          {row.original.redemptionCount}
          {row.original.usageLimitTotal != null ? ` / ${row.original.usageLimitTotal}` : ''}
        </span>
      ),
    },
    {
      id: 'status',
      header: 'Status',
      cell: ({ row }) => (
        <span className="text-xs uppercase tracking-wider text-admin-fg-muted">{row.original.status}</span>
      ),
    },
    {
      id: 'actions',
      header: '',
      cell: ({ row }) => (
        <div className="flex justify-end">
          <Button asChild size="sm" variant="outline" disabled={!canWrite}>
            <Link href={`/admin/billing/coupons/${encodeURIComponent(row.original.code)}`}>
              <Edit className="mr-1 h-3.5 w-3.5" /> Edit
            </Link>
          </Button>
        </div>
      ),
    },
  ], [canWrite]);

  if (!user) return null;
  if (!canRead) return <NoBillingPermission />;

  return (
    <AdminTableLayout
      title="Coupons"
      description="Promotional codes available across the catalog."
      icon={<Ticket aria-hidden className="h-5 w-5" />}
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Billing', href: '/admin/billing' },
        { label: 'Coupons' },
      ]}
      actions={
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="secondary" onClick={() => void load()} startIcon={<RefreshCw className="h-4 w-4" />}>
            Refresh
          </Button>
          <Button asChild disabled={!canWrite} startIcon={<Plus className="h-4 w-4" />}>
            <Link href="/admin/billing/coupons/new">New coupon</Link>
          </Button>
        </div>
      }
      banner={
        <Card>
          <CardContent className="space-y-3 pt-6">
            {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="status">Status</Label>
                <Select value={status} onValueChange={setStatus}>
                  <SelectTrigger id="status">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="__all">All statuses</SelectItem>
                    <SelectItem value="active">Active</SelectItem>
                    <SelectItem value="scheduled">Scheduled</SelectItem>
                    <SelectItem value="archived">Archived</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>
          </CardContent>
        </Card>
      }
    >
      <DataTable
        columns={columns}
        data={coupons}
        loading={loading}
        emptyMessage="No coupons match the current filters."
        searchPlaceholder="Search by code or name..."
      />
    </AdminTableLayout>
  );
}
