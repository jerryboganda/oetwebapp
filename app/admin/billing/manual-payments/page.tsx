'use client';

import { useCallback, useEffect, useState } from 'react';
import type { ColumnDef } from '@tanstack/react-table';
import { AlertTriangle, Check, Clock, Eye, RefreshCw, X } from 'lucide-react';

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
  setManualPaymentStatus,
  getManualPaymentProofBlob,
  type ManualPaymentDto,
} from '@/lib/api';

const STATUS_FILTERS = [
  { value: '__all', label: 'All' },
  { value: 'pending', label: 'Pending' },
  { value: 'needs_review', label: 'Needs review' },
  { value: 'approved', label: 'Approved' },
  { value: 'paid', label: 'Paid' },
  { value: 'rejected', label: 'Rejected' },
  { value: 'cancelled', label: 'Cancelled' },
];

function statusVariant(status: string) {
  if (status === 'approved' || status === 'paid') return 'success' as const;
  if (status === 'rejected') return 'danger' as const;
  if (status === 'needs_review') return 'warning' as const;
  return 'default' as const;
}

interface ProofView {
  rowId: string;
  candidate: string;
  url: string;
  type: string;
}

export default function AdminManualPaymentsPage() {
  const [statusFilter, setStatusFilter] = useState('pending');
  const [rows, setRows] = useState<ManualPaymentDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [decisionFor, setDecisionFor] = useState<{ row: ManualPaymentDto; intent: 'approve' | 'reject' } | null>(null);
  const [notes, setNotes] = useState('');
  const [proofLoadingId, setProofLoadingId] = useState<string | null>(null);
  const [proofView, setProofView] = useState<ProofView | null>(null);

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

  // Revoke the object URL when the proof dialog unmounts.
  useEffect(() => {
    return () => {
      if (proofView) URL.revokeObjectURL(proofView.url);
    };
  }, [proofView]);

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

  async function handleSetStatus(row: ManualPaymentDto, status: 'pending' | 'needs_review') {
    try {
      await setManualPaymentStatus(row.id, status);
      toast.success(status === 'needs_review' ? 'Flagged as needs review.' : 'Moved back to pending.');
      await load();
    } catch (err: any) {
      toast.error(err?.userMessage ?? err?.message ?? 'Action failed.');
    }
  }

  async function handleViewProof(row: ManualPaymentDto) {
    setProofLoadingId(row.id);
    try {
      const blob = await getManualPaymentProofBlob(row.id);
      const url = URL.createObjectURL(blob);
      setProofView({ rowId: row.id, candidate: row.candidateFullName || row.userId, url, type: blob.type });
    } catch (err: any) {
      toast.error(err?.userMessage ?? err?.message ?? 'Could not load proof.');
    } finally {
      setProofLoadingId(null);
    }
  }

  function closeProof() {
    setProofView((current) => {
      if (current) URL.revokeObjectURL(current.url);
      return null;
    });
  }

  const columns: ColumnDef<ManualPaymentDto>[] = [
    {
      id: 'submitted',
      header: 'Submitted',
      cell: ({ row }) => new Date(row.original.submittedAt).toLocaleString(),
    },
    { id: 'user', accessorKey: 'userId', header: 'User' },
    {
      id: 'candidate',
      header: 'Candidate',
      cell: ({ row }) => (
        <div>
          <p className="font-medium text-admin-fg-strong">{row.original.candidateFullName || '-'}</p>
          <p className="text-xs text-admin-fg-muted">{row.original.candidateEmail || '-'}</p>
          <p className="text-xs text-admin-fg-muted">{row.original.candidateWhatsApp || '-'}</p>
        </div>
      ),
    },
    {
      id: 'course',
      header: 'Course',
      cell: ({ row }) => (
        <div>
          <p className="font-medium text-admin-fg-strong">{row.original.courseName || '-'}</p>
          <p className="text-xs text-admin-fg-muted">{row.original.paymentCategory?.replace('_', ' ') || '-'}</p>
        </div>
      ),
    },
    {
      id: 'amount',
      header: 'Amount',
      cell: ({ row }) => `${row.original.amountAmount.toFixed(2)} ${row.original.currency}`,
    },
    {
      id: 'method',
      header: 'Method',
      cell: ({ row }) => row.original.method.replace(/_/g, ' '),
    },
    {
      id: 'reference',
      header: 'Reference',
      cell: ({ row }) => row.original.reference || '-',
    },
    {
      id: 'proof',
      header: 'Proof',
      cell: ({ row }) =>
        row.original.proofUrl ? (
          <Button
            variant="ghost"
            size="sm"
            onClick={() => void handleViewProof(row.original)}
            disabled={proofLoadingId === row.original.id}
            startIcon={<Eye className="h-4 w-4" />}
          >
            {proofLoadingId === row.original.id ? 'Loading…' : 'View'}
          </Button>
        ) : (
          '-'
        ),
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
        if (r.status !== 'pending' && r.status !== 'needs_review') return null;
        return (
          <div className="flex gap-1">
            <Button
              variant="ghost"
              size="sm"
              onClick={() => {
                setDecisionFor({ row: r, intent: 'approve' });
                setNotes('');
              }}
              aria-label="Approve"
              title="Approve & activate access"
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
              title="Reject"
            >
              <X className="h-4 w-4 text-[var(--admin-danger)]" />
            </Button>
            {r.status === 'pending' ? (
              <Button
                variant="ghost"
                size="sm"
                onClick={() => void handleSetStatus(r, 'needs_review')}
                aria-label="Needs review"
                title="Flag as needs review"
              >
                <AlertTriangle className="h-4 w-4 text-[var(--admin-warning)]" />
              </Button>
            ) : (
              <Button
                variant="ghost"
                size="sm"
                onClick={() => void handleSetStatus(r, 'pending')}
                aria-label="Mark pending"
                title="Move back to pending"
              >
                <Clock className="h-4 w-4 text-admin-fg-muted" />
              </Button>
            )}
          </div>
        );
      },
    },
  ];

  return (
    <AdminTableLayout
      title="Manual payments"
      description="Manual payment claims awaiting proof, identity, and course verification."
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

      <Dialog open={proofView !== null} onOpenChange={(open) => !open && closeProof()}>
        <DialogContent className="max-w-2xl">
          <DialogHeader>
            <DialogTitle>Payment proof</DialogTitle>
            <DialogDescription>{proofView?.candidate}</DialogDescription>
          </DialogHeader>
          {proofView ? (
            proofView.type === 'application/pdf' ? (
              <div className="space-y-2">
                <iframe title="Payment proof" src={proofView.url} className="h-[60vh] w-full rounded-md border border-admin-border" />
                <a href={proofView.url} target="_blank" rel="noreferrer" className="text-sm font-medium text-admin-accent hover:underline">
                  Open in new tab
                </a>
              </div>
            ) : proofView.type.startsWith('image/') ? (
              // eslint-disable-next-line @next/next/no-img-element
              <img src={proofView.url} alt="Payment proof" className="max-h-[60vh] w-full rounded-md border border-admin-border object-contain" />
            ) : (
              <a href={proofView.url} download className="text-sm font-medium text-admin-accent hover:underline">
                Download proof file
              </a>
            )
          ) : null}
          <DialogFooter>
            <Button variant="ghost" onClick={closeProof}>
              Close
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </AdminTableLayout>
  );
}
