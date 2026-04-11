'use client';

import { useEffect, useState } from 'react';
import { FileCheck2, Check, X } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteSummaryCard, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { approvePublishRequest, rejectPublishRequest } from '@/lib/api';
import { getAdminPublishRequestsData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminPublishRequest } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

const statusBadge: Record<string, { label: string; variant: 'default' | 'success' | 'destructive' }> = {
  pending: { label: 'Pending', variant: 'default' },
  approved: { label: 'Approved', variant: 'success' },
  rejected: { label: 'Rejected', variant: 'destructive' },
};

export default function PublishRequestsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [filters, setFilters] = useState<Record<string, string[]>>({ status: [] });
  const [requests, setRequests] = useState<AdminPublishRequest[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [reviewTarget, setReviewTarget] = useState<AdminPublishRequest | null>(null);
  const [reviewNote, setReviewNote] = useState('');
  const [isMutating, setIsMutating] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const selectedStatus = filters.status?.[0];

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setPageStatus('loading');
      try {
        const data = await getAdminPublishRequestsData({ status: selectedStatus, page, pageSize: 20 });
        if (cancelled) return;
        setRequests(data.items);
        setTotal(data.total);
        setPageStatus(data.items.length > 0 ? 'success' : 'empty');
      } catch {
        if (!cancelled) setPageStatus('error');
      }
    }
    load();
    return () => { cancelled = true; };
  }, [selectedStatus, page]);

  async function handleApprove() {
    if (!reviewTarget) return;
    setIsMutating(true);
    try {
      await approvePublishRequest(reviewTarget.id, reviewNote || undefined);
      setToast({ variant: 'success', message: 'Publish request approved.' });
      setReviewTarget(null);
      setReviewNote('');
      setRequests((prev) => prev.map((r) => r.id === reviewTarget.id ? { ...r, status: 'approved' as const } : r));
    } catch {
      setToast({ variant: 'error', message: 'Failed to approve request.' });
    } finally {
      setIsMutating(false);
    }
  }

  async function handleReject() {
    if (!reviewTarget) return;
    setIsMutating(true);
    try {
      await rejectPublishRequest(reviewTarget.id, reviewNote || undefined);
      setToast({ variant: 'success', message: 'Publish request rejected.' });
      setReviewTarget(null);
      setReviewNote('');
      setRequests((prev) => prev.map((r) => r.id === reviewTarget.id ? { ...r, status: 'rejected' as const } : r));
    } catch {
      setToast({ variant: 'error', message: 'Failed to reject request.' });
    } finally {
      setIsMutating(false);
    }
  }

  const filterGroups: FilterGroup[] = [
    {
      id: 'status',
      label: 'Status',
      options: [
        { id: 'pending', label: 'Pending' },
        { id: 'approved', label: 'Approved' },
        { id: 'rejected', label: 'Rejected' },
      ],
    },
  ];

  const columns: Column<AdminPublishRequest>[] = [
    { key: 'contentItemId', header: 'Content ID', render: (r) => <span className="font-mono text-xs">{r.contentItemId}</span> },
    { key: 'requestedByName', header: 'Requested By', render: (r) => r.requestedByName },
    {
      key: 'status',
      header: 'Status',
      render: (r) => {
        const badge = statusBadge[r.status] ?? statusBadge.pending;
        return <Badge variant={badge.variant}>{badge.label}</Badge>;
      },
    },
    {
      key: 'requestedAt',
      header: 'Requested',
      render: (r) => new Date(r.requestedAt).toLocaleDateString(),
    },
    {
      key: 'actions' as any,
      header: '',
      render: (r) =>
        r.status === 'pending' ? (
          <Button size="sm" variant="outline" onClick={() => { setReviewTarget(r); setReviewNote(''); }}>Review</Button>
        ) : null,
    },
  ];

  const pendingCount = requests.filter((r) => r.status === 'pending').length;

  return (
    <AdminRouteWorkspace>
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
      <AdminRouteSectionHeader
        title="Publish Requests"
        description="Review and approve content publish requests. Content must be approved by a different admin."
      />

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 mb-6">
        <AdminRouteSummaryCard label="Total Requests" value={total} />
        <AdminRouteSummaryCard label="Pending" value={pendingCount} tone={pendingCount > 0 ? 'warning' : 'default'} />
        <AdminRouteSummaryCard label="Approved" value={requests.filter((r) => r.status === 'approved').length} tone="success" />
      </div>

      <FilterBar groups={filterGroups} value={filters} onChange={setFilters} />

      <AsyncStateWrapper
        status={pageStatus}
        empty={<EmptyState icon={<FileCheck2 className="w-12 h-12" />} heading="No publish requests" body="No content publish requests found." />}
        error={<EmptyState variant="error" heading="Error" body="Unable to load publish requests." />}
      >
        <AdminRoutePanel>
          <DataTable
            columns={columns}
            data={requests}
            mobileCardRender={(r) => (
              <div className="space-y-1">
                <p className="font-mono text-xs">{r.contentItemId}</p>
                <p className="text-sm">{r.requestedByName}</p>
                <Badge variant={statusBadge[r.status]?.variant ?? 'default'}>{statusBadge[r.status]?.label ?? r.status}</Badge>
              </div>
            )}
          />
        </AdminRoutePanel>
      </AsyncStateWrapper>

      {reviewTarget && (
        <Modal
          open={!!reviewTarget}
          onClose={() => setReviewTarget(null)}
          title="Review Publish Request"
        >
          <div className="space-y-4 p-4">
            <div>
              <p className="text-sm text-muted">Content: <span className="font-mono">{reviewTarget.contentItemId}</span></p>
              <p className="text-sm text-muted">Requested by: {reviewTarget.requestedByName}</p>
              {reviewTarget.requestNote && <p className="text-sm mt-2 italic">&ldquo;{reviewTarget.requestNote}&rdquo;</p>}
            </div>
            <Input
              label="Review Note (optional)"
              value={reviewNote}
              onChange={(e) => setReviewNote(e.target.value)}
              placeholder="Add a note..."
            />
          </div>
          <div className="flex justify-end gap-2 pt-4 border-t border-border dark:border-border px-4 pb-4">
            <Button variant="ghost" onClick={() => setReviewTarget(null)}>Cancel</Button>
            <Button variant="destructive" onClick={handleReject} disabled={isMutating}>
              <X className="w-3.5 h-3.5 mr-1" />
              Reject
            </Button>
            <Button onClick={handleApprove} disabled={isMutating}>
              <Check className="w-3.5 h-3.5 mr-1" />
              Approve
            </Button>
          </div>
        </Modal>
      )}
    </AdminRouteWorkspace>
  );
}
