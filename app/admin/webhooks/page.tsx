'use client';

import { useEffect, useState } from 'react';
import { Webhook, RefreshCw, AlertTriangle } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteSummaryCard, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { retryWebhook } from '@/lib/api';
import { getAdminWebhookEventsData, getAdminWebhookSummaryData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminWebhookEvent, AdminWebhookSummary } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

const statusColors: Record<string, 'default' | 'success' | 'danger' | 'muted'> = {
  received: 'muted',
  processing: 'default',
  completed: 'success',
  failed: 'danger',
  ignored: 'muted',
};

export default function WebhooksPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [filters, setFilters] = useState<Record<string, string[]>>({ status: [], gateway: [] });
  const [events, setEvents] = useState<AdminWebhookEvent[]>([]);
  const [summary, setSummary] = useState<AdminWebhookSummary | null>(null);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [isMutating, setIsMutating] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const selectedStatus = filters.status?.[0];
  const selectedGateway = filters.gateway?.[0];

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setPageStatus('loading');
      try {
        const [eventsData, summaryData] = await Promise.all([
          getAdminWebhookEventsData({ status: selectedStatus, gateway: selectedGateway, page, pageSize: 20 }),
          getAdminWebhookSummaryData(),
        ]);
        if (cancelled) return;
        setEvents(eventsData.items);
        setTotal(eventsData.total);
        setSummary(summaryData);
        setPageStatus(eventsData.items.length > 0 ? 'success' : 'empty');
      } catch {
        if (!cancelled) setPageStatus('error');
      }
    }
    load();
    return () => { cancelled = true; };
  }, [selectedStatus, selectedGateway, page]);

  async function handleRetry(eventId: string) {
    setIsMutating(true);
    try {
      await retryWebhook(eventId);
      setToast({ variant: 'success', message: 'Webhook queued for retry.' });
      setEvents((prev) => prev.map((e) => e.id === eventId ? { ...e, processingStatus: 'received' as const } : e));
    } catch {
      setToast({ variant: 'error', message: 'Failed to retry webhook.' });
    } finally {
      setIsMutating(false);
    }
  }

  const filterGroups: FilterGroup[] = [
    {
      id: 'status',
      label: 'Status',
      options: [
        { id: 'received', label: 'Received' },
        { id: 'processing', label: 'Processing' },
        { id: 'completed', label: 'Completed' },
        { id: 'failed', label: 'Failed' },
        { id: 'ignored', label: 'Ignored' },
      ],
    },
    {
      id: 'gateway',
      label: 'Gateway',
      options: [
        { id: 'stripe', label: 'Stripe' },
        { id: 'paypal', label: 'PayPal' },
      ],
    },
  ];

  const columns: Column<AdminWebhookEvent>[] = [
    { key: 'eventType', header: 'Event Type', render: (e) => <span className="font-mono text-xs">{e.eventType}</span> },
    { key: 'gateway', header: 'Gateway', render: (e) => <Badge variant="muted">{e.gateway}</Badge> },
    {
      key: 'processingStatus',
      header: 'Status',
      render: (e) => <Badge variant={statusColors[e.processingStatus] ?? 'default'}>{e.processingStatus}</Badge>,
    },
    {
      key: 'receivedAt',
      header: 'Received',
      render: (e) => new Date(e.receivedAt).toLocaleString(),
    },
    {
      key: 'actions' as any,
      header: '',
      render: (e) =>
        e.processingStatus === 'failed' ? (
          <Button size="sm" variant="outline" onClick={() => handleRetry(e.id)} disabled={isMutating}>
            <RefreshCw className="w-3.5 h-3.5 mr-1" />
            Retry
          </Button>
        ) : null,
    },
  ];

  return (
    <AdminRouteWorkspace>
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
      <AdminRouteSectionHeader
        title="Webhook Monitoring"
        description="Monitor payment webhook events and retry failed deliveries."
      />

      {summary && (
        <div className="grid grid-cols-1 sm:grid-cols-4 gap-4 mb-6">
          <AdminRouteSummaryCard label="Total Events" value={summary.total} />
          <AdminRouteSummaryCard label="Last 24h" value={summary.recent24h} />
          <AdminRouteSummaryCard label="Failed" value={summary.failed} tone={summary.failed > 0 ? 'danger' : 'default'} />
          <AdminRouteSummaryCard label="Failed (24h)" value={summary.failed24h} tone={summary.failed24h > 0 ? 'warning' : 'default'} />
        </div>
      )}

      {summary && summary.recentFailures.length > 0 && (
        <AdminRoutePanel className="mb-6">
          <h3 className="text-sm font-semibold text-navy dark:text-muted mb-2 flex items-center gap-1.5">
            <AlertTriangle className="w-4 h-4 text-danger" /> Recent Failures
          </h3>
          <div className="space-y-2">
            {summary.recentFailures.map((f) => (
              <div key={f.id} className="text-xs text-muted dark:text-muted border-l-2 border-danger/40 pl-3">
                <span className="font-mono">{f.eventType}</span>
                {f.errorMessage && <span className="ml-2 text-danger">{f.errorMessage}</span>}
              </div>
            ))}
          </div>
        </AdminRoutePanel>
      )}

      <FilterBar groups={filterGroups} selected={filters} onChange={(groupId, optionId) => setFilters(prev => { const arr = [...(prev[groupId] || [])]; const i = arr.indexOf(optionId); if (i >= 0) arr.splice(i, 1); else arr.push(optionId); return { ...prev, [groupId]: arr }; })} />

      <AsyncStateWrapper
        status={pageStatus}
        emptyContent={<EmptyState icon={<Webhook className="w-12 h-12" />} title="No webhook events" description="No webhook events found with current filters." />}
        errorMessage="Unable to load webhook events."
      >
        <AdminRoutePanel>
          <DataTable
            columns={columns}
            data={events}
            keyExtractor={(e) => e.id}
            mobileCardRender={(e) => (
              <div className="space-y-1">
                <p className="font-mono text-xs">{e.eventType}</p>
                <div className="flex gap-2">
                  <Badge variant="muted">{e.gateway}</Badge>
                  <Badge variant={statusColors[e.processingStatus] ?? 'default'}>{e.processingStatus}</Badge>
                </div>
                {e.errorMessage && <p className="text-xs text-danger">{e.errorMessage}</p>}
              </div>
            )}
          />
        </AdminRoutePanel>
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
