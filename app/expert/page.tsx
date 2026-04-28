'use client';

import { useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import {
  ArrowRight,
  CalendarClock,
  CheckCircle2,
  Clock3,
  FilePenLine,
  GraduationCap,
  Inbox,
  ShieldAlert,
  Sparkles,
  Users,
} from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Button } from '@/components/ui/button';
import { EmptyState } from '@/components/ui/empty-error';
import { InlineAlert } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';
import { fetchExpertDashboard, isApiError } from '@/lib/api';
import { useExpertAuth } from '@/lib/hooks/use-expert-auth';
import type { ExpertDashboardData, ReviewRequest } from '@/lib/types/expert';

type AsyncStatus = 'loading' | 'error' | 'success';

function reviewRoute(review: ReviewRequest) {
  return `/expert/review/${review.type}/${review.id}`;
}

function toTitleCase(value: string) {
  return value
    .replace(/[_-]/g, ' ')
    .split(' ')
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ');
}

function formatDateTime(value?: string | null) {
  if (!value) return 'Not updated yet';
  return new Intl.DateTimeFormat('en-AU', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value));
}

function formatReviewSummary(review: ReviewRequest) {
  return `${toTitleCase(review.type)} | ${toTitleCase(review.profession)} | ${review.id}`;
}

function ExpertReviewRow({
  review,
  onOpen,
}: {
  review: ReviewRequest;
  onOpen: () => void;
}) {
  const statusTone = review.isOverdue
    ? 'border-rose-200 bg-rose-50 text-rose-700'
    : review.status === 'in_progress'
      ? 'border-emerald-200 bg-emerald-50 text-emerald-700'
      : 'border-slate-200 bg-slate-100 text-slate-600';

  return (
    <button
      type="button"
      onClick={onOpen}
      className="pressable flex w-full items-start justify-between gap-4 rounded-xl border border-gray-200 bg-surface p-4 text-left hover:border-primary/40 hover:shadow-sm"
    >
      <div className="space-y-1">
        <p className="text-sm font-semibold text-navy">{review.learnerName}</p>
        <p className="text-xs text-muted">{formatReviewSummary(review)}</p>
        <div className="flex flex-wrap items-center gap-3 text-xs text-muted">
          <span>SLA {formatDateTime(review.slaDue)}</span>
          <span>Priority {review.priority}</span>
          <span>AI {review.aiConfidence}</span>
        </div>
      </div>
      <div className={`rounded-full border px-2.5 py-1 text-xs font-medium ${statusTone}`}>
        {review.status.replace(/_/g, ' ')}
      </div>
    </button>
  );
}

function ExpertActivityRow({
  title,
  description,
  timestamp,
  onOpen,
}: {
  title: string;
  description?: string | null;
  timestamp: string;
  onOpen?: () => void;
}) {
  return (
    <li className="rounded-xl border border-gray-200 bg-surface p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="space-y-1">
          <p className="text-sm font-semibold text-navy">{title}</p>
          {description ? <p className="text-sm text-muted">{description}</p> : null}
          <p className="text-xs text-muted/80">{formatDateTime(timestamp)}</p>
        </div>
        {onOpen ? (
          <Button variant="ghost" size="sm" onClick={onOpen}>
            Open
          </Button>
        ) : null}
      </div>
    </li>
  );
}

function EmptyDraftState({ actionLabel, onAction }: { actionLabel: string; onAction: () => void }) {
  return (
    <EmptyState
      title="No saved drafts to resume"
      description="New draft workspaces will appear here after the first save."
      action={{ label: actionLabel, onClick: onAction }}
    />
  );
}

