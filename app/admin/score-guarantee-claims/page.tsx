'use client';

import { useEffect, useState } from 'react';
import { Shield, CheckCircle2, XCircle } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteSummaryCard, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { EmptyState } from '@/components/ui/empty-error';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { reviewScoreGuaranteeClaim } from '@/lib/api';
import { getAdminScoreGuaranteeClaimsData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminScoreGuaranteeClaim } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

const statusBadge: Record<string, { label: string; variant: 'default' | 'success' | 'destructive' | 'outline' }> = {
  active: { label: 'Active', variant: 'default' },
  claim_submitted: { label: 'Claim Submitted', variant: 'default' },
  claim_approved: { label: 'Approved', variant: 'success' },
  claim_rejected: { label: 'Rejected', variant: 'destructive' },
  expired: { label: 'Expired', variant: 'outline' },
};

export default function ScoreGuaranteeClaimsPage() {
  const { isAuthenticated } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [filters, setFilters] = useState<Record<string, string[]>>({ status: [] });
  const [claims, setClaims] = useState<AdminScoreGuaranteeClaim[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [reviewTarget, setReviewTarget] = useState<AdminScoreGuaranteeClaim | null>(null);
  const [reviewNote, setReviewNote] = useState('');
  const [isMutating, setIsMutating] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const selectedStatus = filters.status?.[0];

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setPageStatus('loading');
      try {
        const data = await getAdminScoreGuaranteeClaimsData({ status: selectedStatus, page, pageSize: 20 });
        if (cancelled) return;
        setClaims(data.items);
        setTotal(data.total);
        setPageStatus(data.items.length > 0 ? 'success' : 'empty');
      } catch {
        if (!cancelled) setPageStatus('error');
      }
    }
    load();
    return () => { cancelled = true; };
  }, [selectedStatus, page]);

  async function handleReview(decision: 'approve' | 'reject') {
    if (!reviewTarget) return;
    setIsMutating(true);
    try {
      await reviewScoreGuaranteeClaim(reviewTarget.id, decision, reviewNote || undefined);
      setClaims((prev) =>
        prev.map((c) =>
          c.id === reviewTarget.id ? { ...c, status: decision === 'approve' ? 'claim_approved' : 'claim_rejected' } : c,
        ),
      );
      setToast({ variant: 'success', message: `Claim ${decision}d.` });
      setReviewTarget(null);
      setReviewNote('');
    } catch {
      setToast({ variant: 'error', message: 'Failed to review claim.' });
    } finally {
      setIsMutating(false);
    }
  }

  const filterGroups: FilterGroup[] = [
    {
      label: 'Status',
      key: 'status',
      options: [
        { value: 'active', label: 'Active' },
        { value: 'claim_submitted', label: 'Claim Submitted' },
        { value: 'claim_approved', label: 'Approved' },
        { value: 'claim_rejected', label: 'Rejected' },
        { value: 'expired', label: 'Expired' },
      ],
    },
  ];

  const columns: Column<AdminScoreGuaranteeClaim>[] = [
    { key: 'userId', header: 'User', render: (r) => <span className="text-sm font-mono">{r.userId.slice(0, 12)}…</span> },
    { key: 'baselineScore', header: 'Baseline', render: (r) => <span className="text-sm">{r.baselineScore}</span> },
    { key: 'guaranteedImprovement', header: 'Target +', render: (r) => <span className="text-sm">+{r.guaranteedImprovement}</span> },
    { key: 'actualScore', header: 'Actual', render: (r) => <span className="text-sm">{r.actualScore ?? '—'}</span> },
    {
      key: 'status', header: 'Status', render: (r) => (
        <Badge variant={statusBadge[r.status]?.variant ?? 'default'}>{statusBadge[r.status]?.label ?? r.status}</Badge>
      ),
    },
    { key: 'activatedAt', header: 'Activated', render: (r) => <span className="text-sm text-gray-500">{new Date(r.activatedAt).toLocaleDateString()}</span> },
    {
      key: 'actions', header: '', render: (r) =>
        r.status === 'claim_submitted' ? (
          <Button size="sm" variant="outline" onClick={() => { setReviewTarget(r); setReviewNote(''); }}>Review</Button>
        ) : null,
    },
  ];

  return (
    <AdminRouteWorkspace>
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <AdminRouteSectionHeader
        title="Score Guarantee Claims"
        description="Review and process score guarantee refund claims."
      />

      <div className="flex items-center gap-4 mb-4">
        <AdminRouteSummaryCard label="Total Claims" value={total} />
        <AdminRouteSummaryCard label="Pending Review" value={claims.filter((c) => c.status === 'claim_submitted').length} />
      </div>

      <FilterBar groups={filterGroups} values={filters} onChange={setFilters} />

      <AsyncStateWrapper
        status={pageStatus}
        loadingMessage="Loading claims…"
        errorMessage="Unable to load score guarantee claims."
        emptySlot={
          <EmptyState
            icon={<Shield className="w-12 h-12 text-gray-400" />}
            title="No claims found"
            description="No score guarantee claims match the current filters."
          />
        }
      >
        <AdminRoutePanel>
          <DataTable
            data={claims}
            columns={columns}
            keyExtractor={(r) => r.id}
            page={page}
            pageSize={20}
            total={total}
            onPageChange={setPage}
          />
        </AdminRoutePanel>
      </AsyncStateWrapper>

      {/* Review Modal */}
      {reviewTarget && (
        <Modal title="Review Score Guarantee Claim" onClose={() => setReviewTarget(null)}>
          <div className="space-y-4">
            <div className="grid grid-cols-2 gap-4 text-sm">
              <div><span className="text-gray-500">Baseline:</span> <strong>{reviewTarget.baselineScore}</strong></div>
              <div><span className="text-gray-500">Actual:</span> <strong>{reviewTarget.actualScore ?? '—'}</strong></div>
              <div><span className="text-gray-500">Target:</span> <strong>{reviewTarget.baselineScore + reviewTarget.guaranteedImprovement}</strong></div>
              <div><span className="text-gray-500">Improvement needed:</span> <strong>+{reviewTarget.guaranteedImprovement}</strong></div>
            </div>
            {reviewTarget.claimNote && (
              <div className="p-3 bg-gray-50 dark:bg-gray-800 rounded-lg text-sm">
                <span className="text-gray-500">Learner note:</span> {reviewTarget.claimNote}
              </div>
            )}
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Review Note (optional)</label>
              <Input value={reviewNote} onChange={(e) => setReviewNote(e.target.value)} placeholder="Add a note…" />
            </div>
            <div className="flex justify-end gap-2 pt-2">
              <Button variant="outline" onClick={() => handleReview('reject')} disabled={isMutating}>
                <XCircle className="w-4 h-4 mr-1" /> Reject
              </Button>
              <Button onClick={() => handleReview('approve')} disabled={isMutating}>
                <CheckCircle2 className="w-4 h-4 mr-1" /> Approve
              </Button>
            </div>
          </div>
        </Modal>
      )}
    </AdminRouteWorkspace>
  );
}
