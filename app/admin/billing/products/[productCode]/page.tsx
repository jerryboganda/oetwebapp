'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, Package, Save } from 'lucide-react';

import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { PageHeader } from '@/components/admin/ui/page-header';
import { Input } from '@/components/admin/ui/input';
import { Textarea } from '@/components/admin/ui/textarea';
import { Label } from '@/components/admin/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/admin/ui/select';
import { InlineAlert } from '@/components/ui/alert';
import { toast } from '@/components/admin/ui/toaster';
import { NoBillingPermission } from '@/components/admin/billing/no-billing-permission';
import { useAuth } from '@/contexts/auth-context';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import {
  fetchAdminBillingProduct,
  updateAdminBillingProduct,
  type AdminBillingProduct,
  type AdminBillingProductPrice,
} from '@/lib/api';
import { formatMoney } from '@/lib/money';

/**
 * Edit a single billing product. Surface-level metadata (name, status,
 * description, image, display order) lives on this page. Detailed price
 * versioning still happens through the catalog tools in
 * `/admin/billing/catalog`, but visible price snapshots are shown here
 * so admins can sanity-check what learners will see.
 */
export default function AdminProductEditorPage() {
  const router = useRouter();
  const params = useParams<{ productCode: string }>();
  const productCode = typeof params?.productCode === 'string'
    ? decodeURIComponent(params.productCode)
    : '';

  const { user } = useAuth();
  const canRead = hasPermission(user?.adminPermissions, AdminPermission.BillingRead, AdminPermission.BillingWrite);
  const canWrite = hasPermission(user?.adminPermissions, AdminPermission.BillingWrite, AdminPermission.BillingCatalogWrite);

  const [product, setProduct] = useState<AdminBillingProduct | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [form, setForm] = useState({
    name: '',
    description: '',
    productType: '',
    status: 'active',
    imageUrl: '',
    displayOrder: '0',
  });

  const load = useCallback(async () => {
    if (!productCode) return;
    setLoading(true);
    setError(null);
    try {
      const result = await fetchAdminBillingProduct(productCode);
      if (!result) {
        setError('Product not found.');
        setProduct(null);
        return;
      }
      setProduct(result);
      setForm({
        name: result.name,
        description: result.description ?? '',
        productType: result.productType,
        status: result.status,
        imageUrl: result.imageUrl ?? '',
        displayOrder: String(result.displayOrder ?? 0),
      });
    } catch (err) {
      console.error(err);
      setError(err instanceof Error ? err.message : 'Failed to load product.');
    } finally {
      setLoading(false);
    }
  }, [productCode]);

  useEffect(() => {
    void load();
  }, [load]);

  const submit = useCallback(async () => {
    if (!product) return;
    setSaving(true);
    try {
      const next = await updateAdminBillingProduct(product.productCode, {
        name: form.name.trim(),
        description: form.description.trim() || null,
        productType: form.productType.trim(),
        status: form.status,
        imageUrl: form.imageUrl.trim() || null,
        displayOrder: Number.parseInt(form.displayOrder, 10) || 0,
      });
      setProduct(next);
      toast.success(`Saved "${next.name}".`);
    } catch (err) {
      console.error(err);
      toast.error(err instanceof Error ? err.message : 'Save failed.');
    } finally {
      setSaving(false);
    }
  }, [product, form]);

  const breadcrumbs = useMemo(
    () => [
      { label: 'Admin', href: '/admin' },
      { label: 'Billing', href: '/admin/billing' },
      { label: 'Products', href: '/admin/billing/products' },
      { label: productCode },
    ],
    [productCode],
  );

  if (!user) return null;
  if (!canRead) return <NoBillingPermission />;

  return (
    <AdminPageShell>
      <PageHeader
        eyebrow="Billing"
        title={loading ? 'Loading...' : (product?.name ?? productCode)}
        description="Edit product metadata. Detailed price versioning is managed under Catalog tools."
        icon={<Package aria-hidden className="h-5 w-5" />}
        breadcrumbs={breadcrumbs}
        actions={
          <Button asChild variant="ghost" size="sm">
            <Link href="/admin/billing/products">
              <ArrowLeft className="mr-1.5 h-4 w-4" /> Back to products
            </Link>
          </Button>
        }
      />

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <div className="grid gap-6 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle>Product metadata</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <Input
              label="Display name"
              value={form.name}
              onChange={(event) => setForm((s) => ({ ...s, name: event.target.value }))}
              disabled={!canWrite || saving}
            />
            <Textarea
              label="Description"
              rows={4}
              value={form.description}
              onChange={(event) => setForm((s) => ({ ...s, description: event.target.value }))}
              disabled={!canWrite || saving}
            />
            <div className="grid gap-3 sm:grid-cols-2">
              <Input
                label="Product type"
                value={form.productType}
                onChange={(event) => setForm((s) => ({ ...s, productType: event.target.value }))}
                disabled={!canWrite || saving}
              />
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="status">Status</Label>
                <Select value={form.status} onValueChange={(value) => setForm((s) => ({ ...s, status: value }))} disabled={!canWrite || saving}>
                  <SelectTrigger id="status">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="active">Active</SelectItem>
                    <SelectItem value="draft">Draft</SelectItem>
                    <SelectItem value="archived">Archived</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>
            <div className="grid gap-3 sm:grid-cols-2">
              <Input
                label="Image URL"
                value={form.imageUrl}
                onChange={(event) => setForm((s) => ({ ...s, imageUrl: event.target.value }))}
                disabled={!canWrite || saving}
              />
              <Input
                label="Display order"
                type="number"
                value={form.displayOrder}
                onChange={(event) => setForm((s) => ({ ...s, displayOrder: event.target.value }))}
                disabled={!canWrite || saving}
              />
            </div>
            <div className="flex justify-end">
              <Button onClick={() => void submit()} loading={saving} disabled={!canWrite || saving} startIcon={<Save className="h-4 w-4" />}>
                Save changes
              </Button>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-sm">Prices</CardTitle>
          </CardHeader>
          <CardContent>
            {!product ? (
              <p className="text-sm text-admin-fg-muted">Loading...</p>
            ) : product.prices.length === 0 ? (
              <p className="text-sm text-admin-fg-muted">No prices configured.</p>
            ) : (
              <ul className="space-y-2 text-sm">
                {product.prices.map((price) => (
                  <li
                    key={price.priceId}
                    className="rounded-md border border-admin-border bg-admin-bg-subtle px-3 py-2"
                  >
                    <PriceRow price={price} />
                  </li>
                ))}
              </ul>
            )}
            <p className="mt-3 text-xs text-admin-fg-muted">
              Edit price versions in{' '}
              <Link href="/admin/billing/catalog" className="text-[var(--admin-primary)] hover:underline">
                Catalog tools
              </Link>
              .
            </p>
          </CardContent>
        </Card>
      </div>
    </AdminPageShell>
  );
}

function PriceRow({ price }: { price: AdminBillingProductPrice }) {
  return (
    <div className="flex items-baseline justify-between gap-3">
      <div>
        <p className="font-medium text-admin-fg-default">
          {formatMoney(price.amount, { currency: price.currency })}
        </p>
        <p className="text-[11px] uppercase tracking-wider text-admin-fg-muted">{price.interval}</p>
      </div>
      <p className="font-mono text-[10px] text-admin-fg-muted">{price.priceId}</p>
    </div>
  );
}
