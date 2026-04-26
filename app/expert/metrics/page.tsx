'use client';

import { useEffect, useState } from 'react';
import { BarChart, Bar, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from '@/components/charts/dynamic-recharts';
import { AlertTriangle, CheckCircle, Clock, Sparkles, TrendingUp } from 'lucide-react';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Card, CardContent } from '@/components/ui/card';
import { Select } from '@/components/ui/form-controls';
import {
  ExpertRouteFreshnessBadge,
  ExpertRouteHero,
  ExpertRouteSectionHeader,
  ExpertRouteSummaryCard,
  ExpertRouteWorkspace,
} from '@/components/domain/expert-route-surface';
import { fetchExpertMetrics, isApiError } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { ExpertCompletionData, ExpertMetrics } from '@/lib/types/expert';

type AsyncStatus = 'loading' | 'error' | 'success';

export default function PerformanceMetricsPage() {
  const [metrics, setMetrics] = useState<ExpertMetrics | null>(null);
  const [completionData, setCompletionData] = useState<ExpertCompletionData[]>([]);
  const [pageStatus, setPageStatus] = useState<AsyncStatus>('loading');
  const [dateRange, setDateRange] = useState('7');
  const [retryKey, setRetryKey] = useState(0);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [generatedAt, setGeneratedAt] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      try {
        setPageStatus('loading');
        setErrorMessage(null);
        const data = await fetchExpertMetrics(parseInt(dateRange));
        if (cancelled) return;
        setMetrics(data.metrics);
        setCompletionData(data.completionData);
        setGeneratedAt(data.generatedAt);
        setPageStatus('success');
        analytics.track('expert_metrics_viewed', { days: parseInt(dateRange) });
      } catch (error) {
        if (!cancelled) {
          setErrorMessage(isApiError(error) ? error.userMessage : 'Unable to load performance metrics right now.');
          setPageStatus('error');
        }
      }
    }
    void load();
    return () => { cancelled = true; };
  }, [dateRange, retryKey]);

  const dateRangeOptions = [
    { value: '7', label: 'Last 7 Days' },
    { value: '30', label: 'Last 30 Days' },
    { value: '90', label: 'Last 90 Days' },
  ];

  return (
    <ExpertRouteWorkspace role="main" aria-label="Performance Metrics">
      <AsyncStateWrapper status={pageStatus} onRetry={() => setRetryKey((k) => k + 1)} errorMessage={errorMessage ?? undefined}>
        <div className="space-y-6">
          <ExpertRouteHero
            eyebrow="Performance Metrics"
            icon={Sparkles}
            accent="primary"
            title="Dashboard signals"
            description="Historical performance evidence for throughput, SLA discipline, rework rate, and calibration quality is kept in the same visual language as the learner workspace."
            highlights={[
              { icon: TrendingUp, label: 'Completed', value: String(metrics?.totalReviewsCompleted ?? '-') },
              { icon: Clock, label: 'SLA compliance', value: `${metrics?.averageSlaCompliance ?? '-'}%` },
              { icon: CheckCircle, label: 'Alignment', value: `${metrics?.averageCalibrationAlignment ?? '-'}%` },
            ]}
            aside={(
              <div className="space-y-3">
                <ExpertRouteFreshnessBadge value={generatedAt} />
                <div className="w-48">
                  <Select
                    options={dateRangeOptions}
                    value={dateRange}
                    onChange={(event) => setDateRange(event.target.value)}
                    aria-label="Date range filter"
                  />
                </div>
              </div>
            )}
          />

          <section className="space-y-4">
            <ExpertRouteSectionHeader
              eyebrow="Performance Signals"
              title="Operational metrics"
              description="Use learner-style summary cards to scan throughput, SLA discipline, and calibration quality."
            />
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
              <ExpertRouteSummaryCard
                label="Total Completed"
                value={metrics?.totalReviewsCompleted ?? '-'}
                hint="Completed expert reviews in the selected window."
                icon={TrendingUp}
              />
              <ExpertRouteSummaryCard
                label="Draft Reviews"
                value={metrics?.draftReviews ?? '-'}
                hint="Draft review workspaces still open."
                accent="navy"
                icon={Clock}
              />
              <ExpertRouteSummaryCard
                label="SLA Compliance"
                value={`${metrics?.averageSlaCompliance ?? '-'}%`}
                hint="Average compliance across the selected window."
                accent="emerald"
                icon={Clock}
              />
              <ExpertRouteSummaryCard
                label="Avg Turnaround"
                value={`${metrics?.averageTurnaroundHours ?? '-'}h`}
                hint="Average time from assignment to completion."
                accent="primary"
                icon={Clock}
              />
              <ExpertRouteSummaryCard
                label="Alignment Score"
                value={`${metrics?.averageCalibrationAlignment ?? '-'}%`}
                hint="Calibration alignment against benchmark review."
                accent="emerald"
                icon={CheckCircle}
              />
              <ExpertRouteSummaryCard
                label="Rework Rate"
                value={`${metrics?.reworkRate ?? '-'}%`}
                hint="Reviews that need rework or follow-up."
                accent="amber"
                icon={AlertTriangle}
              />
            </div>
          </section>

          <section className="space-y-4">
            <ExpertRouteSectionHeader
              eyebrow="Trend View"
              title={`Reviews completed in the last ${dateRange} days`}
              description="A simple chart keeps the recent throughput trend visible without changing the underlying metrics model."
            />
            <Card>
              <CardContent className="p-5">
                <div className="h-[240px] w-full min-w-0 sm:h-[280px] lg:h-[320px]" role="img" aria-label="Reviews completed bar chart">
                  <ResponsiveContainer width="100%" height="100%">
                    <BarChart data={completionData} margin={{ top: 10, right: 10, left: -20, bottom: 0 }} aria-label="Reviews completed bar chart">
                      <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#f3f4f6" />
                      <XAxis dataKey="day" axisLine={false} tickLine={false} tick={{ fontSize: 12, fill: '#526072' }} dy={10} />
                      <YAxis axisLine={false} tickLine={false} tick={{ fontSize: 12, fill: '#526072' }} />
                      <Tooltip
                        cursor={{ fill: '#f7f5ef' }}
                        contentStyle={{ borderRadius: '8px', border: 'none', boxShadow: '0 4px 6px -1px rgb(0 0 0 / 0.1)' }}
                      />
                      <Bar dataKey="count" fill="#7c3aed" radius={[4, 4, 0, 0]} maxBarSize={40} />
                    </BarChart>
                  </ResponsiveContainer>
                </div>
              </CardContent>
            </Card>
          </section>
        </div>
      </AsyncStateWrapper>
    </ExpertRouteWorkspace>
  );
}
