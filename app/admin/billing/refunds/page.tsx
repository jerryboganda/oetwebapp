'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import type { ColumnDef } from '@tanstack/react-table';
import { Check, RefreshCw, RotateCcw, X } from 'lucide-react';

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
import { toast } from '@/components/admin/ui/toaster';
import { InlineAlert } from '@/components/ui/alert';
import { NoBillingPermission } from '@/components/admin/billing/no-billing-permission';
import { useAuth } from '@/contexts/auth-context';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import {
  fetchAdminRefunds,
  postAdminRefundAction,
  type AdminRefundRequest,
} from '@/lib/api';
import { formatMoney } from '@/lib/money';

/**
 * Refund-request triage page. Lists pending refund requests and exposes
 * Approve / Deny / Issue actions inline. Underlying endpoint is
 * `POST /v1/admin/refunds`. Wave B4 will add the issuer-side webhook.
 */
export default function AdminRefundsPage() {
  const { user } = useAuth();
  const canRead = hasPermission(user?.adminPermissions, AdminPermission.BillingRead, AdminPermission.BillingWrite);
  const canWrite = hasPermission(user?.adminPermissions, AdminPermission.BillingWrite, AdminPermission.BillingSubscriptionWrite);

  const [status, setStatus] = useState<string>('pending');
  const [refunds, setRefunds] = useState<AdminRefundRequest[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [serviceAvailable, setServiceAvailable] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const filter = status === '__all' ? undefined : { status };
      const result = await fetchAdminRefunds(filter);
      setRefunds(result.items);
      setServiceAvailable(result.items.length > 0 || result.total > 0 || status === '__all');
    } catch (err) {
      console.error(err);
      setError(err instanceof Error ? err.message : 'Failed to load refunds.');
    } finally {
      setLoading(false);
    }
  }, [status]);

  useEffect(() => {
    void load();
  }, [load]);

  async function act(refund: AdminRefundRequest, action: 'approve' | 'deny' | 'issue') {
    setBusyId(refund.id);
    try {
      const next = await postAdminRefundAction({ refundId: refund.id, action });
      setRefunds((current) => current.map((r) => (r.id === refund.id ? next : r)));
      toast.success(`Refund ${action}d.`);
    } catch (err) {
      console.error(err);
      toast.error(err instanceof Error ? err.message : 'Refund action failed.');
    } finally {
      setBusyId(null);
    }
  }

  const columns: ColumnDef<AdminRefundRequest>[] = useMemo(() => [
    {
      id: 'requested',
      header: 'Requested',
      cell: ({ row }) => (
        <div>
          <p className="text-sm text-admin-fg-default">{new Date(row.original.requestedAt).toLocaleString()}</p>
          <p className="font-mono text-[10px] text-admin-fg-muted">{row.original.id}</p>
        </div>
      ),
    },
    {
      id: 'user',
      header: 'Customer',
      cell: ({ row }) => (
        <div>
          <p className="text-sm text-admin-fg-default">{row.original.userName}</p>
          <p className="font-mono text-[10px] text-admin-fg-muted">{row.original.userId}</p>
        </div>
      ),
    },
    {
      id: 'invoice',
      header: 'Invoice',
      cell: ({ row }) => (
        <span className="font-mono text-xs text-admin-fg-muted">{row.original.invoiceId}</span>
      ),
    },
    {
      id: 'amount',
      header: 'Amount',
      cell: ({ row }) => (
        <span className="font-semibold text-admin-fg-default">
          {formatMoney(row.original.amount, { currency: row.original.currency })}
        </span>
      ),
    },
    {
      id: 'reason',
      header: 'Reason',
      cell: ({ row }) => (
        <p className="line-clamp-2 max-w-sm text-sm text-admin-fg-muted">{row.original.reason}</p>
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
      cell: ({ row }) => {
        const refund = row.original;
        const disabled = !canWrite || busyId === refund.id;
        if (refund.status === 'issued') {
          return <span className="text-xs text-emerald-700">Issued</span>;
        }
        return (
          <div className="flex flex-wrap justify-end gap-1.5">
            {refund.status === 'pending' ? (
              <>
                <Button size="sm" variant="outline" disabled={disabled} onClick={() => void act(refund, 'approve')}>
                  <Check className="mr-1 h-3.5 w-3.5" /> Approve
                </Button>
                <Button size="sm" variant="outline" disabled={disabled} onClick={() => void act(refund, 'deny')}>
                  <X className="mr-1 h-3.5 w-3.5" /> Deny
                </Button>
              </>
            ) : null}
            {refund.status === 'approved' ? (
              <Button size="sm" disabled={disabled} onClick={() => void act(refund, 'issue')}>
                Issue refund
              </Button>
            ) : null}
          </div>
        );
      },
    },
  ], [busyId, canWrite]);

  if (!user) return null;
  if (!canRead) return <NoBillingPermission />;

  return (
    <AdminTableLayout
      title="Refunds"
      description="Triage refund requests from learners. Approve and issue payouts via Stripe."
      icon={<RotateCcw aria-hidden className="h-5 w-5" />}
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Billing', href: '/admin/billing' },
        { label: 'Refunds' },
      ]}
      actions={
        <Button variant="secondary" onClick={() => void load()} startIcon={<RefreshCw className="h-4 w-4" />}>
          Refresh
        </Button>
      }
      banner={
        <Card>
          <CardContent className="space-y-3 pt-6">
            {!serviceAvailable && !loading && refunds.length === 0 ? (
              <InlineAlert variant="info" title="Refunds service">
                Refund triage data is not available yet. Wave B4 will publish the
                <code> POST /v1/admin/refunds</code> endpoints. Existing requests still flow
                through the legacy billing operations page.
              </InlineAlert>
            ) : null}
            {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="status">Status</Label>
                <Select value={status} onValueChange={setStatus}>
                  <SelectTrigger id="status">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="__all">All</SelectItem>
                    <SelectItem value="pending">Pending</SelectItem>
                    <SelectItem value="approved">Approved</SelectItem>
                    <SelectItem value="issued">Issued</SelectItem>
                    <SelectItem value="denied">Denied</SelectItem>
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
        data={refunds}
        loading={loading}
        emptyMessage="No refund requests match the current filters."
        searchPlaceholder="Search refunds..."
      />
    </AdminTableLayout>
  );
}
