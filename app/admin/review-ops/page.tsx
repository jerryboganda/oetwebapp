'use client';

import { useEffect, useState, type ReactNode } from 'react';
import { MotionConfig, motion, useReducedMotion } from 'motion/react';
import type { Transition } from 'motion/react';
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
type SummaryCardTone = 'default' | 'success' | 'warning' | 'danger';

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
      render: (item) => <span className="font-mono text-xs text-muted">{item.id}</span>,
    },
    {
      key: 'learner',
      header: 'Learner',
      render: (item) => (
        <div className="space-y-1">
          <p className="font-medium text-navy">{item.learnerName}</p>
          <p className="text-xs text-muted">{item.taskId}</p>
        </div>
      ),
    },
    {
      key: 'subtestCode',
      header: 'Subtest',
      render: (item) => <span className="capitalize text-muted">{item.subtestCode}</span>,
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
      render: (item) => <span className="text-sm text-muted">{item.assignedExpertId ?? 'Unassigned'}</span>,
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
            <span className="text-xs text-muted">Completed</span>
          )}
        </div>
      ),
    },
  ];

  const failedReviewColumns: Column<AdminReviewFailures['failedReviews'][number]>[] = [
    {
      key: 'id',
      header: 'Failed Review',
      render: (review) => <span className="font-mono text-xs text-muted">{review.id}</span>,
    },
    {
      key: 'attemptId',
      header: 'Attempt',
      render: (review) => <span className="text-sm text-muted">{review.attemptId}</span>,
    },
    {
      key: 'subtestCode',
      header: 'Subtest',
      render: (review) => <span className="capitalize text-muted">{review.subtestCode}</span>,
    },
    {
      key: 'createdAt',
      header: 'Created',
      render: (review) => <span className="text-sm text-muted">{new Date(review.createdAt).toLocaleString()}</span>,
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
      render: (review) => <span className="font-mono text-xs text-muted">{review.id}</span>,
    },
    {
      key: 'attemptId',
      header: 'Attempt',
      render: (review) => <span className="text-sm text-muted">{review.attemptId}</span>,
    },
    {
      key: 'createdAt',
      header: 'Queued Since',
      render: (review) => <span className="text-sm text-muted">{new Date(review.createdAt).toLocaleString()}</span>,
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
      render: (job) => <span className="font-mono text-xs text-muted">{job.id}</span>,
    },
    {
      key: 'type',
      header: 'Type',
      render: (job) => <span className="text-muted">{job.type}</span>,
    },
    {
      key: 'reason',
      header: 'Reason',
      render: (job) => <span className="text-sm text-muted">{job.reason || 'No reason code'}</span>,
    },
    {
      key: 'retryCount',
      header: 'Retries',
      render: (job) => <span className="text-muted">{job.retryCount}</span>,
    },
    {
      key: 'createdAt',
      header: 'Created',
      render: (job) => <span className="text-sm text-muted">{new Date(job.createdAt).toLocaleString()}</span>,
    },
  ];

  const queueMobileCardRender = (item: AdminReviewQueueItem) => (
    <div className="space-y-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate font-semibold text-navy">{item.learnerName}</p>
          <p className="truncate text-xs uppercase tracking-[0.12em] text-muted">{item.taskId}</p>
        </div>
        <div className="flex flex-col items-end gap-1">
          <Badge variant={item.priority === 'high' ? 'danger' : item.priority === 'normal' ? 'warning' : 'muted'}>
            {item.priority}
          </Badge>
          <Badge variant={item.status === 'completed' ? 'success' : item.status === 'in_progress' ? 'warning' : 'muted'}>
            {item.status.replace('_', ' ')}
          </Badge>
        </div>
      </div>

      <div className="grid grid-cols-2 gap-3 text-sm">
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Subtest</p>
          <p className="mt-1 font-medium capitalize text-navy">{item.subtestCode}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Expert</p>
          <p className="mt-1 font-medium text-navy">{item.assignedExpertId ?? 'Unassigned'}</p>
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
        <p className="text-xs font-medium text-muted">Completed</p>
      )}
    </div>
  );

  const failedReviewMobileCardRender = (review: AdminReviewFailures['failedReviews'][number]) => (
    <div className="space-y-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate font-semibold text-navy">{review.attemptId}</p>
          <p className="truncate text-xs uppercase tracking-[0.12em] text-muted">{review.id}</p>
        </div>
        <Badge variant="danger">failed</Badge>
      </div>

      <div className="grid grid-cols-2 gap-3 text-sm">
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Subtest</p>
          <p className="mt-1 font-medium capitalize text-navy">{review.subtestCode}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Created</p>
          <p className="mt-1 font-medium text-navy">{new Date(review.createdAt).toLocaleString()}</p>
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
          <p className="truncate font-semibold text-navy">{review.attemptId}</p>
          <p className="truncate text-xs uppercase tracking-[0.12em] text-muted">{review.id}</p>
        </div>
        <Badge variant="warning">stuck</Badge>
      </div>

      <div className="rounded-2xl bg-background-light px-3 py-2 text-sm">
        <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Queued since</p>
        <p className="mt-1 font-medium text-navy">{new Date(review.createdAt).toLocaleString()}</p>
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
          <p className="truncate font-semibold text-navy">{job.type}</p>
          <p className="truncate text-xs uppercase tracking-[0.12em] text-muted">{job.id}</p>
        </div>
        <Badge variant="danger">failed</Badge>
      </div>

      <div className="grid grid-cols-2 gap-3 text-sm">
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Reason</p>
          <p className="mt-1 font-medium text-navy">{job.reason || 'No reason code'}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Retries</p>
          <p className="mt-1 font-medium text-navy">{job.retryCount}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Created</p>
          <p className="mt-1 font-medium text-navy">{new Date(job.createdAt).toLocaleString()}</p>
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

  const summaryCards: Array<{ label: string; value: number; icon: ReactNode; tone?: SummaryCardTone }> = summary
    ? [
        { label: 'Backlog', value: summary.backlog, icon: <Inbox className="h-5 w-5" /> },
        { label: 'Overdue', value: summary.overdue, icon: <AlertTriangle className="h-5 w-5" />, tone: summary.overdue > 0 ? 'danger' : 'default' },
        { label: 'SLA Risk', value: summary.slaRisk, icon: <Clock className="h-5 w-5" />, tone: summary.slaRisk > 0 ? 'warning' : 'default' },
        { label: 'Completed', value: summary.statusDistribution.completed, icon: <CheckCircle2 className="h-5 w-5" />, tone: 'success' },
      ]
    : [];

  return (
    <MotionConfig reducedMotion="user">
      <AdminRouteWorkspace role="main" aria-label="Review operations">
        {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

        <motion.div
          initial={{ opacity: 0, y: 8 }}
          animate={{ opacity: 1, y: 0 }}
          transition={cardTransition}
        >
          <AdminRouteSectionHeader
            title="Review Operations"
            description="Manage productive-skill review queue health, expert assignment, and failure recovery from one operational view."
          />
        </motion.div>

        <AsyncStateWrapper
          status={pageStatus}
          onRetry={() => window.location.reload()}
          emptyContent={
            <EmptyState
              icon={<Inbox className="h-10 w-10 text-muted" />}
              title="No review work is currently visible"
              description="Adjust the filters or wait for queued, failed, or stuck reviews to appear."
            />
          }
        >
          {summary ? (
            <motion.div
              className="grid gap-4 md:grid-cols-2 xl:grid-cols-4"
              initial="hidden"
              animate="show"
              variants={{
                hidden: {},
                show: {
                  transition: { staggerChildren: prefersReducedMotion ? 0 : 0.05 },
                },
              }}
            >
              {summaryCards.map((card) => (
                <motion.div
                  key={card.label}
                  variants={{
                    hidden: { opacity: 0, y: 10 },
                    show: { opacity: 1, y: 0 },
                  }}
                  transition={cardTransition}
                >
                  <AdminRouteSummaryCard
                    label={card.label}
                    value={card.value}
                    icon={card.icon}
                    tone={card.tone}
                  />
                </motion.div>
              ))}
            </motion.div>
          ) : null}

          {summary && totalReviews > 0 ? (
            <motion.div initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} transition={cardTransition}>
              <AdminRoutePanel title="Queue Distribution" description="The distribution bar below is computed from live review summary data.">
                <div className="space-y-3">
                  <div className="flex h-3 overflow-hidden rounded-full bg-background-light">
                    <motion.div
                      className="bg-gray-300"
                      initial={{ width: 0 }}
                      animate={{ width: `${(summary.statusDistribution.pending / totalReviews) * 100}%` }}
                      transition={cardTransition}
                    />
                    <motion.div
                      className="bg-amber-400"
                      initial={{ width: 0 }}
                      animate={{ width: `${(summary.statusDistribution.inProgress / totalReviews) * 100}%` }}
                      transition={cardTransition}
                    />
                    <motion.div
                      className="bg-emerald-500"
                      initial={{ width: 0 }}
                      animate={{ width: `${(summary.statusDistribution.completed / totalReviews) * 100}%` }}
                      transition={cardTransition}
                    />
                  </div>
                  <div className="flex flex-wrap gap-4 text-sm text-muted">
                    <span>Pending: {summary.statusDistribution.pending}</span>
                    <span>In progress: {summary.statusDistribution.inProgress}</span>
                    <span>Completed: {summary.statusDistribution.completed}</span>
                  </div>
                </div>
              </AdminRoutePanel>
            </motion.div>
          ) : null}

          <motion.div initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} transition={cardTransition}>
            <AdminRoutePanel title="Live Queue" description="Assign or cancel queued reviews with real backend mutations.">
              <FilterBar groups={filterGroups} selected={filters} onChange={handleFilterChange} onClear={() => setFilters({ status: [], priority: [] })} />
              <DataTable columns={queueColumns} data={queue} keyExtractor={(item) => item.id} mobileCardRender={queueMobileCardRender} />
            </AdminRoutePanel>
          </motion.div>

          {failures ? (
            <motion.div
              className="grid gap-6 xl:grid-cols-3"
              initial="hidden"
              animate="show"
              variants={{
                hidden: {},
                show: {
                  transition: { staggerChildren: prefersReducedMotion ? 0 : 0.05 },
                },
              }}
            >
              {[
                {
                  title: 'Failed Reviews',
                  description: `${failures.summary.failedReviewCount} failed review requests currently need recovery.`,
                  content:
                    failures.failedReviews.length === 0 ? (
                      <p className="text-sm text-muted">No failed review requests at the moment.</p>
                    ) : (
                      <DataTable columns={failedReviewColumns} data={failures.failedReviews} keyExtractor={(item) => item.id} mobileCardRender={failedReviewMobileCardRender} />
                    ),
                },
                {
                  title: 'Stuck Reviews',
                  description: `${failures.summary.stuckReviewCount} reviews have been in review too long and may need intervention.`,
                  content:
                    failures.stuckReviews.length === 0 ? (
                      <p className="text-sm text-muted">No stuck reviews detected.</p>
                    ) : (
                      <DataTable columns={stuckReviewColumns} data={failures.stuckReviews} keyExtractor={(item) => item.id} mobileCardRender={stuckReviewMobileCardRender} />
                    ),
                },
                {
                  title: 'Failed Jobs',
                  description: `${failures.summary.failedJobCount} background jobs failed in the review pipeline.`,
                  content:
                    failures.failedJobs.length === 0 ? (
                      <p className="text-sm text-muted">No failed background jobs detected.</p>
                    ) : (
                      <DataTable columns={failedJobColumns} data={failures.failedJobs} keyExtractor={(item) => item.id} mobileCardRender={failedJobMobileCardRender} />
                    ),
                },
              ].map((panel) => (
                <motion.div
                  key={panel.title}
                  variants={{
                    hidden: { opacity: 0, y: 10 },
                    show: { opacity: 1, y: 0 },
                  }}
                  transition={cardTransition}
                >
                  <AdminRoutePanel title={panel.title} description={panel.description}>
                    {panel.content}
                  </AdminRoutePanel>
                </motion.div>
              ))}
            </motion.div>
          ) : null}
        </AsyncStateWrapper>

        <Modal open={Boolean(assignTarget)} onClose={() => setAssignTarget(null)} title="Assign Review">
          <div className="space-y-4 py-2">
            <div className="rounded-xl border border-gray-200 bg-background-light p-3 text-sm text-muted">
              {assignTarget ? `Assign review ${assignTarget.id} for ${assignTarget.learnerName} (${assignTarget.subtestCode}).` : 'Select an expert to continue.'}
            </div>
            <Select
              label="Expert"
              value={selectedExpertId}
              onChange={(event) => setSelectedExpertId(event.target.value)}
              options={experts.map((expert) => ({ value: expert.id, label: `${expert.name} (${expert.email})` }))}
            />
            <div className="flex justify-end gap-3 border-t border-gray-200 pt-4">
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
            <div className="flex justify-end gap-3 border-t border-gray-200 pt-4">
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
