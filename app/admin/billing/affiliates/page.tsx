'use client';

import { useCallback, useEffect, useState } from 'react';
import { Plus, Save } from 'lucide-react';
import { AdminRouteWorkspace, AdminRoutePanel, AdminRouteSectionHeader } from '@/components/domain/admin-route-surface';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { Badge } from '@/components/ui/badge';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';
import {
  listAffiliates,
  createAffiliate,
  updateAffiliate,
  type AffiliateDto,
  type AffiliateUpsertRequest,
} from '@/lib/api';

const STATUS_OPTIONS = ['active', 'paused', 'terminated'].map((s) => ({ value: s, label: s }));

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
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [editing, setEditing] = useState<{ id: string | null; form: AffiliateUpsertRequest } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows(await listAffiliates());
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
      setToast({ variant: 'success', message: 'Affiliate saved.' });
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

  const columns: Column<AffiliateDto>[] = [
    { key: 'code', header: 'Code', render: (r) => r.code },
    { key: 'owner', header: 'Owner', render: (r) => r.ownerName },
    { key: 'commission', header: 'Commission', render: (r) => `${r.commissionPercent}%` },
    { key: 'cookie', header: 'Cookie', render: (r) => `${r.cookieDays}d` },
    { key: 'payout', header: 'Payout', render: (r) => `${r.payoutThresholdAmount} ${r.payoutCurrency}` },
    { key: 'status', header: 'Status', render: (r) => <Badge variant={r.status === 'active' ? 'success' : 'default'}>{r.status}</Badge> },
    {
      key: 'actions',
      header: '',
      render: (r) => (
        <Button variant="ghost" size="sm" onClick={() => setEditing({ id: r.id, form: {
          code: r.code,
          ownerName: r.ownerName,
          contactEmail: r.contactEmail,
          commissionPercent: r.commissionPercent,
          cookieDays: r.cookieDays,
          payoutThresholdAmount: r.payoutThresholdAmount,
          payoutCurrency: r.payoutCurrency,
          payoutMethod: r.payoutMethod,
          status: r.status,
        } })}>
          Edit
        </Button>
      ),
    },
  ];

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader title="Affiliates" description="Agent / institute partners with referral codes, commission rates, and payout schedules." />

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <AdminRoutePanel>
        {error && <InlineAlert variant="error">{error}</InlineAlert>}
        <div className="mb-4 flex justify-end">
          <Button onClick={() => setEditing({ id: null, form: { ...EMPTY } })}>
            <Plus className="mr-2 h-4 w-4" />
            New affiliate
          </Button>
        </div>
        {loading ? (
          <p className="text-sm text-muted-foreground">Loading…</p>
        ) : (
          <DataTable
            data={rows}
            columns={columns}
            keyExtractor={(r) => r.id}
            emptyMessage="No affiliates yet."
          />
        )}
      </AdminRoutePanel>

      {editing && (
        <Modal open onClose={() => setEditing(null)} title={editing.id ? 'Edit affiliate' : 'New affiliate'}>
          <div className="space-y-3 p-4">
            <div className="grid grid-cols-2 gap-3">
              <Input label="Code" value={editing.form.code} onChange={(e) => updateForm('code', e.target.value)} disabled={editing.id !== null} />
              <Input label="Owner name" value={editing.form.ownerName} onChange={(e) => updateForm('ownerName', e.target.value)} />
              <Input label="Contact email" type="email" value={editing.form.contactEmail} onChange={(e) => updateForm('contactEmail', e.target.value)} />
              <Input label="Commission %" type="number" step="0.1" value={editing.form.commissionPercent} onChange={(e) => updateForm('commissionPercent', Number(e.target.value))} />
              <Input label="Cookie days" type="number" value={editing.form.cookieDays ?? 30} onChange={(e) => updateForm('cookieDays', Number(e.target.value))} />
              <Input label="Payout threshold" type="number" value={editing.form.payoutThresholdAmount} onChange={(e) => updateForm('payoutThresholdAmount', Number(e.target.value))} />
              <Input label="Payout currency" value={editing.form.payoutCurrency ?? 'USD'} onChange={(e) => updateForm('payoutCurrency', e.target.value.toUpperCase())} maxLength={3} />
              <Select label="Status" value={editing.form.status ?? 'active'} options={STATUS_OPTIONS} onChange={(e) => updateForm('status', e.target.value)} />
            </div>
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
