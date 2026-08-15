'use client';

import { useCallback, useEffect, useState } from 'react';
import type { ColumnDef } from '@tanstack/react-table';
import { Plug, Plus, Save, Star, Trash2 } from 'lucide-react';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { DataTable } from '@/components/admin/ui/data-table';
import {
  Dialog,
  DialogContent,
  DialogDescription,
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
import { toast } from '@/components/admin/ui/toaster';
import { InlineAlert } from '@/components/ui/alert';
import {
  createStripeAccount,
  deleteStripeAccount,
  listStripeAccounts,
  setDefaultStripeAccount,
  testStripeAccountConnection,
  updateStripeAccount,
  type StripeAccountProfileDto,
} from '@/lib/api';

interface EditorState {
  /** null = creating a new account. */
  id: string | null;
  label: string;
  mode: string;
  publishableKey: string;
  /** Blank on edit keeps the stored key (rotate-only semantics). */
  secretKey: string;
  webhookSecret: string;
  webhookSecretTouched: boolean;
  routingCountriesCsv: string;
  isActive: boolean;
  isDefault: boolean;
  hasSecretKey: boolean;
  hasWebhookSecret: boolean;
}

const EMPTY: EditorState = {
  id: null,
  label: '',
  mode: 'live',
  publishableKey: '',
  secretKey: '',
  webhookSecret: '',
  webhookSecretTouched: false,
  routingCountriesCsv: '',
  isActive: true,
  isDefault: false,
  hasSecretKey: false,
  hasWebhookSecret: false,
};

export default function AdminStripeAccountsPage() {
  const [rows, setRows] = useState<StripeAccountProfileDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<EditorState | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows(await listStripeAccounts());
      setError(null);
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to load.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function handleSave(testAfterSave = false) {
    if (!editing) return;
    if (!editing.label.trim()) {
      setError('Label is required.');
      return;
    }
    if (editing.id === null && !editing.secretKey.trim()) {
      setError('Secret key is required for a new account.');
      return;
    }
    try {
      const payload = {
        label: editing.label.trim(),
        mode: editing.mode,
        publishableKey: editing.publishableKey.trim() || null,
        secretKey: editing.secretKey.trim() || null,
        // null = keep stored webhook secret; '' = clear it.
        webhookSecret: editing.webhookSecretTouched ? editing.webhookSecret.trim() : null,
        routingCountriesCsv: editing.routingCountriesCsv.trim() || null,
        isActive: editing.isActive,
        isDefault: editing.isDefault,
      };
      let savedId = editing.id;
      if (editing.id === null) {
        const created = await createStripeAccount(payload);
        savedId = created.id;
      } else {
        await updateStripeAccount(editing.id, payload);
      }
      if (testAfterSave && savedId) {
        try {
          const tested = await testStripeAccountConnection(savedId);
          if (tested.lastTestResult === 'ok') {
            toast.success(`Stripe account saved and verified (${tested.stripeAccountId ?? 'Connection OK'}).`);
          } else {
            toast.warning(`Saved, but connection test failed: ${tested.lastTestResult ?? 'Check credentials'}.`);
          }
        } catch {
          toast.warning('Account saved, but connection test could not reach Stripe.');
        }
      } else {
        toast.success('Stripe account saved.');
      }
      setEditing(null);
      setError(null);
      await load();
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Save failed.');
    }
  }

  async function handleSetDefault(row: StripeAccountProfileDto) {
    if (!confirm(`Route all new Stripe checkouts and webhooks through "${row.label}"?`)) return;
    setBusyId(row.id);
    try {
      await setDefaultStripeAccount(row.id);
      toast.success(`"${row.label}" is now the default Stripe account.`);
      await load();
    } catch (err: any) {
      toast.error(err?.userMessage ?? err?.message ?? 'Failed to set default.');
    } finally {
      setBusyId(null);
    }
  }

  async function handleTest(row: StripeAccountProfileDto) {
    setBusyId(row.id);
    try {
      const updated = await testStripeAccountConnection(row.id);
      if (updated.lastTestResult === 'ok') {
        toast.success(`Connection OK — ${updated.stripeAccountId ?? 'account verified'}.`);
      } else {
        toast.error(updated.lastTestResult ?? 'Connection test failed.');
      }
      await load();
    } catch (err: any) {
      toast.error(err?.userMessage ?? err?.message ?? 'Connection test failed.');
    } finally {
      setBusyId(null);
    }
  }

  async function handleDelete(row: StripeAccountProfileDto) {
    if (!confirm(`Delete Stripe account "${row.label}"? This cannot be undone.`)) return;
    setBusyId(row.id);
    try {
      await deleteStripeAccount(row.id);
      toast.success('Deleted.');
      await load();
    } catch (err: any) {
      toast.error(err?.userMessage ?? err?.message ?? 'Delete failed.');
    } finally {
      setBusyId(null);
    }
  }

  function openEditor(row: StripeAccountProfileDto | null) {
    setError(null);
    setEditing(
      row === null
        ? { ...EMPTY }
        : {
            id: row.id,
            label: row.label,
            mode: row.mode === 'test' ? 'test' : 'live',
            publishableKey: row.publishableKey ?? '',
            secretKey: '',
            webhookSecret: '',
            webhookSecretTouched: false,
            routingCountriesCsv: row.routingCountriesCsv ?? '',
            isActive: row.isActive,
            isDefault: row.isDefault,
            hasSecretKey: row.hasSecretKey,
            hasWebhookSecret: row.hasWebhookSecret,
          },
    );
  }

  const columns: ColumnDef<StripeAccountProfileDto>[] = [
    {
      id: 'label',
      accessorKey: 'label',
      header: 'Label',
      cell: ({ row }) => (
        <div className="flex items-center gap-2">
          <span className="font-medium">{row.original.label}</span>
          {row.original.isDefault && <Badge variant="success">Default</Badge>}
        </div>
      ),
    },
    {
      id: 'mode',
      header: 'Mode',
      cell: ({ row }) => (
        <Badge variant={row.original.mode === 'live' ? 'info' : 'default'}>
          {row.original.mode === 'live' ? 'Live' : 'Test'}
        </Badge>
      ),
    },
    {
      id: 'secret',
      header: 'Secret key',
      cell: ({ row }) => (
        <span className="font-mono text-xs">{row.original.secretKeyHint ?? '—'}</span>
      ),
    },
    {
      id: 'account',
      header: 'Stripe account',
      cell: ({ row }) => (
        <span className="font-mono text-xs">{row.original.stripeAccountId ?? '—'}</span>
      ),
    },
    {
      id: 'webhook',
      header: 'Webhook secret',
      cell: ({ row }) => (
        <Badge variant={row.original.hasWebhookSecret ? 'success' : 'warning'}>
          {row.original.hasWebhookSecret ? 'Set' : 'Missing'}
        </Badge>
      ),
    },
    {
      id: 'active',
      header: 'Active',
      cell: ({ row }) => (
        <Badge variant={row.original.isActive ? 'success' : 'default'}>
          {row.original.isActive ? 'Yes' : 'No'}
        </Badge>
      ),
    },
    {
      id: 'lastTest',
      header: 'Last test',
      cell: ({ row }) => {
        const r = row.original;
        if (!r.lastTestedAt) return <span className="text-xs text-muted-foreground">Never</span>;
        const ok = r.lastTestResult === 'ok';
        return (
          <div className="flex flex-col gap-0.5">
            <Badge variant={ok ? 'success' : 'danger'}>{ok ? 'OK' : 'Failed'}</Badge>
            <span className="text-xs text-muted-foreground">
              {new Date(r.lastTestedAt).toLocaleString()}
            </span>
          </div>
        );
      },
    },
    {
      id: 'actions',
      header: '',
      enableSorting: false,
      enableHiding: false,
      cell: ({ row }) => {
        const r = row.original;
        const busy = busyId === r.id;
        return (
          <div className="flex gap-1">
            <Button variant="ghost" size="sm" onClick={() => openEditor(r)}>
              Edit
            </Button>
            <Button
              variant="ghost"
              size="sm"
              disabled={busy}
              onClick={() => handleTest(r)}
              startIcon={<Plug className="h-4 w-4" />}
            >
              Test
            </Button>
            {!r.isDefault && (
              <Button
                variant="ghost"
                size="sm"
                disabled={busy || !r.isActive}
                onClick={() => handleSetDefault(r)}
                startIcon={<Star className="h-4 w-4" />}
              >
                Set default
              </Button>
            )}
            {!r.isDefault && (
              <Button
                variant="ghost"
                size="sm"
                disabled={busy}
                onClick={() => handleDelete(r)}
                aria-label="Delete"
              >
                <Trash2 className="h-4 w-4" />
              </Button>
            )}
          </div>
        );
      },
    },
  ];

  function update<K extends keyof EditorState>(key: K, value: EditorState[K]) {
    if (!editing) return;
    setEditing({ ...editing, [key]: value });
  }

  return (
    <AdminTableLayout
      title="Stripe accounts"
      description="Manage multiple Stripe accounts. The default active account powers checkout and webhook verification — switch it here without redeploying."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Billing', href: '/admin/billing' },
        { label: 'Stripe accounts' },
      ]}
      actions={
        <Button onClick={() => openEditor(null)} startIcon={<Plus className="h-4 w-4" />}>
          New account
        </Button>
      }
      banner={error && editing === null ? <InlineAlert variant="error">{error}</InlineAlert> : null}
    >
      <DataTable
        columns={columns}
        data={rows}
        loading={loading}
        emptyMessage="No Stripe accounts yet — add one to route payments through it."
        searchPlaceholder="Search Stripe accounts…"
      />

      <Dialog open={editing !== null} onOpenChange={(open) => !open && setEditing(null)}>
        <DialogContent size="lg">
          <DialogHeader>
            <DialogTitle>{editing?.id ? 'Edit Stripe account' : 'New Stripe account'}</DialogTitle>
            <DialogDescription className="text-xs text-muted-foreground">
              Configure Stripe credential profile, test live connection, and manage routing.
            </DialogDescription>
          </DialogHeader>
          {editing && (
            <div className="space-y-3">
              {error && <InlineAlert variant="error">{error}</InlineAlert>}
              <div className="grid grid-cols-2 gap-3">
                <Input
                  label="Label"
                  value={editing.label}
                  onChange={(e) => update('label', e.target.value)}
                  placeholder="e.g. Main UK account"
                />
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="stripe-mode">Mode</Label>
                  <Select value={editing.mode} onValueChange={(v) => update('mode', v)}>
                    <SelectTrigger id="stripe-mode">
                      <SelectValue placeholder="Mode" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="live">Live</SelectItem>
                      <SelectItem value="test">Test</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
              </div>
              <Input
                label="Publishable key"
                value={editing.publishableKey}
                onChange={(e) => update('publishableKey', e.target.value)}
                placeholder="pk_live_…"
              />
              <Input
                label="Secret key"
                type="password"
                autoComplete="off"
                value={editing.secretKey}
                onChange={(e) => update('secretKey', e.target.value)}
                placeholder={
                  editing.id && editing.hasSecretKey
                    ? 'Configured - masked (enter new key to replace / rotate)'
                    : 'sk_live_…'
                }
              />
              <Input
                label="Webhook signing secret"
                type="password"
                autoComplete="off"
                value={editing.webhookSecret}
                onChange={(e) => {
                  if (!editing) return;
                  setEditing({
                    ...editing,
                    webhookSecret: e.target.value,
                    webhookSecretTouched: true,
                  });
                }}
                placeholder={
                  editing.id && editing.hasWebhookSecret
                    ? 'Configured - masked (enter new secret to replace / rotate)'
                    : 'whsec_…'
                }
              />
              <Input
                label="Routing countries (optional, CSV)"
                value={editing.routingCountriesCsv}
                onChange={(e) => update('routingCountriesCsv', e.target.value)}
                placeholder="e.g. GB,AE,SA — informational routing note"
              />
              <div className="flex items-center gap-6">
                <div className="flex items-center gap-2">
                  <Switch
                    id="stripe-active"
                    checked={editing.isActive}
                    onCheckedChange={(checked) => update('isActive', checked)}
                  />
                  <Label htmlFor="stripe-active">Active</Label>
                </div>
                <div className="flex items-center gap-2">
                  <Switch
                    id="stripe-default"
                    checked={editing.isDefault}
                    onCheckedChange={(checked) => update('isDefault', checked)}
                  />
                  <Label htmlFor="stripe-default">Use as default for checkout &amp; webhooks</Label>
                </div>
              </div>
              <p className="text-xs text-muted-foreground">
                Secrets are encrypted at rest and never shown again — only a masked hint is
                displayed. Use <strong>Save &amp; Test Connection</strong> to verify the key against
                Stripe and store the connected account identifier immediately.
              </p>
            </div>
          )}
          <DialogFooter className="flex flex-wrap items-center justify-between gap-2 sm:justify-between">
            <Button variant="ghost" onClick={() => setEditing(null)}>
              Cancel
            </Button>
            <div className="flex items-center gap-2">
              <Button variant="outline" onClick={() => void handleSave(false)} startIcon={<Save className="h-4 w-4" />}>
                Save
              </Button>
              <Button onClick={() => void handleSave(true)} startIcon={<Plug className="h-4 w-4" />}>
                Save &amp; Test Connection
              </Button>
            </div>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </AdminTableLayout>
  );
}
