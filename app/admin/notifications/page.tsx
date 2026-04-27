'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Bell, Filter, Mail, RefreshCw, RotateCcw, Send, Siren, Smartphone } from 'lucide-react';
import { AdminRouteSummaryCard, AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { fetchAdminAuditLogs } from '@/lib/api';
import {
  fetchAdminNotificationCatalog,
  fetchAdminNotificationDeliveries,
  fetchAdminNotificationHealth,
  fetchAdminNotificationPolicies,
  resetAdminNotificationPolicyOverride,
  sendAdminNotificationTestEmail,
  updateAdminNotificationPolicy,
} from '@/lib/notifications-api';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type {
  AdminNotificationAudienceChannelPolicy,
  AdminNotificationCatalogEntry,
  AdminNotificationHealthSnapshot,
  AdminNotificationPolicyRow,
  NotificationAudienceRole,
  NotificationChannel,
  NotificationDeliveryAttemptItem,
  NotificationDeliveryStatus,
  NotificationEmailMode,
} from '@/lib/types/notifications';
import type { AdminAuditLogRow } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;
type GlobalChannelKey = keyof AdminNotificationAudienceChannelPolicy;
type DeliveryFilters = {
  status: '' | NotificationDeliveryStatus;
  channel: '' | NotificationChannel;
  audienceRole: '' | NotificationAudienceRole;
  eventKey: string;
};

const GLOBAL_POLICY_EVENT_KEY = '__global__';
const DELIVERY_PAGE_SIZE = 20;
const AUDIENCE_ROLES = ['learner', 'expert', 'admin'] as const satisfies readonly NotificationAudienceRole[];
const GLOBAL_CHANNELS: Array<{ key: GlobalChannelKey; label: string; description: string }> = [
  { key: 'inAppEnabled', label: 'In-app', description: 'Shared inbox and realtime badge.' },
  { key: 'emailEnabled', label: 'Email', description: 'Transactional and digest email fan-out.' },
  { key: 'pushEnabled', label: 'Push', description: 'Browser push and quiet-hour deferral.' },
];
const DEFAULT_GLOBAL_CHANNELS: Record<NotificationAudienceRole, AdminNotificationAudienceChannelPolicy> = {
  learner: { inAppEnabled: true, emailEnabled: true, pushEnabled: true },
  expert: { inAppEnabled: true, emailEnabled: true, pushEnabled: true },
  admin: { inAppEnabled: true, emailEnabled: true, pushEnabled: true },
};
const EMPTY_DELIVERY_FILTERS: DeliveryFilters = {
  status: '',
  channel: '',
  audienceRole: '',
  eventKey: '',
};

interface PolicyDraft extends Pick<AdminNotificationPolicyRow, 'inAppEnabled' | 'emailEnabled' | 'pushEnabled' | 'emailMode'> {}

function policyKey(audienceRole: NotificationAudienceRole, eventKey: string) {
  return `${audienceRole}:${eventKey}`;
}

function globalPolicyKey(audienceRole: NotificationAudienceRole, channelKey: GlobalChannelKey) {
  return `${audienceRole}:${GLOBAL_POLICY_EVENT_KEY}:${channelKey}`;
}

function deliveryStatusVariant(status: NotificationDeliveryAttemptItem['status']) {
  switch (status) {
    case 'sent':
      return 'success';
    case 'failed':
    case 'expired':
      return 'danger';
    case 'suppressed':
      return 'warning';
    default:
      return 'muted';
  }
}

function boolLabel(value: boolean) {
  return value ? 'On' : 'Off';
}

function normalizeGlobalChannels(
  response: { globalEmailEnabledByAudience: Record<NotificationAudienceRole, boolean>; globalChannelEnabledByAudience?: Record<NotificationAudienceRole, AdminNotificationAudienceChannelPolicy> },
): Record<NotificationAudienceRole, AdminNotificationAudienceChannelPolicy> {
  return AUDIENCE_ROLES.reduce((accumulator, audienceRole) => {
    const channelPolicy = response.globalChannelEnabledByAudience?.[audienceRole];
    accumulator[audienceRole] = {
      inAppEnabled: channelPolicy?.inAppEnabled ?? true,
      emailEnabled: channelPolicy?.emailEnabled ?? response.globalEmailEnabledByAudience[audienceRole] ?? true,
      pushEnabled: channelPolicy?.pushEnabled ?? true,
    };
    return accumulator;
  }, { ...DEFAULT_GLOBAL_CHANNELS } as Record<NotificationAudienceRole, AdminNotificationAudienceChannelPolicy>);
}

export default function AdminNotificationsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [catalog, setCatalog] = useState<AdminNotificationCatalogEntry[]>([]);
  const [policies, setPolicies] = useState<AdminNotificationPolicyRow[]>([]);
  const [globalChannelEnabledByAudience, setGlobalChannelEnabledByAudience] = useState<Record<NotificationAudienceRole, AdminNotificationAudienceChannelPolicy>>(DEFAULT_GLOBAL_CHANNELS);
  const [health, setHealth] = useState<AdminNotificationHealthSnapshot | null>(null);
  const [deliveries, setDeliveries] = useState<NotificationDeliveryAttemptItem[]>([]);
  const [deliveryFilters, setDeliveryFilters] = useState<DeliveryFilters>(EMPTY_DELIVERY_FILTERS);
  const [deliveryPage, setDeliveryPage] = useState(1);
  const [deliveryTotalCount, setDeliveryTotalCount] = useState(0);
  const [auditRows, setAuditRows] = useState<AdminAuditLogRow[]>([]);
  const [drafts, setDrafts] = useState<Record<string, PolicyDraft>>({});
  const [savingKey, setSavingKey] = useState<string | null>(null);
  const [reloading, setReloading] = useState(false);
  const [sendingTestEmail, setSendingTestEmail] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);
  const [testRecipientEmail, setTestRecipientEmail] = useState('');
  const [testAudienceRole, setTestAudienceRole] = useState<NotificationAudienceRole>('learner');
  const [testEventKey, setTestEventKey] = useState('');

  const catalogByAudience = useMemo(() => ({
    learner: catalog.filter((entry) => entry.audienceRole === 'learner'),
    expert: catalog.filter((entry) => entry.audienceRole === 'expert'),
    admin: catalog.filter((entry) => entry.audienceRole === 'admin'),
  }), [catalog]);

  const catalogEventOptions = useMemo(() => (
    Array.from(new Map(catalog.map((entry) => [entry.eventKey, entry])).values())
      .sort((left, right) => left.label.localeCompare(right.label))
  ), [catalog]);

  const selectedTestCatalogEntry = useMemo(
    () => catalog.find((entry) => entry.eventKey === testEventKey && entry.audienceRole === testAudienceRole),
    [catalog, testAudienceRole, testEventKey],
  );

  const deliveryTotalPages = Math.max(1, Math.ceil(deliveryTotalCount / DELIVERY_PAGE_SIZE));
  const deliveryStartIndex = deliveryTotalCount === 0 ? 0 : ((deliveryPage - 1) * DELIVERY_PAGE_SIZE) + 1;
  const deliveryEndIndex = Math.min(deliveryPage * DELIVERY_PAGE_SIZE, deliveryTotalCount);

  useEffect(() => {
    if (catalogByAudience[testAudienceRole].length > 0 && !catalogByAudience[testAudienceRole].some((entry) => entry.eventKey === testEventKey)) {
      setTestEventKey(catalogByAudience[testAudienceRole][0].eventKey);
    }
  }, [catalogByAudience, testAudienceRole, testEventKey]);

  const loadPageData = useCallback(async (
    showSpinner = true,
    nextDeliveryPage = 1,
    nextDeliveryFilters = EMPTY_DELIVERY_FILTERS,
  ) => {
    if (showSpinner) {
      setPageStatus('loading');
    } else {
      setReloading(true);
    }

    try {
      const [catalogResponse, policiesResponse, healthResponse, deliveriesResponse, auditResponse] = await Promise.all([
        fetchAdminNotificationCatalog(),
        fetchAdminNotificationPolicies(),
        fetchAdminNotificationHealth(),
        fetchAdminNotificationDeliveries({
          page: nextDeliveryPage,
          pageSize: DELIVERY_PAGE_SIZE,
          status: nextDeliveryFilters.status || undefined,
          channel: nextDeliveryFilters.channel || undefined,
          audienceRole: nextDeliveryFilters.audienceRole || undefined,
          eventKey: nextDeliveryFilters.eventKey || undefined,
        }),
        fetchAdminAuditLogs({ action: 'notification_policy_updated', pageSize: 20 }) as Promise<{ items: AdminAuditLogRow[] }>,
      ]);

      setCatalog(catalogResponse);
      setPolicies(policiesResponse.rows);
      setGlobalChannelEnabledByAudience(normalizeGlobalChannels(policiesResponse));
      setHealth(healthResponse);
      setDeliveries(deliveriesResponse.items);
      setDeliveryPage(deliveriesResponse.page ?? nextDeliveryPage);
      setDeliveryTotalCount(deliveriesResponse.totalCount ?? deliveriesResponse.items.length);
      setAuditRows(auditResponse.items ?? []);

      const hasData =
        catalogResponse.length > 0
        || policiesResponse.rows.length > 0
        || deliveriesResponse.items.length > 0
        || (healthResponse?.failureQueue.length ?? 0) > 0;
      setPageStatus(hasData ? 'success' : 'empty');
    } catch (error) {
      console.error(error);
      setPageStatus('error');
      setToast({ variant: 'error', message: 'Unable to load notification governance data.' });
    } finally {
      setReloading(false);
    }
  }, []);

  useEffect(() => {
    let cancelled = false;

    (async () => {
      await loadPageData(true, 1, EMPTY_DELIVERY_FILTERS);
      if (cancelled) {
        return;
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [loadPageData]);

  const policyColumns: Column<AdminNotificationPolicyRow>[] = [
    {
      key: 'event',
      header: 'Event',
      render: (row) => (
        <div className="space-y-1">
          <p className="font-medium text-navy">{row.label}</p>
          <p className="font-mono text-[11px] text-muted">{row.eventKey}</p>
        </div>
      ),
    },
    {
      key: 'category',
      header: 'Category',
      render: (row) => <Badge variant="muted">{row.category}</Badge>,
    },
    {
      key: 'inAppEnabled',
      header: 'In-app',
      render: (row) => {
        const key = policyKey(row.audienceRole, row.eventKey);
        const draft = drafts[key] ?? row;
        return (
          <Button
            type="button"
            size="sm"
            variant={draft.inAppEnabled ? 'primary' : 'outline'}
            onClick={() => setDrafts((current) => ({
              ...current,
              [key]: { ...draft, inAppEnabled: !draft.inAppEnabled },
            }))}
          >
            {boolLabel(draft.inAppEnabled)}
          </Button>
        );
      },
    },
    {
      key: 'emailEnabled',
      header: 'Email',
      render: (row) => {
        const key = policyKey(row.audienceRole, row.eventKey);
        const draft = drafts[key] ?? row;
        return (
          <Button
            type="button"
            size="sm"
            variant={draft.emailEnabled ? 'primary' : 'outline'}
            onClick={() => setDrafts((current) => ({
              ...current,
              [key]: { ...draft, emailEnabled: !draft.emailEnabled },
            }))}
          >
            {boolLabel(draft.emailEnabled)}
          </Button>
        );
      },
    },
    {
      key: 'pushEnabled',
      header: 'Push',
      render: (row) => {
        const key = policyKey(row.audienceRole, row.eventKey);
        const draft = drafts[key] ?? row;
        return (
          <Button
            type="button"
            size="sm"
            variant={draft.pushEnabled ? 'primary' : 'outline'}
            onClick={() => setDrafts((current) => ({
              ...current,
              [key]: { ...draft, pushEnabled: !draft.pushEnabled },
            }))}
          >
            {boolLabel(draft.pushEnabled)}
          </Button>
        );
      },
    },
    {
      key: 'emailMode',
      header: 'Email Mode',
      render: (row) => {
        const key = policyKey(row.audienceRole, row.eventKey);
        const draft = drafts[key] ?? row;
        return (
          <select
            className="w-full rounded-lg border border-border bg-surface px-3 py-2 text-sm text-navy"
            value={draft.emailMode}
            onChange={(event) => setDrafts((current) => ({
              ...current,
              [key]: { ...draft, emailMode: event.target.value as NotificationEmailMode },
            }))}
          >
            <option value="off">Off</option>
            <option value="immediate">Immediate</option>
            <option value="daily_digest">Daily Digest</option>
          </select>
        );
      },
      className: 'min-w-44',
    },
    {
      key: 'state',
      header: 'State',
      render: (row) => (
        <Badge variant={row.isOverride ? 'info' : 'muted'}>
          {row.isOverride ? 'Override' : 'Default'}
        </Badge>
      ),
    },
    {
      key: 'save',
      header: '',
      render: (row) => {
        const key = policyKey(row.audienceRole, row.eventKey);
        const draft = drafts[key];
        const hasChanges = Boolean(
          draft
          && (
            draft.inAppEnabled !== row.inAppEnabled
            || draft.emailEnabled !== row.emailEnabled
            || draft.pushEnabled !== row.pushEnabled
            || draft.emailMode !== row.emailMode
          ),
        );

        return (
          <div className="flex justify-end gap-2">
            <Button
              type="button"
              size="sm"
              onClick={() => void handleSavePolicy(row.audienceRole, row.eventKey)}
              disabled={!hasChanges}
              loading={savingKey === key}
            >
              Save
            </Button>
            <Button
              type="button"
              size="sm"
              variant="outline"
              onClick={() => void handleResetPolicy(row.audienceRole, row.eventKey)}
              disabled={!row.isOverride}
              loading={savingKey === `${key}:reset`}
              className="gap-1.5"
            >
              <RotateCcw className="h-3.5 w-3.5" />
              Reset
            </Button>
          </div>
        );
      },
      className: 'w-48',
    },
  ];

  const failureQueueColumns: Column<AdminNotificationHealthSnapshot['failureQueue'][number]>[] = [
    {
      key: 'eventKey',
      header: 'Event',
      render: (row) => (
        <div className="space-y-1">
          <p className="font-medium text-navy">{row.eventKey}</p>
          <p className="text-xs text-muted">{row.audienceRole}</p>
        </div>
      ),
    },
    {
      key: 'channel',
      header: 'Channel',
      render: (row) => <Badge variant="muted">{row.channel}</Badge>,
    },
    {
      key: 'status',
      header: 'Status',
      render: (row) => <Badge variant={deliveryStatusVariant(row.status)}>{row.status}</Badge>,
    },
    {
      key: 'error',
      header: 'Error',
      render: (row) => <span className="text-sm text-muted">{row.errorMessage || row.errorCode || 'No error details'}</span>,
    },
    {
      key: 'attemptedAt',
      header: 'Attempted',
      render: (row) => <span className="text-sm text-muted">{new Date(row.attemptedAt).toLocaleString()}</span>,
    },
  ];

  const deliveryColumns: Column<NotificationDeliveryAttemptItem>[] = [
    {
      key: 'eventKey',
      header: 'Event',
      render: (row) => (
        <div className="space-y-1">
          <p className="font-medium text-navy">{row.eventKey}</p>
          <p className="text-xs text-muted">{row.audienceRole}</p>
        </div>
      ),
    },
    {
      key: 'channel',
      header: 'Channel',
      render: (row) => <Badge variant="muted">{row.channel}</Badge>,
    },
    {
      key: 'status',
      header: 'Status',
      render: (row) => <Badge variant={deliveryStatusVariant(row.status)}>{row.status}</Badge>,
    },
    {
      key: 'provider',
      header: 'Provider',
      render: (row) => <span className="text-sm text-muted">{row.provider || 'n/a'}</span>,
    },
    {
      key: 'attemptedAt',
      header: 'Attempted',
      render: (row) => <span className="text-sm text-muted">{new Date(row.attemptedAt).toLocaleString()}</span>,
    },
  ];

  async function handleSavePolicy(audienceRole: NotificationAudienceRole, eventKey: string) {
    const key = policyKey(audienceRole, eventKey);
    const row = policies.find((candidate) => candidate.audienceRole === audienceRole && candidate.eventKey === eventKey);
    const draft = drafts[key];
    if (!row || !draft) {
      return;
    }

    setSavingKey(key);
    try {
      const response = await updateAdminNotificationPolicy(audienceRole, eventKey, draft);
      setPolicies((current) => current.map((candidate) => (
        candidate.audienceRole === audienceRole && candidate.eventKey === eventKey ? response : candidate
      )));
      setDrafts((current) => {
        const next = { ...current };
        delete next[key];
        return next;
      });
      setToast({ variant: 'success', message: `Updated policy for ${audienceRole}/${eventKey}.` });
      await loadPageData(false, deliveryPage, deliveryFilters);
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to save notification policy.' });
    } finally {
      setSavingKey(null);
    }
  }

  async function handleResetPolicy(audienceRole: NotificationAudienceRole, eventKey: string) {
    const key = policyKey(audienceRole, eventKey);
    setSavingKey(`${key}:reset`);
    try {
      const response = await resetAdminNotificationPolicyOverride(audienceRole, eventKey);
      setPolicies((current) => current.map((candidate) => (
        candidate.audienceRole === audienceRole && candidate.eventKey === eventKey ? response : candidate
      )));
      setDrafts((current) => {
        const next = { ...current };
        delete next[key];
        return next;
      });
      setToast({ variant: 'success', message: `Reset policy for ${audienceRole}/${eventKey}.` });
      await loadPageData(false, deliveryPage, deliveryFilters);
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to reset notification policy.' });
    } finally {
      setSavingKey(null);
    }
  }

  async function handleToggleGlobalChannel(audienceRole: NotificationAudienceRole, channelKey: GlobalChannelKey) {
    const key = globalPolicyKey(audienceRole, channelKey);
    setSavingKey(key);
    try {
      const nextValue = !globalChannelEnabledByAudience[audienceRole][channelKey];
      await updateAdminNotificationPolicy(audienceRole, GLOBAL_POLICY_EVENT_KEY, {
        ...globalChannelEnabledByAudience[audienceRole],
        [channelKey]: nextValue,
      });
      setGlobalChannelEnabledByAudience((current) => ({
        ...current,
        [audienceRole]: {
          ...current[audienceRole],
          [channelKey]: nextValue,
        },
      }));
      setToast({ variant: 'success', message: `Updated ${audienceRole} ${channelKey.replace('Enabled', '').toLowerCase()} governance.` });
      await loadPageData(false, deliveryPage, deliveryFilters);
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to update the global channel switch.' });
    } finally {
      setSavingKey(null);
    }
  }

  async function handleApplyDeliveryFilters() {
    setDeliveryPage(1);
    await loadPageData(false, 1, deliveryFilters);
  }

  async function handleClearDeliveryFilters() {
    setDeliveryFilters(EMPTY_DELIVERY_FILTERS);
    setDeliveryPage(1);
    await loadPageData(false, 1, EMPTY_DELIVERY_FILTERS);
  }

  async function handleDeliveryPageChange(nextPage: number) {
    const boundedPage = Math.min(Math.max(1, nextPage), deliveryTotalPages);
    setDeliveryPage(boundedPage);
    await loadPageData(false, boundedPage, deliveryFilters);
  }

  async function handleSendTestEmail() {
    if (!testRecipientEmail || !testEventKey) {
      setToast({ variant: 'error', message: 'Choose an event and recipient email first.' });
      return;
    }

    setSendingTestEmail(true);
    try {
      await sendAdminNotificationTestEmail({
        recipientEmail: testRecipientEmail,
        audienceRole: testAudienceRole,
        eventKey: testEventKey,
      });
      setToast({ variant: 'success', message: 'Notification test email sent.' });
      await loadPageData(false, deliveryPage, deliveryFilters);
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to send the notification test email.' });
    } finally {
      setSendingTestEmail(false);
    }
  }

  if (!isAuthenticated || role !== 'admin') {
    return null;
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="Notifications" className="space-y-5">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <div className="rounded-lg border border-border bg-surface px-4 py-4 shadow-sm sm:px-5">
        <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
          <div className="min-w-0 space-y-1">
            <p className="text-xs font-semibold uppercase tracking-[0.16em] text-muted">Admin Workspace</p>
            <h1 className="text-xl font-semibold tracking-tight text-navy sm:text-2xl">Notifications</h1>
            <p className="max-w-4xl text-sm leading-6 text-muted">
              Govern learner, expert, and admin delivery from one operational surface: role-wide switches, per-event policy, delivery health, test email, and audit visibility.
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {health?.generatedAt ? <Badge variant="muted">Updated {new Date(health.generatedAt).toLocaleString()}</Badge> : null}
          <Button type="button" variant="outline" onClick={() => void loadPageData(false, deliveryPage, deliveryFilters)} loading={reloading} className="gap-2">
            <RefreshCw className="h-4 w-4" />
            Refresh
          </Button>
          </div>
        </div>
      </div>

      <AsyncStateWrapper
        status={pageStatus}
        className="space-y-5"
        onRetry={() => void loadPageData(true, deliveryPage, deliveryFilters)}
        emptyContent={(
          <EmptyState
            icon={<Bell className="h-10 w-10 text-muted" />}
            title="No notification governance data is available yet"
            description="Reload after the first notification events and policy rows have been generated."
          />
        )}
      >
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <AdminRouteSummaryCard label="Queued Events" value={health?.queuedEvents ?? 0} icon={<Bell className="h-5 w-5" />} />
          <AdminRouteSummaryCard label="Failed Deliveries (24h)" value={health?.failedDeliveriesLast24Hours ?? 0} icon={<Siren className="h-5 w-5" />} tone={(health?.failedDeliveriesLast24Hours ?? 0) > 0 ? 'danger' : 'default'} />
          <AdminRouteSummaryCard label="Unread Inbox Items" value={health?.unreadInboxItems ?? 0} icon={<Mail className="h-5 w-5" />} />
          <AdminRouteSummaryCard label="Active Push Subscriptions" value={health?.activePushSubscriptions ?? 0} icon={<Smartphone className="h-5 w-5" />} tone={(health?.activePushSubscriptions ?? 0) > 0 ? 'success' : 'default'} />
        </div>

        <AdminRoutePanel title="Role-wide Channel Governance" description="These switches apply before per-event policy and account preferences. Use them when an entire audience channel needs to be paused or restored.">
          <div className="grid gap-3 md:grid-cols-3">
            {AUDIENCE_ROLES.map((audienceRole) => (
              <div key={audienceRole} className="rounded-lg border border-border bg-background-light p-3 shadow-sm">
                <div className="mb-3 flex items-center justify-between gap-3">
                  <div>
                    <p className="text-sm font-semibold capitalize text-navy">{audienceRole}</p>
                    <p className="text-xs text-muted">Role-wide delivery controls.</p>
                  </div>
                  <Badge variant="muted">{GLOBAL_CHANNELS.filter((channel) => globalChannelEnabledByAudience[audienceRole][channel.key]).length}/3 on</Badge>
                </div>
                <div className="space-y-2">
                  {GLOBAL_CHANNELS.map((channel) => {
                    const enabled = globalChannelEnabledByAudience[audienceRole][channel.key];
                    return (
                      <div key={channel.key} className="rounded-lg border border-border bg-white px-3 py-2">
                        <div className="flex items-center justify-between gap-3">
                          <div>
                            <p className="text-sm font-semibold text-navy">{channel.label}</p>
                            <p className="text-xs text-muted">{channel.description}</p>
                          </div>
                          <Badge variant={enabled ? 'success' : 'warning'}>{enabled ? 'Enabled' : 'Suppressed'}</Badge>
                        </div>
                        <Button
                          type="button"
                          size="sm"
                          variant={enabled ? 'destructive' : 'primary'}
                          onClick={() => void handleToggleGlobalChannel(audienceRole, channel.key)}
                          loading={savingKey === globalPolicyKey(audienceRole, channel.key)}
                          className="mt-2 w-full"
                        >
                          {enabled ? `Disable ${channel.label}` : `Enable ${channel.label}`}
                        </Button>
                      </div>
                    );
                  })}
                </div>
              </div>
            ))}
          </div>
        </AdminRoutePanel>

        {AUDIENCE_ROLES.map((audienceRole) => (
          <AdminRoutePanel
            key={audienceRole}
            title={`${audienceRole.charAt(0).toUpperCase()}${audienceRole.slice(1)} Policy Matrix`}
            description={`Per-event channel policy for ${audienceRole} notifications. Email modes are off, immediate, or daily digest.`}
          >
            <DataTable
              columns={policyColumns}
              data={policies.filter((row) => row.audienceRole === audienceRole)}
              keyExtractor={(row) => policyKey(row.audienceRole, row.eventKey)}
            />
          </AdminRoutePanel>
        ))}

        <div className="grid gap-6 xl:grid-cols-2">
          <AdminRoutePanel title="Email Preview & Test Send" description="Email rendering stays server-owned in this rollout. Use the catalog preview below and send a real test email without editing HTML.">
            <div className="grid gap-4 md:grid-cols-2">
              <Input
                label="Recipient Email"
                type="email"
                value={testRecipientEmail}
                onChange={(event) => setTestRecipientEmail(event.target.value)}
                placeholder="ops@example.com"
              />
              <Select
                label="Audience"
                value={testAudienceRole}
                onChange={(event) => setTestAudienceRole(event.target.value as NotificationAudienceRole)}
                options={[
                  { value: 'learner', label: 'Learner' },
                  { value: 'expert', label: 'Expert' },
                  { value: 'admin', label: 'Admin' },
                ]}
              />
            </div>
            <Select
              label="Catalog Event"
              value={testEventKey}
              onChange={(event) => setTestEventKey(event.target.value)}
              options={catalogByAudience[testAudienceRole].map((entry) => ({
                value: entry.eventKey,
                label: `${entry.label} (${entry.eventKey})`,
              }))}
            />
            {selectedTestCatalogEntry ? (
              <div className="rounded-lg border border-border bg-background-light p-4 text-sm text-muted">
                <p className="font-semibold text-navy">{selectedTestCatalogEntry.label}</p>
                <p className="mt-1">{selectedTestCatalogEntry.description}</p>
                <div className="mt-3 flex flex-wrap gap-2">
                  <Badge variant="info">Severity {selectedTestCatalogEntry.defaultSeverity}</Badge>
                  <Badge variant="muted">Email mode {selectedTestCatalogEntry.defaultEmailMode}</Badge>
                </div>
              </div>
            ) : null}
            <div className="flex justify-end">
              <Button type="button" onClick={() => void handleSendTestEmail()} loading={sendingTestEmail} className="gap-2">
                <Send className="h-4 w-4" />
                Send Test Email
              </Button>
            </div>
          </AdminRoutePanel>

          <AdminRoutePanel title="Delivery Health" description="Recent delivery attempts and failed queue entries stay visible here so admins can react before notifications silently degrade.">
            <div className="grid gap-3 sm:grid-cols-2">
              <div className="rounded-lg border border-border bg-background-light p-3 text-sm">
                <p className="font-semibold text-navy">Failed Events</p>
                <p className="mt-1 text-2xl font-bold text-navy">{health?.failedEvents ?? 0}</p>
              </div>
              <div className="rounded-lg border border-border bg-background-light p-3 text-sm">
                <p className="font-semibold text-navy">Pending Digests</p>
                <p className="mt-1 text-2xl font-bold text-navy">{health?.pendingDigestJobs ?? 0}</p>
              </div>
              <div className="rounded-lg border border-border bg-background-light p-3 text-sm">
                <p className="font-semibold text-navy">Expired Push Subscriptions</p>
                <p className="mt-1 text-2xl font-bold text-navy">{health?.expiredPushSubscriptions ?? 0}</p>
              </div>
              <div className="rounded-lg border border-border bg-background-light p-3 text-sm">
                <p className="font-semibold text-navy">Health Snapshot</p>
                <p className="mt-2 text-muted">{health?.generatedAt ? new Date(health.generatedAt).toLocaleString() : 'Not generated yet'}</p>
              </div>
            </div>
            {health?.channels?.length ? (
              <div className="grid gap-3 sm:grid-cols-3">
                {health.channels.map((channel) => (
                  <div key={channel.channel} className="rounded-lg border border-border bg-background-light p-3 text-sm shadow-sm">
                    <p className="font-semibold capitalize text-navy">{channel.channel}</p>
                    <p className="mt-2 text-muted">Sent {channel.sentLast24Hours}</p>
                    <p className="text-muted">Failed {channel.failedLast24Hours}</p>
                    <p className="text-muted">Suppressed {channel.suppressedLast24Hours}</p>
                  </div>
                ))}
              </div>
            ) : (
              <p className="text-sm text-muted">No channel telemetry has been recorded yet.</p>
            )}
          </AdminRoutePanel>
        </div>

        <div className="grid gap-6 xl:grid-cols-2">
          <AdminRoutePanel title="Failure Queue" description="Manual visibility into delivery failures and threshold-triggered alert candidates.">
            {health?.failureQueue?.length ? (
              <DataTable columns={failureQueueColumns} data={health.failureQueue} keyExtractor={(row) => `${row.eventId}:${row.channel}:${row.attemptedAt}`} />
            ) : (
              <p className="text-sm text-muted">No failed or expired deliveries are currently queued for investigation.</p>
            )}
          </AdminRoutePanel>

          <AdminRoutePanel title="Recent Deliveries" description="Latest delivery attempts across in-app, email, and push channels.">
            <div className="rounded-lg border border-border bg-background-light p-3">
              <div className="mb-3 flex items-center gap-2 text-sm font-semibold text-navy">
                <Filter className="h-4 w-4" />
                Delivery filters
              </div>
              <div className="grid gap-3 lg:grid-cols-4">
                <Select
                  label="Status"
                  value={deliveryFilters.status}
                  onChange={(event) => setDeliveryFilters((current) => ({ ...current, status: event.target.value as DeliveryFilters['status'] }))}
                  options={[
                    { value: '', label: 'All statuses' },
                    { value: 'pending', label: 'Pending' },
                    { value: 'sent', label: 'Sent' },
                    { value: 'suppressed', label: 'Suppressed' },
                    { value: 'failed', label: 'Failed' },
                    { value: 'expired', label: 'Expired' },
                  ]}
                />
                <Select
                  label="Channel"
                  value={deliveryFilters.channel}
                  onChange={(event) => setDeliveryFilters((current) => ({ ...current, channel: event.target.value as DeliveryFilters['channel'] }))}
                  options={[
                    { value: '', label: 'All channels' },
                    { value: 'in_app', label: 'In-app' },
                    { value: 'email', label: 'Email' },
                    { value: 'push', label: 'Push' },
                  ]}
                />
                <Select
                  label="Audience"
                  value={deliveryFilters.audienceRole}
                  onChange={(event) => setDeliveryFilters((current) => ({ ...current, audienceRole: event.target.value as DeliveryFilters['audienceRole'] }))}
                  options={[
                    { value: '', label: 'All audiences' },
                    ...AUDIENCE_ROLES.map((audienceRole) => ({ value: audienceRole, label: audienceRole.charAt(0).toUpperCase() + audienceRole.slice(1) })),
                  ]}
                />
                <Select
                  label="Event"
                  value={deliveryFilters.eventKey}
                  onChange={(event) => setDeliveryFilters((current) => ({ ...current, eventKey: event.target.value }))}
                  options={[
                    { value: '', label: 'All events' },
                    ...catalogEventOptions.map((entry) => ({ value: entry.eventKey, label: `${entry.label} (${entry.eventKey})` })),
                  ]}
                />
              </div>
              <div className="mt-3 flex flex-wrap justify-end gap-2">
                <Button type="button" variant="outline" size="sm" onClick={() => void handleClearDeliveryFilters()}>
                  Clear
                </Button>
                <Button type="button" size="sm" onClick={() => void handleApplyDeliveryFilters()} loading={reloading}>
                  Apply Filters
                </Button>
              </div>
            </div>
            {deliveries.length ? (
              <>
                <DataTable columns={deliveryColumns} data={deliveries} keyExtractor={(row) => row.id} />
                <div className="flex flex-col gap-3 border-t border-border pt-3 text-sm text-muted sm:flex-row sm:items-center sm:justify-between">
                  <span>Showing {deliveryStartIndex}-{deliveryEndIndex} of {deliveryTotalCount}</span>
                  <div className="flex items-center gap-2">
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      onClick={() => void handleDeliveryPageChange(deliveryPage - 1)}
                      disabled={deliveryPage <= 1}
                      loading={reloading && deliveryPage > 1}
                    >
                      Previous
                    </Button>
                    <span className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Page {deliveryPage} / {deliveryTotalPages}</span>
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      onClick={() => void handleDeliveryPageChange(deliveryPage + 1)}
                      disabled={deliveryPage >= deliveryTotalPages}
                      loading={reloading && deliveryPage < deliveryTotalPages}
                    >
                      Next
                    </Button>
                  </div>
                </div>
              </>
            ) : (
              <p className="text-sm text-muted">No delivery attempts have been recorded yet.</p>
            )}
          </AdminRoutePanel>
        </div>

        <AdminRoutePanel title="Policy Change Audit Trail" description="Every policy update is written into the existing admin audit stream. Manual test sends also land in audit logs for traceability.">
          {auditRows.length ? (
            <DataTable
              columns={[
                {
                  key: 'timestamp',
                  header: 'Timestamp',
                  render: (row) => <span className="text-sm text-muted">{new Date(row.timestamp).toLocaleString()}</span>,
                },
                {
                  key: 'actor',
                  header: 'Actor',
                  render: (row) => <span className="font-medium text-navy">{row.actor}</span>,
                },
                {
                  key: 'resource',
                  header: 'Resource',
                  render: (row) => <span className="font-mono text-xs text-muted">{row.resource}</span>,
                },
                {
                  key: 'details',
                  header: 'Details',
                  render: (row) => <span className="text-sm text-muted">{row.details}</span>,
                },
              ]}
              data={auditRows}
              keyExtractor={(row) => row.id}
            />
          ) : (
            <p className="text-sm text-muted">No notification policy audit rows have been recorded yet.</p>
          )}
        </AdminRoutePanel>
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
