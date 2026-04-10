'use client';

import { useEffect, useState } from 'react';
import { BarChart3, TrendingUp, TrendingDown, Users, DollarSign, Target, Activity } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteSummaryCard, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { analytics } from '@/lib/analytics';

// Note: In production, these would come from the backend BI endpoint. 
// For now, we show realistic placeholder data until the aggregate queries are built.

interface BIMetric {
  label: string;
  value: string;
  change: number;
  changeLabel: string;
  icon: React.ReactNode;
  color: string;
}

interface SubtestStat {
  subtest: string;
  completions: number;
  avgScore: number;
}

interface MonthlyRevenue {
  month: string;
  revenue: number;
  subscriptions: number;
}

type PageStatus = 'loading' | 'success' | 'error';

export default function BusinessIntelligencePage() {
  const { isAuthenticated } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [metrics, setMetrics] = useState<BIMetric[]>([]);
  const [subtestStats, setSubtestStats] = useState<SubtestStat[]>([]);
  const [monthly, setMonthly] = useState<MonthlyRevenue[]>([]);

  useEffect(() => {
    analytics.track('content_view', { page: 'admin-bi-dashboard' });

    // Simulated data load — replace with real API call when backend aggregation is ready
    const timer = setTimeout(() => {
      setMetrics([
        { label: 'Monthly Revenue', value: '$48,750', change: 12.3, changeLabel: 'vs last month', icon: <DollarSign className="w-5 h-5" />, color: 'text-green-600 dark:text-green-400' },
        { label: 'Active Learners', value: '2,847', change: 8.1, changeLabel: 'vs last month', icon: <Users className="w-5 h-5" />, color: 'text-blue-600 dark:text-blue-400' },
        { label: 'Retention Rate', value: '87.4%', change: 2.1, changeLabel: 'vs last month', icon: <Target className="w-5 h-5" />, color: 'text-purple-600 dark:text-purple-400' },
        { label: 'Avg Score Improvement', value: '+42 pts', change: 5.7, changeLabel: 'per learner', icon: <TrendingUp className="w-5 h-5" />, color: 'text-indigo-600 dark:text-indigo-400' },
        { label: 'Expert Utilization', value: '73.2%', change: -1.4, changeLabel: 'vs last month', icon: <Activity className="w-5 h-5" />, color: 'text-orange-600 dark:text-orange-400' },
        { label: 'Churn Rate', value: '4.2%', change: -0.8, changeLabel: 'vs last month', icon: <TrendingDown className="w-5 h-5" />, color: 'text-red-600 dark:text-red-400' },
      ]);

      setSubtestStats([
        { subtest: 'Writing', completions: 4521, avgScore: 342 },
        { subtest: 'Speaking', completions: 3187, avgScore: 328 },
        { subtest: 'Reading', completions: 5892, avgScore: 371 },
        { subtest: 'Listening', completions: 5406, avgScore: 365 },
      ]);

      setMonthly([
        { month: 'Jan', revenue: 38200, subscriptions: 142 },
        { month: 'Feb', revenue: 41500, subscriptions: 156 },
        { month: 'Mar', revenue: 39800, subscriptions: 148 },
        { month: 'Apr', revenue: 43100, subscriptions: 163 },
        { month: 'May', revenue: 45600, subscriptions: 171 },
        { month: 'Jun', revenue: 48750, subscriptions: 189 },
      ]);

      setPageStatus('success');
    }, 600);

    return () => clearTimeout(timer);
  }, []);

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        title="Business Intelligence"
        description="Key performance metrics and growth trends."
      />

      <AsyncStateWrapper
        status={pageStatus}
        loadingMessage="Loading analytics…"
        errorMessage="Unable to load business intelligence data."
      >
        {/* KPI Cards */}
        <div className="grid grid-cols-2 lg:grid-cols-3 gap-4 mb-8">
          {metrics.map((m) => (
            <Card key={m.label} className="p-5">
              <div className="flex items-center justify-between mb-3">
                <span className={m.color}>{m.icon}</span>
                <span className={`text-xs font-medium ${m.change >= 0 ? 'text-green-600 dark:text-green-400' : 'text-red-600 dark:text-red-400'}`}>
                  {m.change >= 0 ? '+' : ''}{m.change}% {m.changeLabel}
                </span>
              </div>
              <p className="text-2xl font-bold text-gray-900 dark:text-gray-100">{m.value}</p>
              <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">{m.label}</p>
            </Card>
          ))}
        </div>

        {/* Subtest Performance */}
        <AdminRoutePanel>
          <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100 mb-4">Subtest Performance</h3>
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200 dark:divide-gray-700">
              <thead>
                <tr>
                  <th className="px-4 py-3 text-left text-xs font-semibold text-gray-600 dark:text-gray-300 uppercase">Subtest</th>
                  <th className="px-4 py-3 text-left text-xs font-semibold text-gray-600 dark:text-gray-300 uppercase">Completions</th>
                  <th className="px-4 py-3 text-left text-xs font-semibold text-gray-600 dark:text-gray-300 uppercase">Avg Score</th>
                  <th className="px-4 py-3 text-left text-xs font-semibold text-gray-600 dark:text-gray-300 uppercase">Performance</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-200 dark:divide-gray-700">
                {subtestStats.map((s) => (
                  <tr key={s.subtest} className="hover:bg-gray-50 dark:hover:bg-gray-800/30">
                    <td className="px-4 py-3 text-sm font-medium text-gray-900 dark:text-gray-100">{s.subtest}</td>
                    <td className="px-4 py-3 text-sm text-gray-700 dark:text-gray-300">{s.completions.toLocaleString()}</td>
                    <td className="px-4 py-3 text-sm text-gray-700 dark:text-gray-300">{s.avgScore}/500</td>
                    <td className="px-4 py-3">
                      <div className="w-full bg-gray-200 dark:bg-gray-700 rounded-full h-2">
                        <div
                          className="bg-indigo-600 dark:bg-indigo-500 h-2 rounded-full"
                          style={{ width: `${(s.avgScore / 500) * 100}%` }}
                        />
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </AdminRoutePanel>

        {/* Revenue Trend */}
        <AdminRoutePanel className="mt-6">
          <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100 mb-4">Revenue Trend (6 months)</h3>
          <div className="space-y-3">
            {monthly.map((m) => {
              const maxRevenue = Math.max(...monthly.map((r) => r.revenue));
              const pct = maxRevenue > 0 ? (m.revenue / maxRevenue) * 100 : 0;
              return (
                <div key={m.month} className="flex items-center gap-3">
                  <span className="w-8 text-xs font-medium text-gray-500 dark:text-gray-400">{m.month}</span>
                  <div className="flex-1 bg-gray-200 dark:bg-gray-700 rounded-full h-6 relative">
                    <div
                      className="bg-gradient-to-r from-indigo-500 to-purple-500 h-6 rounded-full flex items-center justify-end pr-2 transition-all duration-500"
                      style={{ width: `${pct}%` }}
                    >
                      <span className="text-xs text-white font-medium">${(m.revenue / 1000).toFixed(1)}k</span>
                    </div>
                  </div>
                  <span className="text-xs text-gray-400 w-16 text-right">{m.subscriptions} subs</span>
                </div>
              );
            })}
          </div>
        </AdminRoutePanel>
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
