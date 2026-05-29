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

interface BankAccountConfigDto {
  id: string;
  region: string;
  currency: string;
  bankName: string;
  accountHolderName: string;
  iban: string | null;
  swiftBic: string | null;
  accountNumber: string | null;
  routingOrSortCode: string | null;
  instructionsMarkdown: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

const REGION_OPTIONS = ['UK', 'GULF', 'EGYPT', 'PK', 'ROW'];

const EMPTY: Omit<BankAccountConfigDto, 'id' | 'createdAt' | 'updatedAt'> = {
  region: 'UK',
  currency: 'GBP',
  bankName: '',
  accountHolderName: '',
  iban: '',
  swiftBic: '',
  accountNumber: '',
  routingOrSortCode: '',
  instructionsMarkdown: '',
  isActive: true,
};

export default function AdminBankAccountsPage() {
  const [rows, setRows] = useState<BankAccountConfigDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<typeof EMPTY | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows(await apiClient.get<BankAccountConfigDto[]>('/v1/admin/billing/bank-accounts'));
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
    if (!editing.bankName.trim() || !editing.accountHolderName.trim()) {
      setError('Bank name and account holder are required.');
      return;
    }
    try {
      await apiClient.post('/v1/admin/billing/bank-accounts', editing);
      toast.success('Bank account saved.');
      setEditing(null);
      await load();
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Save failed.');
    }
  }

  async function handleDelete(id: string) {
    if (!confirm('Delete this bank account?')) return;
    try {
      await apiClient.delete(`/v1/admin/billing/bank-accounts/${id}`);
      toast.success('Deleted.');
      await load();
    } catch (err: any) {
      toast.error(err?.userMessage ?? err?.message ?? 'Delete failed.');
    }
  }

  const columns: ColumnDef<BankAccountConfigDto>[] = [
    { id: 'region', accessorKey: 'region', header: 'Region' },
    { id: 'currency', accessorKey: 'currency', header: 'Currency' },
    { id: 'bank', accessorKey: 'bankName', header: 'Bank' },
    { id: 'holder', accessorKey: 'accountHolderName', header: 'Holder' },
    {
      id: 'iban',
      header: 'IBAN',
      cell: ({ row }) => row.original.iban ?? '-',
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
              onClick={() =>
                setEditing({
                  region: r.region,
                  currency: r.currency,
                  bankName: r.bankName,
                  accountHolderName: r.accountHolderName,
                  iban: r.iban ?? '',
                  swiftBic: r.swiftBic ?? '',
                  accountNumber: r.accountNumber ?? '',
                  routingOrSortCode: r.routingOrSortCode ?? '',
                  instructionsMarkdown: r.instructionsMarkdown ?? '',
                  isActive: r.isActive,
                })
              }
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

  function update<K extends keyof typeof EMPTY>(key: K, value: (typeof EMPTY)[K]) {
    if (!editing) return;
    setEditing({ ...editing, [key]: value });
  }

  return (
    <AdminTableLayout
      title="Bank accounts"
      description="Bank-transfer destination accounts shown to learners during manual payment by region/currency."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Billing', href: '/admin/billing' },
        { label: 'Bank accounts' },
      ]}
      actions={
        <Button onClick={() => setEditing({ ...EMPTY })} startIcon={<Plus className="h-4 w-4" />}>
          New account
        </Button>
      }
      banner={error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
    >
      <DataTable
        columns={columns}
        data={rows}
        loading={loading}
        emptyMessage="No bank accounts yet."
        searchPlaceholder="Search bank accounts…"
      />

      <Dialog open={editing !== null} onOpenChange={(open) => !open && setEditing(null)}>
        <DialogContent size="lg">
          <DialogHeader>
            <DialogTitle>Edit bank account</DialogTitle>
          </DialogHeader>
          {editing && (
            <div className="space-y-3">
              <div className="grid grid-cols-2 gap-3">
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="bank-region">Region</Label>
                  <Select value={editing.region} onValueChange={(v) => update('region', v)}>
                    <SelectTrigger id="bank-region">
                      <SelectValue placeholder="Region" />
                    </SelectTrigger>
                    <SelectContent>
                      {REGION_OPTIONS.map((r) => (
                        <SelectItem key={r} value={r}>
                          {r}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <Input
                  label="Currency"
                  value={editing.currency}
                  onChange={(e) => update('currency', e.target.value.toUpperCase())}
                  maxLength={3}
                />
                <Input
                  label="Bank name"
                  value={editing.bankName}
                  onChange={(e) => update('bankName', e.target.value)}
                />
                <Input
                  label="Account holder"
                  value={editing.accountHolderName}
                  onChange={(e) => update('accountHolderName', e.target.value)}
                />
                <Input
                  label="IBAN"
                  value={editing.iban ?? ''}
                  onChange={(e) => update('iban', e.target.value)}
                />
                <Input
                  label="SWIFT/BIC"
                  value={editing.swiftBic ?? ''}
                  onChange={(e) => update('swiftBic', e.target.value)}
                />
                <Input
                  label="Account number"
                  value={editing.accountNumber ?? ''}
                  onChange={(e) => update('accountNumber', e.target.value)}
                />
                <Input
                  label="Routing / sort code"
                  value={editing.routingOrSortCode ?? ''}
                  onChange={(e) => update('routingOrSortCode', e.target.value)}
                />
              </div>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="bank-instructions">Instructions (Markdown, shown to learners)</Label>
                <Textarea
                  id="bank-instructions"
                  value={editing.instructionsMarkdown ?? ''}
                  onChange={(e) => update('instructionsMarkdown', e.target.value)}
                  rows={4}
                />
              </div>
              <div className="flex items-center gap-2">
                <Switch
                  id="bank-active"
                  checked={editing.isActive}
                  onCheckedChange={(checked) => update('isActive', checked)}
                />
                <Label htmlFor="bank-active">Active</Label>
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
