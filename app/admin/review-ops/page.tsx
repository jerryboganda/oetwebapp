'use client';

import { useEffect, useState } from 'react';
import { AlertTriangle, CheckCircle2, Clock, Inbox, UserRoundCheck } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteSummaryCard, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { assignAdminReview, cancelAdminReview, reopenAdminReview } from '@/lib/api';
import { getAdminReviewFailureData, getAdminReviewOpsSummaryData, getAdminReviewQueueData, getAdminUsersPageData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminReviewFailures, AdminReviewOpsSummary, AdminReviewQueueItem, AdminUserRow } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function ReviewOpsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [filters, setFilters] = useState<Record<string, string[]>>({ status: [], priority: [] });
  const [summary, setSummary] = useState<AdminReviewOpsSummary | null>(null);
  const [queue, setQueue] = useState<AdminReviewQueueItem[]>([]);
  const [failures, setFailures] = useState<AdminReviewFailures | null>(null);
  const [experts, setExperts] = useState<AdminUserRow[]>([]);
  const [assignTarget, setAssignTarget] = useState<AdminReviewQueueItem | null>(null);
  const [selectedExpertId, setSelectedExpertId] = useState('');
  const [cancelTarget, setCancelTarget] = useState<AdminReviewQueueItem | { id: string } | null>(null);
  const [cancelReason, setCancelReason] = useState('');
  const [isMutating, setIsMutating] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const selectedStatus = filters.status?.[0];
  const selectedPriority = filters.priority?.[0];

  useEffect(() => {
    let cancelled = false;

    async function loadReviewOps() {
      setPageStatus('loading');
      try {
        const [summaryData, queueData, failureData, expertData] = await Promise.all([
          getAdminReviewOpsSummaryData(),
          getAdminReviewQueueData({ status: selectedStatus, priority: selectedPriority }),
          getAdminReviewFailureData(),
          getAdminUsersPageData({ role: 'expert', pageSize: 100 }),
        ]);

        if (cancelled) return;

        setSummary(summaryData);
        setQueue(queueData);
        setFailures(failureData);
        setExperts(expertData.items);

        const hasVisibleData =
          queueData.length > 0 ||
          failureData.failedReviews.length > 0 ||
          failureData.stuckReviews.length > 0 ||
          failureData.failedJobs.length > 0;
        setPageStatus(hasVisibleData ? 'success' : 'empty');
      } catch (error) {
        console.error(error);
        if (!cancelled) {
          setPageStatus('error');
          setToast({ variant: 'error', message: 'Unable to load review operations.' });
        }
      }
    }

    loadReviewOps();
    return () => {
      cancelled = true;
    };
  }, [selectedStatus, selectedPriority]);

  const filterGroups: FilterGroup[] = [
    {
      id: 'status',
      label: 'Queue Status',
      options: [
        { id: 'pending', label: 'Pending' },
        { id: 'in_progress', label: 'In Progress' },
        { id: 'completed', label: 'Completed' },
      ],
    },
    {
      id: 'priority',
      label: 'Priority',
      options: [
        { id: 'high', label: 'High' },
        { id: 'normal', label: 'Normal' },
        { id: 'low', label: 'Low' },
      ],
    },
  ];

  const queueColumns: Column<AdminReviewQueueItem>[] = [
    {
      key: 'id',
      header: 'Review Request',
      render: (item) => <span className="font-mono text-xs text-slate-600">{item.id}</span>,
    },
    {
      key: 'learner',
      header: 'Learner',
      render: (item) => (
        <div className="space-y-1">
          <p className="font-medium text-slate-900">{item.learnerName}</p>
          <p className="text-xs text-slate-500">{item.taskId}</p>
        </div>
      ),
    },
    {
      key: 'subtestCode',
      header: 'Subtest',
      render: (item) => <span className="capitalize text-slate-600">{item.subtestCode}</span>,
    },
    {
      key: 'priority',
      header: 'Priority',
      render: (item) => (
        <Badge variant={item.priority === 'high' ? 'danger' : item.priority === 'normal' ? 'warning' : 'muted'}>
          {item.priority}
        </Badge>
      ),
    },
    {
      key: 'status',
      header: 'Status',
      render: (item) => (
        <Badge variant={item.status === 'completed' ? 'success' : item.status === 'in_progress' ? 'warning' : 'muted'}>
          {item.status.replace('_', ' ')}
        </Badge>
      ),
    },
    {
      key: 'assignedExpertId',
      header: 'Assigned Expert',
      render: (item) => <span className="text-sm text-slate-500">{item.assignedExpertId ?? 'Unassigned'}</span>,
    },
    {
      key: 'actions',
      header: '',
      className: 'w-64',
      render: (item) => (
        <div className="flex justify-end gap-2">
          {item.status !== 'completed' ? (
            <>
              <Button variant="outline" size="sm" onClick={() => openAssignModal(item)}>
                Assign
              </Button>
              <Button variant="destructive" size="sm" onClick={() => openCancelModal(item.id)}>
                Cancel
              </Button>
            </>
          ) : (
            <span className="text-xs text-slate-400">Completed</span>
          )}
        </div>
      ),
    },
  ];

  const failedReviewColumns: Column<AdminReviewFailures['failedReviews'][number]>[] = [
    {
      key: 'id',
      header: 'Failed Review',
      render: (review) => <span className="font-mono text-xs text-slate-600">{review.id}</span>,
    },
    {
      key: 'attemptId',
      header: 'Attempt',
      render: (review) => <span className="text-sm text-slate-500">{review.attemptId}</span>,
    },
    {
      key: 'subtestCode',
      header: 'Subtest',
      render: (review) => <span className="capitalize text-slate-600">{review.subtestCode}</span>,
    },
    {
      key: 'createdAt',
      header: 'Created',
      render: (review) => <span className="text-sm text-slate-500">{new Date(review.createdAt).toLocaleString()}</span>,
    },
    {
      key: 'actions',
      header: '',
      className: 'w-32',
      render: (review) => (
        <div className="flex justify-end">
          <Button size="sm" onClick={() => handleReopenReview(review.id)} loading={isMutating}>
            Reopen
          </Button>
        </div>
      ),
    },
  ];

  const stuckReviewColumns: Column<AdminReviewFailures['stuckReviews'][number]>[] = [
    {
      key: 'id',
      header: 'Stuck Review',
      render: (review) => <span className="font-mono text-xs text-slate-600">{review.id}</span>,
    },
    {
      key: 'attemptId',
      header: 'Attempt',
      render: (review) => <span className="text-sm text-slate-500">{review.attemptId}</span>,
    },
    {
      key: 'createdAt',
      header: 'Queued Since',
      render: (review) => <span className="text-sm text-slate-500">{new Date(review.createdAt).toLocaleString()}</span>,
    },
    {
      key: 'actions',
      header: '',
      className: 'w-32',
      render: (review) => (
        <div className="flex justify-end">
          <Button variant="destructive" size="sm" onClick={() => openCancelModal(review.id)}>
            Cancel
          </Button>
        </div>
      ),
    },
  ];

  const failedJobColumns: Column<AdminReviewFailures['failedJobs'][number]>[] = [
    {
      key: 'id',
      header: 'Job',
      render: (job) => <span className="font-mono text-xs text-slate-600">{job.id}</span>,
    },
    {
      key: 'type',
      header: 'Type',
      render: (job) => <span className="text-slate-600">{job.type}</span>,
    },
    {
      key: 'reason',
      header: 'Reason',
      render: (job) => <span className="text-sm text-slate-500">{job.reason || 'No reason code'}</span>,
    },
    {
      key: 'retryCount',
      header: 'Retries',
      render: (job) => <span className="text-slate-600">{job.retryCount}</span>,
    },
    {
      key: 'createdAt',
      header: 'Created',
      render: (job) => <span className="text-sm text-slate-500">{new Date(job.createdAt).toLocaleString()}</span>,
    },
  ];

  function handleFilterChange(groupId: string, optionId: string) {
    setFilters((current) => ({
      ...current,
      [groupId]: current[groupId]?.includes(optionId) ? [] : [optionId],
    }));
  }

  function openAssignModal(item: AdminReviewQueueItem) {
    setAssignTarget(item);
    setSelectedExpertId(item.assignedExpertId ?? experts[0]?.id ?? '');
  }

  function openCancelModal(reviewId: string) {
    setCancelTarget({ id: reviewId });
    setCancelReason('');
  }

  async function reloadReviewOps() {
    const [summaryData, queueData, failureData, expertData] = await Promise.all([
      getAdminReviewOpsSummaryData(),
      getAdminReviewQueueData({ status: selectedStatus, priority: selectedPriority }),
      getAdminReviewFailureData(),
      getAdminUsersPageData({ role: 'expert', pageSize: 100 }),
    ]);

    setSummary(summaryData);
    setQueue(queueData);
    setFailures(failureData);
    setExperts(expertData.items);
    const hasVisibleData =
      queueData.length > 0 ||
      failureData.failedReviews.length > 0 ||
      failureData.stuckReviews.length > 0 ||
      failureData.failedJobs.length > 0;
    setPageStatus(hasVisibleData ? 'success' : 'empty');
  }

  async function handleAssignReview() {
    if (!assignTarget || !selectedExpertId) return;
    setIsMutating(true);
    try {
      await assignAdminReview(assignTarget.id, {
        expertId: selectedExpertId,
        reason: 'Assigned from admin review operations console',
      });
      await reloadReviewOps();
      setAssignTarget(null);
      setToast({ variant: 'success', message: 'Review assignment updated.' });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to assign this review.' });
    } finally {
      setIsMutating(false);
    }
  }

  async function handleCancelReview() {
    if (!cancelTarget) return;
    setIsMutating(true);
    try {
      await cancelAdminReview(cancelTarget.id, {
        reason: cancelReason || 'Cancelled from admin review operations console',
      });
      await reloadReviewOps();
      setCancelTarget(null);
      setCancelReason('');
      setToast({ variant: 'success', message: 'Review cancelled successfully.' });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to cancel this review.' });
    } finally {
      setIsMutating(false);
    }
  }

  async function handleReopenReview(reviewId: string) {
    setIsMutating(true);
    try {
      await reopenAdminReview(reviewId, { reason: 'Reopened from admin review failure workspace' });
      await reloadReviewOps();
      setToast({ variant: 'success', message: 'Review reopened successfully.' });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to reopen this review.' });
    } finally {
      setIsMutating(false);
    }
  }

  if (!isAuthenticated || role !== 'admin') return null;

  const totalReviews =
    (summary?.statusDistribution.pending ?? 0) +
    (summary?.statusDistribution.inProgress ?? 0) +
    (summary?.statusDistribution.completed ?? 0);

  return (
    <AdminRouteWorkspace role="main" aria-label="Review operations">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AdminRouteSectionHeader
        title="Review Operations"
        description="Manage productive-skill review queue health, expert assignment, and failure recovery from one operational view."
      />

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => window.location.reload()}
        emptyContent={
          <EmptyState
            icon={<Inbox className="h-10 w-10 text-slate-400" />}
            title="No review work is currently visible"
            description="Adjust the filters or wait for queued, failed, or stuck reviews to appear."
          />
        }
      >
        {summary ? (
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <AdminRouteSummaryCard label="Backlog" value={summary.backlog} icon={<Inbox className="h-5 w-5" />} />
            <AdminRouteSummaryCard label="Overdue" value={summary.overdue} icon={<AlertTriangle className="h-5 w-5" />} tone={summary.overdue > 0 ? 'danger' : 'default'} />
            <AdminRouteSummaryCard label="SLA Risk" value={summary.slaRisk} icon={<Clock className="h-5 w-5" />} tone={summary.slaRisk > 0 ? 'warning' : 'default'} />
            <AdminRouteSummaryCard label="Completed" value={summary.statusDistribution.completed} icon={<CheckCircle2 className="h-5 w-5" />} tone="success" />
          </div>
        ) : null}

        {summary && totalReviews > 0 ? (
          <AdminRoutePanel title="Queue Distribution" description="The distribution bar below is computed from live review summary data.">
            <div className="space-y-3">
              <div className="flex h-3 rounded-full overflow-hidden bg-slate-100">
                <div className="bg-slate-400" style={{ width: `${(summary.statusDistribution.pending / totalReviews) * 100}%` }} />
                <div className="bg-amber-400" style={{ width: `${(summary.statusDistribution.inProgress / totalReviews) * 100}%` }} />
                <div className="bg-emerald-500" style={{ width: `${(summary.statusDistribution.completed / totalReviews) * 100}%` }} />
              </div>
              <div className="flex flex-wrap gap-4 text-sm text-slate-500">
                <span>Pending: {summary.statusDistribution.pending}</span>
                <span>In progress: {summary.statusDistribution.inProgress}</span>
                <span>Completed: {summary.statusDistribution.completed}</span>
              </div>
            </div>
          </AdminRoutePanel>
        ) : null}

        <AdminRoutePanel title="Live Queue" description="Assign or cancel queued reviews with real backend mutations.">
          <FilterBar groups={filterGroups} selected={filters} onChange={handleFilterChange} onClear={() => setFilters({ status: [], priority: [] })} />
          <DataTable columns={queueColumns} data={queue} keyExtractor={(item) => item.id} />
        </AdminRoutePanel>

        {failures ? (
          <div className="grid gap-6 xl:grid-cols-3">
            <AdminRoutePanel title="Failed Reviews" description={`${failures.summary.failedReviewCount} failed review requests currently need recovery.`}>
              {failures.failedReviews.length === 0 ? (
                <p className="text-sm text-slate-500">No failed review requests at the moment.</p>
              ) : (
                <DataTable columns={failedReviewColumns} data={failures.failedReviews} keyExtractor={(item) => item.id} />
              )}
            </AdminRoutePanel>

            <AdminRoutePanel title="Stuck Reviews" description={`${failures.summary.stuckReviewCount} reviews have been in review too long and may need intervention.`}>
              {failures.stuckReviews.length === 0 ? (
                <p className="text-sm text-slate-500">No stuck reviews detected.</p>
              ) : (
                <DataTable columns={stuckReviewColumns} data={failures.stuckReviews} keyExtractor={(item) => item.id} />
              )}
            </AdminRoutePanel>

            <AdminRoutePanel title="Failed Jobs" description={`${failures.summary.failedJobCount} background jobs failed in the review pipeline.`}>
              {failures.failedJobs.length === 0 ? (
                <p className="text-sm text-slate-500">No failed background jobs detected.</p>
              ) : (
                <DataTable columns={failedJobColumns} data={failures.failedJobs} keyExtractor={(item) => item.id} />
              )}
            </AdminRoutePanel>
          </div>
        ) : null}
      </AsyncStateWrapper>

      <Modal open={Boolean(assignTarget)} onClose={() => setAssignTarget(null)} title="Assign Review">
        <div className="space-y-4 py-2">
          <div className="rounded-xl border border-slate-200 bg-slate-50 p-3 text-sm text-slate-500">
            {assignTarget ? `Assign review ${assignTarget.id} for ${assignTarget.learnerName} (${assignTarget.subtestCode}).` : 'Select an expert to continue.'}
          </div>
          <Select
            label="Expert"
            value={selectedExpertId}
            onChange={(event) => setSelectedExpertId(event.target.value)}
            options={experts.map((expert) => ({ value: expert.id, label: `${expert.name} (${expert.email})` }))}
          />
          <div className="flex justify-end gap-3 border-t border-slate-100 pt-4">
            <Button variant="outline" onClick={() => setAssignTarget(null)}>
              Cancel
            </Button>
            <Button onClick={handleAssignReview} loading={isMutating} className="gap-2">
              <UserRoundCheck className="h-4 w-4" />
              Assign Expert
            </Button>
          </div>
        </div>
      </Modal>

      <Modal open={Boolean(cancelTarget)} onClose={() => setCancelTarget(null)} title="Cancel Review">
        <div className="space-y-4 py-2">
          <Input label="Reason" value={cancelReason} onChange={(event) => setCancelReason(event.target.value)} hint="This reason is stored in audit history." />
          <div className="flex justify-end gap-3 border-t border-slate-100 pt-4">
            <Button variant="outline" onClick={() => setCancelTarget(null)}>
              Keep Review
            </Button>
            <Button variant="destructive" onClick={handleCancelReview} loading={isMutating}>
              Cancel Review
            </Button>
          </div>
        </div>
      </Modal>
    </AdminRouteWorkspace>
  );
}
