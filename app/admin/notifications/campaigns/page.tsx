'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Calendar,
  CheckCircle2,
  Clock,
  Edit3,
  Eye,
  Loader2,
  Mail,
  Megaphone,
  Pause,
  Play,
  Plus,
  Search,
  Send,
  XCircle,
} from 'lucide-react';
import { AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { KpiStrip } from '@/components/admin/layout/admin-operations-layout';
import { TableSkeleton } from '@/components/admin/ui/skeleton';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Input, Select } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { Toast } from '@/components/ui/alert';
import { apiClient } from '@/lib/api';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

type CampaignStatus = 'draft' | 'scheduled' | 'sending' | 'sent' | 'cancelled' | 'failed';
type CampaignChannel = 'inApp' | 'email' | 'push';

interface Campaign {
  id: string;
  name: string;
  subject: string;
  body: string;
  channel: CampaignChannel;
  status: CampaignStatus;
  segmentJson: string;
  scheduledAt: string | null;
  sentAt: string | null;
  recipientCount: number;
  abVariant: string | null;
  createdAt: string;
  updatedAt: string;
}

interface CampaignFormState {
  id: string | null;
  name: string;
  subject: string;
  body: string;
  channel: CampaignChannel;
  segmentJson: string;
  scheduledAt: string;
  abVariant: string;
}

const defaultFormState: CampaignFormState = {
  id: null,
  name: '',
  subject: '',
  body: '',
  channel: 'email',
  segmentJson: '{}',
  scheduledAt: '',
  abVariant: '',
};

const STATUS_VARIANT: Record<CampaignStatus, string> = {
  draft: 'default',
  scheduled: 'info',
  sending: 'warning',
  sent: 'success',
  cancelled: 'danger',
  failed: 'danger',
};

const CHANNEL_ICON: Record<CampaignChannel, typeof Mail> = {
  inApp: Megaphone,
  email: Mail,
  push: Send,
};

