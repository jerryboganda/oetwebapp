'use client';

import { useEffect, useState } from 'react';
import { AlertOctagon, AlertTriangle, CheckCircle2, ShieldAlert, Users, Clock, Activity } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteSummaryCard,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';

interface SlaAlert { reviewId: string; severity: string; message: string; turnaround: string; subtestCode: string; createdAt: string; hoursOverdue?: number; hoursRemaining?: number }
interface SlaData {
  timestamp: string; overallHealth: string;
  summary: { totalOpen: number; breached: number; atRisk: number; healthy: number; unassigned: number; activeExperts: number; queueDepthPerExpert: number; capacityAlert: boolean };
  alerts: SlaAlert[]; recommendations: string[];
}

const apiRequest = apiClient.request;

const HEALTH_CONFIG: Record<string, { icon: typeof AlertOctagon; tone: 'default' | 'success' | 'warning' | 'danger'; label: string }> = {
  critical: { icon: AlertOctagon, tone: 'danger', label: 'Critical' },
  warning: { icon: AlertTriangle, tone: 'warning', label: 'Warning' },
  healthy: { icon: CheckCircle2, tone: 'success', label: 'Healthy' },
};

export default function SlaHealthPage() {
  const [data, setData] = useState<SlaData | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    analytics.track('admin_sla_health_viewed');
    apiRequest<SlaData>('/v1/admin/sla-health').then(setData).catch(() => {}).finally(() => setLoading(false));
  }, []);

  const health = HEALTH_CONFIG[data?.overallHealth ?? 'healthy'] ?? HEALTH_CONFIG.healthy;
  const HealthIcon = health.icon;

  return (
    <AdminRouteWorkspace role="main" aria-label="SLA Health Monitor">
      <AdminRouteHero
        eyebrow="Admin Workspace"
        icon={Activity}
        accent="navy"
        title="SLA Health Monitor"
        description="Real-time view of review SLA compliance, alerts, and capacity."
      />

      {loading ? (
        <div className="space-y-4">{Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-xl" />)}</div>
      ) : data ? (
        <MotionSection className="space-y-6">
          <MotionItem>
            <AdminRouteSummaryCard
              label="Overall SLA Health"
              value={health.label}
              hint={`As of ${new Date(data.timestamp).toLocaleTimeString()}`}
              tone={health.tone}
              icon={<HealthIcon className="h-5 w-5" />}
            />
          </MotionItem>

          <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
            <AdminRouteSummaryCard label="Open Reviews" value={data.summary.totalOpen} icon={<Activity className="h-5 w-5" />} />
            <AdminRouteSummaryCard label="SLA Breached" value={data.summary.breached} tone={data.summary.breached > 0 ? 'danger' : 'default'} icon={<AlertOctagon className="h-5 w-5" />} />
            <AdminRouteSummaryCard label="At Risk" value={data.summary.atRisk} tone={data.summary.atRisk > 0 ? 'warning' : 'default'} icon={<AlertTriangle className="h-5 w-5" />} />
            <AdminRouteSummaryCard label="Healthy" value={data.summary.healthy} tone="success" icon={<CheckCircle2 className="h-5 w-5" />} />
          </div>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <AdminRouteSummaryCard label="Active Experts" value={data.summary.activeExperts} icon={<Users className="h-5 w-5" />} />
            <AdminRouteSummaryCard label="Unassigned" value={data.summary.unassigned} tone={data.summary.unassigned > 0 ? 'warning' : 'default'} icon={<ShieldAlert className="h-5 w-5" />} />
            <AdminRouteSummaryCard
              label="Per Expert"
              value={data.summary.queueDepthPerExpert}
              hint={data.summary.capacityAlert ? 'Over capacity' : undefined}
              tone={data.summary.capacityAlert ? 'danger' : 'default'}
              icon={<Clock className="h-5 w-5" />}
            />
          </div>

          {data.recommendations.length > 0 && (
            <AdminRoutePanel title="Recommendations">
              <ul className="space-y-1">
                {data.recommendations.map((r, i) => <li key={i} className="text-sm text-muted">• {r}</li>)}
              </ul>
            </AdminRoutePanel>
          )}

          {data.alerts.length > 0 && (
            <AdminRoutePanel title={`Alerts (${data.alerts.length})`}>
              <div className="space-y-2">
                {data.alerts.map(alert => (
                  <MotionItem key={alert.reviewId}>
                    <Card className={`p-3 border ${alert.severity === 'breached' ? 'border-danger/30' : 'border-warning/30'}`}>
                      <div className="flex items-center justify-between">
                        <div>
                          <Badge variant={alert.severity === 'breached' ? 'danger' : 'default'} className="text-[10px] uppercase">{alert.severity}</Badge>
                          <span className="text-sm ml-2">{alert.message}</span>
                        </div>
                        <div className="text-xs text-muted capitalize">{alert.subtestCode} • {alert.turnaround}</div>
                      </div>
                    </Card>
                  </MotionItem>
                ))}
              </div>
            </AdminRoutePanel>
          )}
        </MotionSection>
      ) : (
        <AdminRoutePanel><p className="text-center text-sm text-muted">Unable to load SLA health data.</p></AdminRoutePanel>
      )}
    </AdminRouteWorkspace>
  );
}
