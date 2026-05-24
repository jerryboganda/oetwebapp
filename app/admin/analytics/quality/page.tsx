'use client';

import { useEffect, useMemo, useState } from 'react';
import { AlertTriangle, BarChart3, CheckCircle2, Clock, Download, FileText, Users } from 'lucide-react';
import { exportToCsv, formatDateForExport } from '@/lib/csv-export';
import { CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { AdminRouteFreshnessBadge, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { MotionSection } from '@/components/ui/motion-primitives';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { EmptyState as LegacyEmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Select } from '@/components/ui/form-controls';
import { getAdminQualityAnalyticsData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminQualityAnalytics } from '@/lib/types/admin';

import { AdminOperationsLayout, KpiStrip, BentoGrid, BentoCell } from '@/components/admin/layout/admin-operations-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { ChartCard } from '@/components/admin/ui/chart-card';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';

export default function QualityAnalyticsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [timeRange, setTimeRange] = useState('30d');
  const [filters, setFilters] = useState<Record<string, string[]>>({ subtest: [], profession: [] });
  const [analytics, setAnalytics] = useState<AdminQualityAnalytics | null>(null);

  const selectedSubtest = filters.subtest?.[0];
  const selectedProfession = filters.profession?.[0];

  useEffect(() => {
    let cancelled = false;

    async function loadAnalytics() {
      setPageStatus('loading');
      try {
        const result = await getAdminQualityAnalyticsData({
          timeRange,
          subtest: selectedSubtest,
          profession: selectedProfession,
        });

        if (cancelled) return;

        setAnalytics(result);
        const totalSamples = result.freshness.evaluationSampleCount + result.freshness.reviewSampleCount;
        setPageStatus(totalSamples > 0 ? 'success' : 'empty');
      } catch (error) {
        console.error(error);
        if (!cancelled) {
          setPageStatus('error');
        }
      }
    }

    loadAnalytics();
    return () => {
      cancelled = true;
    };
  }, [timeRange, selectedSubtest, selectedProfession]);

  const filterGroups: FilterGroup[] = [
    {
      id: 'subtest',
      label: 'Subtest',
      options: [
        { id: 'writing', label: 'Writing' },
        { id: 'speaking', label: 'Speaking' },
        { id: 'reading', label: 'Reading' },
        { id: 'listening', label: 'Listening' },
      ],
    },
    {
      id: 'profession',
      label: 'Profession',
      options: [
        { id: 'medicine', label: 'Medicine' },
        { id: 'nursing', label: 'Nursing' },
        { id: 'dentistry', label: 'Dentistry' },
        { id: 'pharmacy', label: 'Pharmacy' },
        { id: 'physiotherapy', label: 'Physiotherapy' },
      ],
    },
  ];

  const rateChartData = useMemo(() => {
    if (!analytics) return [];

    const points = new Map<string, { label: string; agreement?: number; appeals?: number }>();
    analytics.trendSeries.agreement.forEach((point) => {
      points.set(point.label, { ...(points.get(point.label) ?? { label: point.label }), agreement: point.value });
    });
    analytics.trendSeries.appeals.forEach((point) => {
      points.set(point.label, { ...(points.get(point.label) ?? { label: point.label }), appeals: point.value });
    });

    return [...points.values()];
  }, [analytics]);

  const operationsChartData = useMemo(() => {
    if (!analytics) return [];

    const points = new Map<string, { label: string; reviewTime?: number; riskCases?: number }>();
    analytics.trendSeries.reviewTime.forEach((point) => {
      points.set(point.label, { ...(points.get(point.label) ?? { label: point.label }), reviewTime: point.value });
    });
    analytics.trendSeries.riskCases.forEach((point) => {
      points.set(point.label, { ...(points.get(point.label) ?? { label: point.label }), riskCases: point.value });
    });

    return [...points.values()];
  }, [analytics]);

  function handleFilterChange(groupId: string, optionId: string) {
    setFilters((current) => ({
      ...current,
      [groupId]: current[groupId]?.includes(optionId) ? [] : [optionId],
    }));
  }

  if (!isAuthenticated || role !== 'admin') return null;

  const headerActions = (
    <div className="flex items-center gap-3">
      {analytics && (
        <Button variant="outline" size="sm" startIcon={<Download className="w-4 h-4" />} onClick={() => {
          const rows: Record<string, unknown>[] = [
            { metric: 'AI-Human Agreement (%)', value: analytics.aiHumanAgreement.value, trend: analytics.aiHumanAgreement.trend },
            { metric: 'Appeals Rate (%)', value: analytics.appealsRate.value, trend: analytics.appealsRate.trend },
            { metric: 'Avg Review Time', value: `${analytics.avgReviewTime.value} ${analytics.avgReviewTime.unit}` },
            { metric: 'SLA Met (%)', value: analytics.reviewSLA.metPercent },
            { metric: 'Risk Cases', value: analytics.riskCases.count, severity: analytics.riskCases.severity },
            { metric: 'Published Content', value: analytics.contentPerformance.publishedCount },
            { metric: 'Active Content', value: analytics.contentPerformance.activeContent },
            { metric: 'Feature Adoption (%)', value: analytics.featureAdoption.adoptionRate, activeUsers: analytics.featureAdoption.activeUsers },
          ];
          exportToCsv(rows, `quality-analytics-${timeRange}-${formatDateForExport(new Date())}.csv`);
        }}>
          Export CSV
        </Button>
      )}
      <AdminRouteFreshnessBadge value={analytics?.freshness.generatedAt} />
      <div className="w-44">
        <Select
          label=""
          value={timeRange}
          onChange={(event) => setTimeRange(event.target.value)}
          options={[
            { value: '7d', label: 'Last 7 Days' },
            { value: '30d', label: 'Last 30 Days' },
            { value: 'ytd', label: 'Year to Date' },
          ]}
        />
      </div>
    </div>
  );

  return (
    <AdminRouteWorkspace role="main" aria-label="Quality analytics">
      <AdminOperationsLayout
        title="Quality Analytics"
        description="Track grading agreement, appeals, turnaround, and risk signals from the live quality analytics service."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Analytics', href: '/admin/analytics' },
          { label: 'Quality' },
        ]}
        actions={headerActions}
      >
        <Card>
          <CardContent className="p-4 sm:p-5 pt-4 sm:pt-5">
            <FilterBar groups={filterGroups} selected={filters} onChange={handleFilterChange} onClear={() => setFilters({ subtest: [], profession: [] })} />
          </CardContent>
        </Card>

        <AsyncStateWrapper
          status={pageStatus}
          onRetry={() => window.location.reload()}
          emptyContent={
            <LegacyEmptyState
              icon={<BarChart3 className="h-10 w-10 text-admin-fg-muted" />}
              title="No quality analytics are available for this filter set"
              description="Try a broader time range or clear the current subtest and profession filters."
            />
          }
        >
          {analytics ? (
            <div className="space-y-8">
              <MotionSection delayIndex={0}>
                <KpiStrip>
                  <KpiTile
                    label="AI-Human Agreement"
                    value={`${analytics.aiHumanAgreement.value}%`}
                    icon={<CheckCircle2 className="h-4 w-4" />}
                    tone={analytics.aiHumanAgreement.trend >= 0 ? 'success' : 'warning'}
                    trend={{
                      value: `${analytics.aiHumanAgreement.trend >= 0 ? '+' : ''}${analytics.aiHumanAgreement.trend}%`,
                      direction: analytics.aiHumanAgreement.trend > 0 ? 'up' : analytics.aiHumanAgreement.trend < 0 ? 'down' : 'flat',
                      positive: analytics.aiHumanAgreement.trend >= 0,
                    }}
                  />
                  <KpiTile
                    label="Appeals Rate"
                    value={`${analytics.appealsRate.value}%`}
                    icon={<AlertTriangle className="h-4 w-4" />}
                    tone={analytics.appealsRate.trend <= 0 ? 'success' : 'warning'}
                    trend={{
                      value: `${analytics.appealsRate.trend >= 0 ? '+' : ''}${analytics.appealsRate.trend}%`,
                      direction: analytics.appealsRate.trend > 0 ? 'up' : analytics.appealsRate.trend < 0 ? 'down' : 'flat',
                      positive: analytics.appealsRate.trend <= 0,
                    }}
                  />
                  <KpiTile
                    label="Avg Review Time"
                    value={`${analytics.avgReviewTime.value} ${analytics.avgReviewTime.unit}`}
                    icon={<Clock className="h-4 w-4" />}
                  />
                  <KpiTile
                    label="Risk Cases"
                    value={analytics.riskCases.count}
                    icon={<AlertTriangle className="h-4 w-4" />}
                    tone={analytics.riskCases.count > 0 ? 'warning' : 'default'}
                  />
                </KpiStrip>
              </MotionSection>

              <MotionSection delayIndex={1}>
                <div className="grid gap-4 md:grid-cols-3">
                  <KpiTile label="Published Content" value={analytics.contentPerformance.publishedCount} icon={<FileText className="h-4 w-4" />} size="sm" />
                  <KpiTile label="Active Content" value={analytics.contentPerformance.activeContent} icon={<FileText className="h-4 w-4" />} size="sm" />
                  <KpiTile label="Feature Adoption" value={`${analytics.featureAdoption.adoptionRate}%`} icon={<Users className="h-4 w-4" />} size="sm" />
                </div>
              </MotionSection>

              <MotionSection delayIndex={2}>
                <BentoGrid>
                  <BentoCell span={{ default: 12, xl: 6 }}>
                    <ChartCard
                      title="Quality Rates Trend"
                      subtitle="Agreement and appeals trends from the current analytics window."
                      height={320}
                    >
                      <ResponsiveContainer width="100%" height={320}>
                        <LineChart data={rateChartData}>
                          <CartesianGrid strokeDasharray="3 3" stroke="var(--admin-border)" />
                          <XAxis dataKey="label" stroke="var(--admin-fg-muted)" fontSize={12} />
                          <YAxis stroke="var(--admin-fg-muted)" fontSize={12} />
                          <Tooltip />
                          <Legend />
                          <Line type="monotone" dataKey="agreement" name="Agreement" stroke="var(--admin-primary)" strokeWidth={2} dot={false} />
                          <Line type="monotone" dataKey="appeals" name="Appeals" stroke="var(--admin-danger)" strokeWidth={2} dot={false} />
                        </LineChart>
                      </ResponsiveContainer>
                    </ChartCard>
                  </BentoCell>

                  <BentoCell span={{ default: 12, xl: 6 }}>
                    <ChartCard
                      title="Operations Trend"
                      subtitle="Review time and risk case trend lines from the live analytics response."
                      height={320}
                    >
                      <ResponsiveContainer width="100%" height={320}>
                        <LineChart data={operationsChartData}>
                          <CartesianGrid strokeDasharray="3 3" stroke="var(--admin-border)" />
                          <XAxis dataKey="label" stroke="var(--admin-fg-muted)" fontSize={12} />
                          <YAxis stroke="var(--admin-fg-muted)" fontSize={12} />
                          <Tooltip />
                          <Legend />
                          <Line type="monotone" dataKey="reviewTime" name="Review Time" stroke="var(--admin-warning)" strokeWidth={2} dot={false} />
                          <Line type="monotone" dataKey="riskCases" name="Risk Cases" stroke="var(--admin-secondary)" strokeWidth={2} dot={false} />
                        </LineChart>
                      </ResponsiveContainer>
                    </ChartCard>
                  </BentoCell>
                </BentoGrid>
              </MotionSection>

              <MotionSection delayIndex={3}>
                <Card>
                  <CardHeader>
                    <div>
                      <CardTitle>Sample Coverage</CardTitle>
                      <p className="text-sm text-admin-fg-muted mt-1">Quality analytics are only as trustworthy as the evidence window behind them.</p>
                    </div>
                  </CardHeader>
                  <CardContent>
                    <div className="grid gap-4 md:grid-cols-3">
                      <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">Evaluation Samples</p>
                        <p className="text-xl font-semibold text-admin-fg-strong">{analytics.freshness.evaluationSampleCount}</p>
                      </div>
                      <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">Review Samples</p>
                        <p className="text-xl font-semibold text-admin-fg-strong">{analytics.freshness.reviewSampleCount}</p>
                      </div>
                      <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">Applied Filters</p>
                        <p className="text-sm text-admin-fg-muted">Subtest: {analytics.filters.subtest}</p>
                        <p className="text-sm text-admin-fg-muted">Profession: {analytics.filters.profession}</p>
                      </div>
                    </div>
                  </CardContent>
                </Card>
              </MotionSection>
            </div>
          ) : null}
        </AsyncStateWrapper>
      </AdminOperationsLayout>
    </AdminRouteWorkspace>
  );
}
