'use client';

import { useEffect, useState } from 'react';
import {
  AlertOctagon,
  AlertTriangle,
  CheckCircle2,
  ShieldAlert,
  Users,
  Clock,
  Activity,
} from 'lucide-react';

import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';

import {
  AdminOperationsLayout,
  KpiStrip,
} from '@/components/admin/layout/admin-operations-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Badge } from '@/components/admin/ui/badge';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { EmptyState } from '@/components/admin/ui/empty-state';

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

const apiRequest = apiClient.request;

const HEALTH_CONFIG: Record<
  string,
  { icon: typeof AlertOctagon; tone: 'default' | 'success' | 'warning' | 'danger'; label: string }
> = {
  critical: { icon: AlertOctagon, tone: 'danger', label: 'Critical' },
  warning: { icon: AlertTriangle, tone: 'warning', label: 'Warning' },
  healthy: { icon: CheckCircle2, tone: 'success', label: 'Healthy' },
};

export default function SlaHealthPage() {
  useAdminAuth();
  const [data, setData] = useState<SlaData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('admin_sla_health_viewed');
    apiRequest<SlaData>('/v1/admin/sla-health')
      .then(setData)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load'))
      .finally(() => setLoading(false));
  }, []);

  const health = HEALTH_CONFIG[data?.overallHealth ?? 'healthy'] ?? HEALTH_CONFIG.healthy;
  const HealthIcon = health.icon;

  return (
    <AdminOperationsLayout
      title="SLA Health Monitor"
      description="Real-time view of review SLA compliance, alerts, and capacity."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'SLA Health' },
      ]}
    >
      {loading ? (
        <div className="space-y-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-24 rounded-admin" />
          ))}
        </div>
      ) : error ? (
        <Card>
          <CardContent>
            <EmptyState
              variant="error"
              size="lg"
              illustration={<AlertOctagon aria-hidden="true" />}
              title="Unable to load SLA health"
              description={error}
            />
          </CardContent>
        </Card>
      ) : data ? (
        <>
          <Card>
            <CardHeader>
              <CardTitle>Overall SLA health</CardTitle>
            </CardHeader>
            <CardContent>
              <div className="flex items-center gap-4">
                <span className="inline-flex h-12 w-12 items-center justify-center rounded-full bg-admin-bg-subtle">
                  <HealthIcon className="h-6 w-6" />
                </span>
                <div>
                  <Badge variant={health.tone} size="lg">
                    {health.label}
                  </Badge>
                  <p className="mt-1 text-xs text-admin-fg-muted">
                    As of {new Date(data.timestamp).toLocaleTimeString()}
                  </p>
                </div>
              </div>
            </CardContent>
          </Card>

          <KpiStrip aria-label="SLA queue summary">
            <KpiTile
              label="Open Reviews"
              value={data.summary.totalOpen}
              icon={<Activity className="h-4 w-4" />}
            />
            <KpiTile
              label="SLA Breached"
              value={data.summary.breached}
              tone={data.summary.breached > 0 ? 'danger' : 'default'}
              icon={<AlertOctagon className="h-4 w-4" />}
            />
            <KpiTile
              label="At Risk"
              value={data.summary.atRisk}
              tone={data.summary.atRisk > 0 ? 'warning' : 'default'}
              icon={<AlertTriangle className="h-4 w-4" />}
            />
            <KpiTile
              label="Healthy"
              value={data.summary.healthy}
              tone="success"
              icon={<CheckCircle2 className="h-4 w-4" />}
            />
          </KpiStrip>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <KpiTile
              label="Active Experts"
              value={data.summary.activeExperts}
              icon={<Users className="h-4 w-4" />}
              size="sm"
            />
            <KpiTile
              label="Unassigned"
              value={data.summary.unassigned}
              tone={data.summary.unassigned > 0 ? 'warning' : 'default'}
              icon={<ShieldAlert className="h-4 w-4" />}
              size="sm"
            />
            <KpiTile
              label="Per Expert"
              value={data.summary.queueDepthPerExpert}
              tone={data.summary.capacityAlert ? 'danger' : 'default'}
              icon={<Clock className="h-4 w-4" />}
              size="sm"
            />
          </div>

          {data.recommendations.length > 0 && (
            <Card>
              <CardHeader>
                <CardTitle>Recommendations</CardTitle>
              </CardHeader>
              <CardContent>
                <ul className="space-y-1.5">
                  {data.recommendations.map((r, i) => (
                    <li key={i} className="text-sm text-admin-fg-muted">
                      • {r}
                    </li>
                  ))}
                </ul>
              </CardContent>
            </Card>
          )}

          {data.alerts.length > 0 && (
            <Card>
              <CardHeader>
                <CardTitle>Alerts ({data.alerts.length})</CardTitle>
              </CardHeader>
              <CardContent>
                <div className="space-y-2">
                  {data.alerts.map((alert) => (
                    <div
                      key={alert.reviewId}
                      className={`flex items-center justify-between gap-3 rounded-admin border px-3 py-2 ${
                        alert.severity === 'breached'
                          ? 'border-[var(--admin-danger-tint-strong)] bg-[var(--admin-danger-tint)]'
                          : 'border-[var(--admin-warning-tint-strong)] bg-[var(--admin-warning-tint)]'
                      }`}
                    >
                      <div className="flex min-w-0 items-center gap-2">
                        <Badge
                          variant={alert.severity === 'breached' ? 'danger' : 'warning'}
                          size="sm"
                        >
                          {alert.severity}
                        </Badge>
                        <span className="truncate text-sm text-admin-fg-default">
                          {alert.message}
                        </span>
                      </div>
                      <div className="shrink-0 text-xs capitalize text-admin-fg-muted">
                        {alert.subtestCode} • {alert.turnaround}
                      </div>
                    </div>
                  ))}
                </div>
              </CardContent>
            </Card>
          )}
        </>
      ) : (
        <Card>
          <CardContent>
            <EmptyState
              size="md"
              title="No SLA data"
              description="Unable to load SLA health data."
            />
          </CardContent>
        </Card>
      )}
    </AdminOperationsLayout>
  );
}
