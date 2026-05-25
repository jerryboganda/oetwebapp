'use client';

import { useCallback, useEffect, useState } from 'react';
import { BarChart3, Clock, AlertTriangle, DollarSign, Users, Wrench, ShieldCheck } from 'lucide-react';
import { AdminOperationsLayout, KpiStrip, BentoGrid, BentoCell } from '@/components/admin/layout/admin-operations-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
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
              className="w-full min-w-[4px] rounded-t bg-[var(--admin-primary)] opacity-70 transition-opacity group-hover:opacity-100"
              style={{ height: `${Math.max(height, 2)}%` }}
            />
            <div className="absolute -top-8 hidden rounded bg-admin-bg-elevated px-2 py-1 text-xs text-admin-fg-strong shadow-lg group-hover:block">
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
      // eslint-disable-next-line react-hooks/set-state-in-effect
      loadAnalytics();
    }
  }, [isAuthenticated, role, loadAnalytics]);

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'AI Assistant', href: '/admin' },
    { label: 'Analytics' },
  ];

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminOperationsLayout
        title="AI Assistant Analytics"
        eyebrow="AI Assistant"
        breadcrumbs={breadcrumbs}
        primaryGrid={
          <EmptyState
            title="Admin access required"
            description="Sign in with an admin account to view this page."
            illustration={<ShieldCheck className="h-8 w-8" />}
          />
        }
      />
    );
  }

  return (
    <AdminOperationsLayout
      title="AI Assistant Analytics"
      description="Latency, error rate, cost, tool usage, and user engagement signal for the in-app AI assistant."
      eyebrow="AI Assistant"
      breadcrumbs={breadcrumbs}
      actions={
        <Button variant="secondary" size="sm" onClick={loadAnalytics}>
          Refresh
        </Button>
      }
      kpis={
        analytics ? (
          <KpiStrip>
            <KpiTile label="Avg Response Time" value={`${analytics.avgResponseTimeMs}ms`} icon={<Clock className="h-4 w-4" />} tone="primary" />
            <KpiTile label="Error Rate" value={`${(analytics.errorRate * 100).toFixed(2)}%`} icon={<AlertTriangle className="h-4 w-4" />} tone={analytics.errorRate > 0.05 ? 'danger' : 'success'} />
            <KpiTile label="Token Cost (30d)" value={`$${analytics.tokenCostEstimate.toFixed(2)}`} icon={<DollarSign className="h-4 w-4" />} tone="warning" />
            <KpiTile label="Active Users (7d)" value={analytics.activeUsers7d} icon={<Users className="h-4 w-4" />} tone="info" />
          </KpiStrip>
        ) : null
      }
      primaryGrid={
        <AsyncStateWrapper status={status} onRetry={loadAnalytics}>
          {analytics && (
            <div className="space-y-6">
              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2">
                    <BarChart3 className="h-4 w-4 text-admin-fg-muted" />
                    Messages per Day (Last 30 Days)
                  </CardTitle>
                </CardHeader>
                <CardContent>
                  {analytics.messagesPerDay.length > 0 ? (
                    <>
                      <BarChart data={analytics.messagesPerDay} maxValue={Math.max(...analytics.messagesPerDay.map((d) => d.count))} />
                      <div className="mt-2 flex justify-between text-xs text-admin-fg-muted">
                        <span>{new Date(analytics.messagesPerDay[0].date).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}</span>
                        <span>{new Date(analytics.messagesPerDay[analytics.messagesPerDay.length - 1].date).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}</span>
                      </div>
                    </>
                  ) : (
                    <p className="py-8 text-center text-sm text-admin-fg-muted">No message data available</p>
                  )}
                </CardContent>
              </Card>

              <BentoGrid>
                <BentoCell span={{ default: 12, xl: 8 }}>
                  <Card>
                    <CardHeader>
                      <CardTitle className="flex items-center gap-2">
                        <Wrench className="h-4 w-4 text-admin-fg-muted" />
                        Tool Usage Breakdown
                      </CardTitle>
                    </CardHeader>
                    <CardContent>
                      {analytics.toolUsage.length > 0 ? (
                        <div className="space-y-2">
                          {analytics.toolUsage
                            .sort((a, b) => b.callCount - a.callCount)
                            .map((tool) => {
                              const maxCalls = analytics.toolUsage[0]?.callCount || 1;
                              const pct = (tool.callCount / maxCalls) * 100;
                              return (
                                <div key={tool.tool} className="flex items-center gap-3">
                                  <span className="w-36 truncate text-xs font-medium text-admin-fg-strong">{tool.tool}</span>
                                  <div className="flex-1">
                                    <div className="h-5 w-full overflow-hidden rounded bg-admin-bg-subtle">
                                      <div className="h-full rounded bg-[var(--admin-primary)] opacity-60" style={{ width: `${pct}%` }} />
                                    </div>
                                  </div>
                                  <span className="w-16 text-right text-xs tabular-nums text-admin-fg-muted">{tool.callCount} calls</span>
                                  <span className="w-16 text-right text-xs tabular-nums text-admin-fg-muted">{tool.avgDurationMs}ms</span>
                                  {tool.errorCount > 0 && (
                                    <Badge variant="danger" intensity="tinted" size="sm">{tool.errorCount} err</Badge>
                                  )}
                                </div>
                              );
                            })}
                        </div>
                      ) : (
                        <p className="py-8 text-center text-sm text-admin-fg-muted">No tool usage data</p>
                      )}
                    </CardContent>
                  </Card>
                </BentoCell>

                <BentoCell span={{ default: 12, xl: 4 }}>
                  <Card>
                    <CardHeader>
                      <CardTitle className="flex items-center gap-2">
                        <Users className="h-4 w-4 text-admin-fg-muted" />
                        User Engagement
                      </CardTitle>
                    </CardHeader>
                    <CardContent>
                      <div className="space-y-3">
                        <div className="rounded-admin bg-admin-bg-subtle p-3">
                          <p className="text-xs text-admin-fg-muted">Avg Threads per User</p>
                          <p className="mt-1 text-xl font-bold tabular-nums text-admin-fg-strong">{analytics.threadsPerUser.toFixed(1)}</p>
                        </div>
                        <div className="rounded-admin bg-admin-bg-subtle p-3">
                          <p className="text-xs text-admin-fg-muted">Avg Messages per Thread</p>
                          <p className="mt-1 text-xl font-bold tabular-nums text-admin-fg-strong">{analytics.messagesPerThread.toFixed(1)}</p>
                        </div>
                        <div className="rounded-admin bg-admin-bg-subtle p-3">
                          <p className="text-xs text-admin-fg-muted">7-Day Retention</p>
                          <p className="mt-1 text-xl font-bold tabular-nums text-admin-fg-strong">
                            {analytics.totalUsers > 0 ? `${((analytics.activeUsers7d / analytics.totalUsers) * 100).toFixed(1)}%` : '—'}
                          </p>
                        </div>
                      </div>
                    </CardContent>
                  </Card>
                </BentoCell>
              </BentoGrid>
            </div>
          )}
        </AsyncStateWrapper>
      }
    />
  );
}
