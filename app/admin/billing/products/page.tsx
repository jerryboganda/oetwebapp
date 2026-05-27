'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import type { ColumnDef } from '@tanstack/react-table';
import { Edit, Package, RefreshCw, Search } from 'lucide-react';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { DataTable } from '@/components/admin/ui/data-table';
import { Input } from '@/components/admin/ui/input';
import { InlineAlert } from '@/components/ui/alert';
import { NoBillingPermission } from '@/components/admin/billing/no-billing-permission';
import { useAuth } from '@/contexts/auth-context';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import {
  fetchAdminBillingProducts,
  type AdminBillingProduct,
} from '@/lib/api';
import { formatMoney } from '@/lib/money';

/**
 * Admin products index — lists every `BillingProduct` with a quick view
 * of its prices and a link to the per-product editor. Reads the same
 * catalog endpoint that the public `/catalog` page uses, but augmented
 * with admin-only fields (status, metadata).
 */
export default function AdminProductsPage() {
  const { user } = useAuth();
  const canRead = hasPermission(user?.adminPermissions, AdminPermission.BillingRead, AdminPermission.BillingWrite);
  const canWrite = hasPermission(user?.adminPermissions, AdminPermission.BillingWrite, AdminPermission.BillingCatalogWrite);

  const [products, setProducts] = useState<AdminBillingProduct[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setProducts(await fetchAdminBillingProducts());
    } catch (err) {
      console.error(err);
      setError(err instanceof Error ? err.message : 'Failed to load products.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const filtered = useMemo(() => {
    if (!search.trim()) return products;
    const needle = search.trim().toLowerCase();
    return products.filter(
      (p) =>
        p.productCode.toLowerCase().includes(needle) ||
        p.name.toLowerCase().includes(needle) ||
        (p.description ?? '').toLowerCase().includes(needle),
    );
  }, [products, search]);

  const columns: ColumnDef<AdminBillingProduct>[] = [
    {
      id: 'product',
      header: 'Product',
      cell: ({ row }) => (
        <div className="space-y-1">
          <p className="font-medium text-admin-fg-default">{row.original.name}</p>
          <p className="text-xs uppercase tracking-wider text-admin-fg-muted">{row.original.productCode}</p>
          {row.original.description ? (
            <p className="line-clamp-1 text-sm text-admin-fg-muted">{row.original.description}</p>
          ) : null}
        </div>
      ),
    },
    {
      id: 'type',
      header: 'Type',
      cell: ({ row }) => <span className="text-admin-fg-muted">{row.original.productType}</span>,
    },
    {
      id: 'prices',
      header: 'Prices',
      cell: ({ row }) => (
        <div className="flex flex-wrap gap-1 text-xs">
          {(row.original.prices ?? []).map((price) => (
            <span
              key={price.priceId}
              className="rounded-md border border-admin-border bg-admin-bg-subtle px-1.5 py-0.5"
            >
              {formatMoney(price.amount, { currency: price.currency })} / {price.interval}
            </span>
          ))}
        </div>
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
            <Link href={`/admin/billing/products/${encodeURIComponent(row.original.productCode)}`}>
              <Edit className="mr-1 h-3.5 w-3.5" /> Edit
            </Link>
          </Button>
        </div>
      ),
    },
  ];

  if (!user) return null;
  if (!canRead) return <NoBillingPermission />;

  return (
    <AdminTableLayout
      title="Products"
      description="Every BillingProduct in the catalog with price snapshots and quick edit links."
      icon={<Package aria-hidden className="h-5 w-5" />}
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Billing', href: '/admin/billing' },
        { label: 'Products' },
      ]}
      actions={
        <Button variant="secondary" onClick={() => void load()} startIcon={<RefreshCw className="h-4 w-4" />}>
          Refresh
        </Button>
      }
      banner={
        <Card>
          <CardContent className="space-y-3 pt-6">
            {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <Input
                label="Search"
                placeholder="Code, name, or description..."
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                startIcon={<Search className="h-4 w-4" />}
              />
            </div>
          </CardContent>
        </Card>
      }
    >
      <DataTable
        columns={columns}
        data={filtered}
        loading={loading}
        emptyMessage="No billing products are visible. Reseed the catalog or check filters."
        searchPlaceholder="Filter products..."
      />
    </AdminTableLayout>
  );
}
