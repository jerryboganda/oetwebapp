'use client';

import { useCallback, useEffect, useState } from 'react';
import type { ColumnDef } from '@tanstack/react-table';
import { Plus, Save, Trash2 } from 'lucide-react';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { DataTable } from '@/components/admin/ui/data-table';
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/admin/ui/dialog';
import { Input } from '@/components/admin/ui/input';
import { Label } from '@/components/admin/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/admin/ui/select';
import { Switch } from '@/components/admin/ui/switch';
import { Textarea } from '@/components/admin/ui/textarea';
import { toast } from '@/components/admin/ui/toaster';
import { InlineAlert } from '@/components/ui/alert';
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

const CHANNELS = ['email', 'sms', 'whatsapp', 'inapp'];

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
  const [editing, setEditing] = useState<typeof EMPTY | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows(await apiClient.get<BillingNotificationTemplateDto[]>('/v1/admin/billing/notification-templates'));
      setError(null);
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
      toast.success('Template saved.');
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
      toast.success('Deleted.');
      await load();
    } catch (err: any) {
      toast.error(err?.userMessage ?? err?.message ?? 'Delete failed.');
    }
  }

  function update<K extends keyof typeof EMPTY>(key: K, value: (typeof EMPTY)[K]) {
    if (!editing) return;
    setEditing({ ...editing, [key]: value });
  }

  const columns: ColumnDef<BillingNotificationTemplateDto>[] = [
    { id: 'code', accessorKey: 'code', header: 'Event code' },
    { id: 'channel', accessorKey: 'channel', header: 'Channel' },
    { id: 'locale', accessorKey: 'localeTag', header: 'Locale' },
    {
      id: 'version',
      header: 'v',
      cell: ({ row }) => `v${row.original.version}`,
    },
    {
      id: 'active',
      header: 'Active',
      cell: ({ row }) => (
        <Badge variant={row.original.isActive ? 'success' : 'default'}>
          {row.original.isActive ? 'on' : 'off'}
        </Badge>
      ),
    },
    {
      id: 'actions',
      header: '',
      enableSorting: false,
      enableHiding: false,
      cell: ({ row }) => {
        const t = row.original;
        return (
          <div className="flex gap-1">
            <Button
              variant="ghost"
              size="sm"
              onClick={() =>
                setEditing({
                  code: t.code,
                  channel: t.channel,
                  localeTag: t.localeTag,
                  subject: t.subject ?? '',
                  bodyTemplate: t.bodyTemplate,
                  variablesJson: t.variablesJson,
                  isActive: t.isActive,
                })
              }
            >
              Edit
            </Button>
            <Button variant="ghost" size="sm" onClick={() => handleDelete(t.id)} aria-label="Delete">
              <Trash2 className="h-4 w-4" />
            </Button>
          </div>
        );
      },
    },
  ];

  return (
    <AdminTableLayout
      title="Notification templates"
      description="Email / SMS / WhatsApp templates rendered for billing-domain events."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Billing', href: '/admin/billing' },
        { label: 'Notification templates' },
      ]}
      actions={
        <Button onClick={() => setEditing({ ...EMPTY })} startIcon={<Plus className="h-4 w-4" />}>
          New template
        </Button>
      }
      banner={error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
    >
      <DataTable
        columns={columns}
        data={rows}
        loading={loading}
        emptyMessage="No templates yet."
        searchPlaceholder="Search templates…"
      />

      <Dialog open={editing !== null} onOpenChange={(open) => !open && setEditing(null)}>
        <DialogContent size="lg">
          <DialogHeader>
            <DialogTitle>Edit notification template</DialogTitle>
          </DialogHeader>
          {editing && (
            <div className="space-y-3">
              <div className="grid grid-cols-3 gap-3">
                <Input
                  label="Event code"
                  value={editing.code}
                  onChange={(e) => update('code', e.target.value)}
                  placeholder="payment_failed"
                />
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="tmpl-channel">Channel</Label>
                  <Select value={editing.channel} onValueChange={(v) => update('channel', v)}>
                    <SelectTrigger id="tmpl-channel">
                      <SelectValue placeholder="Channel" />
                    </SelectTrigger>
                    <SelectContent>
                      {CHANNELS.map((c) => (
                        <SelectItem key={c} value={c}>
                          {c}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <Input
                  label="Locale"
                  value={editing.localeTag}
                  onChange={(e) => update('localeTag', e.target.value)}
                  maxLength={5}
                />
              </div>
              <Input
                label="Subject"
                value={editing.subject}
                onChange={(e) => update('subject', e.target.value)}
              />
              <Textarea
                label="Body template"
                value={editing.bodyTemplate}
                onChange={(e) => update('bodyTemplate', e.target.value)}
                placeholder="Body — use {{variableName}} for substitutions"
                rows={6}
              />
              <Input
                label="Variables JSON"
                value={editing.variablesJson}
                onChange={(e) => update('variablesJson', e.target.value)}
                placeholder='["amount","currency"]'
              />
              <div className="flex items-center gap-2">
                <Switch
                  id="tmpl-active"
                  checked={editing.isActive}
                  onCheckedChange={(c) => update('isActive', c)}
                />
                <Label htmlFor="tmpl-active">Active</Label>
              </div>
            </div>
          )}
          <DialogFooter>
            <Button variant="ghost" onClick={() => setEditing(null)}>
              Cancel
            </Button>
            <Button onClick={handleSave} startIcon={<Save className="h-4 w-4" />}>
              Save
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </AdminTableLayout>
  );
}
