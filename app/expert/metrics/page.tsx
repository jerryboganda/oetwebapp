'use client';

import { useState, useEffect } from 'react';
import { Card, CardContent } from '@/components/ui/card';
import { Select } from '@/components/ui/form-controls';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import type { ExpertMetrics, ExpertCompletionData } from '@/lib/types/expert';
import { CheckCircle, Clock, AlertTriangle, TrendingUp } from 'lucide-react';
import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer } from 'recharts';
import { fetchExpertMetrics, isApiError } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type AsyncStatus = 'loading' | 'error' | 'success';

export default function PerformanceMetricsPage() {
  const [metrics, setMetrics] = useState<ExpertMetrics | null>(null);
  const [completionData, setCompletionData] = useState<ExpertCompletionData[]>([]);
  const [pageStatus, setPageStatus] = useState<AsyncStatus>('loading');
  const [dateRange, setDateRange] = useState('7');
  const [retryKey, setRetryKey] = useState(0);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

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
    <div className="max-w-7xl mx-auto p-4 md:p-8 space-y-6" role="main" aria-label="Performance Metrics">
      <div className="flex items-center justify-between flex-wrap gap-4">
        <div>
          <h1 className="text-2xl font-bold text-navy">Performance Metrics</h1>
          <p className="text-muted text-sm mt-1">Your operational review statistics.</p>
        </div>
        <div className="w-48">
          <Select
            options={dateRangeOptions}
            value={dateRange}
            onChange={(e) => setDateRange(e.target.value)}
            aria-label="Date range filter"
          />
        </div>
      </div>

      <AsyncStateWrapper status={pageStatus} onRetry={() => setRetryKey((k) => k + 1)} errorMessage={errorMessage ?? undefined}>
        {/* KPI Cards */}
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6 gap-4">
          <Card>
            <CardContent className="p-4 flex items-center justify-between">
              <div>
                <p className="text-sm text-muted font-semibold">Total Completed</p>
                <p className="text-2xl font-bold text-navy mt-1">{metrics?.totalReviewsCompleted ?? '-'}</p>
              </div>
              <TrendingUp className="w-8 h-8 text-blue-100" />
            </CardContent>
          </Card>

          <Card>
            <CardContent className="p-4 flex items-center justify-between">
              <div>
                <p className="text-sm text-muted font-semibold">Draft Reviews</p>
                <p className="text-2xl font-bold text-navy mt-1">{metrics?.draftReviews ?? '-'}</p>
              </div>
              <Clock className="w-8 h-8 text-blue-100" />
            </CardContent>
          </Card>

          <Card>
            <CardContent className="p-4 flex items-center justify-between">
              <div>
                <p className="text-sm text-muted font-semibold">SLA Compliance</p>
                <p className="text-2xl font-bold text-emerald-600 mt-1">{metrics?.averageSlaCompliance ?? '-'}%</p>
              </div>
              <Clock className="w-8 h-8 text-emerald-100" />
            </CardContent>
          </Card>

          <Card>
            <CardContent className="p-4 flex items-center justify-between">
              <div>
                <p className="text-sm text-muted font-semibold">Avg Turnaround</p>
                <p className="text-2xl font-bold text-navy mt-1">{metrics?.averageTurnaroundHours ?? '-'}h</p>
              </div>
              <Clock className="w-8 h-8 text-gray-100" />
            </CardContent>
          </Card>

          <Card>
            <CardContent className="p-4 flex items-center justify-between">
              <div>
                <p className="text-sm text-muted font-semibold">Alignment Score</p>
                <p className="text-2xl font-bold text-emerald-600 mt-1">{metrics?.averageCalibrationAlignment ?? '-'}%</p>
              </div>
              <CheckCircle className="w-8 h-8 text-emerald-100" />
            </CardContent>
          </Card>

          <Card>
            <CardContent className="p-4 flex items-center justify-between">
              <div>
                <p className="text-sm text-muted font-semibold">Rework Rate</p>
                <p className="text-2xl font-bold text-amber-600 mt-1">{metrics?.reworkRate ?? '-'}%</p>
              </div>
              <AlertTriangle className="w-8 h-8 text-amber-100" />
            </CardContent>
          </Card>
        </div>

        {/* Chart */}
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          <Card className="flex flex-col">
            <div className="p-4 border-b border-gray-100">
              <h3 className="font-semibold text-navy">Reviews Completed (Last {dateRange} Days)</h3>
            </div>
            <CardContent className="p-4 flex-1 min-h-[300px]">
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={completionData} margin={{ top: 10, right: 10, left: -20, bottom: 0 }} aria-label="Reviews completed bar chart">
                  <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#f3f4f6" />
                  <XAxis dataKey="day" axisLine={false} tickLine={false} tick={{ fontSize: 12, fill: '#6b7280' }} dy={10} />
                  <YAxis axisLine={false} tickLine={false} tick={{ fontSize: 12, fill: '#6b7280' }} />
                  <Tooltip
                    cursor={{ fill: '#f9fafb' }}
                    contentStyle={{ borderRadius: '8px', border: 'none', boxShadow: '0 4px 6px -1px rgb(0 0 0 / 0.1)' }}
                  />
                  <Bar dataKey="count" fill="#3b82f6" radius={[4, 4, 0, 0]} maxBarSize={40} />
                </BarChart>
              </ResponsiveContainer>
            </CardContent>
          </Card>
        </div>
      </AsyncStateWrapper>
    </div>
  );
}
