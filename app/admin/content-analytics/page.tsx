'use client';

import { useEffect, useState } from 'react';
import { BarChart3, TrendingUp, Users, Clock, Search } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { BarMeter } from '@/components/ui/bar-meter';
import { Input } from '@/components/ui/form-controls';
import { EmptyState } from '@/components/ui/empty-error';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRoutePanelFooter,
  AdminRouteStatRow,
  AdminRouteSummaryCard,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { analytics } from '@/lib/analytics';

interface ContentAnalytics {
  contentId: string;
  title: string;
  subtestCode: string;
  status: string;
  metrics: {
    totalAttempts: number;
    completedAttempts: number;
    completionRate: number;
    averageTimeMinutes: number;
    uniqueLearners: number;
    averageScore: number | null;
    medianScore: number | null;
    scoreStdDev: number | null;
  };
  monthlyTrend: { month: string; attempts: number; completed: number }[];
  evaluationCount: number;
}

type Status = 'idle' | 'loading' | 'success' | 'error' | 'empty';

async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, {
    ...init,
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
      ...init?.headers,
    },
  });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

export default function ContentAnalyticsPage() {
  const [data, setData] = useState<ContentAnalytics | null>(null);
  const [status, setStatus] = useState<Status>('idle');
  const [contentId, setContentId] = useState('');

  useEffect(() => {
    analytics.track('admin_content_analytics_viewed');
  }, []);

  const load = async () => {
    if (!contentId) return;
    setStatus('loading');
    try {
      const res = await apiRequest<ContentAnalytics>(`/v1/admin/content-analytics/${contentId}`);
      setData(res);
      setStatus('success');
    } catch {
      setData(null);
      setStatus('error');
    }
  };

  const maxAttempts = data ? Math.max(1, ...data.monthlyTrend.map((m) => m.attempts)) : 1;

  return (
    <AdminRouteWorkspace role="main" aria-label="Content item analytics">
      <AdminRouteHero
        eyebrow="Content · Deep dive"
        icon={BarChart3}
        accent="navy"
        title="Content Item Analytics"
        description="Inspect per-item usage, completion rates, and learner outcomes for any content ID."
      />

      <AdminRoutePanel
        eyebrow="Lookup"
        title="Find an item"
        description="Enter a content ID to pull its full analytics profile."
      >
        <form
          onSubmit={(event) => {
            event.preventDefault();
            void load();
          }}
          className="flex flex-col gap-3 sm:flex-row sm:items-end"
        >
          <div className="flex-1">
            <Input
              label="Content ID"
              placeholder="Enter content ID…"
              value={contentId}
              onChange={(event) => setContentId(event.target.value)}
            />
          </div>
          <Button type="submit" loading={status === 'loading'} disabled={!contentId}>
            <Search className="h-4 w-4" /> Analyze
          </Button>
        </form>
      </AdminRoutePanel>

      {status !== 'idle' ? (
        <AsyncStateWrapper
          status={status === 'error' ? 'error' : status === 'loading' ? 'loading' : status === 'success' ? 'success' : 'empty'}
          onRetry={() => void load()}
          emptyContent={
            <EmptyState
              icon={<BarChart3 className="h-6 w-6" aria-hidden />}
              title="No analytics yet"
              description="Try a different content ID, or check that attempts have been recorded."
            />
          }
        >
          {data ? (
            <>
              <AdminRoutePanel
                eyebrow="Item"
                title={data.title}
                description="Quick identity + current lifecycle state."
                actions={
                  <div className="flex flex-wrap gap-2">
                    <Badge variant="outline" className="capitalize">{data.subtestCode}</Badge>
                    <Badge variant="muted">{data.status}</Badge>
                  </div>
                }
              >
                <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
                  <AdminRouteSummaryCard
                    label="Total attempts"
                    value={data.metrics.totalAttempts}
                    icon={<BarChart3 className="h-5 w-5" />}
                  />
                  <AdminRouteSummaryCard
                    label="Completion rate"
                    value={`${data.metrics.completionRate}%`}
                    icon={<TrendingUp className="h-5 w-5" />}
                    tone="success"
                  />
                  <AdminRouteSummaryCard
                    label="Unique learners"
                    value={data.metrics.uniqueLearners}
                    icon={<Users className="h-5 w-5" />}
                  />
                  <AdminRouteSummaryCard
                    label="Avg time"
                    value={`${data.metrics.averageTimeMinutes}m`}
                    icon={<Clock className="h-5 w-5" />}
                    tone="warning"
                  />
                </div>
              </AdminRoutePanel>

              {data.metrics.averageScore !== null ? (
                <AdminRoutePanel
                  eyebrow="Scoring"
                  title="Score distribution"
                  description="Central tendency + spread across completed attempts."
                >
                  <AdminRouteStatRow
                    items={[
                      { label: 'Avg score', value: data.metrics.averageScore ?? '—' },
                      { label: 'Median score', value: data.metrics.medianScore ?? '—' },
                      { label: 'Std deviation', value: data.metrics.scoreStdDev ?? '—' },
                    ]}
                  />
                </AdminRoutePanel>
              ) : null}

              {data.monthlyTrend.length > 0 ? (
                <AdminRoutePanel
                  eyebrow="Trend"
                  title="Monthly usage trend"
                  description="Attempts vs completions per month."
                >
                  <div className="space-y-3">
                    {data.monthlyTrend.map((m) => (
                      <BarMeter
                        key={m.month}
                        label={m.month}
                        value={m.attempts}
                        max={maxAttempts}
                        showValue={false}
                        hint={`${m.attempts} attempts · ${m.completed} completed`}
                      />
                    ))}
                  </div>
                  <AdminRoutePanelFooter source="Attempt telemetry" />
                </AdminRoutePanel>
              ) : null}
            </>
          ) : null}
        </AsyncStateWrapper>
      ) : null}
    </AdminRouteWorkspace>
  );
}
