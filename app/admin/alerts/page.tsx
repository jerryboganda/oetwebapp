'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { AlertCircle, AlertTriangle, Bell, ChevronRight, Info, RefreshCw } from 'lucide-react';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { CardContent } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Skeleton } from '@/components/admin/ui/skeleton';
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

const SEVERITY_BADGE: Record<AdminAlert['severity'], { variant: 'danger' | 'warning' | 'info'; label: string }> = {
  critical: { variant: 'danger', label: 'Critical' },
  warning: { variant: 'warning', label: 'Warning' },
  info: { variant: 'info', label: 'Info' },
};

function severityIcon(s: AdminAlert['severity']) {
  if (s === 'critical') return <AlertCircle className="h-5 w-5 text-[var(--admin-danger)]" />;
  if (s === 'warning') return <AlertTriangle className="h-5 w-5 text-[var(--admin-warning)]" />;
  return <Info className="h-5 w-5 text-[var(--admin-info)]" />;
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

  const headerActions = (
    <Button variant="outline" onClick={load} loading={loading} startIcon={<RefreshCw className="h-4 w-4" />}>
      Refresh
    </Button>
  );

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Alerts' },
  ];

  if (loading) {
    return (
      <AdminTableLayout
        title="Alerts"
        description="Platform monitoring and notifications."
        breadcrumbs={breadcrumbs}
        actions={headerActions}
      >
        <CardContent className="space-y-3 p-4 sm:p-5">
          {[...Array(4)].map((_, i) => <Skeleton key={i} className="h-20 w-full rounded-admin" />)}
        </CardContent>
      </AdminTableLayout>
    );
  }

  if (error) {
    return (
      <AdminTableLayout
        title="Alerts"
        description="Platform monitoring and notifications."
        breadcrumbs={breadcrumbs}
        actions={headerActions}
      >
        <CardContent className="p-4 sm:p-5">
          <EmptyState
            variant="error"
            title="Unable to load alerts"
            description={error}
            primaryAction={{ label: 'Retry', onClick: load }}
          />
        </CardContent>
      </AdminTableLayout>
    );
  }

  const hasAlerts = (data?.alerts.length ?? 0) > 0;

  return (
    <AdminTableLayout
      title="Alerts"
      description="Platform monitoring and notifications."
      breadcrumbs={breadcrumbs}
      actions={headerActions}
      banner={
        data ? (
          <div className="grid grid-cols-1 gap-3 sm:gap-4 md:grid-cols-3">
            <KpiTile label="Critical" value={data.criticalCount} tone="danger" icon={<AlertCircle className="h-5 w-5" />} />
            <KpiTile label="Warnings" value={data.warningCount} tone="warning" icon={<AlertTriangle className="h-5 w-5" />} />
            <KpiTile label="Info" value={data.infoCount} tone="info" icon={<Info className="h-5 w-5" />} />
          </div>
        ) : null
      }
    >
      {data ? (
        hasAlerts ? (
          <CardContent className="space-y-2 p-4 sm:p-5">
            {data.alerts.map((alert, i) => {
              const sev = SEVERITY_BADGE[alert.severity];
              return (
                <button
                  key={`${alert.alertType}-${i}`}
                  type="button"
                  onClick={() => router.push(alert.actionRoute)}
                  className="flex w-full items-start gap-3 rounded-admin-lg border border-admin-border bg-admin-bg-surface p-4 text-left transition-[border-color,background-color] duration-[var(--admin-dur-base)] ease-[var(--admin-ease-out)] hover:border-admin-border-strong hover:bg-[var(--admin-state-hover)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)]"
                >
                    <div className="mt-0.5 shrink-0">{severityIcon(alert.severity)}</div>
                    <div className="min-w-0 flex-1">
                      <div className="mb-1 flex flex-wrap items-center gap-2">
                        <p className="text-sm font-medium text-admin-fg-strong">{alert.title}</p>
                        <Badge variant={sev.variant}>{sev.label}</Badge>
                      </div>
                      <p className="text-xs text-admin-fg-muted">{alert.description}</p>
                      <p className="mt-1 text-xs text-admin-fg-muted">
                        {new Date(alert.detectedAt).toLocaleString()}
                      </p>
                    </div>
                    <ChevronRight className="mt-1 h-4 w-4 shrink-0 text-admin-fg-muted" aria-hidden="true" />
                </button>
              );
            })}
          </CardContent>
        ) : (
          <CardContent className="p-4 sm:p-5">
            <EmptyState
              illustration={<Bell />}
              title="No alerts at this time"
              description="All systems operational."
            />
          </CardContent>
        )
      ) : null}
    </AdminTableLayout>
  );
}
