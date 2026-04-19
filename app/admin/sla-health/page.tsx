'use client';

import { useEffect, useState } from 'react';
import { AlertOctagon, AlertTriangle, CheckCircle2, ShieldAlert, Users, Clock } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem } from '@/components/ui/motion-primitives';
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

interface SlaAlert {
  reviewId: string;
  severity: string;
  message: string;
  turnaround: string;
  subtestCode: string;
  createdAt: string;
  hoursOverdue?: number;
  hoursRemaining?: number;
}

interface SlaData {
  timestamp: string;
  overallHealth: string;
  summary: {
    totalOpen: number;
    breached: number;
    atRisk: number;
    healthy: number;
    unassigned: number;
    activeExperts: number;
    queueDepthPerExpert: number;
    capacityAlert: boolean;
  };
  alerts: SlaAlert[];
  recommendations: string[];
}

type Status = 'loading' | 'error' | 'success';

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

const HEALTH_CONFIG: Record<
  string,
  {
    icon: typeof AlertOctagon;
    variant: 'error' | 'warning' | 'success';
    label: string;
    accent: 'rose' | 'amber' | 'emerald';
  }
> = {
  critical: { icon: AlertOctagon, variant: 'error', label: 'Critical', accent: 'rose' },
  warning: { icon: AlertTriangle, variant: 'warning', label: 'Warning', accent: 'amber' },
  healthy: { icon: CheckCircle2, variant: 'success', label: 'Healthy', accent: 'emerald' },
};

export default function SlaHealthPage() {
  const [data, setData] = useState<SlaData | null>(null);
  const [status, setStatus] = useState<Status>('loading');

  useEffect(() => {
    analytics.track('admin_sla_health_viewed');
    apiRequest<SlaData>('/v1/admin/sla-health')
      .then((res) => {
        setData(res);
        setStatus('success');
      })
      .catch(() => setStatus('error'));
  }, []);

  const health = HEALTH_CONFIG[data?.overallHealth ?? 'healthy'] ?? HEALTH_CONFIG.healthy;
  const HealthIcon = health.icon;

  return (
    <AdminRouteWorkspace role="main" aria-label="SLA health monitor">
      <AdminRouteHero
        eyebrow="Operations"
        icon={HealthIcon}
        accent={health.accent}
        title="SLA Health Monitor"
        description="Real-time view of review SLA compliance, alerts, and reviewer capacity across the productive-skill pipeline."
        highlights={
          data
            ? [
                { label: 'Open reviews', value: data.summary.totalOpen.toLocaleString() },
                { label: 'Breached', value: data.summary.breached.toLocaleString() },
                { label: 'At risk', value: data.summary.atRisk.toLocaleString() },
              ]
            : undefined
        }
      />

      <AsyncStateWrapper status={status} onRetry={() => window.location.reload()}>
        {data ? (
          <>
            <InlineAlert variant={health.variant} title={`Overall SLA health: ${health.label}`}>
              Snapshot generated {new Date(data.timestamp).toLocaleString()}.{' '}
              {data.summary.capacityAlert
                ? 'Queue capacity is at or above the safe threshold — route new items carefully.'
                : 'Capacity within safe thresholds.'}
            </InlineAlert>

            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <AdminRouteSummaryCard
                label="Open reviews"
                value={data.summary.totalOpen}
                icon={<Clock className="h-5 w-5" />}
              />
              <AdminRouteSummaryCard
                label="SLA breached"
                value={data.summary.breached}
                icon={<AlertOctagon className="h-5 w-5" />}
                tone={data.summary.breached > 0 ? 'danger' : 'default'}
              />
              <AdminRouteSummaryCard
                label="At risk"
                value={data.summary.atRisk}
                icon={<AlertTriangle className="h-5 w-5" />}
                tone={data.summary.atRisk > 0 ? 'warning' : 'default'}
              />
              <AdminRouteSummaryCard
                label="Healthy"
                value={data.summary.healthy}
                icon={<CheckCircle2 className="h-5 w-5" />}
                tone="success"
              />
            </div>

            <AdminRoutePanel
              eyebrow="Capacity"
              title="Reviewer capacity"
              description="Staffing and queue depth per expert."
            >
              <AdminRouteStatRow
                items={[
                  { label: 'Active experts', value: data.summary.activeExperts },
                  { label: 'Unassigned', value: data.summary.unassigned, tone: data.summary.unassigned > 0 ? 'warning' : 'default' },
                  {
                    label: 'Per expert',
                    value: data.summary.queueDepthPerExpert,
                    tone: data.summary.capacityAlert ? 'danger' : 'default',
                  },
                ]}
              />
              {data.summary.capacityAlert ? (
                <div className="flex items-center gap-2 text-sm text-danger">
                  <ShieldAlert className="h-4 w-4" aria-hidden />
                  <span>Over capacity — consider re-assigning to available reviewers.</span>
                </div>
              ) : null}
              <AdminRoutePanelFooter updatedAt={data.timestamp} source="Review pipeline" />
            </AdminRoutePanel>

            {data.recommendations.length > 0 ? (
              <AdminRoutePanel
                eyebrow="Recommendations"
                title="Next steps"
                description="System-suggested actions to restore green health."
              >
                <ul className="space-y-2 text-sm text-navy">
                  {data.recommendations.map((r, i) => (
                    <li key={i} className="flex items-start gap-2">
                      <span className="mt-1 inline-block h-1.5 w-1.5 shrink-0 rounded-full bg-primary" aria-hidden />
                      <span>{r}</span>
                    </li>
                  ))}
                </ul>
              </AdminRoutePanel>
            ) : null}

            <AdminRoutePanel
              eyebrow="Alerts"
              title={`Active alerts (${data.alerts.length})`}
              description="Specific reviews that are breached or at risk."
            >
              {data.alerts.length === 0 ? (
                <EmptyState
                  icon={<CheckCircle2 className="h-6 w-6" aria-hidden />}
                  title="No alerts"
                  description="Every review in the queue is within SLA."
                />
              ) : (
                <div className="space-y-2">
                  {data.alerts.map((alert) => (
                    <MotionItem key={alert.reviewId}>
                      <div className="flex items-start justify-between gap-3 rounded-2xl border border-border bg-background-light px-4 py-3">
                        <div className="min-w-0">
                          <div className="mb-1 flex flex-wrap items-center gap-2">
                            <Badge variant={alert.severity === 'breached' ? 'danger' : 'warning'}>{alert.severity}</Badge>
                            <Users className="h-3 w-3 text-muted" aria-hidden />
                            <span className="text-xs font-semibold text-muted capitalize">
                              {alert.subtestCode} · {alert.turnaround}
                            </span>
                          </div>
                          <p className="text-sm text-navy">{alert.message}</p>
                        </div>
                      </div>
                    </MotionItem>
                  ))}
                </div>
              )}
            </AdminRoutePanel>
          </>
        ) : null}
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
