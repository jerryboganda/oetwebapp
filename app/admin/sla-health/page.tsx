'use client';

import { useEffect, useState } from 'react';
import { AlertOctagon, AlertTriangle, CheckCircle2, ShieldAlert, Users, Clock } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';

interface SlaAlert { reviewId: string; severity: string; message: string; turnaround: string; subtestCode: string; createdAt: string; hoursOverdue?: number; hoursRemaining?: number }
interface SlaData {
  timestamp: string; overallHealth: string;
  summary: { totalOpen: number; breached: number; atRisk: number; healthy: number; unassigned: number; activeExperts: number; queueDepthPerExpert: number; capacityAlert: boolean };
  alerts: SlaAlert[]; recommendations: string[];
}

async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, { ...init, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', ...init?.headers } });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

const HEALTH_CONFIG: Record<string, { icon: typeof AlertOctagon; color: string; bg: string; label: string }> = {
  critical: { icon: AlertOctagon, color: 'text-red-600', bg: 'bg-red-50 dark:bg-red-950', label: 'Critical' },
  warning: { icon: AlertTriangle, color: 'text-amber-600', bg: 'bg-amber-50 dark:bg-amber-950', label: 'Warning' },
  healthy: { icon: CheckCircle2, color: 'text-emerald-600', bg: 'bg-emerald-50 dark:bg-emerald-950', label: 'Healthy' },
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
    <div className="min-h-screen bg-background">
      <div className="max-w-5xl mx-auto px-4 py-8">
        <h1 className="text-2xl font-bold mb-1">SLA Health Monitor</h1>
        <p className="text-muted-foreground mb-6">Real-time view of review SLA compliance, alerts, and capacity.</p>

        {loading ? <div className="space-y-4">{Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-xl" />)}</div> : data ? (
          <MotionSection className="space-y-6">
            {/* Overall health */}
            <MotionItem><Card className={`p-6 ${health.bg}`}><div className="flex items-center gap-3"><HealthIcon className={`w-8 h-8 ${health.color}`} /><div><h2 className="text-xl font-bold">{health.label}</h2><p className="text-sm text-muted-foreground">Overall SLA health as of {new Date(data.timestamp).toLocaleTimeString()}</p></div></div></Card></MotionItem>

            {/* Summary cards */}
            <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
              <Card className="p-4 text-center"><p className="text-2xl font-bold">{data.summary.totalOpen}</p><p className="text-xs text-muted-foreground">Open Reviews</p></Card>
              <Card className="p-4 text-center bg-red-50 dark:bg-red-950"><p className="text-2xl font-bold text-red-600">{data.summary.breached}</p><p className="text-xs text-red-600">SLA Breached</p></Card>
              <Card className="p-4 text-center bg-amber-50 dark:bg-amber-950"><p className="text-2xl font-bold text-amber-600">{data.summary.atRisk}</p><p className="text-xs text-amber-600">At Risk</p></Card>
              <Card className="p-4 text-center"><p className="text-2xl font-bold text-emerald-600">{data.summary.healthy}</p><p className="text-xs text-muted-foreground">Healthy</p></Card>
            </div>

            <div className="grid grid-cols-3 gap-4">
              <Card className="p-4 text-center"><Users className="w-5 h-5 mx-auto mb-1 text-blue-500" /><p className="text-xl font-bold">{data.summary.activeExperts}</p><p className="text-xs text-muted-foreground">Active Experts</p></Card>
              <Card className="p-4 text-center"><ShieldAlert className="w-5 h-5 mx-auto mb-1 text-amber-500" /><p className="text-xl font-bold">{data.summary.unassigned}</p><p className="text-xs text-muted-foreground">Unassigned</p></Card>
              <Card className="p-4 text-center"><Clock className="w-5 h-5 mx-auto mb-1 text-purple-500" /><p className="text-xl font-bold">{data.summary.queueDepthPerExpert}</p><p className="text-xs text-muted-foreground">Per Expert</p>{data.summary.capacityAlert && <Badge variant="destructive" className="mt-1 text-[10px]">Over Capacity</Badge>}</Card>
            </div>

            {/* Recommendations */}
            {data.recommendations.length > 0 && (
              <Card className="p-4 bg-amber-50/50 dark:bg-amber-950/20">
                <h3 className="font-semibold mb-2">Recommendations</h3>
                {data.recommendations.map((r, i) => <p key={i} className="text-sm text-muted-foreground">• {r}</p>)}
              </Card>
            )}

            {/* Alerts */}
            {data.alerts.length > 0 && (
              <>
                <h3 className="text-lg font-semibold">Alerts ({data.alerts.length})</h3>
                <div className="space-y-2">
                  {data.alerts.map(alert => (
                    <MotionItem key={alert.reviewId}>
                      <Card className={`p-3 border ${alert.severity === 'breached' ? 'border-red-200 bg-red-50/50 dark:bg-red-950/20' : 'border-amber-200 bg-amber-50/50 dark:bg-amber-950/20'}`}>
                        <div className="flex items-center justify-between">
                          <div><Badge variant={alert.severity === 'breached' ? 'destructive' : 'default'} className="text-[10px] uppercase">{alert.severity}</Badge><span className="text-sm ml-2">{alert.message}</span></div>
                          <div className="text-xs text-muted-foreground capitalize">{alert.subtestCode} • {alert.turnaround}</div>
                        </div>
                      </Card>
                    </MotionItem>
                  ))}
                </div>
              </>
            )}
          </MotionSection>
        ) : <Card className="p-8 text-center text-muted-foreground"><p>Unable to load SLA health data.</p></Card>}
      </div>
    </div>
  );
}
