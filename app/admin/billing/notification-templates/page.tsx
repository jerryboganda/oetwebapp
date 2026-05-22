'use client';

import { useCallback, useEffect, useState } from 'react';
import { Plus, Save, Trash2 } from 'lucide-react';
import { AdminRouteWorkspace, AdminRoutePanel, AdminRouteSectionHeader } from '@/components/domain/admin-route-surface';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Button } from '@/components/ui/button';
import { Input, Select, Checkbox, Textarea } from '@/components/ui/form-controls';
import { Badge } from '@/components/ui/badge';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';
import { apiClient } from '@/lib/api';

interface BillingNotificationTemplateDto {
  id: string;
  code: string;
  channel: string;
  localeTag: string;
  subject: string | null;
  bodyTemplate: string;
  variablesJson: string;
  version: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

const CHANNELS = ['email', 'sms', 'whatsapp', 'inapp'].map((c) => ({ value: c, label: c }));

const EMPTY = {
  code: '',
  channel: 'email',
  localeTag: 'en',
  subject: '',
  bodyTemplate: '',
  variablesJson: '[]',
  isActive: true,
};

export default function AdminNotificationTemplatesPage() {
  const [rows, setRows] = useState<BillingNotificationTemplateDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [editing, setEditing] = useState<typeof EMPTY | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows(await apiClient.get<BillingNotificationTemplateDto[]>('/v1/admin/billing/notification-templates'));
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to load.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function handleSave() {
    if (!editing) return;
    if (!editing.code.trim() || !editing.bodyTemplate.trim()) {
      setError('Code and body template are required.');
      return;
    }
    try {
      await apiClient.post('/v1/admin/billing/notification-templates', editing);
      setToast({ variant: 'success', message: 'Template saved.' });
      setEditing(null);
      await load();
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Save failed.');
    }
  }

  async function handleDelete(id: string) {
    if (!confirm('Delete this template?')) return;
    try {
      await apiClient.delete(`/v1/admin/billing/notification-templates/${id}`);
      setToast({ variant: 'success', message: 'Deleted.' });
      await load();
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.userMessage ?? err?.message ?? 'Delete failed.' });
    }
  }

  function update<K extends keyof typeof EMPTY>(key: K, value: (typeof EMPTY)[K]) {
    if (!editing) return;
    setEditing({ ...editing, [key]: value });
  }

  const columns: Column<BillingNotificationTemplateDto>[] = [
    { key: 'code', header: 'Event code', render: (t) => t.code },
    { key: 'channel', header: 'Channel', render: (t) => t.channel },
    { key: 'locale', header: 'Locale', render: (t) => t.localeTag },
    { key: 'version', header: 'v', render: (t) => `v${t.version}` },
    { key: 'active', header: 'Active', render: (t) => <Badge variant={t.isActive ? 'success' : 'muted'}>{t.isActive ? 'on' : 'off'}</Badge> },
    {
      key: 'actions',
      header: '',
      render: (t) => (
        <div className="flex gap-1">
          <Button variant="ghost" size="sm" onClick={() => setEditing({
            code: t.code,
            channel: t.channel,
            localeTag: t.localeTag,
            subject: t.subject ?? '',
            bodyTemplate: t.bodyTemplate,
            variablesJson: t.variablesJson,
            isActive: t.isActive,
          })}>Edit</Button>
          <Button variant="ghost" size="sm" onClick={() => handleDelete(t.id)}>
            <Trash2 className="h-4 w-4" />
          </Button>
        </div>
      ),
    },
  ];

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader title="Notification templates" description="Email / SMS / WhatsApp templates rendered for billing-domain events." />

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <AdminRoutePanel>
        {error && <InlineAlert variant="error">{error}</InlineAlert>}
        <div className="mb-4 flex justify-end">
          <Button onClick={() => setEditing({ ...EMPTY })}>
            <Plus className="mr-2 h-4 w-4" />
            New template
          </Button>
        </div>
        {loading ? (
          <p className="text-sm text-muted-foreground">Loading…</p>
        ) : (
          <DataTable data={rows} columns={columns} keyExtractor={(r) => r.id} emptyMessage="No templates yet." />
        )}
      </AdminRoutePanel>

      {editing && (
        <Modal open onClose={() => setEditing(null)} title="Edit notification template" size="lg">
          <div className="space-y-3 p-4">
            <div className="grid grid-cols-3 gap-3">
              <Input label="Event code" value={editing.code} onChange={(e) => update('code', e.target.value)} placeholder="payment_failed" />
              <Select label="Channel" value={editing.channel} options={CHANNELS} onChange={(e) => update('channel', e.target.value)} />
              <Input label="Locale" value={editing.localeTag} onChange={(e) => update('localeTag', e.target.value)} maxLength={5} />
            </div>
            <Input label="Subject" value={editing.subject} onChange={(e) => update('subject', e.target.value)} />
            <Textarea
              value={editing.bodyTemplate}
              onChange={(e) => update('bodyTemplate', e.target.value)}
              placeholder="Body — use {{variableName}} for substitutions"
            />
            <Input label="Variables JSON" value={editing.variablesJson} onChange={(e) => update('variablesJson', e.target.value)} placeholder='["amount","currency"]' />
            <Checkbox label="Active" checked={editing.isActive} onChange={(e) => update('isActive', e.target.checked)} />
            <div className="flex justify-end gap-2">
              <Button variant="ghost" onClick={() => setEditing(null)}>Cancel</Button>
              <Button onClick={handleSave}>
                <Save className="mr-2 h-4 w-4" />
                Save
              </Button>
            </div>
          </div>
        </Modal>
      )}
    </AdminRouteWorkspace>
  );
}
