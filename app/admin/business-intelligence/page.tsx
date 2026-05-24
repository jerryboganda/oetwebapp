'use client';

import { useEffect, useState } from 'react';
import { Activity, DollarSign, Layers3, Target, TrendingDown, Users } from 'lucide-react';
import { AdminRouteFreshnessBadge, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AdminQuickAction } from '@/components/domain/admin-quick-action';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { analytics } from '@/lib/analytics';
import { getAdminBusinessIntelligenceData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminBusinessIntelligenceData } from '@/lib/types/admin';

import { AdminOperationsLayout, KpiStrip, BentoGrid, BentoCell } from '@/components/admin/layout/admin-operations-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Badge } from '@/components/admin/ui/badge';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Skeleton } from '@/components/admin/ui/skeleton';

type PageStatus = 'loading' | 'success' | 'error';

const numberFormatter = new Intl.NumberFormat('en-AU', {
  maximumFractionDigits: 0,
});

const decimalFormatter = new Intl.NumberFormat('en-AU', {
  maximumFractionDigits: 1,
});

const efficiencyBadgeVariant: Record<string, 'success' | 'warning' | 'danger' | 'default'> = {
  high: 'success',
  medium: 'warning',
  low: 'danger',
  'no-data': 'default',
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
          <Skeleton key={`bi-kpi-${index}`} className="h-32 rounded-admin-lg" />
        ))}
      </div>

      <div className="grid gap-6 xl:grid-cols-2">
        <Skeleton className="h-[30rem] rounded-admin-lg" />
        <Skeleton className="h-[30rem] rounded-admin-lg" />
      </div>

      <div className="grid gap-6 xl:grid-cols-2">
        <Skeleton className="h-[34rem] rounded-admin-lg" />
        <Skeleton className="h-[34rem] rounded-admin-lg" />
      </div>

      <Skeleton className="h-28 rounded-admin-lg" />
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

  const subscription = data?.subscriptionHealth ?? null;
  const cohortData = data?.cohortAnalysis ?? null;
  const contentData = data?.contentEffectiveness ?? null;
  const expertData = data?.expertEfficiency ?? null;

  const cohorts = cohortData?.cohorts ?? [];
  const contentItems = contentData?.items ?? [];
  const experts = expertData?.experts ?? [];
  const topCohort = cohorts[0];
  const topContent = contentItems[0];
  const topExpert = experts[0];

  const unavailableSections: string[] = [];
  if (data && !subscription) unavailableSections.push('Subscription health');
  if (data && !cohortData) unavailableSections.push('Learner cohorts');
  if (data && !contentData) unavailableSections.push('Content effectiveness');
  if (data && !expertData) unavailableSections.push('Tutor efficiency');

  const topPlanRevenue = Math.max(1, ...(subscription?.revenueByPlan.map((plan) => plan.monthlyRevenue) ?? []));
  const maxTrendVolume = Math.max(1, ...(subscription?.monthlyTrend.map((point) => point.newSubscriptions + point.cancellations) ?? []));

  return (
    <AdminRouteWorkspace role="main" aria-label="Business intelligence">
      <AdminOperationsLayout
        title="Business Intelligence"
        description="Live backend aggregates for subscription health, learner cohorts, content effectiveness, and tutor throughput."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Business Intelligence' },
        ]}
        actions={<AdminRouteFreshnessBadge value={data?.generatedAt} />}
      >
      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => window.location.reload()}
        loadingContent={buildLoadingContent()}
        errorMessage="Unable to load business intelligence data."
      >
        {data ? (
          <>
            {unavailableSections.length > 0 ? (
              <Card surface="tinted-warning" role="status" aria-live="polite">
                <CardContent className="px-4 py-3 text-sm pt-3">
                  <p className="font-semibold text-[var(--admin-warning)]">Some panels are temporarily unavailable</p>
                  <p className="mt-1 text-admin-fg-default">
                    {unavailableSections.join(', ')} {unavailableSections.length === 1 ? 'is' : 'are'} offline. The
                    remaining sections are still live and the page will recover automatically once the backend
                    responds.
                  </p>
                </CardContent>
              </Card>
            ) : null}

            <KpiStrip className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              <KpiTile
                label="Monthly recurring revenue"
                value={formatCurrency(subscription?.mrr ?? 0)}
                icon={<DollarSign className="h-4 w-4" />}
                tone="success"
              />
              <KpiTile
                label="Active subscriptions"
                value={formatNumber(subscription?.activeSubscriptions ?? 0)}
                icon={<Users className="h-4 w-4" />}
              />
              <KpiTile
                label="Churn rate"
                value={formatPercent(subscription?.churnRate ?? 0)}
                icon={<TrendingDown className="h-4 w-4" />}
                tone={(subscription?.churnRate ?? 0) > 5 ? 'warning' : 'success'}
              />
              <KpiTile
                label="Total learners"
                value={formatNumber(cohortData?.totalLearners ?? 0)}
                icon={<Layers3 className="h-4 w-4" />}
              />
              <KpiTile
                label="Content effectiveness"
                value={formatDecimal(topContent?.effectivenessScore ?? null)}
                icon={<Target className="h-4 w-4" />}
                tone={topContent ? 'success' : 'default'}
              />
              <KpiTile
                label="Tutor throughput"
                value={formatDecimal(expertData?.summary.averageReviewsPerExpertPerDay ?? null)}
                icon={<Activity className="h-4 w-4" />}
              />
            </KpiStrip>

            <BentoGrid>
              <BentoCell span={{ default: 12, xl: 6 }}>
                <Card>
                  <CardHeader>
                    <div>
                      <CardTitle>Subscription Health</CardTitle>
                      <p className="text-sm text-admin-fg-muted mt-1">{`ARPU ${formatCurrency(subscription?.arpu ?? 0, 2)}, trial conversion ${formatPercent(subscription?.trialConversionRate ?? 0)}, and six-month movement.`}</p>
                    </div>
                  </CardHeader>
                  <CardContent className="space-y-6">
                <div className="grid gap-4 md:grid-cols-3">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">ARPU</p>
                    <p className="text-xl font-semibold text-admin-fg-strong">{formatCurrency(subscription?.arpu ?? 0, 2)}</p>
                  </div>
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">Trial conversion</p>
                    <p className="text-xl font-semibold text-admin-fg-strong">{formatPercent(subscription?.trialConversionRate ?? 0)}</p>
                  </div>
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">New this month</p>
                    <p className="text-xl font-semibold text-admin-fg-strong">{formatNumber(subscription?.newSubscriptionsThisMonth ?? 0)}</p>
                  </div>
                </div>

                <div className="space-y-4">
                  {subscription?.revenueByPlan.length ? subscription.revenueByPlan.map((plan) => (
                    <div key={plan.planId} className="space-y-2 rounded-admin-lg border border-admin-border bg-admin-bg-subtle p-4">
                      <div className="flex items-center justify-between gap-3">
                        <div className="min-w-0">
                          <p className="truncate text-sm font-semibold text-admin-fg-strong">{plan.planName}</p>
                          <p className="text-xs text-admin-fg-muted">{formatNumber(plan.subscribers)} subscribers</p>
                        </div>
                        <p className="text-sm font-semibold text-admin-fg-strong">{formatCurrency(plan.monthlyRevenue)}</p>
                      </div>
                      <div className="h-2 overflow-hidden rounded-full bg-admin-bg-surface">
                        <div className="h-full rounded-full bg-[var(--admin-primary)]" style={{ width: barWidth(plan.monthlyRevenue, topPlanRevenue) }} />
                      </div>
                    </div>
                  )) : (
                    <p className="text-sm text-admin-fg-muted">No subscription plan revenue is available yet.</p>
                  )}
                </div>

                <div className="space-y-3">
                  <div className="flex items-center justify-between gap-3">
                    <h3 className="text-sm font-semibold text-admin-fg-strong">Monthly trend</h3>
                    <p className="text-xs text-admin-fg-muted">New subscriptions and cancellations</p>
                  </div>

                  <div className="space-y-3">
                    {subscription?.monthlyTrend.length ? subscription.monthlyTrend.map((point) => {
                      const total = point.newSubscriptions + point.cancellations;

                      return (
                        <div key={point.month} className="flex items-center gap-3">
                          <span className="w-16 text-xs font-medium text-admin-fg-muted">{point.month}</span>
                          <div className="flex h-6 flex-1 overflow-hidden rounded-full bg-admin-bg-subtle">
                            <div className="flex h-full" style={{ width: barWidth(total, maxTrendVolume) }}>
                              <div className="h-full bg-[var(--admin-success)]" style={{ width: total > 0 ? `${(point.newSubscriptions / total) * 100}%` : '0%' }} />
                              <div className="h-full bg-[var(--admin-danger)]" style={{ width: total > 0 ? `${(point.cancellations / total) * 100}%` : '0%' }} />
                            </div>
                          </div>
                          <span className="w-24 text-right text-xs text-admin-fg-muted">+{point.newSubscriptions} / -{point.cancellations}</span>
                        </div>
                      );
                    }) : (
                      <p className="text-sm text-admin-fg-muted">No monthly trend data is available yet.</p>
                    )}
                  </div>
                </div>
                </CardContent>
              </Card>
              </BentoCell>

              <BentoCell span={{ default: 12, xl: 6 }}>
                <Card>
                  <CardHeader>
                    <div>
                      <CardTitle>Learner Cohorts</CardTitle>
                      <p className="text-sm text-admin-fg-muted mt-1">{`Grouped by ${cohortData?.groupBy ?? 'profession'}. ${formatNumber(cohortData?.totalLearners ?? 0)} learners are in view.`}</p>
                    </div>
                  </CardHeader>
                  <CardContent className="space-y-6">
                <div className="grid gap-3 md:grid-cols-3">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">Top cohort</p>
                    <p className="text-lg font-semibold text-admin-fg-strong">{topCohort ? topCohort.cohortName : 'No data'}</p>
                  </div>
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">Learners</p>
                    <p className="text-lg font-semibold text-admin-fg-strong">{topCohort ? formatNumber(topCohort.learnerCount) : '--'}</p>
                  </div>
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">Avg score</p>
                    <p className="text-lg font-semibold text-admin-fg-strong">{formatDecimal(topCohort?.averageScore ?? null)}</p>
                  </div>
                </div>

                <div className="space-y-3">
                  {cohorts.length ? cohorts.map((cohort) => (
                    <div key={cohort.cohortKey} className="rounded-admin-lg border border-admin-border bg-admin-bg-subtle p-4">
                      <div className="flex items-start justify-between gap-4">
                        <div className="min-w-0">
                          <p className="truncate text-sm font-semibold text-admin-fg-strong">{cohort.cohortName}</p>
                          <p className="text-xs text-admin-fg-muted">{formatNumber(cohort.evaluationCount)} evaluations, {formatNumber(cohort.activeLastMonth)} active in the last 30 days</p>
                        </div>
                        <Badge variant="default" intensity="tinted" size="sm">
                          {formatNumber(cohort.learnerCount)} learners
                        </Badge>
                      </div>

                      <div className="mt-4 grid gap-3 sm:grid-cols-3">
                        <div>
                          <p className="text-xs uppercase tracking-[0.12em] text-admin-fg-muted">Avg score</p>
                          <p className="text-sm font-semibold text-admin-fg-strong">{formatDecimal(cohort.averageScore)}</p>
                        </div>
                        <div>
                          <p className="text-xs uppercase tracking-[0.12em] text-admin-fg-muted">Evaluations</p>
                          <p className="text-sm font-semibold text-admin-fg-strong">{formatNumber(cohort.evaluationCount)}</p>
                        </div>
                        <div>
                          <p className="text-xs uppercase tracking-[0.12em] text-admin-fg-muted">Active 30d</p>
                          <p className="text-sm font-semibold text-admin-fg-strong">{formatNumber(cohort.activeLastMonth)}</p>
                        </div>
                      </div>
                    </div>
                  )) : (
                    <p className="text-sm text-admin-fg-muted">No cohort data is available yet.</p>
                  )}
                </div>
                </CardContent>
              </Card>
              </BentoCell>

              <BentoCell span={{ default: 12, xl: 6 }}>
                <Card>
                  <CardHeader>
                    <div>
                      <CardTitle>Content Effectiveness</CardTitle>
                      <p className="text-sm text-admin-fg-muted mt-1">{`Top ${contentItems.length || 0} published items ranked by effectiveness.`}</p>
                    </div>
                  </CardHeader>
                  <CardContent className="space-y-6">
                <div className="grid gap-3 md:grid-cols-3">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">Top item</p>
                    <p className="text-lg font-semibold text-admin-fg-strong">{topContent ? truncate(topContent.title) : 'No data'}</p>
                  </div>
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">Top score</p>
                    <p className="text-lg font-semibold text-admin-fg-strong">{formatDecimal(topContent?.effectivenessScore ?? null)}</p>
                  </div>
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">Completion</p>
                    <p className="text-lg font-semibold text-admin-fg-strong">{topContent ? formatPercent(topContent.completionRate) : '--'}</p>
                  </div>
                </div>

                <div className="space-y-3">
                  {contentItems.length ? contentItems.map((item) => (
                    <div key={item.contentId} className="rounded-admin-lg border border-admin-border bg-admin-bg-subtle p-4">
                      <div className="flex items-start justify-between gap-4">
                        <div className="min-w-0">
                          <p className="truncate text-sm font-semibold text-admin-fg-strong">{item.title}</p>
                          <div className="mt-2 flex flex-wrap gap-2">
                            <Badge variant="default" intensity="tinted" size="sm">{item.subtestCode}</Badge>
                            <Badge variant="default" intensity="tinted" size="sm" className="capitalize">{item.difficulty}</Badge>
                          </div>
                        </div>
                        <div className="text-right">
                          <p className="text-lg font-semibold text-admin-fg-strong">{formatDecimal(item.effectivenessScore)}</p>
                          <p className="text-xs text-admin-fg-muted">Effectiveness</p>
                        </div>
                      </div>

                      <div className="mt-4 grid gap-3 sm:grid-cols-4">
                        <div>
                          <p className="text-xs uppercase tracking-[0.12em] text-admin-fg-muted">Attempts</p>
                          <p className="text-sm font-semibold text-admin-fg-strong">{formatNumber(item.totalAttempts)}</p>
                        </div>
                        <div>
                          <p className="text-xs uppercase tracking-[0.12em] text-admin-fg-muted">Complete</p>
                          <p className="text-sm font-semibold text-admin-fg-strong">{formatPercent(item.completionRate)}</p>
                        </div>
                        <div>
                          <p className="text-xs uppercase tracking-[0.12em] text-admin-fg-muted">Avg score</p>
                          <p className="text-sm font-semibold text-admin-fg-strong">{formatDecimal(item.averageScore)}</p>
                        </div>
                        <div>
                          <p className="text-xs uppercase tracking-[0.12em] text-admin-fg-muted">Avg time</p>
                          <p className="text-sm font-semibold text-admin-fg-strong">{formatDurationSeconds(item.avgTimeSeconds)}</p>
                        </div>
                      </div>
                    </div>
                  )) : (
                    <p className="text-sm text-admin-fg-muted">No content effectiveness data is available yet.</p>
                  )}
                </div>
                </CardContent>
              </Card>
              </BentoCell>

              <BentoCell span={{ default: 12, xl: 6 }}>
                <Card>
                  <CardHeader>
                    <div>
                      <CardTitle>Tutor Efficiency</CardTitle>
                      <p className="text-sm text-admin-fg-muted mt-1">{`${formatNumber(expertData?.summary.activeExperts ?? 0)} active of ${formatNumber(expertData?.summary.totalExperts ?? 0)} tutors over the last ${expertData?.period ?? 30} days.`}</p>
                    </div>
                  </CardHeader>
                  <CardContent className="space-y-6">
                <div className="grid gap-3 md:grid-cols-3">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">Reviews completed</p>
                    <p className="text-lg font-semibold text-admin-fg-strong">{formatNumber(expertData?.summary.totalReviewsCompleted ?? 0)}</p>
                  </div>
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">Reviews per tutor</p>
                    <p className="text-lg font-semibold text-admin-fg-strong">{formatDecimal(expertData?.summary.averageReviewsPerExpertPerDay ?? null)}</p>
                  </div>
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">Top tutor</p>
                    <p className="text-lg font-semibold text-admin-fg-strong">{topExpert ? truncate(topExpert.expertName) : 'No data'}</p>
                  </div>
                </div>

                <div className="space-y-3">
                  {experts.length ? experts.map((expert) => {
                    const badgeVariant = efficiencyBadgeVariant[expert.efficiency] ?? efficiencyBadgeVariant['no-data'];

                    return (
                      <div key={expert.expertId} className="rounded-admin-lg border border-admin-border bg-admin-bg-subtle p-4">
                        <div className="flex items-center justify-between gap-3">
                          <div className="flex min-w-0 items-center gap-3">
                            <div className="flex h-10 w-10 items-center justify-center rounded-full bg-admin-bg-surface text-sm font-semibold text-admin-fg-strong">
                              {expert.expertName.slice(0, 1).toUpperCase()}
                            </div>
                            <div className="min-w-0">
                              <p className="truncate text-sm font-semibold text-admin-fg-strong">{expert.expertName}</p>
                              <p className="text-xs text-admin-fg-muted">{formatNumber(expert.assignmentsReceived)} assignments received</p>
                            </div>
                          </div>
                          <Badge variant={badgeVariant} intensity="tinted">{expert.efficiency}</Badge>
                        </div>

                        <div className="mt-4 grid gap-3 sm:grid-cols-5">
                          <div>
                            <p className="text-xs uppercase tracking-[0.12em] text-admin-fg-muted">Done</p>
                            <p className="text-sm font-semibold text-admin-fg-strong">{formatNumber(expert.reviewsCompleted)}</p>
                          </div>
                          <div>
                            <p className="text-xs uppercase tracking-[0.12em] text-admin-fg-muted">Avg time</p>
                            <p className="text-sm font-semibold text-admin-fg-strong">{formatMinutes(expert.averageReviewTimeMinutes)}</p>
                          </div>
                          <div>
                            <p className="text-xs uppercase tracking-[0.12em] text-admin-fg-muted">Per day</p>
                            <p className="text-sm font-semibold text-admin-fg-strong">{decimalFormatter.format(expert.reviewsPerDay)}</p>
                          </div>
                          <div>
                            <p className="text-xs uppercase tracking-[0.12em] text-admin-fg-muted">AI align</p>
                            <p className="text-sm font-semibold text-admin-fg-strong">{formatDecimal(expert.aiAlignmentScore)}</p>
                          </div>
                          <div>
                            <p className="text-xs uppercase tracking-[0.12em] text-admin-fg-muted">Period</p>
                            <p className="text-sm font-semibold text-admin-fg-strong">{expert.period} days</p>
                          </div>
                        </div>
                      </div>
                    );
                  }) : (
                    <p className="text-sm text-admin-fg-muted">No tutor efficiency data is available yet.</p>
                  )}
                </div>
                </CardContent>
              </Card>
              </BentoCell>
            </BentoGrid>

            <Card>
              <CardHeader>
                <div>
                  <CardTitle>Analytics Routes</CardTitle>
                  <p className="text-sm text-admin-fg-muted mt-1">Open the dedicated slices when you need to drill beyond the summary view.</p>
                </div>
              </CardHeader>
              <CardContent>
                <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                  {[
                    { href: '/admin/analytics/subscription-health', label: 'Subscription health' },
                    { href: '/admin/analytics/cohort', label: 'Learner cohorts' },
                    { href: '/admin/analytics/content-effectiveness', label: 'Content effectiveness' },
                    { href: '/admin/analytics/expert-efficiency', label: 'Tutor efficiency' },
                    { href: '/admin/analytics/reading', label: 'Reading analytics' },
                  ].map((link) => (
                    <AdminQuickAction key={link.href} href={link.href} label={link.label} />
                  ))}
                </div>
              </CardContent>
            </Card>
          </>
        ) : null}
      </AsyncStateWrapper>
      </AdminOperationsLayout>
    </AdminRouteWorkspace>
  );
}
