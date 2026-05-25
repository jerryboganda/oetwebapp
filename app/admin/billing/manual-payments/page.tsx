'use client';

import { useCallback, useEffect, useState } from 'react';
import type { ColumnDef } from '@tanstack/react-table';
import { Check, RefreshCw, X } from 'lucide-react';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { DataTable } from '@/components/admin/ui/data-table';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/admin/ui/dialog';
import { Label } from '@/components/admin/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/admin/ui/select';
import { Textarea } from '@/components/admin/ui/textarea';
import { toast } from '@/components/admin/ui/toaster';
import { InlineAlert } from '@/components/ui/alert';
import {
  listAdminManualPayments,
  approveManualPayment,
  rejectManualPayment,
  type ManualPaymentDto,
} from '@/lib/api';

const STATUS_FILTERS = [
  { value: '__all', label: 'All' },
  { value: 'pending', label: 'Pending' },
  { value: 'under_review', label: 'Under review' },
  { value: 'approved', label: 'Approved' },
  { value: 'rejected', label: 'Rejected' },
  { value: 'cancelled', label: 'Cancelled' },
];

function statusVariant(status: string) {
  if (status === 'approved') return 'success' as const;
  if (status === 'rejected') return 'danger' as const;
  return 'default' as const;
}

export default function AdminManualPaymentsPage() {
  const [statusFilter, setStatusFilter] = useState('pending');
  const [rows, setRows] = useState<ManualPaymentDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [decisionFor, setDecisionFor] = useState<{ row: ManualPaymentDto; intent: 'approve' | 'reject' } | null>(null);
  const [notes, setNotes] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const filter = statusFilter === '__all' ? undefined : statusFilter;
      setRows(await listAdminManualPayments(filter));
      setError(null);
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to load.');
    } finally {
      setLoading(false);
    }
  }, [statusFilter]);

  useEffect(() => { void load(); }, [load]);

  async function handleDecide() {
    if (!decisionFor) return;
    try {
      if (decisionFor.intent === 'approve') {
        await approveManualPayment(decisionFor.row.id, notes || undefined);
        toast.success('Approved.');
      } else {
        await rejectManualPayment(decisionFor.row.id, notes || 'Rejected.');
        toast.success('Rejected.');
      }
      setDecisionFor(null);
      setNotes('');
      await load();
    } catch (err: any) {
      toast.error(err?.userMessage ?? err?.message ?? 'Action failed.');
    }
  }

  const columns: ColumnDef<ManualPaymentDto>[] = [
    {
      id: 'submitted',
      header: 'Submitted',
      cell: ({ row }) => new Date(row.original.submittedAt).toLocaleString(),
    },
    { id: 'user', accessorKey: 'userId', header: 'User' },
    {
      id: 'amount',
      header: 'Amount',
      cell: ({ row }) => `${row.original.amountAmount.toFixed(2)} ${row.original.currency}`,
    },
    {
      id: 'method',
      header: 'Method',
      cell: ({ row }) => row.original.method.replace('_', ' '),
    },
    {
      id: 'reference',
      header: 'Reference',
      cell: ({ row }) => row.original.reference || '—',
    },
    {
      id: 'status',
      header: 'Status',
      cell: ({ row }) => <Badge variant={statusVariant(row.original.status)}>{row.original.status}</Badge>,
    },
    {
      id: 'actions',
      header: '',
      enableSorting: false,
      enableHiding: false,
      cell: ({ row }) => {
        const r = row.original;
        return r.status === 'pending' || r.status === 'under_review' ? (
          <div className="flex gap-1">
            <Button
              variant="ghost"
              size="sm"
              onClick={() => {
                setDecisionFor({ row: r, intent: 'approve' });
                setNotes('');
              }}
              aria-label="Approve"
            >
              <Check className="h-4 w-4 text-[var(--admin-success)]" />
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => {
                setDecisionFor({ row: r, intent: 'reject' });
                setNotes('');
              }}
              aria-label="Reject"
            >
              <X className="h-4 w-4 text-[var(--admin-danger)]" />
            </Button>
          </div>
        ) : null;
      },
    },
  ];

  return (
    <AdminTableLayout
      title="Manual payments"
      description="Bank transfer / Wise / Fawry voucher claims awaiting verification."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Billing', href: '/admin/billing' },
        { label: 'Manual payments' },
      ]}
      actions={
        <Button variant="ghost" onClick={() => void load()} startIcon={<RefreshCw className="h-4 w-4" />}>
          Refresh
        </Button>
      }
      banner={
        <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:gap-4">
          {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="payment-status" className="text-xs text-admin-fg-muted">
              Status
            </Label>
            <Select value={statusFilter} onValueChange={setStatusFilter}>
              <SelectTrigger id="payment-status" className="w-[200px]">
                <SelectValue placeholder="Status" />
              </SelectTrigger>
              <SelectContent>
                {STATUS_FILTERS.map((opt) => (
                  <SelectItem key={opt.value} value={opt.value}>
                    {opt.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        </div>
      }
    >
      <DataTable
        columns={columns}
        data={rows}
        loading={loading}
        emptyMessage="No manual payments match this filter."
        searchPlaceholder="Search payments…"
      />

      <Dialog open={decisionFor !== null} onOpenChange={(open) => !open && setDecisionFor(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{decisionFor?.intent === 'approve' ? 'Approve payment' : 'Reject payment'}</DialogTitle>
            <DialogDescription>
              {decisionFor?.intent === 'approve'
                ? 'This will grant access to the learner. Confirm the proof and amount match before approving.'
                : 'This will close the request. The learner is notified with your reason.'}
            </DialogDescription>
          </DialogHeader>
          <Textarea
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            placeholder={decisionFor?.intent === 'approve' ? 'Optional notes' : 'Reason for rejection (required)'}
            rows={3}
          />
          <DialogFooter>
            <Button variant="ghost" onClick={() => setDecisionFor(null)}>
              Cancel
            </Button>
            <Button
              variant={decisionFor?.intent === 'approve' ? 'primary' : 'destructive'}
              onClick={handleDecide}
            >
              {decisionFor?.intent === 'approve' ? 'Approve' : 'Reject'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </AdminTableLayout>
  );
}
