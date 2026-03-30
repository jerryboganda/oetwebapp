'use client';

import { useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import { CalendarClock, CheckCircle2, FilePenLine, GraduationCap, Inbox, ShieldAlert } from 'lucide-react';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { EmptyState } from '@/components/ui/empty-error';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { ExpertFreshnessBadge, ExpertMetricCard, ExpertPageHeader, ExpertSectionPanel } from '@/components/domain/expert-surface';
import { fetchExpertDashboard, isApiError } from '@/lib/api';
import type { ExpertDashboardData, ReviewRequest } from '@/lib/types/expert';
import { analytics } from '@/lib/analytics';

type AsyncStatus = 'loading' | 'error' | 'success';

function reviewRoute(review: ReviewRequest) {
  return `/expert/review/${review.type}/${review.id}`;
}

export default function ExpertDashboardPage() {
  const router = useRouter();
  const [dashboard, setDashboard] = useState<ExpertDashboardData | null>(null);
  const [status, setStatus] = useState<AsyncStatus>('loading');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [reloadToken, setReloadToken] = useState(0);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        setStatus('loading');
        setErrorMessage(null);
        const data = await fetchExpertDashboard();
        if (cancelled) return;
        setDashboard(data);
        setStatus('success');
        analytics.track('expert_dashboard_viewed', {
          activeAssignedReviews: data.activeAssignedReviews,
          savedDraftCount: data.savedDraftCount,
        });
      } catch (error) {
        if (!cancelled) {
          setErrorMessage(isApiError(error) ? error.userMessage : 'Unable to load the expert dashboard right now.');
          setStatus('error');
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [reloadToken]);

  const availabilityHint = useMemo(() => {
    if (!dashboard) return 'Availability information is still loading.';
    if (!dashboard.availability.activeToday) {
      return `No active availability window is configured for ${dashboard.availability.todayKey}.`;
    }
    return `${dashboard.availability.todayKey} window: ${dashboard.availability.todayWindow ?? 'set in schedule'}.`;
  }, [dashboard]);

  return (
    <div className="mx-auto flex max-w-7xl flex-col gap-6 p-4 md:p-8" role="main" aria-label="Expert dashboard">
      <ExpertPageHeader
        meta="Expert Operations"
        title="Dashboard"
        description="Run your expert workflow from one place: active reviews, draft recovery, calibration obligations, and the latest operational activity."
        actions={
          <>
            <Button variant="outline" onClick={() => router.push('/expert/queue')}>
              Open Queue
            </Button>
            <Button onClick={() => router.push('/expert/calibration')}>
              Open Calibration
            </Button>
          </>
        }
      />

      <AsyncStateWrapper
        status={status}
        onRetry={() => setReloadToken((current) => current + 1)}
        errorMessage={errorMessage ?? undefined}
      >
        {dashboard && (
          <>
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-5">
              <ExpertMetricCard
                label="Assigned Reviews"
                value={dashboard.activeAssignedReviews}
                hint="Reviews currently in your ownership window."
                icon={<Inbox className="h-5 w-5" />}
              />
              <ExpertMetricCard
                label="Overdue / At Risk"
                value={dashboard.overdueAssignedReviews}
                hint="Reviews already overdue and needing immediate attention."
                tone={dashboard.overdueAssignedReviews > 0 ? 'danger' : 'success'}
                icon={<ShieldAlert className="h-5 w-5" />}
              />
              <ExpertMetricCard
                label="Saved Drafts"
                value={dashboard.savedDraftCount}
                hint="Draft review workspaces you can resume."
                tone={dashboard.savedDraftCount > 0 ? 'warning' : 'default'}
                icon={<FilePenLine className="h-5 w-5" />}
              />
              <ExpertMetricCard
                label="Calibration Due"
                value={dashboard.calibrationDueCount}
                hint="Benchmark cases still awaiting your submission."
                tone={dashboard.calibrationDueCount > 0 ? 'warning' : 'success'}
                icon={<GraduationCap className="h-5 w-5" />}
              />
              <ExpertMetricCard
                label="SLA Compliance"
                value={`${dashboard.metrics.averageSlaCompliance}%`}
                hint={`${dashboard.metrics.totalReviewsCompleted} completed reviews in scope.`}
                tone={dashboard.metrics.averageSlaCompliance >= 95 ? 'success' : 'default'}
                icon={<CheckCircle2 className="h-5 w-5" />}
              />
            </div>

            <InlineAlert variant="info" title="Privacy scoped learner access">
              The learner directory and review context panels only expose learners and evidence tied to reviews assigned to you, including historical assignments needed for continuity.
            </InlineAlert>

            <div className="grid grid-cols-1 gap-6 xl:grid-cols-[1.35fr_0.95fr]">
              <ExpertSectionPanel
                title="Work To Resume"
                description="Jump back into owned reviews or saved drafts without going through the full queue."
                actions={<ExpertFreshnessBadge value={dashboard.generatedAt} />}
              >
                {dashboard.resumeDrafts.length === 0 ? (
                  <EmptyState
                    title="No saved drafts to resume"
                    description="New draft workspaces will appear here after the first save."
                    action={{ label: 'Open Review Queue', onClick: () => router.push('/expert/queue') }}
                  />
                ) : (
                  <div className="space-y-3">
                    {dashboard.resumeDrafts.map((review) => (
                      <button
                        key={review.id}
                        type="button"
                        onClick={() => router.push(reviewRoute(review))}
                        className="flex w-full flex-col gap-2 rounded-2xl border border-slate-200 bg-white p-4 text-left transition hover:border-primary/40 hover:shadow-sm"
                      >
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <div>
                            <p className="text-sm font-semibold text-slate-900">{review.learnerName}</p>
                            <p className="text-xs text-slate-500">
                              {review.type} review | {review.profession.replace(/_/g, ' ')} | {review.id}
                            </p>
                          </div>
                          <span className={`inline-flex items-center rounded-full px-2.5 py-1 text-xs font-medium ${review.isOverdue ? 'bg-rose-50 text-rose-700' : 'bg-amber-50 text-amber-700'}`}>
                            {review.isOverdue ? 'Overdue' : 'Draft ready'}
                          </span>
                        </div>
                        <div className="flex flex-wrap items-center gap-4 text-xs text-slate-500">
                          <span>SLA: {new Date(review.slaDue).toLocaleString()}</span>
                          <span>AI confidence: {review.aiConfidence}</span>
                          <span>Status: {review.status.replace(/_/g, ' ')}</span>
                        </div>
                      </button>
                    ))}
                  </div>
                )}
              </ExpertSectionPanel>

              <ExpertSectionPanel
                title="Today's Availability"
                description="Keep operational expectations aligned with the hours you have configured."
                actions={<Button variant="outline" size="sm" onClick={() => router.push('/expert/schedule')}>Update Schedule</Button>}
              >
                <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                  <div className="flex items-center justify-between gap-3">
                    <div>
                      <p className="text-sm font-semibold text-slate-900">{dashboard.availability.timezone}</p>
                      <p className="text-sm text-slate-500">{availabilityHint}</p>
                    </div>
                    <CalendarClock className="h-5 w-5 text-slate-400" />
                  </div>
                  <div className="mt-4 flex flex-wrap items-center gap-4 text-xs text-slate-500">
                    <span>Draft count: {dashboard.savedDraftCount}</span>
                    <span>Assigned learners: {dashboard.assignedLearnerCount}</span>
                    <ExpertFreshnessBadge value={dashboard.availability.lastUpdatedAt} />
                  </div>
                </div>

                <div className="rounded-2xl border border-slate-200 bg-white p-4">
                  <h3 className="text-sm font-semibold text-slate-900">Operational now</h3>
                  <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2">
                    <div className="rounded-xl bg-slate-50 p-3">
                      <p className="text-xs font-medium uppercase tracking-[0.12em] text-slate-500">Turnaround</p>
                      <p className="mt-2 text-lg font-semibold text-slate-900">{dashboard.metrics.averageTurnaroundHours}h</p>
                    </div>
                    <div className="rounded-xl bg-slate-50 p-3">
                      <p className="text-xs font-medium uppercase tracking-[0.12em] text-slate-500">Calibration Alignment</p>
                      <p className="mt-2 text-lg font-semibold text-slate-900">{dashboard.metrics.averageCalibrationAlignment}%</p>
                    </div>
                  </div>
                </div>
              </ExpertSectionPanel>
            </div>

            <div className="grid grid-cols-1 gap-6 xl:grid-cols-[1.2fr_0.8fr]">
              <ExpertSectionPanel
                title="Assigned Review Queue"
                description="These are the owned reviews that currently define your working set."
                actions={<Button variant="outline" size="sm" onClick={() => router.push('/expert/queue?assignment=assigned')}>Open Assigned Queue</Button>}
              >
                {dashboard.assignedReviews.length === 0 ? (
                  <EmptyState
                    title="No assigned reviews"
                    description="Claim a queued review to start your next expert workspace."
                    action={{ label: 'Open Queue', onClick: () => router.push('/expert/queue') }}
                  />
                ) : (
                  <div className="space-y-3">
                    {dashboard.assignedReviews.map((review) => (
                      <button
                        key={review.id}
                        type="button"
                        onClick={() => router.push(reviewRoute(review))}
                        className="flex w-full items-start justify-between gap-4 rounded-2xl border border-slate-200 bg-white p-4 text-left transition hover:border-primary/40 hover:shadow-sm"
                      >
                        <div className="space-y-1">
                          <p className="text-sm font-semibold text-slate-900">{review.learnerName}</p>
                          <p className="text-xs text-slate-500">
                            {review.type} | {review.profession.replace(/_/g, ' ')} | {review.id}
                          </p>
                          <div className="flex flex-wrap items-center gap-3 text-xs text-slate-500">
                            <span>SLA due {new Date(review.slaDue).toLocaleString()}</span>
                            <span>Priority {review.priority}</span>
                            <span>AI {review.aiConfidence}</span>
                          </div>
                        </div>
                        <div className={`rounded-full px-2.5 py-1 text-xs font-medium ${review.isOverdue ? 'bg-rose-50 text-rose-700' : review.status === 'in_progress' ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-600'}`}>
                          {review.status.replace(/_/g, ' ')}
                        </div>
                      </button>
                    ))}
                  </div>
                )}
              </ExpertSectionPanel>

              <ExpertSectionPanel
                title="Recent Activity"
                description="Audit-backed expert actions from the latest operational window."
                actions={<Button variant="outline" size="sm" onClick={() => router.push('/expert/learners')}>Assigned Learners</Button>}
              >
                {dashboard.recentActivity.length === 0 ? (
                  <EmptyState
                    title="No recent activity yet"
                    description="Claim or complete work to build your activity timeline."
                  />
                ) : (
                  <ol className="space-y-3">
                    {dashboard.recentActivity.map((activity) => (
                      <li key={`${activity.timestamp}-${activity.title}`} className="rounded-2xl border border-slate-200 bg-white p-4">
                        <div className="flex items-start justify-between gap-3">
                          <div className="space-y-1">
                            <p className="text-sm font-semibold text-slate-900">{activity.title}</p>
                            {activity.description ? <p className="text-sm text-slate-500">{activity.description}</p> : null}
                            <p className="text-xs text-slate-400">{new Date(activity.timestamp).toLocaleString()}</p>
                          </div>
                          {activity.route ? (
                            <Button variant="ghost" size="sm" onClick={() => router.push(activity.route ?? '/expert')}>
                              Open
                            </Button>
                          ) : null}
                        </div>
                      </li>
                    ))}
                  </ol>
                )}
              </ExpertSectionPanel>
            </div>

            {dashboard.overdueAssignedReviews > 0 ? (
              <InlineAlert variant="warning" title="Immediate SLA attention recommended">
                {dashboard.overdueAssignedReviews} review{dashboard.overdueAssignedReviews === 1 ? '' : 's'} in your ownership set are already overdue. Resume those first before claiming new work from the shared queue.
              </InlineAlert>
            ) : (
              <InlineAlert variant="success" title="Operational status is healthy">
                No owned reviews are currently overdue. You can use the queue to pick up additional work when ready.
              </InlineAlert>
            )}
          </>
        )}
      </AsyncStateWrapper>
    </div>
  );
}
