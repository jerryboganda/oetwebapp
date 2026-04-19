'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { ArrowRight, Activity, DollarSign, Layers3, Target, TrendingDown, Users } from 'lucide-react';
import { AdminRouteFreshnessBadge, AdminRoutePanel, AdminRouteSectionHeader, AdminRouteSummaryCard, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { getAdminBusinessIntelligenceData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminBusinessIntelligenceData } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'error';

const numberFormatter = new Intl.NumberFormat('en-AU', {
  maximumFractionDigits: 0,
});

const decimalFormatter = new Intl.NumberFormat('en-AU', {
  maximumFractionDigits: 1,
});

const efficiencyBadgeClasses: Record<string, string> = {
  high: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400',
  medium: 'bg-amber-50 text-amber-700 dark:bg-amber-900/30 dark:text-amber-400',
  low: 'bg-danger/10 text-danger dark:bg-danger/10 dark:text-danger',
  'no-data': 'bg-background-light text-muted',
};

function formatCurrency(value: number, maximumFractionDigits = 0) {
  return new Intl.NumberFormat('en-AU', {
    style: 'currency',
    currency: 'AUD',
    maximumFractionDigits,
  }).format(value);
}

function formatPercent(value: number) {
  return `${decimalFormatter.format(value)}%`;
}

function formatNumber(value: number) {
  return numberFormatter.format(value);
}

function formatDecimal(value: number | null) {
  return value == null ? '--' : decimalFormatter.format(value);
}

function formatMinutes(value: number | null) {
  return value == null ? '--' : `${decimalFormatter.format(value)}m`;
}

function formatDurationSeconds(value: number | null) {
  if (value == null) return '--';
  const totalSeconds = Math.max(0, Math.round(value));
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;

  if (minutes === 0) {
    return `${seconds}s`;
  }

  if (seconds === 0) {
    return `${minutes}m`;
  }

  return `${minutes}m ${String(seconds).padStart(2, '0')}s`;
}

function truncate(value: string, maxLength = 40) {
  if (value.length <= maxLength) return value;
  return `${value.slice(0, maxLength - 1)}…`;
}

function barWidth(value: number, maxValue: number) {
  if (maxValue <= 0) return '0%';
  return `${Math.max(0, Math.min(100, (value / maxValue) * 100))}%`;
}

function buildLoadingContent() {
  return (
    <div className="space-y-6">
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        {Array.from({ length: 6 }).map((_, index) => (
          <Skeleton key={`bi-kpi-${index}`} className="h-32 rounded-2xl" />
        ))}
      </div>

      <div className="grid gap-6 xl:grid-cols-2">
        <Skeleton className="h-[30rem] rounded-2xl" />
        <Skeleton className="h-[30rem] rounded-2xl" />
      </div>

      <div className="grid gap-6 xl:grid-cols-2">
        <Skeleton className="h-[34rem] rounded-2xl" />
        <Skeleton className="h-[34rem] rounded-2xl" />
      </div>

      <Skeleton className="h-28 rounded-2xl" />
    </div>
  );
}

export default function BusinessIntelligencePage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [data, setData] = useState<AdminBusinessIntelligenceData | null>(null);

  useEffect(() => {
    let cancelled = false;

    void analytics.track('admin_view', { page: 'business-intelligence' });

    async function load() {
      setPageStatus('loading');

      try {
        const result = await getAdminBusinessIntelligenceData();

        if (cancelled) {
          return;
        }

        setData(result);
        setPageStatus('success');
      } catch (error) {
        console.error(error);

        if (!cancelled) {
          setPageStatus('error');
        }
      }
    }

    load();

    return () => {
      cancelled = true;
    };
  }, []);

  if (!isAuthenticated || role !== 'admin') return null;

  const subscription = data?.subscriptionHealth;
  const cohorts = data?.cohortAnalysis.cohorts ?? [];
  const contentItems = data?.contentEffectiveness.items ?? [];
  const experts = data?.expertEfficiency.experts ?? [];
  const topCohort = cohorts[0];
  const topContent = contentItems[0];
  const topExpert = experts[0];

  const topPlanRevenue = Math.max(1, ...(subscription?.revenueByPlan.map((plan) => plan.monthlyRevenue) ?? []));
  const maxTrendVolume = Math.max(1, ...(subscription?.monthlyTrend.map((point) => point.newSubscriptions + point.cancellations) ?? []));

  return (
    <AdminRouteWorkspace role="main" aria-label="Business intelligence">
      <AdminRouteSectionHeader
        title="Business Intelligence"
        description="Live backend aggregates for subscription health, learner cohorts, content effectiveness, and expert throughput."
        actions={<AdminRouteFreshnessBadge value={data?.generatedAt} />}
        meta="Live backend aggregates"
      />

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => window.location.reload()}
        loadingContent={buildLoadingContent()}
        errorMessage="Unable to load business intelligence data."
      >
        {data ? (
          <>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              <AdminRouteSummaryCard
                label="Monthly recurring revenue"
                value={formatCurrency(subscription?.mrr ?? 0)}
                hint={`${formatNumber(subscription?.activeSubscriptions ?? 0)} active subscriptions`}
                icon={<DollarSign className="h-5 w-5" />}
                tone="success"
              />
              <AdminRouteSummaryCard
                label="Active subscriptions"
                value={formatNumber(subscription?.activeSubscriptions ?? 0)}
                hint={`ARPU ${formatCurrency(subscription?.arpu ?? 0, 2)}`}
                icon={<Users className="h-5 w-5" />}
              />
              <AdminRouteSummaryCard
                label="Churn rate"
                value={formatPercent(subscription?.churnRate ?? 0)}
                hint={`${formatNumber(subscription?.newSubscriptionsThisMonth ?? 0)} new this month`}
                icon={<TrendingDown className="h-5 w-5" />}
                tone={(subscription?.churnRate ?? 0) > 5 ? 'warning' : 'success'}
              />
              <AdminRouteSummaryCard
                label="Total learners"
                value={formatNumber(data.cohortAnalysis.totalLearners)}
                hint={`Grouped by ${data.cohortAnalysis.groupBy}`}
                icon={<Layers3 className="h-5 w-5" />}
              />
              <AdminRouteSummaryCard
                label="Content effectiveness"
                value={formatDecimal(topContent?.effectivenessScore ?? null)}
                hint={topContent ? truncate(topContent.title) : 'No published content yet'}
                icon={<Target className="h-5 w-5" />}
                tone={topContent ? 'success' : 'default'}
              />
              <AdminRouteSummaryCard
                label="Expert throughput"
                value={formatDecimal(data.expertEfficiency.summary.averageReviewsPerExpertPerDay)}
                hint={`${formatNumber(data.expertEfficiency.summary.activeExperts)} active of ${formatNumber(data.expertEfficiency.summary.totalExperts)} experts`}
                icon={<Activity className="h-5 w-5" />}
              />
            </div>

            <div className="grid gap-6 xl:grid-cols-2">
              <AdminRoutePanel
                title="Subscription Health"
                description={`ARPU ${formatCurrency(subscription?.arpu ?? 0, 2)}, trial conversion ${formatPercent(subscription?.trialConversionRate ?? 0)}, and six-month movement.`}
              >
                <div className="grid gap-4 md:grid-cols-3">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">ARPU</p>
                    <p className="text-xl font-semibold text-navy">{formatCurrency(subscription?.arpu ?? 0, 2)}</p>
                  </div>
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Trial conversion</p>
                    <p className="text-xl font-semibold text-navy">{formatPercent(subscription?.trialConversionRate ?? 0)}</p>
                  </div>
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">New this month</p>
                    <p className="text-xl font-semibold text-navy">{formatNumber(subscription?.newSubscriptionsThisMonth ?? 0)}</p>
                  </div>
                </div>

                <div className="space-y-4">
                  {subscription?.revenueByPlan.length ? subscription.revenueByPlan.map((plan) => (
                    <div key={plan.planId} className="space-y-2 rounded-2xl border border-border bg-background-light p-4">
                      <div className="flex items-center justify-between gap-3">
                        <div className="min-w-0">
                          <p className="truncate text-sm font-semibold text-navy">{plan.planName}</p>
                          <p className="text-xs text-muted">{formatNumber(plan.subscribers)} subscribers</p>
                        </div>
                        <p className="text-sm font-semibold text-navy">{formatCurrency(plan.monthlyRevenue)}</p>
                      </div>
                      <div className="h-2 overflow-hidden rounded-full bg-surface">
                        <div className="h-full rounded-full bg-primary" style={{ width: barWidth(plan.monthlyRevenue, topPlanRevenue) }} />
                      </div>
                    </div>
                  )) : (
                    <p className="text-sm text-muted">No subscription plan revenue is available yet.</p>
                  )}
                </div>

                <div className="space-y-3">
                  <div className="flex items-center justify-between gap-3">
                    <h3 className="text-sm font-semibold text-navy">Monthly trend</h3>
                    <p className="text-xs text-muted">New subscriptions and cancellations</p>
                  </div>

                  <div className="space-y-3">
                    {subscription?.monthlyTrend.length ? subscription.monthlyTrend.map((point) => {
                      const total = point.newSubscriptions + point.cancellations;

                      return (
                        <div key={point.month} className="flex items-center gap-3">
                          <span className="w-16 text-xs font-medium text-muted">{point.month}</span>
                          <div className="flex h-6 flex-1 overflow-hidden rounded-full bg-surface">
                            <div className="flex h-full" style={{ width: barWidth(total, maxTrendVolume) }}>
                              <div className="h-full bg-success" style={{ width: total > 0 ? `${(point.newSubscriptions / total) * 100}%` : '0%' }} />
                              <div className="h-full bg-danger" style={{ width: total > 0 ? `${(point.cancellations / total) * 100}%` : '0%' }} />
                            </div>
                          </div>
                          <span className="w-24 text-right text-xs text-muted">+{point.newSubscriptions} / -{point.cancellations}</span>
                        </div>
                      );
                    }) : (
                      <p className="text-sm text-muted">No monthly trend data is available yet.</p>
                    )}
                  </div>
                </div>
              </AdminRoutePanel>

              <AdminRoutePanel
                title="Learner Cohorts"
                description={`Grouped by ${data.cohortAnalysis.groupBy}. ${formatNumber(data.cohortAnalysis.totalLearners)} learners are in view.`}
              >
                <div className="grid gap-3 md:grid-cols-3">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Top cohort</p>
                    <p className="text-lg font-semibold text-navy">{topCohort ? topCohort.cohortName : 'No data'}</p>
                  </div>
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Learners</p>
                    <p className="text-lg font-semibold text-navy">{topCohort ? formatNumber(topCohort.learnerCount) : '--'}</p>
                  </div>
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Avg score</p>
                    <p className="text-lg font-semibold text-navy">{formatDecimal(topCohort?.averageScore ?? null)}</p>
                  </div>
                </div>

                <div className="space-y-3">
                  {cohorts.length ? cohorts.map((cohort) => (
                    <div key={cohort.cohortKey} className="rounded-2xl border border-border bg-background-light p-4">
                      <div className="flex items-start justify-between gap-4">
                        <div className="min-w-0">
                          <p className="truncate text-sm font-semibold text-navy">{cohort.cohortName}</p>
                          <p className="text-xs text-muted">{formatNumber(cohort.evaluationCount)} evaluations, {formatNumber(cohort.activeLastMonth)} active in the last 30 days</p>
                        </div>
                        <Badge variant="outline" className="text-[10px] uppercase tracking-[0.12em]">
                          {formatNumber(cohort.learnerCount)} learners
                        </Badge>
                      </div>

                      <div className="mt-4 grid gap-3 sm:grid-cols-3">
                        <div>
                          <p className="text-xs uppercase tracking-[0.12em] text-muted">Avg score</p>
                          <p className="text-sm font-semibold text-navy">{formatDecimal(cohort.averageScore)}</p>
                        </div>
                        <div>
                          <p className="text-xs uppercase tracking-[0.12em] text-muted">Evaluations</p>
                          <p className="text-sm font-semibold text-navy">{formatNumber(cohort.evaluationCount)}</p>
                        </div>
                        <div>
                          <p className="text-xs uppercase tracking-[0.12em] text-muted">Active 30d</p>
                          <p className="text-sm font-semibold text-navy">{formatNumber(cohort.activeLastMonth)}</p>
                        </div>
                      </div>
                    </div>
                  )) : (
                    <p className="text-sm text-muted">No cohort data is available yet.</p>
                  )}
                </div>
              </AdminRoutePanel>
            </div>

            <div className="grid gap-6 xl:grid-cols-2">
              <AdminRoutePanel
                title="Content Effectiveness"
                description={`Top ${contentItems.length || 0} published items ranked by effectiveness.`}
              >
                <div className="grid gap-3 md:grid-cols-3">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Top item</p>
                    <p className="text-lg font-semibold text-navy">{topContent ? truncate(topContent.title) : 'No data'}</p>
                  </div>
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Top score</p>
                    <p className="text-lg font-semibold text-navy">{formatDecimal(topContent?.effectivenessScore ?? null)}</p>
                  </div>
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Completion</p>
                    <p className="text-lg font-semibold text-navy">{topContent ? formatPercent(topContent.completionRate) : '--'}</p>
                  </div>
                </div>

                <div className="space-y-3">
                  {contentItems.length ? contentItems.map((item) => (
                    <div key={item.contentId} className="rounded-2xl border border-border bg-background-light p-4">
                      <div className="flex items-start justify-between gap-4">
                        <div className="min-w-0">
                          <p className="truncate text-sm font-semibold text-navy">{item.title}</p>
                          <div className="mt-2 flex flex-wrap gap-2">
                            <Badge variant="outline" className="text-[10px] uppercase tracking-[0.12em]">{item.subtestCode}</Badge>
                            <Badge variant="outline" className="text-[10px] capitalize">{item.difficulty}</Badge>
                          </div>
                        </div>
                        <div className="text-right">
                          <p className="text-lg font-semibold text-navy">{formatDecimal(item.effectivenessScore)}</p>
                          <p className="text-xs text-muted">Effectiveness</p>
                        </div>
                      </div>

                      <div className="mt-4 grid gap-3 sm:grid-cols-4">
                        <div>
                          <p className="text-xs uppercase tracking-[0.12em] text-muted">Attempts</p>
                          <p className="text-sm font-semibold text-navy">{formatNumber(item.totalAttempts)}</p>
                        </div>
                        <div>
                          <p className="text-xs uppercase tracking-[0.12em] text-muted">Complete</p>
                          <p className="text-sm font-semibold text-navy">{formatPercent(item.completionRate)}</p>
                        </div>
                        <div>
                          <p className="text-xs uppercase tracking-[0.12em] text-muted">Avg score</p>
                          <p className="text-sm font-semibold text-navy">{formatDecimal(item.averageScore)}</p>
                        </div>
                        <div>
                          <p className="text-xs uppercase tracking-[0.12em] text-muted">Avg time</p>
                          <p className="text-sm font-semibold text-navy">{formatDurationSeconds(item.avgTimeSeconds)}</p>
                        </div>
                      </div>
                    </div>
                  )) : (
                    <p className="text-sm text-muted">No content effectiveness data is available yet.</p>
                  )}
                </div>
              </AdminRoutePanel>

              <AdminRoutePanel
                title="Expert Efficiency"
                description={`${formatNumber(data.expertEfficiency.summary.activeExperts)} active of ${formatNumber(data.expertEfficiency.summary.totalExperts)} experts over the last ${data.expertEfficiency.period} days.`}
              >
                <div className="grid gap-3 md:grid-cols-3">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Reviews completed</p>
                    <p className="text-lg font-semibold text-navy">{formatNumber(data.expertEfficiency.summary.totalReviewsCompleted)}</p>
                  </div>
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Reviews per expert</p>
                    <p className="text-lg font-semibold text-navy">{formatDecimal(data.expertEfficiency.summary.averageReviewsPerExpertPerDay)}</p>
                  </div>
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Top expert</p>
                    <p className="text-lg font-semibold text-navy">{topExpert ? truncate(topExpert.expertName) : 'No data'}</p>
                  </div>
                </div>

                <div className="space-y-3">
                  {experts.length ? experts.map((expert) => {
                    const badgeClassName = efficiencyBadgeClasses[expert.efficiency] ?? efficiencyBadgeClasses['no-data'];

                    return (
                      <div key={expert.expertId} className="rounded-2xl border border-border bg-background-light p-4">
                        <div className="flex items-center justify-between gap-3">
                          <div className="flex min-w-0 items-center gap-3">
                            <div className="flex h-10 w-10 items-center justify-center rounded-full bg-surface text-sm font-semibold text-navy">
                              {expert.expertName.slice(0, 1).toUpperCase()}
                            </div>
                            <div className="min-w-0">
                              <p className="truncate text-sm font-semibold text-navy">{expert.expertName}</p>
                              <p className="text-xs text-muted">{formatNumber(expert.assignmentsReceived)} assignments received</p>
                            </div>
                          </div>
                          <Badge className={badgeClassName}>{expert.efficiency}</Badge>
                        </div>

                        <div className="mt-4 grid gap-3 sm:grid-cols-5">
                          <div>
                            <p className="text-xs uppercase tracking-[0.12em] text-muted">Done</p>
                            <p className="text-sm font-semibold text-navy">{formatNumber(expert.reviewsCompleted)}</p>
                          </div>
                          <div>
                            <p className="text-xs uppercase tracking-[0.12em] text-muted">Avg time</p>
                            <p className="text-sm font-semibold text-navy">{formatMinutes(expert.averageReviewTimeMinutes)}</p>
                          </div>
                          <div>
                            <p className="text-xs uppercase tracking-[0.12em] text-muted">Per day</p>
                            <p className="text-sm font-semibold text-navy">{decimalFormatter.format(expert.reviewsPerDay)}</p>
                          </div>
                          <div>
                            <p className="text-xs uppercase tracking-[0.12em] text-muted">AI align</p>
                            <p className="text-sm font-semibold text-navy">{formatDecimal(expert.aiAlignmentScore)}</p>
                          </div>
                          <div>
                            <p className="text-xs uppercase tracking-[0.12em] text-muted">Period</p>
                            <p className="text-sm font-semibold text-navy">{expert.period} days</p>
                          </div>
                        </div>
                      </div>
                    );
                  }) : (
                    <p className="text-sm text-muted">No expert efficiency data is available yet.</p>
                  )}
                </div>
              </AdminRoutePanel>
            </div>

            <AdminRoutePanel
              title="Analytics Routes"
              description="Open the dedicated slices when you need to drill beyond the summary view."
            >
              <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                {[
                  { href: '/admin/analytics/subscription-health', label: 'Subscription health' },
                  { href: '/admin/analytics/cohort', label: 'Learner cohorts' },
                  { href: '/admin/analytics/content-effectiveness', label: 'Content effectiveness' },
                  { href: '/admin/analytics/expert-efficiency', label: 'Expert efficiency' },
                ].map((link) => (
                  <Link
                    key={link.href}
                    href={link.href}
                    className="inline-flex items-center justify-between gap-2 rounded-lg border border-border px-5 py-3 text-sm font-medium text-navy transition-all duration-200 hover:border-border-hover hover:bg-surface"
                  >
                    {link.label}
                    <ArrowRight className="h-4 w-4" />
                  </Link>
                ))}
              </div>
            </AdminRoutePanel>
          </>
        ) : null}
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
