'use client';

import { useEffect, useMemo, useState } from 'react';
import { AlertCircle, AlertTriangle, Info, ShieldAlert } from 'lucide-react';
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { CardContent, CardDescription, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Badge } from '@/components/admin/ui/badge';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Drawer } from '@/components/ui/modal';
import { Pagination } from '@/components/ui/pagination';
import { fetchAdminAlerts } from '@/lib/api';
import { fetchAdminSecurityEvents, type AdminSecurityEventRow } from '@/lib/api/admin-security';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';

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
}

const SEVERITY_BADGE: Record<string, { variant: 'danger' | 'warning' | 'info'; label: string }> = {
  critical: { variant: 'danger', label: 'Critical' },
  warning: { variant: 'warning', label: 'Warning' },
  info: { variant: 'info', label: 'Info' },
};

const KIND_OPTIONS = [
  'auth.sign_in_succeeded', 'auth.sign_in_failed', 'auth.sign_out', 'auth.mfa_failed',
  'auth.refresh_reuse_detected', 'auth.refresh_device_mismatch', 'auth.token_rejected',
  'session.created', 'session.revoked', 'session.revoked_all',
  'device.trust_requested', 'device.trusted', 'device.revoked', 'device.change_blocked_cooldown', 'device.admin_reset',
  'risk.country_changed', 'risk.impossible_travel', 'risk.step_up_required', 'risk.sign_in_blocked',
  'admin.session_revoked', 'admin.device_reset', 'admin.account_suspended', 'admin.playback_blocked',
];

