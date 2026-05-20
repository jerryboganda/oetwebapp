'use client';

import { useCallback, useEffect, useState } from 'react';
import { BarChart3, Clock, AlertTriangle, DollarSign, Users, Wrench, ShieldCheck } from 'lucide-react';
import { AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge } from '@/components/ui/badge';
import { EmptyState } from '@/components/ui/empty-error';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { apiClient } from '@/lib/api';

interface DailyMessageCount {
  date: string;
  count: number;
}

interface ToolUsageStat {
  tool: string;
  callCount: number;
  avgDurationMs: number;
  errorCount: number;
}

interface AnalyticsData {
  messagesPerDay: DailyMessageCount[];
  avgResponseTimeMs: number;
  toolUsage: ToolUsageStat[];
  errorRate: number;
  totalErrors: number;
  totalRequests: number;
  tokenCostEstimate: number;
  threadsPerUser: number;
  messagesPerThread: number;
  activeUsers7d: number;
  totalUsers: number;
}

type PageStatus = 'loading' | 'success' | 'error';

function BarChart({ data, maxValue }: { data: DailyMessageCount[]; maxValue: number }) {
  return (
    <div className="flex items-end gap-1" style={{ height: '160px' }}>
      {data.map((d) => {
        const height = maxValue > 0 ? (d.count / maxValue) * 100 : 0;
        return (
          <div key={d.date} className="group relative flex flex-1 flex-col items-center justify-end">
            <div
              className="w-full min-w-[4px] rounded-t bg-violet-500/70 transition-colors group-hover:bg-violet-400"
              style={{ height: `${Math.max(height, 2)}%` }}
            />
            <div className="absolute -top-8 hidden rounded bg-admin-surface-raised px-2 py-1 text-xs text-admin-text shadow-lg group-hover:block">
              {d.count} msgs
              <br />
              {new Date(d.date).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}
            </div>
          </div>
        );
      })}
    </div>
  );
}

function MetricCard({ icon: Icon, label, value, subtext }: { icon: React.ElementType; label: string; value: string; subtext?: string }) {
  return (
    <div className="rounded-xl border border-admin-border bg-admin-surface p-4">
      <div className="flex items-center gap-2">
        <Icon className="h-4 w-4 text-admin-text-muted" />
        <span className="text-xs font-bold uppercase tracking-wide text-admin-text-muted">{label}</span>
      </div>
      <p className="mt-2 text-2xl font-bold text-admin-text">{value}</p>
      {subtext && <p className="mt-0.5 text-xs text-admin-text-muted">{subtext}</p>}
    </div>
  );
}

