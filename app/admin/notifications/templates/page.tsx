'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  CheckCircle2,
  Code2,
  Copy,
  Edit3,
  Eye,
  FileText,
  Loader2,
  Mail,
  Plus,
  Power,
  PowerOff,
  Search,
  Send,
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

type TemplateChannel = 'inApp' | 'email' | 'push';
type TemplateStatus = 'active' | 'inactive';

interface NotificationTemplate {
  id: string;
  name: string;
  eventKey: string;
  channel: TemplateChannel;
  locale: string;
  version: number;
  status: TemplateStatus;
  subjectTemplate: string;
  bodyTemplate: string;
  textTemplate: string;
  htmlTemplate: string;
  variables: string[];
  createdAt: string;
  updatedAt: string;
}

interface TemplateFormState {
  id: string | null;
  name: string;
  eventKey: string;
  channel: TemplateChannel;
  locale: string;
  subjectTemplate: string;
  bodyTemplate: string;
  textTemplate: string;
  htmlTemplate: string;
}

const defaultFormState: TemplateFormState = {
  id: null,
  name: '',
  eventKey: '',
  channel: 'email',
  locale: 'en',
  subjectTemplate: '',
  bodyTemplate: '',
  textTemplate: '',
  htmlTemplate: '',
};

export default function AdminNotificationTemplatesPage() {
  const { isAuthenticated, isLoading: authLoading } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [templates, setTemplates] = useState<NotificationTemplate[]>([]);
  const [filter, setFilter] = useState('');
  const [channelFilter, setChannelFilter] = useState<'' | TemplateChannel>('');
  const [statusFilter, setStatusFilter] = useState<'' | TemplateStatus>('');
  const [toast, setToast] = useState<ToastState>(null);

  // Form modal
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [form, setForm] = useState<TemplateFormState>(defaultFormState);
  const [isSaving, setIsSaving] = useState(false);

  // Preview modal
  const [isPreviewOpen, setIsPreviewOpen] = useState(false);
  const [previewTemplate, setPreviewTemplate] = useState<NotificationTemplate | null>(null);
  const [previewVariables, setPreviewVariables] = useState<Record<string, string>>({});

  // Action loading
  const [actionLoadingId, setActionLoadingId] = useState<string | null>(null);

  const loadTemplates = useCallback(async () => {
    setPageStatus('loading');
    try {
      const data = await apiClient.get<{ items: NotificationTemplate[] }>(
        '/v1/admin/notification-templates'
      );
      setTemplates(data.items ?? []);
      setPageStatus(data.items?.length ? 'success' : 'empty');
    } catch {
      setPageStatus('error');
      setToast({ variant: 'error', message: 'Failed to load templates.' });
    }
  }, []);

  useEffect(() => {
    if (!authLoading && isAuthenticated) {
      loadTemplates();
    }
  }, [authLoading, isAuthenticated, loadTemplates]);

  const filtered = useMemo(() => {
    let items = templates;
    if (channelFilter) {
      items = items.filter((t) => t.channel === channelFilter);
    }
    if (statusFilter) {
      items = items.filter((t) => t.status === statusFilter);
    }
    if (filter) {
      const q = filter.toLowerCase();
      items = items.filter(
        (t) =>
          t.name.toLowerCase().includes(q) ||
          t.eventKey.toLowerCase().includes(q)
      );
    }
    return items;
  }, [templates, filter, channelFilter, statusFilter]);

  const metrics = useMemo(() => {
    const active = templates.filter((t) => t.status === 'active').length;
    const inactive = templates.filter((t) => t.status === 'inactive').length;
    const emailCount = templates.filter((t) => t.channel === 'email').length;
    const pushCount = templates.filter((t) => t.channel === 'push').length;
    return { total: templates.length, active, inactive, emailCount, pushCount };
  }, [templates]);

  // --- Form handlers ---

  const openCreateForm = () => {
    setForm(defaultFormState);
    setIsFormOpen(true);
  };

  const openEditForm = (template: NotificationTemplate) => {
    setForm({
      id: template.id,
      name: template.name,
      eventKey: template.eventKey,
      channel: template.channel,
      locale: template.locale,
      subjectTemplate: template.subjectTemplate,
      bodyTemplate: template.bodyTemplate,
      textTemplate: template.textTemplate,
      htmlTemplate: template.htmlTemplate,
    });
    setIsFormOpen(true);
  };

  const handleSave = async () => {
    setIsSaving(true);
    try {
      if (form.id) {
        await apiClient.put(`/v1/admin/notification-templates/${form.id}`, {
          name: form.name,
          eventKey: form.eventKey,
          channel: form.channel,
          locale: form.locale,
          subjectTemplate: form.subjectTemplate,
          bodyTemplate: form.bodyTemplate,
          textTemplate: form.textTemplate,
          htmlTemplate: form.htmlTemplate,
        });
        setToast({ variant: 'success', message: 'Template updated.' });
      } else {
        await apiClient.post('/v1/admin/notification-templates', {
          name: form.name,
          eventKey: form.eventKey,
          channel: form.channel,
          locale: form.locale,
          subjectTemplate: form.subjectTemplate,
          bodyTemplate: form.bodyTemplate,
          textTemplate: form.textTemplate,
          htmlTemplate: form.htmlTemplate,
        });
        setToast({ variant: 'success', message: 'Template created.' });
      }
      setIsFormOpen(false);
      loadTemplates();
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.userMessage ?? 'Failed to save template.' });
    } finally {
      setIsSaving(false);
    }
  };

  // --- Toggle active/inactive ---

  const handleToggleStatus = async (template: NotificationTemplate) => {
    setActionLoadingId(template.id);
    const newStatus = template.status === 'active' ? 'inactive' : 'active';
    try {
      await apiClient.patch(`/v1/admin/notification-templates/${template.id}/status`, {
        status: newStatus,
      });
      setToast({
        variant: 'success',
        message: `Template ${newStatus === 'active' ? 'activated' : 'deactivated'}.`,
      });
      loadTemplates();
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.userMessage ?? 'Toggle failed.' });
    } finally {
      setActionLoadingId(null);
    }
  };

  // --- Test send ---

  const handleTestSend = async (templateId: string) => {
    setActionLoadingId(templateId);
    try {
      await apiClient.post(`/v1/admin/notification-templates/${templateId}/test-send`);
      setToast({ variant: 'success', message: 'Test notification sent.' });
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.userMessage ?? 'Test send failed.' });
    } finally {
      setActionLoadingId(null);
    }
  };

  // --- Preview ---

  const openPreview = (template: NotificationTemplate) => {
    setPreviewTemplate(template);
    const vars: Record<string, string> = {};
    template.variables.forEach((v) => {
      vars[v] = `{{${v}}}`;
    });
    setPreviewVariables(vars);
    setIsPreviewOpen(true);
  };

  const renderPreview = (template: string) => {
    let result = template;
    Object.entries(previewVariables).forEach(([key, value]) => {
      result = result.replaceAll(`{{${key}}}`, value);
    });
    return result;
  };

  // --- Table columns ---

  const columns: Column<NotificationTemplate>[] = [
    {
      key: 'name',
      header: 'Template',
      render: (template) => (
        <div className="min-w-0">
          <p className="truncate font-semibold text-admin-fg-strong">{template.name}</p>
          <p className="mt-0.5 truncate font-mono text-xs text-admin-fg-muted">
            {template.eventKey}
          </p>
        </div>
      ),
    },
    {
      key: 'channel',
      header: 'Channel',
      render: (template) => (
        <Badge variant="default">{template.channel}</Badge>
      ),
    },
    {
      key: 'locale',
      header: 'Locale',
      render: (template) => (
        <span className="text-sm text-admin-fg-default">{template.locale}</span>
      ),
      hideOnMobile: true,
    },
    {
      key: 'version',
      header: 'Version',
      render: (template) => (
        <span className="font-mono text-sm tabular-nums text-admin-fg-muted">
          v{template.version}
        </span>
      ),
      hideOnMobile: true,
    },
    {
      key: 'status',
      header: 'Status',
      render: (template) => (
        <Badge variant={template.status === 'active' ? 'success' : 'warning'}>
          {template.status}
        </Badge>
      ),
    },
    {
      key: 'updated',
      header: 'Updated',
      render: (template) => (
        <span className="text-sm tabular-nums text-admin-fg-muted">
          {new Date(template.updatedAt).toLocaleDateString()}
        </span>
      ),
      hideOnMobile: true,
    },
    {
      key: 'actions',
      header: '',
      render: (template) => {
        const isLoading = actionLoadingId === template.id;
        return (
          <div className="flex items-center gap-1">
            <Button
              variant="secondary"
              size="sm"
              onClick={() => openEditForm(template)}
              disabled={isLoading}
              title="Edit"
            >
              <Edit3 className="h-3.5 w-3.5" />
            </Button>
            <Button
              variant="secondary"
              size="sm"
              onClick={() => openPreview(template)}
              disabled={isLoading}
              title="Preview"
            >
              <Eye className="h-3.5 w-3.5" />
            </Button>
            <Button
              variant="secondary"
              size="sm"
              onClick={() => handleTestSend(template.id)}
              disabled={isLoading}
              title="Test send"
            >
              {isLoading ? (
                <Loader2 className="h-3.5 w-3.5 animate-spin" />
              ) : (
                <Send className="h-3.5 w-3.5" />
              )}
            </Button>
            <Button
              variant={template.status === 'active' ? 'destructive' : 'primary'}
              size="sm"
              onClick={() => handleToggleStatus(template)}
              disabled={isLoading}
              title={template.status === 'active' ? 'Deactivate' : 'Activate'}
            >
              {template.status === 'active' ? (
                <PowerOff className="h-3.5 w-3.5" />
              ) : (
                <Power className="h-3.5 w-3.5" />
              )}
            </Button>
          </div>
        );
      },
    },
  ];

  return (
    <AdminRouteWorkspace>
      <AdminTableLayout
        title="Notification Templates"
        description="Manage notification templates for all channels and event types."
        eyebrow="Notifications"
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Notifications', href: '/admin/notifications' },
          { label: 'Templates' },
        ]}
        actions={
          <Button variant="primary" onClick={openCreateForm}>
            <Plus className="h-4 w-4" />
            New Template
          </Button>
        }
        banner={
          <div className="space-y-4">
            <KpiStrip>
              <KpiTile label="Total" value={metrics.total} />
              <KpiTile label="Active" value={metrics.active} />
              <KpiTile label="Inactive" value={metrics.inactive} />
              <KpiTile label="Email" value={metrics.emailCount} />
              <KpiTile label="Push" value={metrics.pushCount} />
            </KpiStrip>

            <div className="flex flex-col gap-3 rounded-admin-lg border border-admin-border bg-admin-bg-surface p-3 shadow-admin-sm sm:flex-row sm:items-center sm:justify-between">
              <div className="flex items-center gap-2">
                <Select
                  value={channelFilter}
                  onChange={(e) => setChannelFilter(e.target.value as '' | TemplateChannel)}
                  options={[
                    { value: '', label: 'All channels' },
                    { value: 'email', label: 'Email' },
                    { value: 'inApp', label: 'In-App' },
                    { value: 'push', label: 'Push' },
                  ]}
                />
                <Select
                  value={statusFilter}
                  onChange={(e) => setStatusFilter(e.target.value as '' | TemplateStatus)}
                  options={[
                    { value: '', label: 'All statuses' },
                    { value: 'active', label: 'Active' },
                    { value: 'inactive', label: 'Inactive' },
                  ]}
                />
              </div>
              <div className="relative w-full min-w-[240px] sm:w-80">
                <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-admin-fg-muted" />
                <Input
                  placeholder="Search by name or event key…"
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
          <TableSkeleton rows={6} columns={7} />
        ) : pageStatus === 'error' ? (
          <EmptyState
            variant="error"
            illustration={<FileText className="h-10 w-10" />}
            title="Could not load templates"
            description="Check your connection and try again."
          />
        ) : pageStatus === 'empty' ? (
          <EmptyState
            variant="default"
            illustration={<FileText className="h-10 w-10" />}
            title="No templates yet"
            description="Create your first notification template."
          />
        ) : (
          <DataTable
            columns={columns}
            data={filtered}
            keyExtractor={(t) => t.id}
            emptyMessage="No templates match the current filters."
            aria-label="Notification templates"
          />
        )}
      </AdminTableLayout>

      {/* Create/Edit Form Modal */}
      {isFormOpen && (
        <Modal
          open
          onClose={() => setIsFormOpen(false)}
          title={form.id ? 'Edit Template' : 'Create Template'}
        >
          <div className="max-h-[70vh] space-y-4 overflow-y-auto p-4">
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <label className="text-sm font-medium text-admin-fg-strong">Name</label>
                <Input
                  placeholder="Template name"
                  value={form.name}
                  onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
                />
              </div>
              <div className="space-y-2">
                <label className="text-sm font-medium text-admin-fg-strong">Event Key</label>
                <Input
                  placeholder="e.g. user.welcome, exam.reminder"
                  value={form.eventKey}
                  onChange={(e) => setForm((f) => ({ ...f, eventKey: e.target.value }))}
                />
              </div>
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <label className="text-sm font-medium text-admin-fg-strong">Channel</label>
                <Select
                  value={form.channel}
                  onChange={(e) =>
                    setForm((f) => ({ ...f, channel: e.target.value as TemplateChannel }))
                  }
                  options={[
                    { value: 'email', label: 'Email' },
                    { value: 'inApp', label: 'In-App' },
                    { value: 'push', label: 'Push' },
                  ]}
                />
              </div>
              <div className="space-y-2">
                <label className="text-sm font-medium text-admin-fg-strong">Locale</label>
                <Input
                  placeholder="e.g. en, ar, fr"
                  value={form.locale}
                  onChange={(e) => setForm((f) => ({ ...f, locale: e.target.value }))}
                />
              </div>
            </div>

            <div className="space-y-2">
              <label className="text-sm font-medium text-admin-fg-strong">Subject Template</label>
              <Input
                placeholder="Welcome to OET, {{firstName}}!"
                value={form.subjectTemplate}
                onChange={(e) => setForm((f) => ({ ...f, subjectTemplate: e.target.value }))}
              />
            </div>

            <div className="space-y-2">
              <label className="text-sm font-medium text-admin-fg-strong">Body Template</label>
              <textarea
                className="w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-default placeholder:text-admin-fg-muted focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
                rows={4}
                placeholder="Hi {{firstName}}, your exam is in {{daysUntilExam}} days…"
                value={form.bodyTemplate}
                onChange={(e) => setForm((f) => ({ ...f, bodyTemplate: e.target.value }))}
              />
            </div>

            <div className="space-y-2">
              <label className="text-sm font-medium text-admin-fg-strong">
                Text Template (plain text fallback)
              </label>
              <textarea
                className="w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 font-mono text-sm text-admin-fg-default placeholder:text-admin-fg-muted focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
                rows={3}
                placeholder="Plain text version of the notification…"
                value={form.textTemplate}
                onChange={(e) => setForm((f) => ({ ...f, textTemplate: e.target.value }))}
              />
            </div>

            <div className="space-y-2">
              <label className="text-sm font-medium text-admin-fg-strong">
                HTML Template (email only)
              </label>
              <textarea
                className="w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 font-mono text-sm text-admin-fg-default placeholder:text-admin-fg-muted focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
                rows={5}
                placeholder="<html><body>{{bodyContent}}</body></html>"
                value={form.htmlTemplate}
                onChange={(e) => setForm((f) => ({ ...f, htmlTemplate: e.target.value }))}
              />
            </div>

            <div className="flex justify-end gap-2 pt-2">
              <Button variant="secondary" onClick={() => setIsFormOpen(false)}>
                Cancel
              </Button>
              <Button
                variant="primary"
                onClick={handleSave}
                disabled={isSaving || !form.name.trim() || !form.eventKey.trim()}
              >
                {isSaving && <Loader2 className="h-4 w-4 animate-spin" />}
                {form.id ? 'Update' : 'Create'}
              </Button>
            </div>
          </div>
        </Modal>
      )}

      {/* Preview Modal */}
      {isPreviewOpen && previewTemplate && (
        <Modal
          open
          onClose={() => setIsPreviewOpen(false)}
          title={`Preview: ${previewTemplate.name}`}
        >
          <div className="max-h-[70vh] space-y-4 overflow-y-auto p-4">
            {/* Variables input */}
            {previewTemplate.variables.length > 0 && (
              <div className="space-y-2">
                <p className="text-sm font-medium text-admin-fg-strong">
                  Sample Variables
                </p>
                <div className="grid grid-cols-2 gap-2">
                  {previewTemplate.variables.map((variable) => (
                    <div key={variable} className="space-y-1">
                      <label className="text-xs text-admin-fg-muted">{variable}</label>
                      <Input
                        value={previewVariables[variable] ?? ''}
                        onChange={(e) =>
                          setPreviewVariables((prev) => ({
                            ...prev,
                            [variable]: e.target.value,
                          }))
                        }
                        placeholder={variable}
                      />
                    </div>
                  ))}
                </div>
              </div>
            )}

            {/* Rendered preview */}
            <div className="space-y-3">
              <div className="space-y-1">
                <p className="text-xs font-medium uppercase text-admin-fg-muted">Subject</p>
                <p className="rounded-admin border border-admin-border bg-admin-bg-subtle p-2 text-sm text-admin-fg-strong">
                  {renderPreview(previewTemplate.subjectTemplate)}
                </p>
              </div>

              <div className="space-y-1">
                <p className="text-xs font-medium uppercase text-admin-fg-muted">Body</p>
                <div className="rounded-admin border border-admin-border bg-admin-bg-subtle p-2 text-sm text-admin-fg-default">
                  {renderPreview(previewTemplate.bodyTemplate)}
                </div>
              </div>

              {previewTemplate.htmlTemplate && (
                <div className="space-y-1">
                  <p className="text-xs font-medium uppercase text-admin-fg-muted">
                    HTML Preview
                  </p>
                  <div
                    className="rounded-admin border border-admin-border bg-white p-3 text-sm"
                    dangerouslySetInnerHTML={{
                      __html: renderPreview(previewTemplate.htmlTemplate),
                    }}
                  />
                </div>
              )}

              {/* Version history */}
              <div className="space-y-1 border-t border-admin-border pt-3">
                <p className="text-xs font-medium uppercase text-admin-fg-muted">Version</p>
                <p className="text-sm text-admin-fg-default">
                  v{previewTemplate.version} · Created{' '}
                  {new Date(previewTemplate.createdAt).toLocaleDateString()} · Updated{' '}
                  {new Date(previewTemplate.updatedAt).toLocaleDateString()}
                </p>
              </div>
            </div>

            <div className="flex justify-end gap-2 pt-2">
              <Button variant="secondary" onClick={() => setIsPreviewOpen(false)}>
                Close
              </Button>
              <Button
                variant="primary"
                onClick={() => handleTestSend(previewTemplate.id)}
                disabled={actionLoadingId === previewTemplate.id}
              >
                {actionLoadingId === previewTemplate.id ? (
                  <Loader2 className="h-4 w-4 animate-spin" />
                ) : (
                  <Send className="h-4 w-4" />
                )}
                Test Send
              </Button>
            </div>
          </div>
        </Modal>
      )}

      {toast && (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      )}
    </AdminRouteWorkspace>
  );
}
