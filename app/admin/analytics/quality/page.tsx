'use client';

import { useState, useEffect } from 'react';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { mockQualityAnalytics } from '@/lib/mock-admin-data';
import { Select } from '@/components/ui/form-controls';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Toast } from '@/components/ui/alert';
import { TrendingUp, TrendingDown, Clock, FileText, CheckCircle2, Users, AlertTriangle, BarChart3 } from 'lucide-react';
import { analytics } from '@/lib/analytics';

export default function QualityAnalyticsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<'loading' | 'success'>('loading');
  const [timeRange, setTimeRange] = useState('7d');
  const [segmentFilters, setSegmentFilters] = useState<Record<string, string[]>>({});
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [lastRefreshed] = useState(() => new Date());

  useEffect(() => {
    const load = async () => {
      await new Promise(resolve => setTimeout(resolve, 300));
      setPageStatus('success');
      analytics.track('admin_quality_analytics_viewed', {});
    };
    load();
  }, []);

  const data = mockQualityAnalytics;

  const filterGroups: FilterGroup[] = [
    {
      id: 'subtest',
      label: 'Sub-test',
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
      ],
    },
  ];

  const handleTimeRangeChange = (value: string) => {
    setTimeRange(value);
    analytics.track('admin_quality_analytics_viewed', { timeRange: value });
    const label = value === '7d' ? 'Last 7 Days' : value === '30d' ? 'Last 30 Days' : 'Year to Date';
    setToast({ variant: 'success', message: `Analytics view updated to ${label}.` });
  };

  const handleFilterChange = (groupId: string, optionId: string) => {
    setSegmentFilters(prev => {
      const current = prev[groupId] || [];
      const updated = current.includes(optionId) ? current.filter(id => id !== optionId) : [...current, optionId];
      return { ...prev, [groupId]: updated };
    });
  };

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <div className="space-y-6 max-w-7xl mx-auto" role="main" aria-label="Quality Analytics">
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-slate-900">Quality Analytics</h1>
          <p className="text-sm text-slate-500 mt-1">Review AI grading accuracy vs Human expert divergence.</p>
        </div>
        <div className="flex items-center gap-3">
          <span className="text-xs text-slate-400" aria-label="Data freshness">Data as of {lastRefreshed.toLocaleTimeString()}</span>
          <Select
            label=""
            value={timeRange}
            onChange={(e) => handleTimeRangeChange(e.target.value)}
            options={[
              { value: '7d', label: 'Last 7 Days' },
              { value: '30d', label: 'Last 30 Days' },
              { value: 'ytd', label: 'Year to Date' },
            ]}
          />
        </div>
      </div>
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-4">
        <FilterBar groups={filterGroups} selected={segmentFilters} onChange={handleFilterChange} onClear={() => setSegmentFilters({})} />
      </div>

      <AsyncStateWrapper status={pageStatus} onRetry={() => window.location.reload()}>
        {/* Primary KPIs row */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
          <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-6">
            <div className="flex items-center gap-2 mb-2">
              <CheckCircle2 className="w-4 h-4 text-slate-400" />
              <h3 className="text-sm font-medium text-slate-500">AI-Human Agreement</h3>
            </div>
            <div className="text-3xl font-semibold text-slate-900 mb-1">{data.aiHumanAgreement.value}%</div>
            <div className={`text-sm font-medium flex items-center gap-1 ${data.aiHumanAgreement.trend > 0 ? 'text-green-600' : 'text-red-600'}`}>
              {data.aiHumanAgreement.trend > 0 ? <TrendingUp className="w-3.5 h-3.5" /> : <TrendingDown className="w-3.5 h-3.5" />}
              {data.aiHumanAgreement.trend > 0 ? '+' : ''}{data.aiHumanAgreement.trend}% from last period
            </div>
          </div>
          <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-6">
            <div className="flex items-center gap-2 mb-2">
              <AlertTriangle className="w-4 h-4 text-slate-400" />
              <h3 className="text-sm font-medium text-slate-500">Appeals Rate</h3>
            </div>
            <div className="text-3xl font-semibold text-slate-900 mb-1">{data.appealsRate.value}%</div>
            <div className={`text-sm font-medium flex items-center gap-1 ${data.appealsRate.trend < 0 ? 'text-green-600' : 'text-red-600'}`}>
              {data.appealsRate.trend < 0 ? <TrendingDown className="w-3.5 h-3.5" /> : <TrendingUp className="w-3.5 h-3.5" />}
              {data.appealsRate.trend > 0 ? '+' : ''}{data.appealsRate.trend}% from last period
            </div>
          </div>
          <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-6">
            <div className="flex items-center gap-2 mb-2">
              <Clock className="w-4 h-4 text-slate-400" />
              <h3 className="text-sm font-medium text-slate-500">Avg Review Time</h3>
            </div>
            <div className="text-3xl font-semibold text-slate-900 mb-1">{data.avgReviewTime.value}{data.avgReviewTime.unit === 'min' ? 'm' : data.avgReviewTime.unit}</div>
            <div className="text-sm text-slate-500 font-medium">Stable</div>
          </div>
        </div>

        {/* Extended KPIs row — §4.12 */}
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-5 flex items-center gap-4">
            <div className="w-10 h-10 rounded-lg bg-blue-50 text-blue-600 flex items-center justify-center">
              <FileText className="w-5 h-5" />
            </div>
            <div>
              <div className="text-lg font-semibold text-slate-900">{data.contentPerformance.avgScore}/9</div>
              <div className="text-sm text-slate-500">Content Avg Score</div>
              <div className="text-xs text-slate-400">Top: {data.contentPerformance.topContent}</div>
            </div>
          </div>
          <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-5 flex items-center gap-4">
            <div className="w-10 h-10 rounded-lg bg-green-50 text-green-600 flex items-center justify-center">
              <CheckCircle2 className="w-5 h-5" />
            </div>
            <div>
              <div className="text-lg font-semibold text-slate-900">{data.reviewSLA.metPercent}%</div>
              <div className="text-sm text-slate-500">Review SLA Met</div>
              <div className="text-xs text-slate-400">Avg turnaround: {data.reviewSLA.avgTurnaround}</div>
            </div>
          </div>
          <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-5 flex items-center gap-4">
            <div className="w-10 h-10 rounded-lg bg-purple-50 text-purple-600 flex items-center justify-center">
              <Users className="w-5 h-5" />
            </div>
            <div>
              <div className="text-lg font-semibold text-slate-900">{data.featureAdoption.adoptionRate}%</div>
              <div className="text-sm text-slate-500">Feature Adoption</div>
              <div className="text-xs text-slate-400">{data.featureAdoption.activeUsers.toLocaleString()} active users</div>
            </div>
          </div>
          <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-5 flex items-center gap-4">
            <div className="w-10 h-10 rounded-lg bg-amber-50 text-amber-600 flex items-center justify-center">
              <AlertTriangle className="w-5 h-5" />
            </div>
            <div>
              <div className="text-lg font-semibold text-slate-900">{data.riskCases.count}</div>
              <div className="text-sm text-slate-500">Risk Cases</div>
              <div className="text-xs text-slate-400">Severity: {data.riskCases.severity}</div>
            </div>
          </div>
        </div>

        {/* Chart placeholder */}
        <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-8 text-center flex flex-col items-center justify-center min-h-[300px]">
          <div className="w-16 h-16 bg-blue-50 text-blue-600 rounded-full flex items-center justify-center mb-4">
            <BarChart3 className="w-8 h-8" />
          </div>
          <h3 className="text-lg font-medium text-slate-900">Divergence Trend Chart</h3>
          <p className="text-sm text-slate-500 mt-2 max-w-sm">
            A charting library would be integrated here to visualize AI-human divergence trends over time.
          </p>
        </div>
      </AsyncStateWrapper>
    </div>
  );
}
