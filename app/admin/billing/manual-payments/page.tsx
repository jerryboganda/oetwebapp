'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import type { ColumnDef } from '@tanstack/react-table';
import {
  AlertTriangle,
  Check,
  ChevronLeft,
  ChevronRight,
  Clock,
  Eye,
  FileX2,
  PackageCheck,
  RefreshCw,
  RotateCcw,
  ShieldOff,
  X,
} from 'lucide-react';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Badge, type BadgeProps } from '@/components/admin/ui/badge';
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
import { cn } from '@/lib/utils';
import {
  listAdminManualPayments,
  approveManualPayment,
  rejectManualPayment,
  setManualPaymentStatus,
  waiveManualPaymentProof,
  reopenManualPayment,
  getManualPaymentProofBlob,
  listPendingFulfilment,
  markSubscriptionFulfilled,
  type ManualPaymentDto,
  type PendingFulfilmentDto,
} from '@/lib/api';

const PAGE_SIZE = 50;

/**
 * The stored statuses are unchanged (no data migration); the owner-facing vocabulary is
 * Pending / Verified / Rejected. Every option therefore SHOWS the spec word and SENDS the
 * internal value. "Pending" covers two internal states, so it is offered as two options
 * rather than silently hiding whichever one the admin didn't pick.
 */
const STATUS_FILTERS = [
  { value: '__all', label: 'All' },
  { value: 'pending', label: 'Pending' },
  { value: 'needs_review', label: 'Pending — needs review' },
  { value: 'approved', label: 'Verified — approved' },
  { value: 'paid', label: 'Verified — paid' },
  { value: 'rejected', label: 'Rejected' },
  { value: 'cancelled', label: 'Cancelled' },
];

const KIND_FILTERS = [
  { value: '__all', label: 'All kinds' },
  { value: 'learner_upload', label: 'Learner upload' },
  { value: 'gateway_receipt', label: 'Gateway receipt' },
];

const STATUS_DISPLAY: Record<string, { label: string; variant: BadgeProps['variant']; note?: string }> = {
  pending: { label: 'Pending', variant: 'warning' },
  needs_review: { label: 'Pending', variant: 'warning', note: 'needs review' },
  approved: { label: 'Verified', variant: 'success' },
  paid: { label: 'Verified', variant: 'success', note: 'gateway paid' },
  rejected: { label: 'Rejected', variant: 'danger' },
  cancelled: { label: 'Cancelled', variant: 'muted' },
};

const DELIVERY_LABELS: Record<string, string> = {
  automatic_web: 'Automatic (web)',
  manual_web: 'Manual release (web)',
  telegram: 'Telegram invite',
  manual_material: 'Manual material',
};

function isOpen(status: string) {
  return status === 'pending' || status === 'needs_review';
}

function titleise(value: string) {
  return value.replace(/_/g, ' ');
}

interface ProofView {
  candidate: string;
  url: string;
  type: string;
}

type Tab = 'proofs' | 'fulfilment';

type Decision =
  | { kind: 'approve' | 'reject'; row: ManualPaymentDto }
  | { kind: 'waive'; row: ManualPaymentDto }
  | { kind: 'fulfil'; row: PendingFulfilmentDto };

