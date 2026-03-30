'use client';

import { useEffect, useMemo, useState } from 'react';
import { Bell, Mail, RefreshCw, Send, Siren, Smartphone } from 'lucide-react';
import { AdminMetricCard, AdminPageHeader, AdminSectionPanel } from '@/components/domain/admin-surface';
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
  sendAdminNotificationTestEmail,
  updateAdminNotificationPolicy,
} from '@/lib/notifications-api';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type {
  AdminNotificationCatalogEntry,
  AdminNotificationHealthSnapshot,
  AdminNotificationPolicyRow,
  NotificationAudienceRole,
  NotificationDeliveryAttemptItem,
  NotificationEmailMode,
} from '@/lib/types/notifications';
import type { AdminAuditLogRow } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

const GLOBAL_POLICY_EVENT_KEY = '__global__';

interface PolicyDraft extends Pick<AdminNotificationPolicyRow, 'inAppEnabled' | 'emailEnabled' | 'pushEnabled' | 'emailMode'> {}

function policyKey(audienceRole: NotificationAudienceRole, eventKey: string) {
  return `${audienceRole}:${eventKey}`;
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

export default function AdminNotificationsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [catalog, setCatalog] = useState<AdminNotificationCatalogEntry[]>([]);
  const [policies, setPolicies] = useState<AdminNotificationPolicyRow[]>([]);
  const [globalEmailEnabledByAudience, setGlobalEmailEnabledByAudience] = useState<Record<NotificationAudienceRole, boolean>>({
    learner: true,
    expert: true,
    admin: true,
  });
  const [health, setHealth] = useState<AdminNotificationHealthSnapshot | null>(null);
  const [deliveries, setDeliveries] = useState<NotificationDeliveryAttemptItem[]>([]);
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

  useEffect(() => {
    if (catalogByAudience[testAudienceRole].length > 0 && !catalogByAudience[testAudienceRole].some((entry) => entry.eventKey === testEventKey)) {
      setTestEventKey(catalogByAudience[testAudienceRole][0].eventKey);
    }
  }, [catalogByAudience, testAudienceRole, testEventKey]);

  async function loadPageData(showSpinner = true) {
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
        fetchAdminNotificationDeliveries({ page: 1, pageSize: 20 }),
        fetchAdminAuditLogs({ action: 'notification_policy_updated', pageSize: 20 }) as Promise<{ items: AdminAuditLogRow[] }>,
      ]);

      setCatalog(catalogResponse);
      setPolicies(policiesResponse.rows);
      setGlobalEmailEnabledByAudience(policiesResponse.globalEmailEnabledByAudience);
      setHealth(healthResponse);
      setDeliveries(deliveriesResponse.items);
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
  }

  useEffect(() => {
    let cancelled = false;

    (async () => {
      await loadPageData(true);
      if (cancelled) {
        return;
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  const policyColumns: Column<AdminNotificationPolicyRow>[] = [
    {
      key: 'event',
      header: 'Event',
      render: (row) => (
        <div className="space-y-1">
          <p className="font-medium text-slate-900">{row.label}</p>
          <p className="font-mono text-[11px] text-slate-500">{row.eventKey}</p>
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
            className="w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm text-navy"
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
          <div className="flex justify-end">
            <Button
              type="button"
              size="sm"
              onClick={() => void handleSavePolicy(row.audienceRole, row.eventKey)}
              disabled={!hasChanges}
              loading={savingKey === key}
            >
              Save
            </Button>
          </div>
        );
      },
      className: 'w-28',
    },
  ];

  const failureQueueColumns: Column<AdminNotificationHealthSnapshot['failureQueue'][number]>[] = [
    {
      key: 'eventKey',
      header: 'Event',
      render: (row) => (
        <div className="space-y-1">
          <p className="font-medium text-slate-900">{row.eventKey}</p>
          <p className="text-xs text-slate-500">{row.audienceRole}</p>
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
      render: (row) => <span className="text-sm text-slate-500">{row.errorMessage || row.errorCode || 'No error details'}</span>,
    },
    {
      key: 'attemptedAt',
      header: 'Attempted',
      render: (row) => <span className="text-sm text-slate-500">{new Date(row.attemptedAt).toLocaleString()}</span>,
    },
  ];

  const deliveryColumns: Column<NotificationDeliveryAttemptItem>[] = [
    {
      key: 'eventKey',
      header: 'Event',
      render: (row) => (
        <div className="space-y-1">
          <p className="font-medium text-slate-900">{row.eventKey}</p>
          <p className="text-xs text-slate-500">{row.audienceRole}</p>
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
      render: (row) => <span className="text-sm text-slate-500">{row.provider || 'n/a'}</span>,
    },
    {
      key: 'attemptedAt',
      header: 'Attempted',
      render: (row) => <span className="text-sm text-slate-500">{new Date(row.attemptedAt).toLocaleString()}</span>,
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
      await loadPageData(false);
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to save notification policy.' });
    } finally {
      setSavingKey(null);
    }
  }

  async function handleToggleGlobalEmail(audienceRole: NotificationAudienceRole) {
    const key = policyKey(audienceRole, GLOBAL_POLICY_EVENT_KEY);
    setSavingKey(key);
    try {
      const nextValue = !globalEmailEnabledByAudience[audienceRole];
      await updateAdminNotificationPolicy(audienceRole, GLOBAL_POLICY_EVENT_KEY, { emailEnabled: nextValue });
      setGlobalEmailEnabledByAudience((current) => ({ ...current, [audienceRole]: nextValue }));
      setToast({ variant: 'success', message: `Updated ${audienceRole} email governance.` });
      await loadPageData(false);
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to update the global email switch.' });
    } finally {
      setSavingKey(null);
    }
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
      await loadPageData(false);
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
    <div className="max-w-7xl space-y-6">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AdminPageHeader
        title="Notifications"
        description="Govern learner, expert, and admin notification delivery from one operational surface: global switches, per-event policy, delivery health, test email, and audit visibility."
        actions={(
          <Button type="button" variant="outline" onClick={() => void loadPageData(false)} loading={reloading} className="gap-2">
            <RefreshCw className="h-4 w-4" />
            Refresh
          </Button>
        )}
      />

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => void loadPageData(true)}
        emptyContent={(
          <EmptyState
            icon={<Bell className="h-10 w-10 text-slate-400" />}
            title="No notification governance data is available yet"
            description="Reload after the first notification events and policy rows have been generated."
          />
        )}
      >
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          <AdminMetricCard label="Queued Events" value={health?.queuedEvents ?? 0} icon={<Bell className="h-5 w-5" />} />
          <AdminMetricCard label="Failed Deliveries (24h)" value={health?.failedDeliveriesLast24Hours ?? 0} icon={<Siren className="h-5 w-5" />} tone={(health?.failedDeliveriesLast24Hours ?? 0) > 0 ? 'danger' : 'default'} />
          <AdminMetricCard label="Unread Inbox Items" value={health?.unreadInboxItems ?? 0} icon={<Mail className="h-5 w-5" />} />
          <AdminMetricCard label="Active Push Subscriptions" value={health?.activePushSubscriptions ?? 0} icon={<Smartphone className="h-5 w-5" />} tone={(health?.activePushSubscriptions ?? 0) > 0 ? 'success' : 'default'} />
        </div>

        <AdminSectionPanel title="Global Email Governance" description="These switches suppress email only. In-app delivery remains active even when a role-wide email switch is off.">
          <div className="grid gap-3 md:grid-cols-3">
            {(['learner', 'expert', 'admin'] as NotificationAudienceRole[]).map((audienceRole) => (
              <div key={audienceRole} className="rounded-2xl border border-slate-200 bg-white p-4">
                <div className="space-y-2">
                  <div className="flex items-center justify-between gap-3">
                    <div>
                      <p className="text-sm font-semibold capitalize text-slate-900">{audienceRole} email</p>
                      <p className="text-xs text-slate-500">Role-wide email enable/disable.</p>
                    </div>
                    <Badge variant={globalEmailEnabledByAudience[audienceRole] ? 'success' : 'warning'}>
                      {globalEmailEnabledByAudience[audienceRole] ? 'Enabled' : 'Suppressed'}
                    </Badge>
                  </div>
                  <Button
                    type="button"
                    variant={globalEmailEnabledByAudience[audienceRole] ? 'destructive' : 'primary'}
                    onClick={() => void handleToggleGlobalEmail(audienceRole)}
                    loading={savingKey === policyKey(audienceRole, GLOBAL_POLICY_EVENT_KEY)}
                    className="w-full"
                  >
                    {globalEmailEnabledByAudience[audienceRole] ? 'Disable Email' : 'Enable Email'}
                  </Button>
                </div>
              </div>
            ))}
          </div>
        </AdminSectionPanel>

        {(['learner', 'expert', 'admin'] as NotificationAudienceRole[]).map((audienceRole) => (
          <AdminSectionPanel
            key={audienceRole}
            title={`${audienceRole.charAt(0).toUpperCase()}${audienceRole.slice(1)} Policy Matrix`}
            description={`Per-event channel policy for ${audienceRole} notifications. Email modes are off, immediate, or daily digest.`}
          >
            <DataTable
              columns={policyColumns}
              data={policies.filter((row) => row.audienceRole === audienceRole)}
              keyExtractor={(row) => policyKey(row.audienceRole, row.eventKey)}
            />
          </AdminSectionPanel>
        ))}

        <div className="grid gap-6 xl:grid-cols-2">
          <AdminSectionPanel title="Email Preview & Test Send" description="Email rendering stays server-owned in this rollout. Use the catalog preview below and send a real test email without editing HTML.">
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
            {catalog.find((entry) => entry.eventKey === testEventKey && entry.audienceRole === testAudienceRole) ? (
              <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4 text-sm text-slate-600">
                <p className="font-semibold text-slate-900">
                  {catalog.find((entry) => entry.eventKey === testEventKey && entry.audienceRole === testAudienceRole)?.label}
                </p>
                <p className="mt-1">
                  {catalog.find((entry) => entry.eventKey === testEventKey && entry.audienceRole === testAudienceRole)?.description}
                </p>
                <div className="mt-3 flex flex-wrap gap-2">
                  <Badge variant="info">Severity {catalog.find((entry) => entry.eventKey === testEventKey && entry.audienceRole === testAudienceRole)?.defaultSeverity}</Badge>
                  <Badge variant="muted">Email mode {catalog.find((entry) => entry.eventKey === testEventKey && entry.audienceRole === testAudienceRole)?.defaultEmailMode}</Badge>
                </div>
              </div>
            ) : null}
            <div className="flex justify-end">
              <Button type="button" onClick={() => void handleSendTestEmail()} loading={sendingTestEmail} className="gap-2">
                <Send className="h-4 w-4" />
                Send Test Email
              </Button>
            </div>
          </AdminSectionPanel>

          <AdminSectionPanel title="Delivery Health" description="Recent delivery attempts and failed queue entries stay visible here so admins can react before notifications silently degrade.">
            {health?.channels?.length ? (
              <div className="grid gap-3 sm:grid-cols-3">
                {health.channels.map((channel) => (
                  <div key={channel.channel} className="rounded-2xl border border-slate-200 bg-white p-4 text-sm">
                    <p className="font-semibold capitalize text-slate-900">{channel.channel}</p>
                    <p className="mt-2 text-slate-500">Sent {channel.sentLast24Hours}</p>
                    <p className="text-slate-500">Failed {channel.failedLast24Hours}</p>
                    <p className="text-slate-500">Suppressed {channel.suppressedLast24Hours}</p>
                  </div>
                ))}
              </div>
            ) : (
              <p className="text-sm text-slate-500">No channel telemetry has been recorded yet.</p>
            )}
          </AdminSectionPanel>
        </div>

        <div className="grid gap-6 xl:grid-cols-2">
          <AdminSectionPanel title="Failure Queue" description="Manual visibility into delivery failures and threshold-triggered alert candidates.">
            {health?.failureQueue?.length ? (
              <DataTable columns={failureQueueColumns} data={health.failureQueue} keyExtractor={(row) => `${row.eventId}:${row.channel}:${row.attemptedAt}`} />
            ) : (
              <p className="text-sm text-slate-500">No failed or expired deliveries are currently queued for investigation.</p>
            )}
          </AdminSectionPanel>

          <AdminSectionPanel title="Recent Deliveries" description="Latest delivery attempts across in-app, email, and push channels.">
            {deliveries.length ? (
              <DataTable columns={deliveryColumns} data={deliveries} keyExtractor={(row) => row.id} />
            ) : (
              <p className="text-sm text-slate-500">No delivery attempts have been recorded yet.</p>
            )}
          </AdminSectionPanel>
        </div>

        <AdminSectionPanel title="Policy Change Audit Trail" description="Every policy update is written into the existing admin audit stream. Manual test sends also land in audit logs for traceability.">
          {auditRows.length ? (
            <DataTable
              columns={[
                {
                  key: 'timestamp',
                  header: 'Timestamp',
                  render: (row) => <span className="text-sm text-slate-500">{new Date(row.timestamp).toLocaleString()}</span>,
                },
                {
                  key: 'actor',
                  header: 'Actor',
                  render: (row) => <span className="font-medium text-slate-900">{row.actor}</span>,
                },
                {
                  key: 'resource',
                  header: 'Resource',
                  render: (row) => <span className="font-mono text-xs text-slate-500">{row.resource}</span>,
                },
                {
                  key: 'details',
                  header: 'Details',
                  render: (row) => <span className="text-sm text-slate-500">{row.details}</span>,
                },
              ]}
              data={auditRows}
              keyExtractor={(row) => row.id}
            />
          ) : (
            <p className="text-sm text-slate-500">No notification policy audit rows have been recorded yet.</p>
          )}
        </AdminSectionPanel>
      </AsyncStateWrapper>
    </div>
  );
}
