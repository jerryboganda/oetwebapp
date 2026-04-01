'use client';

import { useEffect, useMemo, useState } from 'react';
import { AlertTriangle, BarChart3, CheckCircle2, Clock, FileText, Users } from 'lucide-react';
import { CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { AdminRouteFreshnessBadge, AdminRoutePanel, AdminRouteSectionHeader, AdminRouteSummaryCard, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { EmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Select } from '@/components/ui/form-controls';
import { getAdminQualityAnalyticsData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminQualityAnalytics } from '@/lib/types/admin';

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

  return (
    <AdminRouteWorkspace role="main" aria-label="Quality analytics">
      <AdminRouteSectionHeader
        title="Quality Analytics"
        description="Track grading agreement, appeals, turnaround, and risk signals from the live quality analytics service."
        meta={analytics ? `Window ${analytics.freshness.windowDays} days` : undefined}
        actions={
          <div className="flex items-center gap-3">
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
        }
      />

      <div className="rounded-xl border border-slate-200 bg-white p-4">
        <FilterBar groups={filterGroups} selected={filters} onChange={handleFilterChange} onClear={() => setFilters({ subtest: [], profession: [] })} />
      </div>

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => window.location.reload()}
        emptyContent={
          <EmptyState
            icon={<BarChart3 className="h-10 w-10 text-slate-400" />}
            title="No quality analytics are available for this filter set"
            description="Try a broader time range or clear the current subtest and profession filters."
          />
        }
      >
        {analytics ? (
          <>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <AdminRouteSummaryCard label="AI-Human Agreement" value={`${analytics.aiHumanAgreement.value}%`} hint={`${analytics.aiHumanAgreement.trend >= 0 ? '+' : ''}${analytics.aiHumanAgreement.trend}% vs prior window`} icon={<CheckCircle2 className="h-5 w-5" />} tone={analytics.aiHumanAgreement.trend >= 0 ? 'success' : 'warning'} />
              <AdminRouteSummaryCard label="Appeals Rate" value={`${analytics.appealsRate.value}%`} hint={`${analytics.appealsRate.trend >= 0 ? '+' : ''}${analytics.appealsRate.trend}% vs prior window`} icon={<AlertTriangle className="h-5 w-5" />} tone={analytics.appealsRate.trend <= 0 ? 'success' : 'warning'} />
              <AdminRouteSummaryCard label="Avg Review Time" value={`${analytics.avgReviewTime.value} ${analytics.avgReviewTime.unit}`} hint={`SLA met ${analytics.reviewSLA.metPercent}%`} icon={<Clock className="h-5 w-5" />} />
              <AdminRouteSummaryCard label="Risk Cases" value={analytics.riskCases.count} hint={`Severity ${analytics.riskCases.severity}`} icon={<AlertTriangle className="h-5 w-5" />} tone={analytics.riskCases.count > 0 ? 'warning' : 'default'} />
            </div>

            <div className="grid gap-4 md:grid-cols-3">
              <AdminRouteSummaryCard label="Published Content" value={analytics.contentPerformance.publishedCount} icon={<FileText className="h-5 w-5" />} />
              <AdminRouteSummaryCard label="Active Content" value={analytics.contentPerformance.activeContent} icon={<FileText className="h-5 w-5" />} />
              <AdminRouteSummaryCard label="Feature Adoption" value={`${analytics.featureAdoption.adoptionRate}%`} hint={`${analytics.featureAdoption.activeUsers} active users`} icon={<Users className="h-5 w-5" />} />
            </div>

            <div className="grid gap-6 xl:grid-cols-2">
              <AdminRoutePanel title="Quality Rates Trend" description="Agreement and appeals trends from the current analytics window.">
                <div className="h-80 min-h-[20rem]">
                  <ResponsiveContainer width="100%" height="100%">
                    <LineChart data={rateChartData}>
                      <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                      <XAxis dataKey="label" stroke="#64748b" fontSize={12} />
                      <YAxis stroke="#64748b" fontSize={12} />
                      <Tooltip />
                      <Legend />
                      <Line type="monotone" dataKey="agreement" name="Agreement" stroke="#2563eb" strokeWidth={2} dot={false} />
                      <Line type="monotone" dataKey="appeals" name="Appeals" stroke="#dc2626" strokeWidth={2} dot={false} />
                    </LineChart>
                  </ResponsiveContainer>
                </div>
              </AdminRoutePanel>

              <AdminRoutePanel title="Operations Trend" description="Review time and risk case trend lines from the live analytics response.">
                <div className="h-80 min-h-[20rem]">
                  <ResponsiveContainer width="100%" height="100%">
                    <LineChart data={operationsChartData}>
                      <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                      <XAxis dataKey="label" stroke="#64748b" fontSize={12} />
                      <YAxis stroke="#64748b" fontSize={12} />
                      <Tooltip />
                      <Legend />
                      <Line type="monotone" dataKey="reviewTime" name="Review Time" stroke="#f59e0b" strokeWidth={2} dot={false} />
                      <Line type="monotone" dataKey="riskCases" name="Risk Cases" stroke="#7c3aed" strokeWidth={2} dot={false} />
                    </LineChart>
                  </ResponsiveContainer>
                </div>
              </AdminRoutePanel>
            </div>

            <AdminRoutePanel title="Sample Coverage" description="Quality analytics are only as trustworthy as the evidence window behind them.">
              <div className="grid gap-4 md:grid-cols-3">
                <div>
                  <p className="text-xs font-semibold uppercase tracking-[0.12em] text-slate-400">Evaluation Samples</p>
                  <p className="text-xl font-semibold text-slate-900">{analytics.freshness.evaluationSampleCount}</p>
                </div>
                <div>
                  <p className="text-xs font-semibold uppercase tracking-[0.12em] text-slate-400">Review Samples</p>
                  <p className="text-xl font-semibold text-slate-900">{analytics.freshness.reviewSampleCount}</p>
                </div>
                <div>
                  <p className="text-xs font-semibold uppercase tracking-[0.12em] text-slate-400">Applied Filters</p>
                  <p className="text-sm text-slate-600">Subtest: {analytics.filters.subtest}</p>
                  <p className="text-sm text-slate-600">Profession: {analytics.filters.profession}</p>
                </div>
              </div>
            </AdminRoutePanel>
          </>
        ) : null}
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
