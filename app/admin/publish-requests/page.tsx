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
import { Input, Textarea } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { approvePublishRequest, rejectPublishRequest, editorApproveContent, editorRejectContent, publisherApproveContent, publisherRejectContent } from '@/lib/api';
import { getAdminPublishRequestsData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { hasPermission, AdminPermission } from '@/lib/admin-permissions';
import type { AdminPublishRequest } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

const statusBadge: Record<string, { label: string; variant: 'default' | 'success' | 'danger' | 'info' | 'warning' }> = {
  pending: { label: 'Pending', variant: 'default' },
  editor_review: { label: 'Editor Review', variant: 'info' },
  publisher_approval: { label: 'Publisher Approval', variant: 'warning' },
  approved: { label: 'Published', variant: 'success' },
  rejected: { label: 'Rejected', variant: 'danger' },
};

const stageBadge: Record<string, { label: string; variant: 'default' | 'info' }> = {
  editor_review: { label: 'Editor Review', variant: 'info' },
  publisher_approval: { label: 'Publisher Approval', variant: 'default' },
};

function StatusPipeline({ status }: { status: string }) {
  const steps = ['editor_review', 'publisher_approval', 'approved'] as const;
  const labels: Record<string, string> = { editor_review: 'Editor', publisher_approval: 'Publisher', approved: 'Published' };
  const stepIndex = steps.indexOf(status as typeof steps[number]);

  return (
    <div className="flex items-center gap-1 text-xs">
      {steps.map((step, i) => {
        const isActive = status === step;
        const isPast = stepIndex > i;
        return (
          <span key={step} className="flex items-center gap-1">
            {i > 0 ? <span className="text-[var(--color-border)]">→</span> : null}
            <span className={`px-1.5 py-0.5 rounded ${isActive ? 'bg-[var(--color-accent)] text-white' : isPast ? 'bg-[var(--color-success-bg)] text-[var(--color-success)]' : 'bg-[var(--color-surface-secondary)] text-[var(--color-text-tertiary)]'}`}>
              {labels[step]}
            </span>
          </span>
        );
      })}
      {status === 'rejected' ? (
        <span className="ml-1 px-1.5 py-0.5 rounded bg-[var(--color-error-bg)] text-[var(--color-error)]">Rejected</span>
      ) : null}
    </div>
  );
}

export default function PublishRequestsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const { user } = useCurrentUser();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [filters, setFilters] = useState<Record<string, string[]>>({ status: [], stage: [] });
  const [requests, setRequests] = useState<AdminPublishRequest[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [reviewTarget, setReviewTarget] = useState<AdminPublishRequest | null>(null);
  const [reviewNote, setReviewNote] = useState('');
  const [rejectionReason, setRejectionReason] = useState('');
  const [showRejectForm, setShowRejectForm] = useState(false);
  const [isMutating, setIsMutating] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const selectedStatus = filters.status?.[0];
  const selectedStage = filters.stage?.[0];
  const userPerms = user?.adminPermissions ?? [];
  const canEditorReview = hasPermission(userPerms, AdminPermission.ContentEditorReview, AdminPermission.ContentPublish);
  const canPublisherApprove = hasPermission(userPerms, AdminPermission.ContentPublisherApproval, AdminPermission.ContentPublish);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setPageStatus('loading');
      try {
        const statusParam = selectedStage || selectedStatus;
        const data = await getAdminPublishRequestsData({ status: statusParam, page, pageSize: 20 });
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
  }, [selectedStatus, selectedStage, page]);

  async function handleEditorApprove() {
    if (!reviewTarget) return;
    setIsMutating(true);
    try {
      await editorApproveContent(reviewTarget.contentItemId, reviewNote || undefined);
      setToast({ variant: 'success', message: 'Editor approved — moved to publisher review.' });
      setReviewTarget(null);
      setReviewNote('');
      setRequests((prev) => prev.map((r) =>
        r.id === reviewTarget.id ? { ...r, status: 'publisher_approval' as const, stage: 'publisher_approval' as const } : r));
    } catch {
      setToast({ variant: 'error', message: 'Failed to approve.' });
    } finally {
      setIsMutating(false);
    }
  }

  async function handleEditorReject() {
    if (!reviewTarget || !rejectionReason.trim()) return;
    setIsMutating(true);
    try {
      await editorRejectContent(reviewTarget.contentItemId, rejectionReason);
      setToast({ variant: 'success', message: 'Editor rejected — returned to draft.' });
      setReviewTarget(null);
      setRejectionReason('');
      setShowRejectForm(false);
      setRequests((prev) => prev.map((r) =>
        r.id === reviewTarget.id ? { ...r, status: 'rejected' as const } : r));
    } catch {
      setToast({ variant: 'error', message: 'Failed to reject.' });
    } finally {
      setIsMutating(false);
    }
  }

  async function handlePublisherApprove() {
    if (!reviewTarget) return;
    setIsMutating(true);
    try {
      await publisherApproveContent(reviewTarget.contentItemId, reviewNote || undefined);
      setToast({ variant: 'success', message: 'Publisher approved — content published.' });
      setReviewTarget(null);
      setReviewNote('');
      setRequests((prev) => prev.map((r) =>
        r.id === reviewTarget.id ? { ...r, status: 'approved' as const } : r));
    } catch {
      setToast({ variant: 'error', message: 'Failed to approve.' });
    } finally {
      setIsMutating(false);
    }
  }

  async function handlePublisherReject() {
    if (!reviewTarget || !rejectionReason.trim()) return;
    setIsMutating(true);
    try {
      await publisherRejectContent(reviewTarget.contentItemId, rejectionReason);
      setToast({ variant: 'success', message: 'Publisher rejected — returned to editor review.' });
      setReviewTarget(null);
      setRejectionReason('');
      setShowRejectForm(false);
      setRequests((prev) => prev.map((r) =>
        r.id === reviewTarget.id ? { ...r, status: 'editor_review' as const, stage: 'editor_review' as const } : r));
    } catch {
      setToast({ variant: 'error', message: 'Failed to reject.' });
    } finally {
      setIsMutating(false);
    }
  }

  // Legacy single-stage approve/reject still wired for backward compat
  async function handleLegacyApprove() {
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

  async function handleLegacyReject() {
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
        { id: 'editor_review', label: 'Editor Review' },
        { id: 'publisher_approval', label: 'Publisher Approval' },
        { id: 'approved', label: 'Approved' },
        { id: 'rejected', label: 'Rejected' },
        { id: 'pending', label: 'Pending (Legacy)' },
      ],
    },
  ];

  const columns: Column<AdminPublishRequest>[] = [
    { key: 'contentItemId', header: 'Content ID', render: (r) => <span className="font-mono text-xs">{r.contentItemId}</span> },
    { key: 'requestedByName', header: 'Requested By', render: (r) => r.requestedByName },
    {
      key: 'status',
      header: 'Pipeline',
      render: (r) => <StatusPipeline status={r.status} />,
    },
    {
      key: 'stage' as keyof AdminPublishRequest,
      header: 'Stage',
      render: (r) => {
        const badge = stageBadge[r.stage];
        return badge ? <Badge variant={badge.variant}>{badge.label}</Badge> : <span className="text-xs text-[var(--color-text-tertiary)]">—</span>;
      },
    },
    {
      key: 'requestedAt',
      header: 'Requested',
      render: (r) => new Date(r.requestedAt).toLocaleDateString(),
    },
    {
      key: 'rejectionReason' as keyof AdminPublishRequest,
      header: 'Last Rejection',
      render: (r) => r.rejectionReason ? (
        <span className="text-xs text-[var(--color-error)]" title={r.rejectionReason}>
          {r.rejectionReason.length > 40 ? `${r.rejectionReason.slice(0, 40)}…` : r.rejectionReason}
        </span>
      ) : null,
    },
    {
      key: 'actions' as keyof AdminPublishRequest,
      header: '',
      render: (r) => {
        const isEditorStage = r.status === 'editor_review';
        const isPublisherStage = r.status === 'publisher_approval';
        const isLegacyPending = r.status === 'pending';
        if (isEditorStage && canEditorReview) {
          return <Button size="sm" variant="outline" onClick={() => { setReviewTarget(r); setReviewNote(''); setRejectionReason(''); setShowRejectForm(false); }}>Review (Editor)</Button>;
        }
        if (isPublisherStage && canPublisherApprove) {
          return <Button size="sm" variant="outline" onClick={() => { setReviewTarget(r); setReviewNote(''); setRejectionReason(''); setShowRejectForm(false); }}>Review (Publisher)</Button>;
        }
        if (isLegacyPending) {
          return <Button size="sm" variant="outline" onClick={() => { setReviewTarget(r); setReviewNote(''); setRejectionReason(''); setShowRejectForm(false); }}>Review</Button>;
        }
        return null;
      },
    },
  ];

  const editorReviewCount = requests.filter((r) => r.status === 'editor_review').length;
  const publisherApprovalCount = requests.filter((r) => r.status === 'publisher_approval').length;

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace>
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
      <AdminRouteSectionHeader
        title="Publish Requests"
        description="Multi-stage content approval: Editor Review → Publisher Approval → Published."
      />

      <div className="grid grid-cols-1 sm:grid-cols-4 gap-4 mb-6">
        <AdminRouteSummaryCard label="Total Requests" value={total} />
        <AdminRouteSummaryCard label="Editor Review" value={editorReviewCount} tone={editorReviewCount > 0 ? 'warning' : 'default'} />
        <AdminRouteSummaryCard label="Publisher Approval" value={publisherApprovalCount} tone={publisherApprovalCount > 0 ? 'warning' : 'default'} />
        <AdminRouteSummaryCard label="Published" value={requests.filter((r) => r.status === 'approved').length} tone="success" />
      </div>

      <FilterBar groups={filterGroups} selected={filters} onChange={(groupId, optionId) => setFilters(prev => { const arr = [...(prev[groupId] || [])]; const i = arr.indexOf(optionId); if (i >= 0) arr.splice(i, 1); else arr.push(optionId); return { ...prev, [groupId]: arr }; })} />

      <AsyncStateWrapper
        status={pageStatus}
        emptyContent={<EmptyState icon={<FileCheck2 className="w-12 h-12" />} title="No publish requests" description="No content publish requests found." />}
        errorMessage="Unable to load publish requests."
      >
        <AdminRoutePanel>
          <DataTable
            columns={columns}
            data={requests}
            keyExtractor={(r) => r.id}
            mobileCardRender={(r) => (
              <div className="space-y-1">
                <p className="font-mono text-xs">{r.contentItemId}</p>
                <p className="text-sm">{r.requestedByName}</p>
                <StatusPipeline status={r.status} />
                {r.rejectionReason ? <p className="text-xs text-[var(--color-error)]">{r.rejectionReason}</p> : null}
              </div>
            )}
          />
        </AdminRoutePanel>
      </AsyncStateWrapper>

      {reviewTarget && (
        <Modal
          open={!!reviewTarget}
          onClose={() => { setReviewTarget(null); setShowRejectForm(false); }}
          title={
            reviewTarget.status === 'editor_review' ? 'Editor Review'
            : reviewTarget.status === 'publisher_approval' ? 'Publisher Review'
            : 'Review Publish Request'
          }
        >
          <div className="space-y-4 p-4">
            <div>
              <p className="text-sm text-muted">Content: <span className="font-mono">{reviewTarget.contentItemId}</span></p>
              <p className="text-sm text-muted">Requested by: {reviewTarget.requestedByName}</p>
              {reviewTarget.requestNote && <p className="text-sm mt-2 italic">&ldquo;{reviewTarget.requestNote}&rdquo;</p>}
              {reviewTarget.editorNotes && (
                <p className="text-sm mt-1 text-[var(--color-text-secondary)]">Editor notes: {reviewTarget.editorNotes}</p>
              )}
              {reviewTarget.rejectionReason && (
                <p className="text-sm mt-1 text-[var(--color-error)]">Previous rejection: {reviewTarget.rejectionReason} (at {reviewTarget.rejectionStage})</p>
              )}
              <div className="mt-2">
                <StatusPipeline status={reviewTarget.status} />
              </div>
            </div>

            {!showRejectForm ? (
              <Input
                label="Notes (optional)"
                value={reviewNote}
                onChange={(e) => setReviewNote(e.target.value)}
                placeholder="Add a note..."
              />
            ) : (
              <Textarea
                label="Rejection Reason (required)"
                value={rejectionReason}
                onChange={(e) => setRejectionReason(e.target.value)}
                placeholder="Explain why this content is being rejected..."
                rows={3}
              />
            )}
          </div>
          <div className="flex justify-end gap-2 pt-4 border-t border-border dark:border-border px-4 pb-4">
            <Button variant="ghost" onClick={() => { setReviewTarget(null); setShowRejectForm(false); }}>Cancel</Button>
            {!showRejectForm ? (
              <>
                <Button variant="destructive" onClick={() => setShowRejectForm(true)} disabled={isMutating}>
                  <X className="w-3.5 h-3.5 mr-1" />
                  Reject
                </Button>
                {reviewTarget.status === 'editor_review' ? (
                  <Button onClick={handleEditorApprove} disabled={isMutating}>
                    <Check className="w-3.5 h-3.5 mr-1" />
                    Approve → Publisher
                  </Button>
                ) : reviewTarget.status === 'publisher_approval' ? (
                  <Button onClick={handlePublisherApprove} disabled={isMutating}>
                    <Check className="w-3.5 h-3.5 mr-1" />
                    Approve → Publish
                  </Button>
                ) : (
                  <Button onClick={handleLegacyApprove} disabled={isMutating}>
                    <Check className="w-3.5 h-3.5 mr-1" />
                    Approve
                  </Button>
                )}
              </>
            ) : (
              <>
                <Button variant="ghost" onClick={() => setShowRejectForm(false)}>Back</Button>
                {reviewTarget.status === 'editor_review' ? (
                  <Button variant="destructive" onClick={handleEditorReject} disabled={isMutating || !rejectionReason.trim()}>
                    <X className="w-3.5 h-3.5 mr-1" />
                    Confirm Rejection
                  </Button>
                ) : reviewTarget.status === 'publisher_approval' ? (
                  <Button variant="destructive" onClick={handlePublisherReject} disabled={isMutating || !rejectionReason.trim()}>
                    <X className="w-3.5 h-3.5 mr-1" />
                    Reject → Editor Review
                  </Button>
                ) : (
                  <Button variant="destructive" onClick={handleLegacyReject} disabled={isMutating}>
                    <X className="w-3.5 h-3.5 mr-1" />
                    Reject
                  </Button>
                )}
              </>
            )}
          </div>
        </Modal>
      )}
    </AdminRouteWorkspace>
  );
}
