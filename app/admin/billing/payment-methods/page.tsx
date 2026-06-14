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
import {
  listAdminPaymentMethods,
  upsertPaymentMethod,
  deletePaymentMethod,
  uploadPaymentMethodQr,
  type PaymentMethodConfigDto,
  type PaymentMethodConfigUpsertRequest,
} from '@/lib/api';

const CATEGORY_OPTIONS: { value: string; label: string }[] = [
  { value: 'inside_egypt', label: 'Inside Egypt' },
  { value: 'international', label: 'International' },
];

type FormState = PaymentMethodConfigUpsertRequest;

const EMPTY: FormState = {
  key: '',
  label: '',
  category: 'inside_egypt',
  detail: '',
  meta: '',
  instructions: '',
  note: '',
  referenceRule: false,
  showQr: false,
  iconName: '',
  isActive: true,
  displayOrder: 0,
};

function readFileAsBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const result = reader.result;
      if (typeof result === 'string') {
        const commaIndex = result.indexOf(',');
        resolve(commaIndex >= 0 ? result.slice(commaIndex + 1) : result);
      } else {
        reject(new Error('Unsupported reader result type.'));
      }
    };
    reader.onerror = () => reject(reader.error ?? new Error('File read failed.'));
    reader.readAsDataURL(file);
  });
}

