'use client';

import { useCallback, useEffect, useState } from 'react';
import { Check, X, RefreshCw } from 'lucide-react';
import { AdminRouteWorkspace, AdminRoutePanel, AdminRouteSectionHeader } from '@/components/domain/admin-route-surface';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Button } from '@/components/ui/button';
import { Select, Textarea } from '@/components/ui/form-controls';
import { Badge } from '@/components/ui/badge';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';
import {
  listAdminManualPayments,
  approveManualPayment,
  rejectManualPayment,
  type ManualPaymentDto,
} from '@/lib/api';

const STATUS_FILTERS = [
  { value: '', label: 'all' },
  { value: 'pending', label: 'pending' },
  { value: 'under_review', label: 'under_review' },
  { value: 'approved', label: 'approved' },
  { value: 'rejected', label: 'rejected' },
  { value: 'cancelled', label: 'cancelled' },
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
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [decisionFor, setDecisionFor] = useState<{ row: ManualPaymentDto; intent: 'approve' | 'reject' } | null>(null);
  const [notes, setNotes] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows(await listAdminManualPayments(statusFilter || undefined));
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
        setToast({ variant: 'success', message: 'Approved.' });
      } else {
        await rejectManualPayment(decisionFor.row.id, notes || 'Rejected.');
        setToast({ variant: 'success', message: 'Rejected.' });
      }
      setDecisionFor(null);
      setNotes('');
      await load();
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.userMessage ?? err?.message ?? 'Action failed.' });
    }
  }

  const columns: Column<ManualPaymentDto>[] = [
    { key: 'submitted', header: 'Submitted', render: (r) => new Date(r.submittedAt).toLocaleString() },
    { key: 'user', header: 'User', render: (r) => r.userId },
    { key: 'amount', header: 'Amount', render: (r) => `${r.amountAmount.toFixed(2)} ${r.currency}` },
    { key: 'method', header: 'Method', render: (r) => r.method.replace('_', ' ') },
    { key: 'reference', header: 'Reference', render: (r) => r.reference || '—' },
    { key: 'status', header: 'Status', render: (r) => <Badge variant={statusVariant(r.status)}>{r.status}</Badge> },
    {
      key: 'actions',
      header: '',
      render: (r) => r.status === 'pending' || r.status === 'under_review' ? (
        <div className="flex gap-1">
          <Button variant="ghost" size="sm" onClick={() => { setDecisionFor({ row: r, intent: 'approve' }); setNotes(''); }} aria-label="Approve">
            <Check className="h-4 w-4 text-emerald-600" />
          </Button>
          <Button variant="ghost" size="sm" onClick={() => { setDecisionFor({ row: r, intent: 'reject' }); setNotes(''); }} aria-label="Reject">
            <X className="h-4 w-4 text-rose-600" />
          </Button>
        </div>
      ) : null,
    },
  ];

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader title="Manual payments" description="Bank transfer / Wise / Fawry voucher claims awaiting verification." />

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <AdminRoutePanel>
        {error && <InlineAlert variant="error">{error}</InlineAlert>}
        <div className="mb-4 flex items-center gap-3">
          <Select label="Filter" value={statusFilter} options={STATUS_FILTERS} onChange={(e) => setStatusFilter(e.target.value)} />
          <Button variant="ghost" onClick={() => void load()} aria-label="Refresh">
            <RefreshCw className="h-4 w-4" />
          </Button>
        </div>
        {loading ? (
          <p className="text-sm text-muted-foreground">Loading…</p>
        ) : (
          <DataTable
            data={rows}
            columns={columns}
            keyExtractor={(r) => r.id}
            emptyMessage="No manual payments match this filter."
          />
        )}
      </AdminRoutePanel>

      {decisionFor && (
        <Modal open onClose={() => setDecisionFor(null)} title={decisionFor.intent === 'approve' ? 'Approve payment' : 'Reject payment'}>
          <div className="space-y-3 p-4">
            <p className="text-sm">
              {decisionFor.intent === 'approve'
                ? 'This will grant access to the learner. Confirm the proof and amount match before approving.'
                : 'This will close the request. The learner is notified with your reason.'}
            </p>
            <Textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder={decisionFor.intent === 'approve' ? 'Optional notes' : 'Reason for rejection (required)'}
            />
            <div className="flex justify-end gap-2">
              <Button variant="ghost" onClick={() => setDecisionFor(null)}>Cancel</Button>
              <Button variant={decisionFor.intent === 'approve' ? 'primary' : 'destructive'} onClick={handleDecide}>
                {decisionFor.intent === 'approve' ? 'Approve' : 'Reject'}
              </Button>
            </div>
          </div>
        </Modal>
      )}
    </AdminRouteWorkspace>
  );
}
