'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import { Download, FileText, Search } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Toast } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Drawer } from '@/components/ui/modal';
import { exportAdminAuditLogs } from '@/lib/api';
import { getAdminAuditLogDetailData, getAdminAuditLogPageData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminAuditLogDetail, AdminAuditLogRow } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function AuditLogsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [filters, setFilters] = useState<Record<string, string[]>>({ action: [], actor: [] });
  const [searchQuery, setSearchQuery] = useState('');
  const [rows, setRows] = useState<AdminAuditLogRow[]>([]);
  const [actionOptions, setActionOptions] = useState<string[]>([]);
  const [actorOptions, setActorOptions] = useState<string[]>([]);
  const [selectedLogId, setSelectedLogId] = useState<string | null>(null);
  const [selectedLogDetail, setSelectedLogDetail] = useState<AdminAuditLogDetail | null>(null);
  const [isDetailLoading, setIsDetailLoading] = useState(false);
  const [isExporting, setIsExporting] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);
  const lastOpenedLogIdRef = useRef<string | null>(null);

  const selectedAction = filters.action?.[0];
  const selectedActor = filters.actor?.[0];

  useEffect(() => {
    let cancelled = false;

    async function loadAuditLogs() {
      setPageStatus('loading');
      try {
        const basePromise = actionOptions.length === 0 && actorOptions.length === 0
          ? getAdminAuditLogPageData({ pageSize: 100 })
          : Promise.resolve(null);

        const result = await getAdminAuditLogPageData({
          action: selectedAction,
          actor: selectedActor,
          search: searchQuery || undefined,
          pageSize: 100,
        });
        const base = await basePromise;

        if (cancelled) return;

        if (base) {
          setActionOptions([...new Set(base.items.map((item) => item.action))].filter(Boolean));
          setActorOptions([...new Set(base.items.map((item) => item.actor))].filter(Boolean));
        }

        setRows(result.items);
        setPageStatus(result.items.length > 0 ? 'success' : 'empty');
      } catch (error) {
        console.error(error);
        if (!cancelled) {
          setPageStatus('error');
          setToast({ variant: 'error', message: 'Unable to load audit logs.' });
        }
      }
    }

    const handle = window.setTimeout(loadAuditLogs, 200);
    return () => {
      cancelled = true;
      window.clearTimeout(handle);
    };
  }, [selectedAction, selectedActor, searchQuery, actionOptions.length, actorOptions.length]);

  useEffect(() => {
    if (!selectedLogId) {
      setSelectedLogDetail(null);
      return;
    }
    const logId: string = selectedLogId;

    let cancelled = false;
    async function loadDetail() {
      setIsDetailLoading(true);
      try {
        const detail = await getAdminAuditLogDetailData(logId);
        if (!cancelled) {
          setSelectedLogDetail(detail);
        }
      } catch (error) {
        console.error(error);
        if (!cancelled) {
          setToast({ variant: 'error', message: 'Unable to load audit event detail.' });
        }
      } finally {
        if (!cancelled) {
          setIsDetailLoading(false);
        }
      }
    }

    loadDetail();
    return () => {
      cancelled = true;
    };
  }, [selectedLogId]);

  const filterGroups: FilterGroup[] = useMemo(() => [
    {
      id: 'action',
      label: 'Action',
      options: actionOptions.map((action) => ({ id: action, label: action })),
    },
    {
      id: 'actor',
      label: 'Actor',
      options: actorOptions.map((actor) => ({ id: actor, label: actor })),
    },
  ], [actionOptions, actorOptions]);

  const columns: Column<AdminAuditLogRow>[] = useMemo(
    () => [
      {
        key: 'timestamp',
        header: 'Timestamp',
        render: (log) => <span className="text-sm text-slate-600">{new Date(log.timestamp).toLocaleString()}</span>,
      },
      {
        key: 'actor',
        header: 'Actor',
        render: (log) => <span className="font-medium text-slate-900">{log.actor}</span>,
      },
      {
        key: 'action',
        header: 'Action',
        render: (log) => <span className="text-slate-900">{log.action}</span>,
      },
      {
        key: 'resource',
        header: 'Resource',
        render: (log) => <span className="font-mono text-xs text-slate-500">{log.resource}</span>,
      },
      {
        key: 'details',
        header: 'Details',
        render: (log) => <span className="text-sm text-slate-500">{log.details}</span>,
      },
    ],
    [],
  );

  function handleFilterChange(groupId: string, optionId: string) {
    setFilters((current) => ({
      ...current,
      [groupId]: current[groupId]?.includes(optionId) ? [] : [optionId],
    }));
  }

  function handleRowClick(log: AdminAuditLogRow) {
    lastOpenedLogIdRef.current = log.id;
    setSelectedLogId(log.id);
  }

  function handleDrawerClose() {
    const restoreLogId = lastOpenedLogIdRef.current;
    setSelectedLogId(null);

    if (!restoreLogId) {
      return;
    }

    const focusRow = () => {
      const row = document.querySelector<HTMLElement>(`[data-row-key="${restoreLogId}"]`);
      if (!row) {
        return;
      }

      row.focus();
      if (document.activeElement !== row) {
        requestAnimationFrame(() => row.focus());
      }
    };

    window.setTimeout(focusRow, 0);
  }

  async function handleExport() {
    setIsExporting(true);
    try {
      const result = await exportAdminAuditLogs({
        action: selectedAction,
        actor: selectedActor,
        search: searchQuery || undefined,
      });

      const objectUrl = URL.createObjectURL(result.blob);
      const link = document.createElement('a');
      link.href = objectUrl;
      link.download = result.fileName;
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(objectUrl);

      setToast({ variant: 'success', message: 'Audit log export downloaded.' });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to export audit logs.' });
    } finally {
      setIsExporting(false);
    }
  }

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="Audit logs">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AdminRouteSectionHeader
        title="Audit Logs"
        description="Operational and security events are loaded from the live audit stream, with export and drill-in detail backed by real endpoints."
        actions={
          <Button variant="outline" onClick={handleExport} loading={isExporting} className="gap-2">
            <Download className="h-4 w-4" />
            Export CSV
          </Button>
        }
      />

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => window.location.reload()}
        emptyContent={
          <EmptyState
            icon={<FileText className="h-10 w-10 text-slate-400" />}
            title="No audit events found"
            description="Adjust the search or filters, or wait for more operational activity to be recorded."
          />
        }
      >
        <AdminRoutePanel title="Audit Stream" description="Search, filter, and inspect individual events without leaving the admin console.">
          <div className="max-w-md">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
              <Input placeholder="Search actions, actors, resources, or details" value={searchQuery} onChange={(event) => setSearchQuery(event.target.value)} className="pl-9" />
            </div>
          </div>
          <FilterBar groups={filterGroups} selected={filters} onChange={handleFilterChange} onClear={() => { setFilters({ action: [], actor: [] }); setSearchQuery(''); }} />
          <DataTable columns={columns} data={rows} keyExtractor={(log) => log.id} onRowClick={handleRowClick} />
        </AdminRoutePanel>
      </AsyncStateWrapper>

      <Drawer open={Boolean(selectedLogId)} onClose={handleDrawerClose} title="Audit Event Detail">
        {isDetailLoading || !selectedLogDetail ? (
          <p className="text-sm text-slate-500">Loading event detail...</p>
        ) : (
          <div className="space-y-4 text-sm">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.12em] text-slate-400">Event ID</p>
              <p className="font-mono text-slate-900">{selectedLogDetail.id}</p>
            </div>
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.12em] text-slate-400">Timestamp</p>
              <p className="text-slate-900">{new Date(selectedLogDetail.timestamp).toLocaleString()}</p>
            </div>
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.12em] text-slate-400">Actor</p>
              <p className="text-slate-900">{selectedLogDetail.actorName}</p>
              <p className="font-mono text-xs text-slate-500">{selectedLogDetail.actorId}</p>
            </div>
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.12em] text-slate-400">Action</p>
              <p className="text-slate-900">{selectedLogDetail.action}</p>
            </div>
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.12em] text-slate-400">Resource</p>
              <p className="text-slate-900">{selectedLogDetail.resourceType}</p>
              <p className="font-mono text-xs text-slate-500">{selectedLogDetail.resourceId || 'No resource ID'}</p>
            </div>
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.12em] text-slate-400">Details</p>
              <p className="whitespace-pre-wrap rounded-xl border border-slate-200 bg-slate-50 p-4 text-slate-700">
                {selectedLogDetail.details || 'No additional details recorded.'}
              </p>
            </div>
          </div>
        )}
      </Drawer>
    </AdminRouteWorkspace>
  );
}