export default function AdminPaymentMethodsPage() {
  const [rows, setRows] = useState<PaymentMethodConfigDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<FormState | null>(null);
  const [qrFile, setQrFile] = useState<File | null>(null);
  const [saving, setSaving] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows(await listAdminPaymentMethods());
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
    if (!editing.key.trim() || !editing.label.trim()) {
      setError('Key and label are required.');
      return;
    }
    setSaving(true);
    try {
      await upsertPaymentMethod(editing);
      if (qrFile) {
        const base64 = await readFileAsBase64(qrFile);
        await uploadPaymentMethodQr(editing.key.trim(), base64);
      }
      toast.success('Payment method saved.');
      setEditing(null);
      setQrFile(null);
      setError(null);
      await load();
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Save failed.');
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete(id: string) {
    if (!confirm('Delete this payment method?')) return;
    try {
      await deletePaymentMethod(id);
      toast.success('Deleted.');
      await load();
    } catch (err: any) {
      toast.error(err?.userMessage ?? err?.message ?? 'Delete failed.');
    }
  }

  const columns: ColumnDef<PaymentMethodConfigDto>[] = [
    { id: 'order', accessorKey: 'displayOrder', header: '#' },
    { id: 'key', accessorKey: 'key', header: 'Key' },
    { id: 'label', accessorKey: 'label', header: 'Label' },
    {
      id: 'category',
      header: 'Category',
      cell: ({ row }) => row.original.category.replace('_', ' '),
    },
    {
      id: 'qr',
      header: 'QR',
      cell: ({ row }) =>
        row.original.showQr ? (
          <Badge variant={row.original.hasQrImage ? 'success' : 'default'}>
            {row.original.hasQrImage ? 'Uploaded' : 'None'}
          </Badge>
        ) : (
          '-'
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
      id: 'actions',
      header: '',
      enableSorting: false,
      enableHiding: false,
      cell: ({ row }) => {
        const r = row.original;
        return (
          <div className="flex gap-1">
            <Button
              variant="ghost"
              size="sm"
              onClick={() => {
                setQrFile(null);
                setError(null);
                setEditing({
                  key: r.key,
                  label: r.label,
                  category: r.category,
                  detail: r.detail,
                  meta: r.meta ?? '',
                  instructions: r.instructions,
                  note: r.note ?? '',
                  referenceRule: r.referenceRule,
                  showQr: r.showQr,
                  iconName: r.iconName ?? '',
                  isActive: r.isActive,
                  displayOrder: r.displayOrder,
                });
              }}
            >
              Edit
            </Button>
            <Button variant="ghost" size="sm" onClick={() => handleDelete(r.id)} aria-label="Delete">
              <Trash2 className="h-4 w-4" />
            </Button>
          </div>
        );
      },
    },
  ];

  function update<K extends keyof FormState>(key: K, value: FormState[K]) {
    if (!editing) return;
    setEditing({ ...editing, [key]: value });
  }

  return (
    <AdminTableLayout
      title="Payment methods"
      description="Manual payment methods shown to learners on the payment page (InstaPay, Vodafone, QNB, Stripe, PayPal, Monzo)."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Billing', href: '/admin/billing' },
        { label: 'Payment methods' },
      ]}
      actions={
        <Button
          onClick={() => {
            setQrFile(null);
            setError(null);
            setEditing({ ...EMPTY });
          }}
          startIcon={<Plus className="h-4 w-4" />}
        >
          New method
        </Button>
      }
      banner={error && !editing ? <InlineAlert variant="error">{error}</InlineAlert> : null}
    >
      <DataTable
        columns={columns}
        data={rows}
        loading={loading}
        emptyMessage="No payment methods yet."
        searchPlaceholder="Search payment methods…"
      />

      <Dialog open={editing !== null} onOpenChange={(open) => !open && setEditing(null)}>
        <DialogContent size="lg">
          <DialogHeader>
            <DialogTitle>Edit payment method</DialogTitle>
          </DialogHeader>
          {editing && (
            <div className="space-y-3">
              {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
              <div className="grid grid-cols-2 gap-3">
                <Input
                  label="Key (slug)"
                  value={editing.key}
                  onChange={(e) => update('key', e.target.value)}
                  placeholder="instapay_qr_link"
                />
                <Input
                  label="Label"
                  value={editing.label}
                  onChange={(e) => update('label', e.target.value)}
                />
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="pm-category">Category</Label>
                  <Select value={editing.category} onValueChange={(v) => update('category', v)}>
                    <SelectTrigger id="pm-category">
                      <SelectValue placeholder="Category" />
                    </SelectTrigger>
                    <SelectContent>
                      {CATEGORY_OPTIONS.map((c) => (
                        <SelectItem key={c.value} value={c.value}>
                          {c.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <Input
                  label="Display order"
                  type="number"
                  value={String(editing.displayOrder)}
                  onChange={(e) => update('displayOrder', Number(e.target.value) || 0)}
                />
                <Input
                  label="Detail line"
                  value={editing.detail}
                  onChange={(e) => update('detail', e.target.value)}
                />
                <Input
                  label="Meta line"
                  value={editing.meta ?? ''}
                  onChange={(e) => update('meta', e.target.value)}
                />
                <Input
                  label="Note / badge"
                  value={editing.note ?? ''}
                  onChange={(e) => update('note', e.target.value)}
                  placeholder="e.g. Inside Egypt only."
                />
                <Input
                  label="Icon name (Lucide)"
                  value={editing.iconName ?? ''}
                  onChange={(e) => update('iconName', e.target.value)}
                  placeholder="QrCode, Landmark, WalletCards, CreditCard"
                />
              </div>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="pm-instructions">Instructions (shown to learners)</Label>
                <Textarea
                  id="pm-instructions"
                  value={editing.instructions}
                  onChange={(e) => update('instructions', e.target.value)}
                  rows={3}
                />
              </div>
              <div className="flex flex-wrap items-center gap-6">
                <div className="flex items-center gap-2">
                  <Switch
                    id="pm-reference-rule"
                    checked={editing.referenceRule}
                    onCheckedChange={(checked) => update('referenceRule', checked)}
                  />
                  <Label htmlFor="pm-reference-rule">Reference = name + course</Label>
                </div>
                <div className="flex items-center gap-2">
                  <Switch
                    id="pm-show-qr"
                    checked={editing.showQr}
                    onCheckedChange={(checked) => update('showQr', checked)}
                  />
                  <Label htmlFor="pm-show-qr">Show QR</Label>
                </div>
                <div className="flex items-center gap-2">
                  <Switch
                    id="pm-active"
                    checked={editing.isActive}
                    onCheckedChange={(checked) => update('isActive', checked)}
                  />
                  <Label htmlFor="pm-active">Active</Label>
                </div>
              </div>
              {editing.showQr ? (
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="pm-qr-file">QR image (PNG/JPG, optional)</Label>
                  <input
                    id="pm-qr-file"
                    type="file"
                    accept="image/*"
                    onChange={(e) => setQrFile(e.target.files?.[0] ?? null)}
                    className="block w-full text-sm"
                  />
                </div>
              ) : null}
            </div>
          )}
          <DialogFooter>
            <Button variant="ghost" onClick={() => setEditing(null)}>
              Cancel
            </Button>
            <Button onClick={handleSave} disabled={saving} startIcon={<Save className="h-4 w-4" />}>
              {saving ? 'Saving…' : 'Save'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </AdminTableLayout>
  );
}
