'use client';

import { useEffect, useState } from 'react';
import { Webhook, RefreshCw, AlertTriangle } from 'lucide-react';
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { KpiStrip } from '@/components/admin/layout/admin-operations-layout';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { retryWebhook } from '@/lib/api';
import { getAdminWebhookEventsData, getAdminWebhookSummaryData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminWebhookEvent, AdminWebhookRetryResponse, AdminWebhookSummary } from '@/lib/types/admin';
import { BulkActionBar } from '@/components/ui/bulk-action-bar';

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
  useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [filters, setFilters] = useState<Record<string, string[]>>({ status: [], gateway: [] });
  const [events, setEvents] = useState<AdminWebhookEvent[]>([]);
  const [summary, setSummary] = useState<AdminWebhookSummary | null>(null);
  const [, setTotal] = useState(0);
  const [page] = useState(1);
  const [isMutating, setIsMutating] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());

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
      const result = await retryWebhook(eventId) as AdminWebhookRetryResponse;
      const [eventsData, summaryData] = await Promise.all([
        getAdminWebhookEventsData({ status: selectedStatus, gateway: selectedGateway, page, pageSize: 20 }),
        getAdminWebhookSummaryData(),
      ]);
      setEvents(eventsData.items);
      setTotal(eventsData.total);
      setSummary(summaryData);
      setPageStatus(eventsData.items.length > 0 ? 'success' : 'empty');
      setToast({ variant: result.processingStatus === 'failed' ? 'error' : 'success', message: formatRetryToast(result) });
    } catch {
      setToast({ variant: 'error', message: 'Webhook retry was not accepted. Check retry eligibility.' });
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
      key: 'normalizedStatus',
      header: 'Payment',
      render: (e) => e.normalizedStatus ? <Badge variant="muted">{e.normalizedStatus}</Badge> : <span className="text-xs text-admin-fg-muted">-</span>,
    },
    {
      key: 'processingStatus',
      header: 'Status',
      render: (e) => <Badge variant={statusColors[e.processingStatus] ?? 'default'}>{e.processingStatus}</Badge>,
    },
    {
      key: 'attemptCount',
      header: 'Attempts',
      render: (e) => <span className="text-xs text-admin-fg-muted">{e.attemptCount} / {e.retryCount}</span>,
    },
    {
      key: 'receivedAt',
      header: 'Received',
      render: (e) => new Date(e.receivedAt).toLocaleString(),
    },
    {
      key: 'actions',
      header: '',
      render: (e) => {
        if (e.processingStatus !== 'failed') return null;
        return (
          <Button
            size="sm"
            variant="outline"
            onClick={() => handleRetry(e.id)}
            disabled={isMutating || !e.retryable}
            title={e.retryable ? 'Retry verified webhook processing' : e.retryBlockedReason ?? 'Webhook is not retryable'}
          >
            <RefreshCw className="w-3.5 h-3.5 mr-1" />
            Retry
          </Button>
        );
      },
    },
  ];

  return (
    <>
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
      <AdminTableLayout
        title="Webhook Monitoring"
        description="Monitor payment webhook processing and retry verified failed processing attempts."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Webhooks' },
        ]}
        banner={
          <div className="space-y-4">
            {summary && (
              <KpiStrip>
                <KpiTile label="Total Events" value={summary.total} />
                <KpiTile label="Last 24h" value={summary.recent24h} />
                <KpiTile
                  label="Failed"
                  value={summary.failed}
                  tone={summary.failed > 0 ? 'danger' : 'default'}
                />
                <KpiTile
                  label="Failed (24h)"
                  value={summary.failed24h}
                  tone={summary.failed24h > 0 ? 'warning' : 'default'}
                />
              </KpiStrip>
            )}

            {summary && summary.recentFailures.length > 0 && (
              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-1.5 text-sm">
                    <AlertTriangle className="w-4 h-4 text-[var(--admin-danger)]" aria-hidden="true" />
                    Recent Failures
                  </CardTitle>
                </CardHeader>
                <CardContent className="pt-0">
                  <div className="space-y-2">
                    {summary.recentFailures.map((f) => (
                      <div
                        key={f.id}
                        className="text-xs text-admin-fg-muted border-l-2 border-[var(--admin-danger)]/40 pl-3"
                      >
                        <span className="font-mono">{f.eventType}</span>
                        {f.errorMessage && (
                          <span className="ml-2 text-[var(--admin-danger)]">{f.errorMessage}</span>
                        )}
                        {!f.retryable && f.retryBlockedReason && (
                          <span className="ml-2 text-admin-fg-muted">{f.retryBlockedReason}</span>
                        )}
                      </div>
                    ))}
                  </div>
                </CardContent>
              </Card>
            )}

            <FilterBar
              groups={filterGroups}
              selected={filters}
              onChange={(groupId, optionId) =>
                setFilters((prev) => {
                  const arr = [...(prev[groupId] || [])];
                  const i = arr.indexOf(optionId);
                  if (i >= 0) arr.splice(i, 1);
                  else arr.push(optionId);
                  return { ...prev, [groupId]: arr };
                })
              }
            />
          </div>
        }
      >
        <AsyncStateWrapper
          status={pageStatus}
          emptyContent={
            <EmptyState
              illustration={<Webhook className="w-12 h-12" />}
              title="No webhook events"
              description="No webhook events found with current filters."
            />
          }
          errorMessage="Unable to load webhook events."
        >
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
                  {e.normalizedStatus && <Badge variant="muted">{e.normalizedStatus}</Badge>}
                </div>
                <p className="text-xs text-admin-fg-muted">Attempts {e.attemptCount} / retries {e.retryCount}</p>
                {e.errorMessage && <p className="text-xs text-[var(--admin-danger)]">{e.errorMessage}</p>}
                {!e.retryable && e.retryBlockedReason && <p className="text-xs text-admin-fg-muted">{e.retryBlockedReason}</p>}
              </div>
            )}
            selectable
            selectedKeys={selectedKeys}
            onSelectionChange={setSelectedKeys}
          />
          <BulkActionBar
            selectedCount={selectedKeys.size}
            onClearSelection={() => setSelectedKeys(new Set())}
            actions={[
              {
                key: 'retry',
                label: 'Retry selected',
                onClick: () => setToast({ variant: 'error', message: 'Bulk retry coming soon.' }),
              },
            ]}
          />
        </AsyncStateWrapper>
      </AdminTableLayout>
    </>
  );
}

function formatRetryToast(result: AdminWebhookRetryResponse) {
  if (result.processingStatus === 'failed') {
    return result.errorMessage ? `Webhook retry still failed: ${result.errorMessage}` : 'Webhook retry still failed.';
  }

  if (result.status === 'no_effect') {
    return 'Webhook retry completed with no billing change.';
  }

  return 'Webhook retry reprocessed successfully.';
}
