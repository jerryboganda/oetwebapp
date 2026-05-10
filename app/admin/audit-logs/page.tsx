'use client';

import { useEffect, useMemo, useRef, useState, type KeyboardEvent as ReactKeyboardEvent, type MouseEvent as ReactMouseEvent } from 'react';
import { Download, FileText, Search } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Toast } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { Drawer } from '@/components/ui/modal';
import { Pagination } from '@/components/ui/pagination';
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
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [total, setTotal] = useState(0);
  const [actionOptions, setActionOptions] = useState<string[]>([]);
  const [actorOptions, setActorOptions] = useState<string[]>([]);
  const [selectedLogId, setSelectedLogId] = useState<string | null>(null);
  const [selectedLogDetail, setSelectedLogDetail] = useState<AdminAuditLogDetail | null>(null);
  const [isDetailLoading, setIsDetailLoading] = useState(false);
  const [isExporting, setIsExporting] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);
  const lastOpenedLogIdRef = useRef<string | null>(null);
  const lastOpenedRowRef = useRef<HTMLElement | null>(null);

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
          page,
          pageSize,
        });
        const base = await basePromise;

        if (cancelled) return;

        if (base) {
          setActionOptions([...new Set(base.items.map((item) => item.action))].filter(Boolean));
          setActorOptions([...new Set(base.items.map((item) => item.actor))].filter(Boolean));
        }

        setRows(result.items);
        setTotal(result.total);
        setPage(result.page);
        setPageSize(result.pageSize);
        setPageStatus(result.total > 0 ? 'success' : 'empty');
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
  }, [selectedAction, selectedActor, searchQuery, page, pageSize, actionOptions.length, actorOptions.length]);

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

  const totalPages = Math.max(1, Math.ceil(total / pageSize));

  const columns: Column<AdminAuditLogRow>[] = useMemo(
    () => [
      {
        key: 'timestamp',
        header: 'Timestamp',
        render: (log) => <span className="text-sm text-muted">{new Date(log.timestamp).toLocaleString()}</span>,
      },
      {
        key: 'actor',
        header: 'Actor',
        render: (log) => <span className="font-medium text-navy">{log.actor}</span>,
      },
      {
        key: 'action',
        header: 'Action',
        render: (log) => <span className="text-navy">{log.action}</span>,
      },
      {
        key: 'resource',
        header: 'Resource',
        render: (log) => <span className="font-mono text-xs text-muted">{log.resource}</span>,
      },
      {
        key: 'details',
        header: 'Details',
        render: (log) => <span className="text-sm text-muted">{log.details}</span>,
      },
    ],
    [],
  );

  const mobileCardRender = (log: AdminAuditLogRow) => (
    <div className="space-y-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate font-semibold text-navy">{log.actor}</p>
          <p className="truncate text-xs uppercase tracking-[0.12em] text-muted">{log.resource}</p>
        </div>
        <span className="rounded-full bg-background-light px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">
          {new Date(log.timestamp).toLocaleDateString()}
        </span>
      </div>

      <div className="rounded-2xl bg-background-light px-3 py-2 text-sm">
        <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Action</p>
        <p className="mt-1 font-medium text-navy">{log.action}</p>
      </div>

      <div className="rounded-2xl bg-background-light px-3 py-2 text-sm">
        <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Details</p>
        <p className="mt-1 line-clamp-3 text-muted">{log.details}</p>
      </div>

      <p className="text-xs font-medium text-primary">Tap to inspect the full event.</p>
    </div>
  );

  function handleFilterChange(groupId: string, optionId: string) {
    setFilters((current) => ({
      ...current,
      [groupId]: current[groupId]?.includes(optionId) ? [] : [optionId],
    }));
  }

  function handleRowClick(
    log: AdminAuditLogRow,
    event: ReactMouseEvent<HTMLElement> | ReactKeyboardEvent<HTMLElement>,
  ) {
    lastOpenedLogIdRef.current = log.id;
    lastOpenedRowRef.current = event.currentTarget;
    setSelectedLogId(log.id);
  }

  function handleDrawerClose() {
    const restoreRow = lastOpenedRowRef.current;
    const restoreLogId = lastOpenedLogIdRef.current;
    setSelectedLogId(null);

    if (!restoreRow && !restoreLogId) {
      return;
    }

    let attempts = 0;
    const focusRow = () => {
      const fallbackCandidates = restoreLogId
        ? Array.from(document.querySelectorAll<HTMLElement>(`[data-row-key="${restoreLogId}"]`))
        : [];
      const row = restoreRow?.isConnected && restoreRow.getClientRects().length > 0
        ? restoreRow
        : fallbackCandidates.find((candidate) => candidate.getClientRects().length > 0) ?? fallbackCandidates[0];
      if (!row) {
        if (attempts < 6) {
          attempts += 1;
          window.setTimeout(focusRow, 50);
        }
        return;
      }

      row.focus();
      if (document.activeElement !== row && attempts < 6) {
        attempts += 1;
        requestAnimationFrame(() => {
          row.focus();
          if (document.activeElement !== row) {
            window.setTimeout(focusRow, 50);
          }
        });
      }
    };

    window.setTimeout(focusRow, 50);
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
            icon={<FileText className="h-10 w-10 text-muted" />}
            title="No audit events found"
            description="Adjust the search or filters, or wait for more operational activity to be recorded."
          />
        }
      >
        <AdminRoutePanel title="Audit Stream" description="Search, filter, and inspect individual events without leaving the admin console.">
          <div className="max-w-md">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
              <Input placeholder="Search actions, actors, resources, or details" value={searchQuery} onChange={(event) => { setPage(1); setSearchQuery(event.target.value); }} className="pl-9" />
            </div>
          </div>
          <FilterBar groups={filterGroups} selected={filters} onChange={(groupId, optionId) => { setPage(1); handleFilterChange(groupId, optionId); }} onClear={() => { setPage(1); setFilters({ action: [], actor: [] }); setSearchQuery(''); }} />
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
          <DataTable columns={columns} data={rows} keyExtractor={(log) => log.id} onRowClick={handleRowClick} mobileCardRender={mobileCardRender} />
        </AdminRoutePanel>
      </AsyncStateWrapper>

      <Drawer open={Boolean(selectedLogId)} onClose={handleDrawerClose} title="Audit Event Detail" restoreFocusOnClose={false}>
        {isDetailLoading || !selectedLogDetail ? (
          <p className="text-sm text-muted">Loading event detail...</p>
        ) : (
          <div className="space-y-4 text-sm">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Event ID</p>
              <p className="font-mono text-navy">{selectedLogDetail.id}</p>
            </div>
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Timestamp</p>
              <p className="text-navy">{new Date(selectedLogDetail.timestamp).toLocaleString()}</p>
            </div>
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Actor</p>
              <p className="text-navy">{selectedLogDetail.actorName}</p>
              <p className="font-mono text-xs text-muted">{selectedLogDetail.actorId}</p>
            </div>
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Action</p>
              <p className="text-navy">{selectedLogDetail.action}</p>
            </div>
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Resource</p>
              <p className="text-navy">{selectedLogDetail.resourceType}</p>
              <p className="font-mono text-xs text-muted">{selectedLogDetail.resourceId || 'No resource ID'}</p>
            </div>
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Details</p>
              <p className="whitespace-pre-wrap rounded-xl border border-border bg-background-light p-4 text-navy">
                {selectedLogDetail.details || 'No additional details recorded.'}
              </p>
            </div>
          </div>
        )}
      </Drawer>
    </AdminRouteWorkspace>
  );
}