export default function AiAssistantAnalyticsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [analytics, setAnalytics] = useState<AnalyticsData | null>(null);

  const loadAnalytics = useCallback(async () => {
    try {
      setStatus('loading');
      const data = await apiClient.get<AnalyticsData>('/v1/admin/ai-assistant/stats/analytics');
      setAnalytics(data);
      setStatus('success');
    } catch {
      setStatus('error');
    }
  }, []);

  useEffect(() => {
    if (isAuthenticated && role === 'admin') {
      // Data fetch on mount — setState inside async callback is fine
      // eslint-disable-next-line react-hooks/set-state-in-effect
      loadAnalytics();
    }
  }, [isAuthenticated, role, loadAnalytics]);

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminRouteWorkspace>
        <EmptyState icon={<ShieldCheck className="w-8 h-8" />} title="Admin access required" description="Sign in with an admin account to view this page." />
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace>
      <AsyncStateWrapper status={status} onRetry={loadAnalytics}>
        {analytics && (
          <>
            {/* Key metrics */}
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
              <MetricCard
                icon={Clock}
                label="Avg Response Time"
                value={`${analytics.avgResponseTimeMs}ms`}
                subtext="Mean time to first token"
              />
              <MetricCard
                icon={AlertTriangle}
                label="Error Rate"
                value={`${(analytics.errorRate * 100).toFixed(2)}%`}
                subtext={`${analytics.totalErrors} errors / ${analytics.totalRequests} requests`}
              />
              <MetricCard
                icon={DollarSign}
                label="Token Cost (30d)"
                value={`$${analytics.tokenCostEstimate.toFixed(2)}`}
                subtext="Estimated spend"
              />
              <MetricCard
                icon={Users}
                label="Active Users (7d)"
                value={String(analytics.activeUsers7d)}
                subtext={`of ${analytics.totalUsers} total`}
              />
            </div>

            {/* Messages per day chart */}
            <AdminRoutePanel>
              <div className="p-4">
                <div className="mb-4 flex items-center gap-2">
                  <BarChart3 className="h-4 w-4 text-admin-text-muted" />
                  <h3 className="text-sm font-bold text-admin-text">Messages per Day (Last 30 Days)</h3>
                </div>
                {analytics.messagesPerDay.length > 0 ? (
                  <>
                    <BarChart
                      data={analytics.messagesPerDay}
                      maxValue={Math.max(...analytics.messagesPerDay.map((d) => d.count))}
                    />
                    <div className="mt-2 flex justify-between text-xs text-admin-text-muted">
                      <span>{new Date(analytics.messagesPerDay[0].date).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}</span>
                      <span>{new Date(analytics.messagesPerDay[analytics.messagesPerDay.length - 1].date).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}</span>
                    </div>
                  </>
                ) : (
                  <p className="py-8 text-center text-sm text-admin-text-muted">No message data available</p>
                )}
              </div>
            </AdminRoutePanel>

            {/* Tool usage breakdown */}
            <AdminRoutePanel>
              <div className="p-4">
                <div className="mb-4 flex items-center gap-2">
                  <Wrench className="h-4 w-4 text-admin-text-muted" />
                  <h3 className="text-sm font-bold text-admin-text">Tool Usage Breakdown</h3>
                </div>
                {analytics.toolUsage.length > 0 ? (
                  <div className="space-y-2">
                    {analytics.toolUsage
                      .sort((a, b) => b.callCount - a.callCount)
                      .map((tool) => {
                        const maxCalls = analytics.toolUsage[0]?.callCount || 1;
                        const pct = (tool.callCount / maxCalls) * 100;
                        return (
                          <div key={tool.tool} className="flex items-center gap-3">
                            <span className="w-36 truncate text-xs font-medium text-admin-text">{tool.tool}</span>
                            <div className="flex-1">
                              <div className="h-5 w-full overflow-hidden rounded-md bg-admin-surface-raised">
                                <div
                                  className="h-full rounded-md bg-violet-500/50"
                                  style={{ width: `${pct}%` }}
                                />
                              </div>
                            </div>
                            <span className="w-16 text-right text-xs tabular-nums text-admin-text-muted">{tool.callCount} calls</span>
                            <span className="w-16 text-right text-xs tabular-nums text-admin-text-muted">{tool.avgDurationMs}ms</span>
                            {tool.errorCount > 0 && (
                              <Badge variant="danger">{tool.errorCount} err</Badge>
                            )}
                          </div>
                        );
                      })}
                  </div>
                ) : (
                  <p className="py-8 text-center text-sm text-admin-text-muted">No tool usage data</p>
                )}
              </div>
            </AdminRoutePanel>

            {/* User engagement */}
            <AdminRoutePanel>
              <div className="p-4">
                <div className="mb-4 flex items-center gap-2">
                  <Users className="h-4 w-4 text-admin-text-muted" />
                  <h3 className="text-sm font-bold text-admin-text">User Engagement</h3>
                </div>
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                  <div className="rounded-lg bg-admin-surface-raised p-3">
                    <p className="text-xs text-admin-text-muted">Avg Threads per User</p>
                    <p className="mt-1 text-xl font-bold text-admin-text">{analytics.threadsPerUser.toFixed(1)}</p>
                  </div>
                  <div className="rounded-lg bg-admin-surface-raised p-3">
                    <p className="text-xs text-admin-text-muted">Avg Messages per Thread</p>
                    <p className="mt-1 text-xl font-bold text-admin-text">{analytics.messagesPerThread.toFixed(1)}</p>
                  </div>
                  <div className="rounded-lg bg-admin-surface-raised p-3">
                    <p className="text-xs text-admin-text-muted">7-Day Retention</p>
                    <p className="mt-1 text-xl font-bold text-admin-text">
                      {analytics.totalUsers > 0
                        ? `${((analytics.activeUsers7d / analytics.totalUsers) * 100).toFixed(1)}%`
                        : '—'}
                    </p>
                  </div>
                </div>
              </div>
            </AdminRoutePanel>
          </>
        )}
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
