'use client';

import { useCallback, useEffect, useState } from 'react';
import type { ColumnDef } from '@tanstack/react-table';
import { Plus, Save } from 'lucide-react';

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
import { toast } from '@/components/admin/ui/toaster';
import { InlineAlert } from '@/components/ui/alert';
import {
  listAffiliates,
  createAffiliate,
  updateAffiliate,
  type AffiliateDto,
  type AffiliateUpsertRequest,
} from '@/lib/api';

const STATUS_OPTIONS = ['active', 'paused', 'terminated'];

const EMPTY: AffiliateUpsertRequest = {
  code: '',
  ownerName: '',
  contactEmail: '',
  commissionPercent: 15,
  cookieDays: 30,
  payoutThresholdAmount: 100,
  payoutCurrency: 'USD',
  payoutMethod: 'bank_transfer',
  status: 'active',
};

export default function AdminAffiliatesPage() {
  const [rows, setRows] = useState<AffiliateDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<{ id: string | null; form: AffiliateUpsertRequest } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows(await listAffiliates());
      setError(null);
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to load affiliates.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function handleSave() {
    if (!editing) return;
    if (!editing.form.code.trim() || !editing.form.ownerName.trim()) {
      setError('Code and owner name are required.');
      return;
    }
    try {
      if (editing.id) {
        await updateAffiliate(editing.id, editing.form);
      } else {
        await createAffiliate(editing.form);
      }
      toast.success('Affiliate saved.');
      setEditing(null);
      await load();
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Save failed.');
    }
  }

  function updateForm<K extends keyof AffiliateUpsertRequest>(key: K, value: AffiliateUpsertRequest[K]) {
    if (!editing) return;
    setEditing({ ...editing, form: { ...editing.form, [key]: value } });
  }

  const columns: ColumnDef<AffiliateDto>[] = [
    { id: 'code', accessorKey: 'code', header: 'Code' },
    { id: 'owner', accessorKey: 'ownerName', header: 'Owner' },
    { id: 'commission', header: 'Commission', cell: ({ row }) => `${row.original.commissionPercent}%` },
    { id: 'cookie', header: 'Cookie', cell: ({ row }) => `${row.original.cookieDays}d` },
    {
      id: 'payout',
      header: 'Payout',
      cell: ({ row }) => `${row.original.payoutThresholdAmount} ${row.original.payoutCurrency}`,
    },
    {
      id: 'status',
      header: 'Status',
      cell: ({ row }) => (
        <Badge variant={row.original.status === 'active' ? 'success' : 'default'}>
          {row.original.status}
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
          <Button
            variant="ghost"
            size="sm"
            onClick={() =>
              setEditing({
                id: r.id,
                form: {
                  code: r.code,
                  ownerName: r.ownerName,
                  contactEmail: r.contactEmail,
                  commissionPercent: r.commissionPercent,
                  cookieDays: r.cookieDays,
                  payoutThresholdAmount: r.payoutThresholdAmount,
                  payoutCurrency: r.payoutCurrency,
                  payoutMethod: r.payoutMethod,
                  status: r.status,
                },
              })
            }
          >
            Edit
          </Button>
        );
      },
    },
  ];

  return (
    <AdminTableLayout
      title="Affiliates"
      description="Agent / institute partners with referral codes, commission rates, and payout schedules."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Billing', href: '/admin/billing' },
        { label: 'Affiliates' },
      ]}
      actions={
        <Button onClick={() => setEditing({ id: null, form: { ...EMPTY } })} startIcon={<Plus className="h-4 w-4" />}>
          New affiliate
        </Button>
      }
      banner={error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
    >
      <DataTable
        columns={columns}
        data={rows}
        loading={loading}
        emptyMessage="No affiliates yet."
        searchPlaceholder="Search affiliates…"
      />

      <Dialog open={editing !== null} onOpenChange={(open) => !open && setEditing(null)}>
        <DialogContent size="lg">
          <DialogHeader>
            <DialogTitle>{editing?.id ? 'Edit affiliate' : 'New affiliate'}</DialogTitle>
          </DialogHeader>
          {editing && (
            <div className="space-y-3">
              <div className="grid grid-cols-2 gap-3">
                <Input
                  label="Code"
                  value={editing.form.code}
                  onChange={(e) => updateForm('code', e.target.value)}
                  disabled={editing.id !== null}
                />
                <Input
                  label="Owner name"
                  value={editing.form.ownerName}
                  onChange={(e) => updateForm('ownerName', e.target.value)}
                />
                <Input
                  label="Contact email"
                  type="email"
                  value={editing.form.contactEmail}
                  onChange={(e) => updateForm('contactEmail', e.target.value)}
                />
                <Input
                  label="Commission %"
                  type="number"
                  step="0.1"
                  value={editing.form.commissionPercent}
                  onChange={(e) => updateForm('commissionPercent', Number(e.target.value))}
                />
                <Input
                  label="Cookie days"
                  type="number"
                  value={editing.form.cookieDays ?? 30}
                  onChange={(e) => updateForm('cookieDays', Number(e.target.value))}
                />
                <Input
                  label="Payout threshold"
                  type="number"
                  value={editing.form.payoutThresholdAmount}
                  onChange={(e) => updateForm('payoutThresholdAmount', Number(e.target.value))}
                />
                <Input
                  label="Payout currency"
                  value={editing.form.payoutCurrency ?? 'USD'}
                  onChange={(e) => updateForm('payoutCurrency', e.target.value.toUpperCase())}
                  maxLength={3}
                />
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="affiliate-status">Status</Label>
                  <Select
                    value={editing.form.status ?? 'active'}
                    onValueChange={(v) => updateForm('status', v)}
                  >
                    <SelectTrigger id="affiliate-status">
                      <SelectValue placeholder="Status" />
                    </SelectTrigger>
                    <SelectContent>
                      {STATUS_OPTIONS.map((s) => (
                        <SelectItem key={s} value={s}>
                          {s}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
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