export default function ExpertDashboardPage() {
  const router = useRouter();
  const { expert } = useExpertAuth();
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
          setErrorMessage(isApiError(error) ? error.userMessage : 'Unable to load the tutor dashboard right now.');
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

  const heroHighlights = useMemo(() => {
    if (!dashboard) return [];

    return [
      {
        icon: Inbox,
        label: 'Assigned reviews',
        value: String(dashboard.activeAssignedReviews),
      },
      {
        icon: ShieldAlert,
        label: 'Overdue / at risk',
        value: String(dashboard.overdueAssignedReviews),
      },
      {
        icon: GraduationCap,
        label: 'Calibration due',
        value: String(dashboard.calibrationDueCount),
      },
    ];
  }, [dashboard]);

  return (
    <AsyncStateWrapper
      status={status}
      onRetry={() => setReloadToken((current) => current + 1)}
      errorMessage={errorMessage ?? undefined}
    >
      {dashboard ? (
        <div className="space-y-6">
            {expert?.isOnboardingComplete === false && (
              <InlineAlert
                variant="info"
                title="Complete your tutor onboarding"
                action={(
                  <Button variant="primary" size="sm" onClick={() => router.push('/expert/onboarding')}>
                    Start Onboarding
                  </Button>
                )}
              >
                Set up your profile, qualifications, schedule, and rates so learners can find you and book sessions.
              </InlineAlert>
            )}
            <LearnerPageHero
              eyebrow="Current Focus"
              icon={Sparkles}
              accent="primary"
              title="Keep owned reviews and exam signals in view"
              description="Use the dashboard to decide the next action, check readiness evidence, and move without guesswork."
              highlights={heroHighlights}
              aside={(
                <div className="space-y-4 rounded-2xl border border-gray-200 bg-background-light p-4 shadow-sm">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.16em] text-muted">Quick Actions</p>
                    <p className="mt-1 text-sm text-muted">Jump straight into the highest-priority expert workflows.</p>
                  </div>
                  <div className="space-y-2">
                    <Button fullWidth onClick={() => router.push('/expert/queue')}>
                      Open Queue
                    </Button>
                    <Button fullWidth variant="outline" onClick={() => router.push('/expert/calibration')}>
                      Open Calibration
                    </Button>
                  </div>
                  <div className="flex items-center gap-2 text-xs text-muted">
                    <Clock3 className="h-4 w-4" />
                    Updated {formatDateTime(dashboard.generatedAt)}
                  </div>
                </div>
              )}
            />

            <InlineAlert
              variant="warning"
              title="Recommended security step"
              action={(
                <Button variant="outline" size="sm" onClick={() => router.push('/mfa/setup?next=/expert')}>
                  Set up MFA
                </Button>
              )}
            >
              Multi-factor authentication is recommended for privileged access. You can keep working without it, but enabling an authenticator app adds a much stronger layer of account protection.
            </InlineAlert>

            <section className="space-y-4">
              <LearnerSurfaceSectionHeader
                eyebrow="Expert workflow"
                title="Resume what is already in motion"
                description="Same visual rhythm as the learner dashboard, tuned for tutor review work."
                action={(
                  <Button variant="ghost" size="sm" onClick={() => router.push('/expert/queue')}>
                    View Queue <ArrowRight className="h-4 w-4" />
                  </Button>
                )}
              />

              <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
                <LearnerSurfaceCard
                  card={{
                    kind: 'task',
                    sourceType: 'backend_summary',
                    eyebrow: 'Recommended Next',
                    eyebrowIcon: Sparkles,
                    accent: 'primary',
                    title: 'Work to Resume',
                    description: 'Jump back into a saved draft or an owned review without going through the full queue.',
                    metaItems: [
                      { icon: FilePenLine, label: `${dashboard.savedDraftCount} saved drafts` },
                      { icon: Inbox, label: `${dashboard.activeAssignedReviews} assigned reviews` },
                      { icon: GraduationCap, label: `${dashboard.calibrationDueCount} calibration due` },
                    ],
                    primaryAction: {
                      label: 'Open Review Queue',
                      href: '/expert/queue',
                    },
                    secondaryAction: {
                      label: 'Open Assigned Queue',
                      href: '/expert/queue?assignment=assigned',
                      variant: 'outline',
                    },
                  }}
                >
                  <div className="space-y-3">
                    {dashboard.resumeDrafts.length === 0 ? (
                      <EmptyDraftState actionLabel="Open Review Queue" onAction={() => router.push('/expert/queue')} />
                    ) : (
                      dashboard.resumeDrafts.map((review) => (
                        <ExpertReviewRow
                          key={review.id}
                          review={review}
                          onOpen={() => router.push(reviewRoute(review))}
                        />
                      ))
                    )}
                  </div>
                </LearnerSurfaceCard>

                <LearnerSurfaceCard
                  card={{
                    kind: 'status',
                    sourceType: 'backend_summary',
                    eyebrow: 'Schedule',
                    eyebrowIcon: CalendarClock,
                    accent: 'navy',
                    title: "Today's Availability",
                    description: 'Keep operational expectations aligned with the hours you have configured.',
                    metaItems: [
                      { icon: Users, label: `${dashboard.assignedLearnerCount} assigned learners` },
                      { icon: Clock3, label: dashboard.availability.timezone },
                      { icon: CheckCircle2, label: dashboard.availability.activeToday ? 'Active today' : 'Paused today' },
                    ],
                    primaryAction: {
                      label: 'Update Schedule',
                      href: '/expert/schedule',
                    },
                    secondaryAction: {
                      label: 'View Metrics',
                      href: '/expert/metrics',
                      variant: 'outline',
                    },
                  }}
                >
                  <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                    <div className="rounded-xl bg-background-light p-3">
                      <p className="text-xs font-semibold uppercase tracking-[0.14em] text-muted">Availability</p>
                      <p className="mt-2 text-sm font-semibold text-navy">{availabilityHint}</p>
                    </div>
                    <div className="rounded-xl bg-background-light p-3">
                      <p className="text-xs font-semibold uppercase tracking-[0.14em] text-muted">Turnaround</p>
                      <p className="mt-2 text-lg font-semibold text-navy">{dashboard.metrics.averageTurnaroundHours}h</p>
                    </div>
                    <div className="rounded-xl bg-background-light p-3">
                      <p className="text-xs font-semibold uppercase tracking-[0.14em] text-muted">Calibration alignment</p>
                      <p className="mt-2 text-lg font-semibold text-navy">{dashboard.metrics.averageCalibrationAlignment}%</p>
                    </div>
                    <div className="rounded-xl bg-background-light p-3">
                      <p className="text-xs font-semibold uppercase tracking-[0.14em] text-muted">SLA compliance</p>
                      <p className="mt-2 text-lg font-semibold text-navy">{dashboard.metrics.averageSlaCompliance}%</p>
                    </div>
                  </div>
                </LearnerSurfaceCard>
              </div>
            </section>

            <section className="space-y-4">
              <LearnerSurfaceSectionHeader
                eyebrow="Signals"
                title="Queue and activity stay visible together"
                description="Expert work is easier to scan when queue health and recent actions are presented in the same rhythm."
                action={(
                  <Button variant="ghost" size="sm" onClick={() => router.push('/expert/learners')}>
                    View Learners <ArrowRight className="h-4 w-4" />
                  </Button>
                )}
              />

              <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
                <LearnerSurfaceCard
                  card={{
                    kind: 'navigation',
                    sourceType: 'backend_summary',
                    eyebrow: 'Queue',
                    eyebrowIcon: Inbox,
                    accent: dashboard.overdueAssignedReviews > 0 ? 'rose' : 'emerald',
                    title: 'Assigned Review Queue',
                    description: 'These are the owned reviews that currently define your working set.',
                    statusLabel: dashboard.overdueAssignedReviews > 0
                      ? `${dashboard.overdueAssignedReviews} overdue`
                      : 'On track',
                    metaItems: [
                      { icon: ShieldAlert, label: `${dashboard.overdueAssignedReviews} overdue / at risk` },
                      { icon: CheckCircle2, label: `${dashboard.metrics.totalReviewsCompleted} completed` },
                      { icon: GraduationCap, label: `${dashboard.calibrationDueCount} calibration due` },
                    ],
                    primaryAction: {
                      label: 'Open Assigned Queue',
                      href: '/expert/queue?assignment=assigned',
                    },
                    secondaryAction: {
                      label: 'Open Queue',
                      href: '/expert/queue',
                      variant: 'outline',
                    },
                  }}
                >
                  <div className="space-y-3">
                    {dashboard.assignedReviews.length === 0 ? (
                      <EmptyState
                        title="No assigned reviews"
                        description="Claim a queued review to start your next tutor workspace."
                        action={{ label: 'Open Queue', onClick: () => router.push('/expert/queue') }}
                      />
                    ) : (
                      dashboard.assignedReviews.map((review) => (
                        <ExpertReviewRow
                          key={review.id}
                          review={review}
                          onOpen={() => router.push(reviewRoute(review))}
                        />
                      ))
                    )}
                  </div>
                </LearnerSurfaceCard>

                <LearnerSurfaceCard
                  card={{
                    kind: 'insight',
                    sourceType: 'backend_summary',
                    eyebrow: 'Activity',
                    eyebrowIcon: CheckCircle2,
                    accent: 'slate',
                    title: 'Recent Activity',
                    description: 'Audit-backed expert actions from the latest operational window.',
                    metaItems: [
                      { icon: Clock3, label: `Generated ${formatDateTime(dashboard.generatedAt)}` },
                      { icon: CheckCircle2, label: `${dashboard.metrics.totalReviewsCompleted} reviews completed` },
                      { icon: Users, label: `${dashboard.assignedLearnerCount} learners in scope` },
                    ],
                    primaryAction: {
                      label: 'Assigned Learners',
                      href: '/expert/learners',
                    },
                    secondaryAction: {
                      label: 'Open Queue',
                      href: '/expert/queue',
                      variant: 'outline',
                    },
                  }}
                >
                  {dashboard.recentActivity.length === 0 ? (
                    <EmptyState
                      title="No recent activity yet"
                      description="Claim or complete work to build your activity timeline."
                    />
                  ) : (
                    <ol className="space-y-3">
                      {dashboard.recentActivity.map((activity) => (
                        <ExpertActivityRow
                          key={`${activity.timestamp}-${activity.title}`}
                          title={activity.title}
                          description={activity.description}
                          timestamp={activity.timestamp}
                          onOpen={activity.route ? () => router.push(activity.route ?? '/expert') : undefined}
                        />
                      ))}
                    </ol>
                  )}
                </LearnerSurfaceCard>
              </div>
            </section>

            {dashboard.overdueAssignedReviews > 0 ? (
              <InlineAlert variant="warning" title="Immediate SLA attention recommended">
                {dashboard.overdueAssignedReviews} review{dashboard.overdueAssignedReviews === 1 ? '' : 's'} in your ownership set are already overdue. Resume those first before claiming new work from the shared queue.
              </InlineAlert>
            ) : (
              <InlineAlert variant="success" title="Operational status is healthy">
                No owned reviews are currently overdue. You can use the queue to pick up additional work when ready.
              </InlineAlert>
            )}
        </div>
      ) : null}
    </AsyncStateWrapper>
  );
}
