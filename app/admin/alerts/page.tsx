'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { AdminRouteWorkspace, AdminRouteHero } from '@/components/domain/admin-route-surface';
import { Skeleton } from '@/components/ui/skeleton';
import { ErrorState } from '@/components/ui/empty-error';
import { Badge } from '@/components/ui/badge';
import { Bell, AlertTriangle, Info, AlertCircle, ChevronRight } from 'lucide-react';
import { fetchAdminAlerts } from '@/lib/api';

interface AdminAlert {
  alertType: string;
  severity: 'critical' | 'warning' | 'info';
  title: string;
  description: string;
  actionRoute: string;
  detectedAt: string;
}

interface AlertSummary {
  alerts: AdminAlert[];
  criticalCount: number;
  warningCount: number;
  infoCount: number;
  generatedAt: string;
}

export default function AdminAlertsPage() {
  useAdminAuth();
  const router = useRouter();
  const [data, setData] = useState<AlertSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await fetchAdminAlerts() as AlertSummary;
      setData(result);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load alerts');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { load(); }, []);

  const severityIcon = (s: string) => {
    if (s === 'critical') return <AlertCircle className="w-5 h-5 text-red-500" />;
    if (s === 'warning') return <AlertTriangle className="w-5 h-5 text-amber-500" />;
    return <Info className="w-5 h-5 text-blue-500" />;
  };

  const severityBadge = (s: string) => {
    if (s === 'critical') return <Badge variant="danger">Critical</Badge>;
    if (s === 'warning') return <Badge variant="warning">Warning</Badge>;
    return <Badge variant="info">Info</Badge>;
  };

  if (loading) {
    return (
      <AdminRouteWorkspace>
        <AdminRouteHero title="Alerts" description="Platform monitoring and notifications." />
        <div className="space-y-3">{[...Array(4)].map((_, i) => <Skeleton key={i} className="h-20 rounded-lg" />)}</div>
      </AdminRouteWorkspace>
    );
  }

  if (error) {
    return (
      <AdminRouteWorkspace>
        <AdminRouteHero title="Alerts" description="Platform monitoring and notifications." />
        <ErrorState message={error} onRetry={load} />
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace>
      <AdminRouteHero title="Alerts" description="Platform monitoring and notifications." />

      {data && (
        <>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
            <div className="rounded-lg border border-red-200 bg-red-50 p-4">
              <p className="text-2xl font-bold text-red-700">{data.criticalCount}</p>
              <p className="text-sm text-red-600">Critical</p>
            </div>
            <div className="rounded-lg border border-amber-200 bg-amber-50 p-4">
              <p className="text-2xl font-bold text-amber-700">{data.warningCount}</p>
              <p className="text-sm text-amber-600">Warnings</p>
            </div>
            <div className="rounded-lg border border-blue-200 bg-blue-50 p-4">
              <p className="text-2xl font-bold text-blue-700">{data.infoCount}</p>
              <p className="text-sm text-blue-600">Info</p>
            </div>
          </div>

          {data.alerts.length === 0 ? (
            <div className="text-center py-12 rounded-lg border border-dashed">
              <Bell className="w-8 h-8 mx-auto text-muted-foreground mb-2" />
              <p className="text-muted-foreground">No alerts at this time.</p>
              <p className="text-xs text-muted-foreground mt-1">All systems operational.</p>
            </div>
          ) : (
            <div className="space-y-2">
              {data.alerts.map((alert, i) => (
                <button
                  key={`${alert.alertType}-${i}`}
                  onClick={() => router.push(alert.actionRoute)}
                  className="w-full text-left rounded-lg border p-4 hover:bg-accent transition-colors flex items-start gap-3"
                >
                  <div className="mt-0.5">{severityIcon(alert.severity)}</div>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 mb-1">
                      <p className="font-medium text-sm">{alert.title}</p>
                      {severityBadge(alert.severity)}
                    </div>
                    <p className="text-xs text-muted-foreground">{alert.description}</p>
                    <p className="text-xs text-muted-foreground mt-1">{new Date(alert.detectedAt).toLocaleString()}</p>
                  </div>
                  <ChevronRight className="w-4 h-4 text-muted-foreground mt-1 flex-shrink-0" />
                </button>
              ))}
            </div>
          )}
        </>
      )}
    </AdminRouteWorkspace>
  );
}