export default function AdminPaymentProofsPage() {
  const [tab, setTab] = useState<Tab>('proofs');

  const [statusFilter, setStatusFilter] = useState('pending');
  const [kindFilter, setKindFilter] = useState('__all');
  const [page, setPage] = useState(1);
  const [rows, setRows] = useState<ManualPaymentDto[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [fulfilment, setFulfilment] = useState<PendingFulfilmentDto[]>([]);
  const [fulfilmentLoading, setFulfilmentLoading] = useState(true);

  const [decision, setDecision] = useState<Decision | null>(null);
  const [notes, setNotes] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [proofLoadingId, setProofLoadingId] = useState<string | null>(null);
  const [proofView, setProofView] = useState<ProofView | null>(null);

  const loadProofs = useCallback(async () => {
    setLoading(true);
    try {
      const response = await listAdminManualPayments({
        status: statusFilter === '__all' ? undefined : statusFilter,
        kind: kindFilter === '__all' ? undefined : kindFilter,
        page,
        pageSize: PAGE_SIZE,
      });
      setRows(response.items);
      setTotal(response.total);
      setError(null);
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to load.');
    } finally {
      setLoading(false);
    }
  }, [statusFilter, kindFilter, page]);

  const loadFulfilment = useCallback(async () => {
    setFulfilmentLoading(true);
    try {
      setFulfilment(await listPendingFulfilment());
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to load the fulfilment queue.');
    } finally {
      setFulfilmentLoading(false);
    }
  }, []);

  useEffect(() => { void loadProofs(); }, [loadProofs]);
  useEffect(() => { void loadFulfilment(); }, [loadFulfilment]);

  // Revoke the object URL when the proof dialog unmounts.
  useEffect(() => {
    return () => {
      if (proofView) URL.revokeObjectURL(proofView.url);
    };
  }, [proofView]);

  function changeStatusFilter(value: string) {
    setStatusFilter(value);
    setPage(1);
  }

  function changeKindFilter(value: string) {
    setKindFilter(value);
    setPage(1);
  }

  async function handleSetStatus(row: ManualPaymentDto, status: 'pending' | 'needs_review') {
    try {
      await setManualPaymentStatus(row.id, status);
      toast.success(status === 'needs_review' ? 'Flagged as needs review.' : 'Moved back to pending.');
      await loadProofs();
    } catch (err: any) {
      toast.error(err?.userMessage ?? err?.message ?? 'Action failed.');
    }
  }

  async function handleReopen(row: ManualPaymentDto) {
    try {
      await reopenManualPayment(row.id);
      toast.success('Reopened — the proof is pending again.');
      await loadProofs();
    } catch (err: any) {
      toast.error(err?.userMessage ?? err?.message ?? 'Action failed.');
    }
  }

  async function handleConfirmDecision() {
    if (!decision) return;
    if (decision.kind === 'waive' && !notes.trim()) {
      toast.error('A reason is required to waive the proof requirement.');
      return;
    }
    setSubmitting(true);
    try {
      switch (decision.kind) {
        case 'approve':
          await approveManualPayment(decision.row.id, notes || undefined);
          toast.success('Approved.');
          await loadProofs();
          break;
        case 'reject':
          await rejectManualPayment(decision.row.id, notes || 'Rejected.');
          toast.success('Rejected.');
          await loadProofs();
          break;
        case 'waive':
          await waiveManualPaymentProof(decision.row.id, notes.trim());
          toast.success('Proof requirement waived.');
          await loadProofs();
          break;
        case 'fulfil':
          await markSubscriptionFulfilled(decision.row.subscriptionId, notes || undefined);
          toast.success('Marked fulfilled — access released.');
          await loadFulfilment();
          break;
      }
      setDecision(null);
      setNotes('');
    } catch (err: any) {
      toast.error(err?.userMessage ?? err?.message ?? 'Action failed.');
    } finally {
      setSubmitting(false);
    }
  }

  async function handleViewProof(row: ManualPaymentDto) {
    setProofLoadingId(row.id);
    try {
      const blob = await getManualPaymentProofBlob(row.id);
      const url = URL.createObjectURL(blob);
      setProofView({ candidate: row.candidateFullName || row.userId, url, type: blob.type });
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

  /**
   * A gateway receipt has no file by design — its evidence is the gateway reference, so
   * render that instead of a View button that would open an empty viewer.
   */
  function renderProofCell(row: ManualPaymentDto) {
    if (row.kind === 'gateway_receipt') {
      return (
        <div className="text-xs">
          <p className="font-medium text-admin-fg-default">System receipt</p>
          <p className="text-admin-fg-muted">{row.reference || 'no reference'}</p>
        </div>
      );
    }
    if (row.hasProof) {
      return (
        <Button
          variant="ghost"
          size="sm"
          onClick={() => void handleViewProof(row)}
          disabled={proofLoadingId === row.id}
          startIcon={<Eye className="h-4 w-4" />}
        >
          {proofLoadingId === row.id ? 'Loading…' : 'View'}
        </Button>
      );
    }
    if (row.proofWaivedAt) {
      return <span className="text-xs text-admin-fg-muted">Waived — no file</span>;
    }
    return (
      <span className="inline-flex items-center gap-1 text-xs text-admin-fg-muted">
        <FileX2 className="h-3.5 w-3.5" /> None
      </span>
    );
  }

  const proofColumns: ColumnDef<ManualPaymentDto>[] = [
    {
      id: 'submitted',
      header: 'Submitted',
      cell: ({ row }) => new Date(row.original.submittedAt).toLocaleString(),
    },
    {
      id: 'kind',
      header: 'Kind',
      cell: ({ row }) =>
        row.original.kind === 'gateway_receipt' ? (
          <Badge variant="info">Gateway receipt</Badge>
        ) : (
          <Badge variant="outline">Learner upload</Badge>
        ),
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
          <p className="text-xs text-admin-fg-muted">{titleise(row.original.paymentCategory || '-')}</p>
        </div>
      ),
    },
    {
      id: 'profession',
      header: 'Profession',
      cell: ({ row }) =>
        row.original.professionId ? (
          <span className="text-sm">{titleise(row.original.professionId)}</span>
        ) : (
          <span className="text-xs text-admin-fg-muted">-</span>
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
      cell: ({ row }) => titleise(row.original.method),
    },
    {
      id: 'gateway',
      header: 'Gateway',
      cell: ({ row }) =>
        row.original.gateway ? (
          <span className="text-sm">{titleise(row.original.gateway)}</span>
        ) : (
          <span className="text-xs text-admin-fg-muted">-</span>
        ),
    },
    {
      id: 'reference',
      header: 'Reference',
      cell: ({ row }) => row.original.reference || '-',
    },
    {
      id: 'proof',
      header: 'Proof',
      cell: ({ row }) => renderProofCell(row.original),
    },
    {
      id: 'waived',
      header: 'Waived',
      cell: ({ row }) =>
        row.original.proofWaivedAt ? (
          <div>
            <Badge variant="warning" startIcon={<ShieldOff className="h-3 w-3" />}>
              Waived
            </Badge>
            <p className="mt-1 max-w-[16rem] text-xs text-admin-fg-muted">
              {row.original.proofWaiverReason || 'No reason recorded.'}
            </p>
          </div>
        ) : (
          <span className="text-xs text-admin-fg-muted">-</span>
        ),
    },
    {
      id: 'status',
      header: 'Status',
      cell: ({ row }) => {
        const display = STATUS_DISPLAY[row.original.status] ?? {
          label: row.original.status,
          variant: 'default' as const,
          note: undefined,
        };
        return (
          <div>
            <Badge variant={display.variant}>{display.label}</Badge>
            {display.note ? <p className="mt-1 text-xs text-admin-fg-muted">{display.note}</p> : null}
          </div>
        );
      },
    },
    {
      id: 'actions',
      header: '',
      enableSorting: false,
      enableHiding: false,
      cell: ({ row }) => {
        const r = row.original;
        if (r.status === 'rejected') {
          return (
            <Button
              variant="ghost"
              size="sm"
              onClick={() => void handleReopen(r)}
              startIcon={<RotateCcw className="h-4 w-4" />}
              title="Reopen — move back to pending"
            >
              Reopen
            </Button>
          );
        }
        if (!isOpen(r.status)) return null;
        return (
          <div className="flex gap-1">
            <Button
              variant="ghost"
              size="sm"
              onClick={() => {
                setDecision({ kind: 'approve', row: r });
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
                setDecision({ kind: 'reject', row: r });
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
            {r.kind === 'learner_upload' && !r.hasProof && !r.proofWaivedAt ? (
              <Button
                variant="ghost"
                size="sm"
                onClick={() => {
                  setDecision({ kind: 'waive', row: r });
                  setNotes('');
                }}
                aria-label="Waive proof"
                title="Waive the upload requirement"
              >
                <ShieldOff className="h-4 w-4 text-admin-fg-muted" />
              </Button>
            ) : null}
          </div>
        );
      },
    },
  ];

  const fulfilmentColumns: ColumnDef<PendingFulfilmentDto>[] = [
    {
      id: 'waiting',
      header: 'Waiting since',
      cell: ({ row }) => new Date(row.original.changedAt).toLocaleString(),
    },
    {
      id: 'learner',
      header: 'Learner',
      cell: ({ row }) => (
        <div>
          <p className="font-medium text-admin-fg-strong">{row.original.displayName || '-'}</p>
          <p className="text-xs text-admin-fg-muted">{row.original.email || row.original.userId}</p>
        </div>
      ),
    },
    {
      id: 'plan',
      header: 'Plan',
      cell: ({ row }) => (
        <div>
          <p className="font-medium text-admin-fg-strong">{row.original.planName}</p>
          <p className="text-xs text-admin-fg-muted">{row.original.planCode}</p>
        </div>
      ),
    },
    {
      id: 'delivery',
      header: 'Delivery',
      cell: ({ row }) => (
        <Badge variant="secondary">
          {DELIVERY_LABELS[row.original.deliveryMethod] ?? titleise(row.original.deliveryMethod)}
        </Badge>
      ),
    },
    {
      id: 'handover',
      header: 'Hand-over',
      cell: ({ row }) => (
        <div className="max-w-[20rem] space-y-1">
          {row.original.telegramInviteUrl ? (
            <a
              href={row.original.telegramInviteUrl}
              target="_blank"
              rel="noreferrer"
              className="block truncate text-sm font-medium text-admin-accent hover:underline"
            >
              {row.original.telegramInviteUrl}
            </a>
          ) : null}
          {row.original.deliveryInstructions ? (
            <p className="text-xs text-admin-fg-muted">{row.original.deliveryInstructions}</p>
          ) : null}
          {!row.original.telegramInviteUrl && !row.original.deliveryInstructions ? (
            <span className="text-xs text-admin-fg-muted">No invite or instructions on this plan.</span>
          ) : null}
        </div>
      ),
    },
    {
      id: 'proof',
      header: 'Proof',
      cell: ({ row }) =>
        row.original.proof ? (
          renderProofCell(row.original.proof)
        ) : (
          <span className="text-xs text-admin-fg-muted">No proof row</span>
        ),
    },
    {
      id: 'status',
      header: 'Status',
      cell: ({ row }) => (
        <div>
          <Badge variant="warning">Pending fulfilment</Badge>
          <p className="mt-1 text-xs text-admin-fg-muted">Subscription {row.original.status}</p>
        </div>
      ),
    },
    {
      id: 'actions',
      header: '',
      enableSorting: false,
      enableHiding: false,
      cell: ({ row }) => (
        <Button
          variant="secondary"
          size="sm"
          onClick={() => {
            setDecision({ kind: 'fulfil', row: row.original });
            setNotes('');
          }}
          startIcon={<PackageCheck className="h-4 w-4" />}
        >
          Mark fulfilled
        </Button>
      ),
    },
  ];

  const pageCount = Math.max(1, Math.ceil(total / PAGE_SIZE));
  const firstOnPage = total === 0 ? 0 : (page - 1) * PAGE_SIZE + 1;
  const lastOnPage = Math.min(page * PAGE_SIZE, total);

  const dialogCopy = useMemo(() => {
    switch (decision?.kind) {
      case 'approve':
        return {
          title: 'Approve payment',
          description: 'This will grant access to the learner. Confirm the proof and amount match before approving.',
          placeholder: 'Optional notes',
          confirm: 'Approve',
          variant: 'primary' as const,
        };
      case 'reject':
        return {
          title: 'Reject payment',
          description: 'This will close the request. The learner is notified with your reason. You can reopen it afterwards if this was a mis-click.',
          placeholder: 'Reason for rejection (required)',
          confirm: 'Reject',
          variant: 'destructive' as const,
        };
      case 'waive':
        return {
          title: 'Waive the proof requirement',
          description: 'Use this only when you have confirmed the payment out-of-band. The waiver, your name, and the reason are recorded against the order.',
          placeholder: 'Why is the upload being waived? (required)',
          confirm: 'Waive proof',
          variant: 'primary' as const,
        };
      case 'fulfil':
        return {
          title: 'Mark fulfilled',
          description: 'This releases access and reveals the delivery details to the learner. Hand the package over first, then mark it here.',
          placeholder: 'Optional notes (e.g. invite sent 12:04)',
          confirm: 'Mark fulfilled',
          variant: 'primary' as const,
        };
      default:
        return null;
    }
  }, [decision]);

  const tabs: { value: Tab; label: string; count?: number }[] = [
    { value: 'proofs', label: 'Payment proofs' },
    { value: 'fulfilment', label: 'Pending fulfilment', count: fulfilment.length },
  ];

  return (
    <AdminTableLayout
      title="Payment proofs"
      description="Every order carries a proof of payment — a learner upload for offline methods, or a system receipt for card gateways. Manual and Telegram orders wait here for hand-over."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Billing', href: '/admin/billing' },
        { label: 'Payment proofs' },
      ]}
      actions={
        <Button
          variant="ghost"
          onClick={() => void (tab === 'proofs' ? loadProofs() : loadFulfilment())}
          startIcon={<RefreshCw className="h-4 w-4" />}
        >
          Refresh
        </Button>
      }
      banner={
        <div className="flex flex-col gap-3">
          {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
          <div
            role="tablist"
            aria-label="Payment proof views"
            className="flex w-fit gap-1 rounded-lg border border-admin-border bg-admin-bg-subtle p-1"
          >
            {tabs.map((t) => (
              <button
                key={t.value}
                type="button"
                role="tab"
                aria-selected={tab === t.value}
                onClick={() => setTab(t.value)}
                className={cn(
                  'inline-flex items-center gap-2 rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                  tab === t.value
                    ? 'bg-admin-bg-surface text-admin-fg-strong shadow-sm'
                    : 'text-admin-fg-muted hover:text-admin-fg-default',
                )}
              >
                {t.label}
                {t.count ? (
                  <Badge variant="warning" size="sm">
                    {t.count}
                  </Badge>
                ) : null}
              </button>
            ))}
          </div>

          {tab === 'proofs' ? (
            <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:gap-4">
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="payment-status" className="text-xs text-admin-fg-muted">
                  Status
                </Label>
                <Select value={statusFilter} onValueChange={changeStatusFilter}>
                  <SelectTrigger id="payment-status" className="w-[220px]">
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
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="payment-kind" className="text-xs text-admin-fg-muted">
                  Kind
                </Label>
                <Select value={kindFilter} onValueChange={changeKindFilter}>
                  <SelectTrigger id="payment-kind" className="w-[200px]">
                    <SelectValue placeholder="Kind" />
                  </SelectTrigger>
                  <SelectContent>
                    {KIND_FILTERS.map((opt) => (
                      <SelectItem key={opt.value} value={opt.value}>
                        {opt.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </div>
          ) : null}
        </div>
      }
      footer={
        tab === 'proofs' ? (
          <div className="flex items-center justify-between gap-4 text-sm text-admin-fg-muted">
            <span>
              {total === 0 ? 'No proofs match this filter.' : `Showing ${firstOnPage}–${lastOnPage} of ${total}`}
            </span>
            <div className="flex items-center gap-2">
              <Button
                variant="outline"
                size="sm"
                disabled={page <= 1 || loading}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                startIcon={<ChevronLeft className="h-4 w-4" />}
              >
                Previous
              </Button>
              <span className="tabular-nums">
                Page {page} of {pageCount}
              </span>
              <Button
                variant="outline"
                size="sm"
                disabled={page >= pageCount || loading}
                onClick={() => setPage((p) => p + 1)}
                endIcon={<ChevronRight className="h-4 w-4" />}
              >
                Next
              </Button>
            </div>
          </div>
        ) : null
      }
    >
      {tab === 'proofs' ? (
        <DataTable
          columns={proofColumns}
          data={rows}
          loading={loading}
          // The server owns paging — a second client-side pager over the current
          // page would silently hide the rest of the result set.
          enablePagination={false}
          emptyMessage="No payment proofs match this filter."
          searchPlaceholder="Search this page…"
        />
      ) : (
        <DataTable
          columns={fulfilmentColumns}
          data={fulfilment}
          loading={fulfilmentLoading}
          emptyMessage="Nothing is waiting for hand-over."
          searchPlaceholder="Search the queue…"
        />
      )}

      <Dialog
        open={decision !== null}
        onOpenChange={(open) => {
          if (!open) {
            setDecision(null);
            setNotes('');
          }
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{dialogCopy?.title}</DialogTitle>
            <DialogDescription>{dialogCopy?.description}</DialogDescription>
          </DialogHeader>
          <Textarea
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            placeholder={dialogCopy?.placeholder}
            rows={3}
          />
          <DialogFooter>
            <Button
              variant="ghost"
              onClick={() => {
                setDecision(null);
                setNotes('');
              }}
            >
              Cancel
            </Button>
            <Button
              variant={dialogCopy?.variant ?? 'primary'}
              onClick={handleConfirmDecision}
              disabled={submitting}
            >
              {submitting ? 'Working…' : dialogCopy?.confirm}
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