export default function AdminSecurityPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [rows, setRows] = useState<AdminSecurityEventRow[]>([]);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [total, setTotal] = useState(0);
  const [filters, setFilters] = useState<Record<string, string[]>>({ kind: [], severity: [] });
  const [accountId, setAccountId] = useState('');
  const [selected, setSelected] = useState<AdminSecurityEventRow | null>(null);
  const [alertSummary, setAlertSummary] = useState<AlertSummary | null>(null);

  const selectedKind = filters.kind?.[0];
  const selectedSeverity = filters.severity?.[0];

  useEffect(() => {
    fetchAdminAlerts()
      .then((result) => setAlertSummary(result as AlertSummary))
      .catch(() => setAlertSummary(null));
  }, []);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      setPageStatus('loading');
      try {
        const result = await fetchAdminSecurityEvents({
          accountId: accountId || undefined,
          kind: selectedKind,
          severity: selectedSeverity,
          page,
          pageSize,
        });
        if (cancelled) return;
        setRows(result.items);
        setTotal(result.totalCount);
        setPage(result.page);
        setPageSize(result.pageSize);
        setPageStatus(result.totalCount > 0 ? 'success' : 'empty');
      } catch (error) {
        console.error(error);
        if (!cancelled) setPageStatus('error');
      }
    }

    const handle = window.setTimeout(load, 200);
    return () => {
      cancelled = true;
      window.clearTimeout(handle);
    };
  }, [accountId, selectedKind, selectedSeverity, page, pageSize]);

  const securityAlerts = useMemo(
    () => (alertSummary?.alerts ?? []).filter((alert) => alert.actionRoute === '/admin/security'),
    [alertSummary],
  );
  const criticalCount = securityAlerts.filter((a) => a.severity === 'critical').length;
  const warningCount = securityAlerts.filter((a) => a.severity === 'warning').length;

  const filterGroups: FilterGroup[] = useMemo(() => [
    {
      id: 'kind',
      label: 'Event Kind',
      options: KIND_OPTIONS.map((kind) => ({ id: kind, label: kind })),
    },
    {
      id: 'severity',
      label: 'Severity',
      options: [
        { id: 'critical', label: 'Critical' },
        { id: 'warning', label: 'Warning' },
        { id: 'info', label: 'Info' },
      ],
    },
  ], []);

  const columns: Column<AdminSecurityEventRow>[] = useMemo(() => [
    {
      key: 'occurredAt',
      header: 'Occurred',
      render: (row) => <span className="text-sm text-admin-fg-muted">{new Date(row.occurredAt).toLocaleString()}</span>,
    },
    {
      key: 'kind',
      header: 'Kind',
      render: (row) => <span className="font-mono text-xs text-admin-fg-strong">{row.kind}</span>,
    },
    {
      key: 'severity',
      header: 'Severity',
      render: (row) => {
        const sev = SEVERITY_BADGE[row.severity] ?? SEVERITY_BADGE.info;
        return <Badge variant={sev.variant}>{sev.label}</Badge>;
      },
    },
    {
      key: 'account',
      header: 'Account',
      render: (row) => <span className="font-mono text-xs text-admin-fg-muted">{row.authAccountId ?? '-'}</span>,
    },
    {
      key: 'context',
      header: 'Context',
      render: (row) => (
        <span className="text-sm text-admin-fg-muted">
          {row.ipAddress ?? 'unknown IP'}{row.countryCode ? ` - ${row.countryCode}` : ''}{row.platform ? ` - ${row.platform}` : ''}
        </span>
      ),
    },
  ], []);

  const mobileCardRender = (row: AdminSecurityEventRow) => {
    const sev = SEVERITY_BADGE[row.severity] ?? SEVERITY_BADGE.info;
    return (
      <div className="space-y-3">
        <div className="flex items-start justify-between gap-3">
          <p className="truncate font-mono text-xs font-semibold text-admin-fg-strong">{row.kind}</p>
          <Badge variant={sev.variant}>{sev.label}</Badge>
        </div>
        <p className="text-xs text-admin-fg-muted">{new Date(row.occurredAt).toLocaleString()}</p>
        <p className="font-mono text-xs text-admin-fg-muted">{row.authAccountId ?? 'no account'}</p>
      </div>
    );
  };

  if (!isAuthenticated || role !== 'admin') return null;

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Security' },
  ];

  return (
    <AdminTableLayout
      title="Security"
      description="Security-events feed and computed risk alerts (Course Platform Security Requirements §4.4). Per-account session/device management lives on that user's profile."
      breadcrumbs={breadcrumbs}
      banner={
        <div className="grid grid-cols-1 gap-3 sm:gap-4 md:grid-cols-3">
          <KpiTile label="Critical Alerts" value={criticalCount} tone="danger" icon={<AlertCircle className="h-5 w-5" />} />
          <KpiTile label="Warning Alerts" value={warningCount} tone="warning" icon={<AlertTriangle className="h-5 w-5" />} />
          <KpiTile label="Events (this page)" value={total} tone="info" icon={<Info className="h-5 w-5" />} />
        </div>
      }
    >
      {securityAlerts.length > 0 ? (
        <CardContent className="space-y-2 pb-0">
          {securityAlerts.map((alert, i) => {
            const sev = SEVERITY_BADGE[alert.severity];
            return (
              <div
                key={`${alert.alertType}-${i}`}
                className="flex items-start gap-3 rounded-admin-lg border border-admin-border bg-admin-bg-surface p-4"
              >
                <ShieldAlert className="mt-0.5 h-5 w-5 shrink-0 text-[var(--admin-warning)]" />
                <div className="min-w-0 flex-1">
                  <div className="mb-1 flex flex-wrap items-center gap-2">
                    <p className="text-sm font-medium text-admin-fg-strong">{alert.title}</p>
                    <Badge variant={sev.variant}>{sev.label}</Badge>
                  </div>
                  <p className="text-xs text-admin-fg-muted">{alert.description}</p>
                </div>
              </div>
            );
          })}
        </CardContent>
      ) : null}

      <CardHeader className="flex-col items-start gap-1">
        <CardTitle>Security Events</CardTitle>
        <CardDescription>Auth, session, device, risk, and admin-action events — filter by kind, severity, or account.</CardDescription>
      </CardHeader>

      <CardContent className="space-y-4 pt-0">
        <div className="max-w-md">
          <input
            type="text"
            placeholder="Filter by auth account id"
            value={accountId}
            onChange={(event) => { setPage(1); setAccountId(event.target.value); }}
            className="w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-strong placeholder:text-admin-fg-muted focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
          />
        </div>

        <FilterBar
          groups={filterGroups}
          selected={filters}
          onChange={(groupId, optionId) => {
            setPage(1);
            setFilters((current) => ({
              ...current,
              [groupId]: current[groupId]?.includes(optionId) ? [] : [optionId],
            }));
          }}
          onClear={() => { setPage(1); setFilters({ kind: [], severity: [] }); setAccountId(''); }}
        />

        <AsyncStateWrapper
          status={pageStatus}
          onRetry={() => window.location.reload()}
          emptyContent={
            <EmptyState
              illustration={<ShieldAlert />}
              title="No security events found"
              description="Adjust the filters, or wait for more account activity to be recorded."
            />
          }
        >
          <Pagination
            page={page}
            pageSize={pageSize}
            total={total}
            onPageChange={setPage}
            onPageSizeChange={setPageSize}
            pageSizeOptions={[10, 20, 50, 100]}
            itemLabel="event"
            itemLabelPlural="events"
          />
          <DataTable
            aria-label="Security events"
            columns={columns}
            data={rows}
            keyExtractor={(row) => row.id}
            onRowClick={setSelected}
            mobileCardRender={mobileCardRender}
          />
        </AsyncStateWrapper>
      </CardContent>

      <Drawer open={Boolean(selected)} onClose={() => setSelected(null)} title="Security Event Detail">
        {selected ? (
          <div className="space-y-4 text-sm">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">Occurred</p>
              <p className="text-admin-fg-strong">{new Date(selected.occurredAt).toLocaleString()}</p>
            </div>
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">Kind</p>
              <p className="font-mono text-admin-fg-strong">{selected.kind}</p>
            </div>
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">Account</p>
              <p className="font-mono text-xs text-admin-fg-muted">{selected.authAccountId ?? 'unresolved'}</p>
            </div>
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">Context</p>
              <p className="text-admin-fg-strong">
                {selected.ipAddress ?? 'unknown IP'}{selected.countryCode ? ` - ${selected.countryCode}` : ''}
              </p>
              <p className="text-xs text-admin-fg-muted">{selected.userAgent ?? 'no user agent'}</p>
              {selected.platform ? <p className="text-xs text-admin-fg-muted">Platform: {selected.platform}</p> : null}
              {selected.sessionFamilyId ? <p className="font-mono text-xs text-admin-fg-muted">Family: {selected.sessionFamilyId}</p> : null}
              {selected.deviceId ? <p className="font-mono text-xs text-admin-fg-muted">Device: {selected.deviceId}</p> : null}
            </div>
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">Details</p>
              <p className="whitespace-pre-wrap rounded-admin border border-admin-border bg-admin-bg-subtle p-4 font-mono text-xs text-admin-fg-strong">
                {selected.detailsJson || 'No additional details recorded.'}
              </p>
            </div>
          </div>
        ) : null}
      </Drawer>
    </AdminTableLayout>
  );
}