export default function AdminCampaignsPage() {
  const { isAuthenticated, isLoading: authLoading } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [campaigns, setCampaigns] = useState<Campaign[]>([]);
  const [filter, setFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState<'' | CampaignStatus>('');
  const [toast, setToast] = useState<ToastState>(null);

  // Form modal
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [form, setForm] = useState<CampaignFormState>(defaultFormState);
  const [isSaving, setIsSaving] = useState(false);

  // Action loading
  const [actionLoadingId, setActionLoadingId] = useState<string | null>(null);

  const loadCampaigns = useCallback(async () => {
    setPageStatus('loading');
    try {
      const data = await apiClient.get<{ items: Campaign[] }>(
        '/v1/admin/notification-campaigns'
      );
      setCampaigns(data.items ?? []);
      setPageStatus(data.items?.length ? 'success' : 'empty');
    } catch {
      setPageStatus('error');
      setToast({ variant: 'error', message: 'Failed to load campaigns.' });
    }
  }, []);

  useEffect(() => {
    if (!authLoading && isAuthenticated) {
      loadCampaigns();
    }
  }, [authLoading, isAuthenticated, loadCampaigns]);

  const filtered = useMemo(() => {
    let items = campaigns;
    if (statusFilter) {
      items = items.filter((c) => c.status === statusFilter);
    }
    if (filter) {
      const q = filter.toLowerCase();
      items = items.filter(
        (c) =>
          c.name.toLowerCase().includes(q) ||
          c.subject.toLowerCase().includes(q)
      );
    }
    return items;
  }, [campaigns, filter, statusFilter]);

  const metrics = useMemo(() => {
    const draft = campaigns.filter((c) => c.status === 'draft').length;
    const scheduled = campaigns.filter((c) => c.status === 'scheduled').length;
    const sent = campaigns.filter((c) => c.status === 'sent').length;
    const totalRecipients = campaigns.reduce((sum, c) => sum + c.recipientCount, 0);
    return { total: campaigns.length, draft, scheduled, sent, totalRecipients };
  }, [campaigns]);

  // --- Form handlers ---

  const openCreateForm = () => {
    setForm(defaultFormState);
    setIsFormOpen(true);
  };

  const openEditForm = (campaign: Campaign) => {
    setForm({
      id: campaign.id,
      name: campaign.name,
      subject: campaign.subject,
      body: campaign.body,
      channel: campaign.channel,
      segmentJson: campaign.segmentJson,
      scheduledAt: campaign.scheduledAt ?? '',
      abVariant: campaign.abVariant ?? '',
    });
    setIsFormOpen(true);
  };

  const handleSave = async () => {
    setIsSaving(true);
    try {
      if (form.id) {
        await apiClient.put(`/v1/admin/notification-campaigns/${form.id}`, {
          name: form.name,
          subject: form.subject,
          body: form.body,
          channel: form.channel,
          segmentJson: form.segmentJson,
          scheduledAt: form.scheduledAt || null,
          abVariant: form.abVariant || null,
        });
        setToast({ variant: 'success', message: 'Campaign updated.' });
      } else {
        await apiClient.post('/v1/admin/notification-campaigns', {
          name: form.name,
          subject: form.subject,
          body: form.body,
          channel: form.channel,
          segmentJson: form.segmentJson,
          scheduledAt: form.scheduledAt || null,
          abVariant: form.abVariant || null,
        });
        setToast({ variant: 'success', message: 'Campaign created.' });
      }
      setIsFormOpen(false);
      loadCampaigns();
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.userMessage ?? 'Failed to save campaign.' });
    } finally {
      setIsSaving(false);
    }
  };

  // --- Campaign actions ---

  const handleAction = async (
    campaignId: string,
    action: 'test-send' | 'approve' | 'schedule' | 'pause' | 'cancel'
  ) => {
    setActionLoadingId(campaignId);
    try {
      await apiClient.post(`/v1/admin/notification-campaigns/${campaignId}/${action}`);
      setToast({ variant: 'success', message: `Campaign ${action} succeeded.` });
      loadCampaigns();
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.userMessage ?? `Action ${action} failed.` });
    } finally {
      setActionLoadingId(null);
    }
  };

  // --- Table columns ---

  const columns: Column<Campaign>[] = [
    {
      key: 'name',
      header: 'Campaign',
      render: (campaign) => {
        const ChannelIcon = CHANNEL_ICON[campaign.channel];
        return (
          <div className="flex items-center gap-3">
            <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-admin bg-[var(--admin-primary-tint)] text-[var(--admin-primary)]">
              <ChannelIcon className="h-4 w-4" />
            </div>
            <div className="min-w-0">
              <p className="truncate font-semibold text-admin-fg-strong">{campaign.name}</p>
              <p className="mt-0.5 truncate text-xs text-admin-fg-muted">{campaign.subject}</p>
            </div>
          </div>
        );
      },
    },
    {
      key: 'channel',
      header: 'Channel',
      render: (campaign) => (
        <Badge variant="default">{campaign.channel}</Badge>
      ),
      hideOnMobile: true,
    },
    {
      key: 'status',
      header: 'Status',
      render: (campaign) => (
        <Badge variant={STATUS_VARIANT[campaign.status] as any}>
          {campaign.status}
        </Badge>
      ),
    },
    {
      key: 'recipients',
      header: 'Recipients',
      render: (campaign) => (
        <span className="font-semibold tabular-nums text-admin-fg-strong">
          {campaign.recipientCount.toLocaleString()}
        </span>
      ),
      hideOnMobile: true,
    },
    {
      key: 'schedule',
      header: 'Scheduled',
      render: (campaign) =>
        campaign.scheduledAt ? (
          <span className="text-sm tabular-nums text-admin-fg-muted">
            {new Date(campaign.scheduledAt).toLocaleString()}
          </span>
        ) : (
          <span className="text-xs text-admin-fg-muted">-</span>
        ),
      hideOnMobile: true,
    },
    {
      key: 'actions',
      header: '',
      render: (campaign) => {
        const isLoading = actionLoadingId === campaign.id;
        return (
          <div className="flex items-center gap-1">
            {campaign.status === 'draft' && (
              <>
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => openEditForm(campaign)}
                  disabled={isLoading}
                >
                  <Edit3 className="h-3.5 w-3.5" />
                </Button>
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => handleAction(campaign.id, 'test-send')}
                  disabled={isLoading}
                >
                  <Eye className="h-3.5 w-3.5" />
                </Button>
                <Button
                  variant="primary"
                  size="sm"
                  onClick={() => handleAction(campaign.id, 'approve')}
                  disabled={isLoading}
                >
                  {isLoading ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <CheckCircle2 className="h-3.5 w-3.5" />}
                </Button>
              </>
            )}
            {campaign.status === 'scheduled' && (
              <>
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => handleAction(campaign.id, 'pause')}
                  disabled={isLoading}
                >
                  <Pause className="h-3.5 w-3.5" />
                </Button>
                <Button
                  variant="danger"
                  size="sm"
                  onClick={() => handleAction(campaign.id, 'cancel')}
                  disabled={isLoading}
                >
                  <XCircle className="h-3.5 w-3.5" />
                </Button>
              </>
            )}
            {campaign.status === 'sending' && (
              <Button
                variant="danger"
                size="sm"
                onClick={() => handleAction(campaign.id, 'cancel')}
                disabled={isLoading}
              >
                <XCircle className="h-3.5 w-3.5" />
              </Button>
            )}
          </div>
        );
      },
    },
  ];

  return (
    <AdminRouteWorkspace>
      <AdminTableLayout
        title="Notification Campaigns"
        description="Create, schedule, and manage bulk notification campaigns."
        eyebrow="Notifications"
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Notifications', href: '/admin/notifications' },
          { label: 'Campaigns' },
        ]}
        actions={
          <Button variant="primary" onClick={openCreateForm}>
            <Plus className="h-4 w-4" />
            New Campaign
          </Button>
        }
        banner={
          <div className="space-y-4">
            <KpiStrip>
              <KpiTile label="Total" value={metrics.total} />
              <KpiTile label="Drafts" value={metrics.draft} />
              <KpiTile label="Scheduled" value={metrics.scheduled} />
              <KpiTile label="Sent" value={metrics.sent} />
              <KpiTile label="Recipients" value={metrics.totalRecipients.toLocaleString()} />
            </KpiStrip>

            <div className="flex flex-col gap-3 rounded-admin-lg border border-admin-border bg-admin-bg-surface p-3 shadow-admin-sm sm:flex-row sm:items-center sm:justify-between">
              <div className="flex items-center gap-2">
                <Select
                  value={statusFilter}
                  onChange={(e) => setStatusFilter(e.target.value as '' | CampaignStatus)}
                >
                  <option value="">All statuses</option>
                  <option value="draft">Draft</option>
                  <option value="scheduled">Scheduled</option>
                  <option value="sending">Sending</option>
                  <option value="sent">Sent</option>
                  <option value="cancelled">Cancelled</option>
                  <option value="failed">Failed</option>
                </Select>
              </div>
              <div className="relative w-full min-w-[240px] sm:w-80">
                <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-admin-fg-muted" />
                <Input
                  placeholder="Search campaigns…"
                  value={filter}
                  onChange={(e) => setFilter(e.target.value)}
                  className="pl-9"
                />
              </div>
            </div>
          </div>
        }
      >
        {pageStatus === 'loading' ? (
          <TableSkeleton rows={6} columns={6} />
        ) : pageStatus === 'error' ? (
          <EmptyState
            variant="error"
            illustration={<Megaphone className="h-10 w-10" />}
            title="Could not load campaigns"
            description="Check your connection and try again."
          />
        ) : pageStatus === 'empty' ? (
          <EmptyState
            variant="empty"
            illustration={<Megaphone className="h-10 w-10" />}
            title="No campaigns yet"
            description="Create your first notification campaign to reach learners."
          />
        ) : (
          <DataTable
            columns={columns}
            data={filtered}
            keyExtractor={(c) => c.id}
            emptyMessage="No campaigns match the current filters."
            aria-label="Notification campaigns"
          />
        )}
      </AdminTableLayout>

      {/* Campaign Form Modal */}
      {isFormOpen && (
        <Modal
          open
          onClose={() => setIsFormOpen(false)}
          title={form.id ? 'Edit Campaign' : 'Create Campaign'}
        >
          <div className="space-y-4 p-4">
            <div className="space-y-2">
              <label className="text-sm font-medium text-admin-fg-strong">Name</label>
              <Input
                placeholder="Campaign name"
                value={form.name}
                onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
              />
            </div>

            <div className="space-y-2">
              <label className="text-sm font-medium text-admin-fg-strong">Subject</label>
              <Input
                placeholder="Notification subject line"
                value={form.subject}
                onChange={(e) => setForm((f) => ({ ...f, subject: e.target.value }))}
              />
            </div>

            <div className="space-y-2">
              <label className="text-sm font-medium text-admin-fg-strong">Body</label>
              <textarea
                className="w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-default placeholder:text-admin-fg-muted focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
                rows={5}
                placeholder="Notification body content…"
                value={form.body}
                onChange={(e) => setForm((f) => ({ ...f, body: e.target.value }))}
              />
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <label className="text-sm font-medium text-admin-fg-strong">Channel</label>
                <Select
                  value={form.channel}
                  onChange={(e) =>
                    setForm((f) => ({ ...f, channel: e.target.value as CampaignChannel }))
                  }
                >
                  <option value="email">Email</option>
                  <option value="inApp">In-App</option>
                  <option value="push">Push</option>
                </Select>
              </div>

              <div className="space-y-2">
                <label className="text-sm font-medium text-admin-fg-strong">Schedule</label>
                <Input
                  type="datetime-local"
                  value={form.scheduledAt}
                  onChange={(e) => setForm((f) => ({ ...f, scheduledAt: e.target.value }))}
                />
              </div>
            </div>

            <div className="space-y-2">
              <label className="text-sm font-medium text-admin-fg-strong">
                Segment (JSON)
              </label>
              <textarea
                className="w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 font-mono text-sm text-admin-fg-default placeholder:text-admin-fg-muted focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
                rows={3}
                placeholder='{"role": "learner", "plan": "pro"}'
                value={form.segmentJson}
                onChange={(e) => setForm((f) => ({ ...f, segmentJson: e.target.value }))}
              />
            </div>

            <div className="space-y-2">
              <label className="text-sm font-medium text-admin-fg-strong">
                A/B Variant (optional)
              </label>
              <Input
                placeholder="e.g. variant-b-shorter-subject"
                value={form.abVariant}
                onChange={(e) => setForm((f) => ({ ...f, abVariant: e.target.value }))}
              />
            </div>

            <div className="flex justify-end gap-2 pt-2">
              <Button variant="secondary" onClick={() => setIsFormOpen(false)}>
                Cancel
              </Button>
              <Button
                variant="primary"
                onClick={handleSave}
                disabled={isSaving || !form.name.trim() || !form.subject.trim()}
              >
                {isSaving && <Loader2 className="h-4 w-4 animate-spin" />}
                {form.id ? 'Update' : 'Create'}
              </Button>
            </div>
          </div>
        </Modal>
      )}

      {toast && (
        <Toast variant={toast.variant} onClose={() => setToast(null)}>
          {toast.message}
        </Toast>
      )}
    </AdminRouteWorkspace>
  );
}
