'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, Save, Ticket } from 'lucide-react';

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
import { Switch } from '@/components/admin/ui/switch';
import { toast } from '@/components/admin/ui/toaster';
import { InlineAlert } from '@/components/ui/alert';
import { NoBillingPermission } from '@/components/admin/billing/no-billing-permission';
import { useAuth } from '@/contexts/auth-context';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import {
  createAdminBillingCoupon,
  fetchAdminBillingCoupons,
  updateAdminBillingCoupon,
} from '@/lib/api';
import type { AdminBillingCoupon } from '@/lib/types/admin';

/**
 * Coupon editor. Routes to `new` create a new coupon, all other codes
 * edit an existing one. Detailed redemption history lives on the legacy
 * `/admin/billing` aggregate page; we link there from the footer.
 */
export default function AdminCouponEditorPage() {
  const params = useParams<{ code: string }>();
  const router = useRouter();
  const code = typeof params?.code === 'string' ? decodeURIComponent(params.code) : '';
  const isNew = code === 'new';

  const { user } = useAuth();
  const canRead = hasPermission(user?.adminPermissions, AdminPermission.BillingRead, AdminPermission.BillingWrite);
  const canWrite = hasPermission(user?.adminPermissions, AdminPermission.BillingWrite, AdminPermission.BillingCatalogWrite);

  const [loading, setLoading] = useState(!isNew);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [existing, setExisting] = useState<AdminBillingCoupon | null>(null);
  const [form, setForm] = useState({
    code: '',
    name: '',
    description: '',
    discountType: 'percentage' as 'percentage' | 'fixed',
    discountValue: '10',
    currency: 'AUD',
    startsAt: '',
    endsAt: '',
    usageLimitTotal: '',
    usageLimitPerUser: '',
    minimumSubtotal: '',
    isStackable: true,
    status: 'active',
    applicablePlanCodes: '',
    applicableAddOnCodes: '',
    notes: '',
  });

  const load = useCallback(async () => {
    if (isNew) return;
    setLoading(true);
    setError(null);
    try {
      const result = await fetchAdminBillingCoupons();
      const items = Array.isArray(result) ? result : (result as { items?: AdminBillingCoupon[] }).items ?? [];
      const found = (items as AdminBillingCoupon[]).find((c) => c.code === code) ?? null;
      if (!found) {
        setError('Coupon not found.');
        return;
      }
      setExisting(found);
      setForm({
        code: found.code,
        name: found.name,
        description: found.description ?? '',
        discountType: found.discountType,
        discountValue: String(found.discountValue),
        currency: found.currency,
        startsAt: found.startsAt ? toLocalDateValue(found.startsAt) : '',
        endsAt: found.endsAt ? toLocalDateValue(found.endsAt) : '',
        usageLimitTotal: found.usageLimitTotal != null ? String(found.usageLimitTotal) : '',
        usageLimitPerUser: found.usageLimitPerUser != null ? String(found.usageLimitPerUser) : '',
        minimumSubtotal: found.minimumSubtotal != null ? String(found.minimumSubtotal) : '',
        isStackable: found.isStackable,
        status: found.status,
        applicablePlanCodes: (found.applicablePlanCodes ?? []).join(', '),
        applicableAddOnCodes: (found.applicableAddOnCodes ?? []).join(', '),
        notes: found.notes ?? '',
      });
    } catch (err) {
      console.error(err);
      setError(err instanceof Error ? err.message : 'Failed to load coupon.');
    } finally {
      setLoading(false);
    }
  }, [code, isNew]);

  useEffect(() => {
    void load();
  }, [load]);

  const submit = useCallback(async () => {
    setSaving(true);
    setSaveError(null);
    try {
      const payload = {
        code: form.code.trim().toUpperCase(),
        name: form.name.trim(),
        description: form.description.trim() || undefined,
        discountType: form.discountType,
        discountValue: Number(form.discountValue) || 0,
        currency: form.currency,
        startsAt: form.startsAt ? new Date(form.startsAt).toISOString() : null,
        endsAt: form.endsAt ? new Date(form.endsAt).toISOString() : null,
        usageLimitTotal: form.usageLimitTotal ? Number(form.usageLimitTotal) : null,
        usageLimitPerUser: form.usageLimitPerUser ? Number(form.usageLimitPerUser) : null,
        minimumSubtotal: form.minimumSubtotal ? Number(form.minimumSubtotal) : null,
        isStackable: form.isStackable,
        status: form.status,
        applicablePlanCodesJson: JSON.stringify(splitList(form.applicablePlanCodes)),
        applicableAddOnCodesJson: JSON.stringify(splitList(form.applicableAddOnCodes)),
        notes: form.notes.trim() || null,
      };
      if (existing) {
        await updateAdminBillingCoupon(existing.id, payload);
        toast.success(`Coupon "${form.code}" updated.`);
      } else {
        await createAdminBillingCoupon(payload);
        toast.success(`Coupon "${form.code}" created.`);
        router.push(`/admin/billing/coupons/${encodeURIComponent(form.code)}`);
      }
    } catch (err) {
      console.error(err);
      const message = err instanceof Error ? err.message : 'Save failed.';
      setSaveError(message);
      toast.error(message);
    } finally {
      setSaving(false);
    }
  }, [existing, form, router]);

  const breadcrumbs = useMemo(
    () => [
      { label: 'Admin', href: '/admin' },
      { label: 'Billing', href: '/admin/billing' },
      { label: 'Coupons', href: '/admin/billing/coupons' },
      { label: isNew ? 'New' : code },
    ],
    [code, isNew],
  );

  if (!user) return null;
  if (!canRead) return <NoBillingPermission />;

  return (
    <AdminPageShell>
      <PageHeader
        eyebrow="Billing"
        title={isNew ? 'New coupon' : (existing?.name ?? code)}
        description={isNew ? 'Create a promotional code.' : 'Edit an existing coupon.'}
        icon={<Ticket aria-hidden className="h-5 w-5" />}
        breadcrumbs={breadcrumbs}
        actions={
          <Button asChild variant="ghost" size="sm">
            <Link href="/admin/billing/coupons">
              <ArrowLeft className="mr-1.5 h-4 w-4" /> Back to coupons
            </Link>
          </Button>
        }
      />

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <Card>
        <CardHeader>
          <CardTitle>Coupon details</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid gap-3 sm:grid-cols-2">
            <Input
              label="Code"
              value={form.code}
              onChange={(event) => setForm((s) => ({ ...s, code: event.target.value }))}
              disabled={!isNew || !canWrite || saving}
              required
            />
            <Input
              label="Name"
              value={form.name}
              onChange={(event) => setForm((s) => ({ ...s, name: event.target.value }))}
              disabled={!canWrite || saving}
              required
            />
          </div>
          <Textarea
            label="Description"
            rows={3}
            value={form.description}
            onChange={(event) => setForm((s) => ({ ...s, description: event.target.value }))}
            disabled={!canWrite || saving}
          />
          <div className="grid gap-3 sm:grid-cols-3">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="discountType">Discount type</Label>
              <Select
                value={form.discountType}
                onValueChange={(value) =>
                  setForm((s) => ({ ...s, discountType: value as 'percentage' | 'fixed' }))
                }
                disabled={!canWrite || saving}
              >
                <SelectTrigger id="discountType">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="percentage">Percentage</SelectItem>
                  <SelectItem value="fixed">Fixed amount</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <Input
              label="Discount value"
              type="number"
              step="0.01"
              value={form.discountValue}
              onChange={(event) => setForm((s) => ({ ...s, discountValue: event.target.value }))}
              disabled={!canWrite || saving}
              required
            />
            <Input
              label="Currency"
              value={form.currency}
              onChange={(event) => setForm((s) => ({ ...s, currency: event.target.value.toUpperCase() }))}
              disabled={form.discountType !== 'fixed' || !canWrite || saving}
            />
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <Input
              label="Starts at"
              type="datetime-local"
              value={form.startsAt}
              onChange={(event) => setForm((s) => ({ ...s, startsAt: event.target.value }))}
              disabled={!canWrite || saving}
            />
            <Input
              label="Ends at"
              type="datetime-local"
              value={form.endsAt}
              onChange={(event) => setForm((s) => ({ ...s, endsAt: event.target.value }))}
              disabled={!canWrite || saving}
            />
          </div>
          <div className="grid gap-3 sm:grid-cols-3">
            <Input
              label="Usage limit (total)"
              type="number"
              value={form.usageLimitTotal}
              onChange={(event) => setForm((s) => ({ ...s, usageLimitTotal: event.target.value }))}
              disabled={!canWrite || saving}
            />
            <Input
              label="Usage limit (per user)"
              type="number"
              value={form.usageLimitPerUser}
              onChange={(event) => setForm((s) => ({ ...s, usageLimitPerUser: event.target.value }))}
              disabled={!canWrite || saving}
            />
            <Input
              label="Minimum subtotal"
              type="number"
              step="0.01"
              value={form.minimumSubtotal}
              onChange={(event) => setForm((s) => ({ ...s, minimumSubtotal: event.target.value }))}
              disabled={!canWrite || saving}
            />
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <Input
              label="Applicable plan codes (comma separated)"
              value={form.applicablePlanCodes}
              onChange={(event) => setForm((s) => ({ ...s, applicablePlanCodes: event.target.value }))}
              disabled={!canWrite || saving}
            />
            <Input
              label="Applicable add-on codes (comma separated)"
              value={form.applicableAddOnCodes}
              onChange={(event) => setForm((s) => ({ ...s, applicableAddOnCodes: event.target.value }))}
              disabled={!canWrite || saving}
            />
          </div>
          <div className="flex flex-wrap items-center gap-6">
            <label className="inline-flex items-center gap-2 text-sm">
              <Switch
                checked={form.isStackable}
                onCheckedChange={(checked) => setForm((s) => ({ ...s, isStackable: checked }))}
                disabled={!canWrite || saving}
              />
              Stackable with other promos
            </label>
            <div className="flex items-center gap-2">
              <Label htmlFor="status">Status</Label>
              <Select value={form.status} onValueChange={(value) => setForm((s) => ({ ...s, status: value }))} disabled={!canWrite || saving}>
                <SelectTrigger id="status" className="w-40">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="active">Active</SelectItem>
                  <SelectItem value="scheduled">Scheduled</SelectItem>
                  <SelectItem value="archived">Archived</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>
          <Textarea
            label="Internal notes"
            rows={3}
            value={form.notes}
            onChange={(event) => setForm((s) => ({ ...s, notes: event.target.value }))}
            disabled={!canWrite || saving}
          />
          {saveError ? (
            <InlineAlert variant="error" title="Save failed">
              {saveError}
            </InlineAlert>
          ) : null}
          <div className="flex justify-end">
            <Button
              onClick={() => void submit()}
              loading={saving}
              disabled={!canWrite || saving}
              startIcon={<Save className="h-4 w-4" />}
            >
              {existing ? 'Save changes' : 'Create coupon'}
            </Button>
          </div>
        </CardContent>
      </Card>

      {loading ? <p className="text-sm text-admin-fg-muted">Loading...</p> : null}
    </AdminPageShell>
  );
}

function splitList(value: string): string[] {
  return value
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean);
}

function toLocalDateValue(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  const offset = date.getTimezoneOffset();
  return new Date(date.getTime() - offset * 60_000).toISOString().slice(0, 16);
}
