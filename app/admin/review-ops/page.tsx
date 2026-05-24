'use client';

import { useEffect, useState, type ReactNode } from 'react';
import { MotionConfig, motion, useReducedMotion } from 'motion/react';
import type { Transition } from 'motion/react';
import { AlertTriangle, CheckCircle2, Clock, FileAudio, FileText, Inbox, UserRoundCheck } from 'lucide-react';
import { AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Toast } from '@/components/ui/alert';
import { Input, Select } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { assignAdminReview, cancelAdminReview, reopenAdminReview } from '@/lib/api';
import { getAdminReviewFailureData, getAdminReviewOpsSummaryData, getAdminReviewQueueData, getAdminUsersPageData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminReviewFailures, AdminReviewOpsSummary, AdminReviewQueueItem, AdminUserRow } from '@/lib/types/admin';
import { BulkActionBar } from '@/components/ui/bulk-action-bar';

import { AdminOperationsLayout, KpiStrip, BentoGrid, BentoCell } from '@/components/admin/layout/admin-operations-layout';
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { KpiTile, type KpiTone } from '@/components/admin/ui/kpi-tile';
import { EmptyState } from '@/components/admin/ui/empty-state';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function ReviewOpsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const prefersReducedMotion = useReducedMotion();
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
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());

  const selectedStatus = filters.status?.[0];
  const selectedPriority = filters.priority?.[0];
  const cardTransition: Transition = prefersReducedMotion
    ? { duration: 0.01 }
    : { type: 'spring', stiffness: 420, damping: 38 };

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
      render: (item) => <span className="font-mono text-xs text-admin-fg-muted">{item.id}</span>,
    },
    {
      key: 'learner',
      header: 'Learner',
      render: (item) => (
        <div className="space-y-1">
          <p className="font-medium text-admin-fg-strong">{item.learnerName}</p>
          <p className="text-xs text-admin-fg-muted">{item.taskId}</p>
        </div>
      ),
    },
    {
      key: 'subtestCode',
      header: 'Subtest',
      render: (item) => <span className="capitalize text-admin-fg-muted">{item.subtestCode}</span>,
    },
    {
      key: 'submissionMode',
      header: 'Submission',
      render: (item) => (
        <div className="space-y-1 text-xs text-admin-fg-muted">
          <div className="flex items-center gap-1.5 capitalize"><FileText className="h-3.5 w-3.5" /> {item.submissionMode ?? 'computer'} / {item.assessorType ?? 'instructor'}</div>
          {item.subtestCode === 'writing' ? (
            <div className="flex flex-wrap gap-1">
              <Badge variant={(item.paperAssetCount ?? 0) > 0 ? 'info' : 'default'} size="sm">Paper {item.paperAssetsExtracted ?? 0}/{item.paperAssetCount ?? 0}</Badge>
              <Badge variant={(item.voiceNoteCount ?? 0) > 0 ? 'success' : 'warning'} size="sm" startIcon={<FileAudio className="h-3 w-3" />}>{item.voiceNoteCount ?? 0}</Badge>
            </div>
          ) : null}
        </div>
      ),
    },
    {
      key: 'priority',
      header: 'Priority',
      render: (item) => (
        <Badge variant={item.priority === 'high' ? 'danger' : item.priority === 'normal' ? 'warning' : 'default'}>
          {item.priority}
        </Badge>
      ),
    },
    {
      key: 'status',
      header: 'Status',
      render: (item) => (
        <Badge variant={item.status === 'completed' ? 'success' : item.status === 'in_progress' ? 'warning' : 'default'}>
          {item.status.replace('_', ' ')}
        </Badge>
      ),
    },
    {
      key: 'assignedExpertId',
      header: 'Assigned Expert',
      render: (item) => <span className="text-sm text-admin-fg-muted">{item.assignedExpertId ?? 'Unassigned'}</span>,
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
            <span className="text-xs text-admin-fg-muted">Completed</span>
          )}
        </div>
      ),
    },
  ];

  const failedReviewColumns: Column<AdminReviewFailures['failedReviews'][number]>[] = [
    {
      key: 'id',
      header: 'Failed Review',
      render: (review) => <span className="font-mono text-xs text-admin-fg-muted">{review.id}</span>,
    },
    {
      key: 'attemptId',
      header: 'Attempt',
      render: (review) => <span className="text-sm text-admin-fg-muted">{review.attemptId}</span>,
    },
    {
      key: 'subtestCode',
      header: 'Subtest',
      render: (review) => <span className="capitalize text-admin-fg-muted">{review.subtestCode}</span>,
    },
    {
      key: 'createdAt',
      header: 'Created',
      render: (review) => <span className="text-sm text-admin-fg-muted">{new Date(review.createdAt).toLocaleString()}</span>,
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
      render: (review) => <span className="font-mono text-xs text-admin-fg-muted">{review.id}</span>,
    },
    {
      key: 'attemptId',
      header: 'Attempt',
      render: (review) => <span className="text-sm text-admin-fg-muted">{review.attemptId}</span>,
    },
    {
      key: 'createdAt',
      header: 'Queued Since',
      render: (review) => <span className="text-sm text-admin-fg-muted">{new Date(review.createdAt).toLocaleString()}</span>,
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
      render: (job) => <span className="font-mono text-xs text-admin-fg-muted">{job.id}</span>,
    },
    {
      key: 'type',
      header: 'Type',
      render: (job) => <span className="text-admin-fg-muted">{job.type}</span>,
    },
    {
      key: 'reason',
      header: 'Reason',
      render: (job) => <span className="text-sm text-admin-fg-muted">{job.reason || 'No reason code'}</span>,
    },
    {
      key: 'retryCount',
      header: 'Retries',
      render: (job) => <span className="text-admin-fg-muted">{job.retryCount}</span>,
    },
    {
      key: 'createdAt',
      header: 'Created',
      render: (job) => <span className="text-sm text-admin-fg-muted">{new Date(job.createdAt).toLocaleString()}</span>,
    },
  ];

  const queueMobileCardRender = (item: AdminReviewQueueItem) => (
    <div className="space-y-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate font-semibold text-admin-fg-strong">{item.learnerName}</p>
          <p className="truncate text-xs uppercase tracking-[0.12em] text-admin-fg-muted">{item.taskId}</p>
        </div>
        <div className="flex flex-col items-end gap-1">
          <Badge variant={item.priority === 'high' ? 'danger' : item.priority === 'normal' ? 'warning' : 'default'}>
            {item.priority}
          </Badge>
          <Badge variant={item.status === 'completed' ? 'success' : item.status === 'in_progress' ? 'warning' : 'default'}>
            {item.status.replace('_', ' ')}
          </Badge>
        </div>
      </div>

      <div className="grid grid-cols-2 gap-3 text-sm">
        <div className="rounded-admin bg-admin-bg-subtle px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-admin-fg-muted">Subtest</p>
          <p className="mt-1 font-medium capitalize text-admin-fg-strong">{item.subtestCode}</p>
        </div>
        <div className="rounded-admin bg-admin-bg-subtle px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-admin-fg-muted">Tutor</p>
          <p className="mt-1 font-medium text-admin-fg-strong">{item.assignedExpertId ?? 'Unassigned'}</p>
        </div>
      </div>

      {item.status !== 'completed' ? (
        <div className="flex flex-col gap-2 sm:flex-row">
          <Button variant="outline" size="sm" className="w-full sm:flex-1" onClick={() => openAssignModal(item)}>
            Assign
          </Button>
          <Button variant="destructive" size="sm" className="w-full sm:flex-1" onClick={() => openCancelModal(item.id)}>
            Cancel
          </Button>
        </div>
      ) : (
        <p className="text-xs font-medium text-admin-fg-muted">Completed</p>
      )}
    </div>
  );

  const failedReviewMobileCardRender = (review: AdminReviewFailures['failedReviews'][number]) => (
    <div className="space-y-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate font-semibold text-admin-fg-strong">{review.attemptId}</p>
          <p className="truncate text-xs uppercase tracking-[0.12em] text-admin-fg-muted">{review.id}</p>
        </div>
        <Badge variant="danger">failed</Badge>
      </div>

      <div className="grid grid-cols-2 gap-3 text-sm">
        <div className="rounded-admin bg-admin-bg-subtle px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-admin-fg-muted">Subtest</p>
          <p className="mt-1 font-medium capitalize text-admin-fg-strong">{review.subtestCode}</p>
        </div>
        <div className="rounded-admin bg-admin-bg-subtle px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-admin-fg-muted">Created</p>
          <p className="mt-1 font-medium text-admin-fg-strong">{new Date(review.createdAt).toLocaleString()}</p>
        </div>
      </div>

      <div className="flex justify-end">
        <Button size="sm" className="w-full sm:w-auto" onClick={() => handleReopenReview(review.id)} loading={isMutating}>
          Reopen
        </Button>
      </div>
    </div>
  );

  const stuckReviewMobileCardRender = (review: AdminReviewFailures['stuckReviews'][number]) => (
    <div className="space-y-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate font-semibold text-admin-fg-strong">{review.attemptId}</p>
          <p className="truncate text-xs uppercase tracking-[0.12em] text-admin-fg-muted">{review.id}</p>
        </div>
        <Badge variant="warning">stuck</Badge>
      </div>

      <div className="rounded-admin bg-admin-bg-subtle px-3 py-2 text-sm">
        <p className="text-[11px] uppercase tracking-[0.12em] text-admin-fg-muted">Queued since</p>
        <p className="mt-1 font-medium text-admin-fg-strong">{new Date(review.createdAt).toLocaleString()}</p>
      </div>

      <div className="flex justify-end">
        <Button variant="destructive" size="sm" className="w-full sm:w-auto" onClick={() => openCancelModal(review.id)}>
          Cancel
        </Button>
      </div>
    </div>
  );

  const failedJobMobileCardRender = (job: AdminReviewFailures['failedJobs'][number]) => (
    <div className="space-y-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate font-semibold text-admin-fg-strong">{job.type}</p>
          <p className="truncate text-xs uppercase tracking-[0.12em] text-admin-fg-muted">{job.id}</p>
        </div>
        <Badge variant="danger">failed</Badge>
      </div>

      <div className="grid grid-cols-2 gap-3 text-sm">
        <div className="rounded-admin bg-admin-bg-subtle px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-admin-fg-muted">Reason</p>
          <p className="mt-1 font-medium text-admin-fg-strong">{job.reason || 'No reason code'}</p>
        </div>
        <div className="rounded-admin bg-admin-bg-subtle px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-admin-fg-muted">Retries</p>
          <p className="mt-1 font-medium text-admin-fg-strong">{job.retryCount}</p>
        </div>
        <div className="rounded-admin bg-admin-bg-subtle px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-admin-fg-muted">Created</p>
          <p className="mt-1 font-medium text-admin-fg-strong">{new Date(job.createdAt).toLocaleString()}</p>
        </div>
      </div>
    </div>
  );

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

  const summaryCards: Array<{ label: string; value: number; icon: ReactNode; tone?: KpiTone }> = summary
    ? [
        { label: 'Backlog', value: summary.backlog, icon: <Inbox className="h-4 w-4" /> },
        { label: 'Overdue', value: summary.overdue, icon: <AlertTriangle className="h-4 w-4" />, tone: summary.overdue > 0 ? 'danger' : 'default' },
        { label: 'SLA Risk', value: summary.slaRisk, icon: <Clock className="h-4 w-4" />, tone: summary.slaRisk > 0 ? 'warning' : 'default' },
        { label: 'Completed', value: summary.statusDistribution.completed, icon: <CheckCircle2 className="h-4 w-4" />, tone: 'success' },
      ]
    : [];

  const failurePanels = failures
    ? [
        {
          title: 'Failed Reviews',
          description: `${failures.summary.failedReviewCount} failed review requests currently need recovery.`,
          content:
            failures.failedReviews.length === 0 ? (
              <p className="text-sm text-admin-fg-muted">No failed review requests at the moment.</p>
            ) : (
              <DataTable columns={failedReviewColumns} data={failures.failedReviews} keyExtractor={(item) => item.id} mobileCardRender={failedReviewMobileCardRender} />
            ),
        },
        {
          title: 'Stuck Reviews',
          description: `${failures.summary.stuckReviewCount} reviews have been in review too long and may need intervention.`,
          content:
            failures.stuckReviews.length === 0 ? (
              <p className="text-sm text-admin-fg-muted">No stuck reviews detected.</p>
            ) : (
              <DataTable columns={stuckReviewColumns} data={failures.stuckReviews} keyExtractor={(item) => item.id} mobileCardRender={stuckReviewMobileCardRender} />
            ),
        },
        {
          title: 'Failed Jobs',
          description: `${failures.summary.failedJobCount} background jobs failed in the review pipeline.`,
          content:
            failures.failedJobs.length === 0 ? (
              <p className="text-sm text-admin-fg-muted">No failed background jobs detected.</p>
            ) : (
              <DataTable columns={failedJobColumns} data={failures.failedJobs} keyExtractor={(item) => item.id} mobileCardRender={failedJobMobileCardRender} />
            ),
        },
      ]
    : [];

  return (
    <MotionConfig reducedMotion="user">
      <AdminRouteWorkspace role="main" aria-label="Review operations">
        {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

        <AdminOperationsLayout
          title="Review Operations"
          description="Manage productive-skill review queue health, tutor assignment, and failure recovery from one operational view."
          breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Review Operations' }]}
          kpis={
            summary ? (
              <KpiStrip>
                {summaryCards.map((card) => (
                  <KpiTile key={card.label} label={card.label} value={card.value} icon={card.icon} tone={card.tone} />
                ))}
              </KpiStrip>
            ) : null
          }
          primaryGrid={
            <AsyncStateWrapper
              status={pageStatus}
              onRetry={() => window.location.reload()}
              emptyContent={
                <EmptyState
                  illustration={<Inbox />}
                  title="No review work is currently visible"
                  description="Adjust the filters or wait for queued, failed, or stuck reviews to appear."
                />
              }
            >
              <div className="space-y-6">
                {summary && totalReviews > 0 ? (
                  <Card>
                    <CardHeader className="flex-col items-start gap-1">
                      <CardTitle>Queue Distribution</CardTitle>
                      <CardDescription>The distribution bar below is computed from live review summary data.</CardDescription>
                    </CardHeader>
                    <CardContent>
                      <div className="space-y-3">
                        <div className="flex h-3 overflow-hidden rounded-full bg-admin-bg-subtle">
                          <motion.div
                            className="bg-admin-border"
                            initial={{ width: 0 }}
                            animate={{ width: `${(summary.statusDistribution.pending / totalReviews) * 100}%` }}
                            transition={cardTransition}
                          />
                          <motion.div
                            className="bg-[var(--admin-warning)]"
                            initial={{ width: 0 }}
                            animate={{ width: `${(summary.statusDistribution.inProgress / totalReviews) * 100}%` }}
                            transition={cardTransition}
                          />
                          <motion.div
                            className="bg-[var(--admin-success)]"
                            initial={{ width: 0 }}
                            animate={{ width: `${(summary.statusDistribution.completed / totalReviews) * 100}%` }}
                            transition={cardTransition}
                          />
                        </div>
                        <div className="flex flex-wrap gap-4 text-sm text-admin-fg-muted">
                          <span>Pending: {summary.statusDistribution.pending}</span>
                          <span>In progress: {summary.statusDistribution.inProgress}</span>
                          <span>Completed: {summary.statusDistribution.completed}</span>
                        </div>
                      </div>
                    </CardContent>
                  </Card>
                ) : null}

                <Card>
                  <CardHeader className="flex-col items-start gap-1">
                    <CardTitle>Live Queue</CardTitle>
                    <CardDescription>Assign or cancel queued reviews with real backend mutations.</CardDescription>
                  </CardHeader>
                  <CardContent className="space-y-4">
                    <FilterBar groups={filterGroups} selected={filters} onChange={handleFilterChange} onClear={() => setFilters({ status: [], priority: [] })} />
                    <DataTable columns={queueColumns} data={queue} keyExtractor={(item) => item.id} mobileCardRender={queueMobileCardRender} selectable selectedKeys={selectedKeys} onSelectionChange={setSelectedKeys} />
                    <BulkActionBar
                      selectedCount={selectedKeys.size}
                      onClearSelection={() => setSelectedKeys(new Set())}
                      actions={[
                        { key: 'cancel', label: 'Cancel selected', variant: 'danger', onClick: () => setToast({ variant: 'error', message: 'Bulk cancel coming soon.' }) },
                      ]}
                    />
                  </CardContent>
                </Card>

                {failures ? (
                  <BentoGrid>
                    {failurePanels.map((panel) => (
                      <BentoCell key={panel.title} span={{ default: 12, xl: 4 }}>
                        <Card className="h-full">
                          <CardHeader className="flex-col items-start gap-1">
                            <CardTitle>{panel.title}</CardTitle>
                            <CardDescription>{panel.description}</CardDescription>
                          </CardHeader>
                          <CardContent>{panel.content}</CardContent>
                        </Card>
                      </BentoCell>
                    ))}
                  </BentoGrid>
                ) : null}
              </div>
            </AsyncStateWrapper>
          }
        />

        <Modal open={Boolean(assignTarget)} onClose={() => setAssignTarget(null)} title="Assign Review">
          <div className="space-y-4 py-2">
            <div className="rounded-admin border border-admin-border bg-admin-bg-subtle p-3 text-sm text-admin-fg-muted">
              {assignTarget ? `Assign review ${assignTarget.id} for ${assignTarget.learnerName} (${assignTarget.subtestCode}).` : 'Select a tutor to continue.'}
            </div>
            <Select
              label="Expert"
              value={selectedExpertId}
              onChange={(event) => setSelectedExpertId(event.target.value)}
              options={experts.map((expert) => ({ value: expert.id, label: `${expert.name} (${expert.email})` }))}
            />
            <div className="flex justify-end gap-3 border-t border-admin-border pt-4">
              <Button variant="outline" onClick={() => setAssignTarget(null)}>
                Cancel
              </Button>
              <Button onClick={handleAssignReview} loading={isMutating} startIcon={<UserRoundCheck className="h-4 w-4" />}>
                Assign Expert
              </Button>
            </div>
          </div>
        </Modal>

        <Modal open={Boolean(cancelTarget)} onClose={() => setCancelTarget(null)} title="Cancel Review">
          <div className="space-y-4 py-2">
            <Input label="Reason" value={cancelReason} onChange={(event) => setCancelReason(event.target.value)} hint="This reason is stored in audit history." />
            <div className="flex justify-end gap-3 border-t border-admin-border pt-4">
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
    </MotionConfig>
  );
}
