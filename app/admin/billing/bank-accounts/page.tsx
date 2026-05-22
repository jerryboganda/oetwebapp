'use client';

import { useCallback, useEffect, useState } from 'react';
import { Plus, Save, Trash2 } from 'lucide-react';
import { AdminRouteWorkspace, AdminRoutePanel, AdminRouteSectionHeader } from '@/components/domain/admin-route-surface';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Button } from '@/components/ui/button';
import { Input, Select, Checkbox, Textarea } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';
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

const REGION_OPTIONS = ['UK', 'GULF', 'EGYPT', 'PK', 'ROW'].map((r) => ({ value: r, label: r }));

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
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [editing, setEditing] = useState<typeof EMPTY | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows(await apiClient.get<BankAccountConfigDto[]>('/v1/admin/billing/bank-accounts'));
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
      setToast({ variant: 'success', message: 'Bank account saved.' });
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
      setToast({ variant: 'success', message: 'Deleted.' });
      await load();
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.userMessage ?? err?.message ?? 'Delete failed.' });
    }
  }

  const columns: Column<BankAccountConfigDto>[] = [
    { key: 'region', header: 'Region', render: (r) => r.region },
    { key: 'currency', header: 'Currency', render: (r) => r.currency },
    { key: 'bank', header: 'Bank', render: (r) => r.bankName },
    { key: 'holder', header: 'Holder', render: (r) => r.accountHolderName },
    { key: 'iban', header: 'IBAN', render: (r) => r.iban ?? '—' },
    { key: 'active', header: 'Active', render: (r) => (r.isActive ? 'Yes' : 'No') },
    {
      key: 'actions',
      header: '',
      render: (r) => (
        <div className="flex gap-1">
          <Button variant="ghost" size="sm" onClick={() => setEditing({
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
          })}>
            Edit
          </Button>
          <Button variant="ghost" size="sm" onClick={() => handleDelete(r.id)}>
            <Trash2 className="h-4 w-4" />
          </Button>
        </div>
      ),
    },
  ];

  function update<K extends keyof typeof EMPTY>(key: K, value: (typeof EMPTY)[K]) {
    if (!editing) return;
    setEditing({ ...editing, [key]: value });
  }

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader title="Bank accounts" description="Bank-transfer destination accounts shown to learners during manual payment by region/currency." />

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <AdminRoutePanel>
        {error && <InlineAlert variant="error">{error}</InlineAlert>}
        <div className="mb-4 flex justify-end">
          <Button onClick={() => setEditing({ ...EMPTY })}>
            <Plus className="mr-2 h-4 w-4" />
            New account
          </Button>
        </div>
        {loading ? (
          <p className="text-sm text-muted-foreground">Loading…</p>
        ) : (
          <DataTable data={rows} columns={columns} keyExtractor={(r) => r.id} emptyMessage="No bank accounts yet." />
        )}
      </AdminRoutePanel>

      {editing && (
        <Modal open onClose={() => setEditing(null)} title="Edit bank account">
          <div className="space-y-3 p-4">
            <div className="grid grid-cols-2 gap-3">
              <Select label="Region" value={editing.region} options={REGION_OPTIONS} onChange={(e) => update('region', e.target.value)} />
              <Input label="Currency" value={editing.currency} onChange={(e) => update('currency', e.target.value.toUpperCase())} maxLength={3} />
              <Input label="Bank name" value={editing.bankName} onChange={(e) => update('bankName', e.target.value)} />
              <Input label="Account holder" value={editing.accountHolderName} onChange={(e) => update('accountHolderName', e.target.value)} />
              <Input label="IBAN" value={editing.iban ?? ''} onChange={(e) => update('iban', e.target.value)} />
              <Input label="SWIFT/BIC" value={editing.swiftBic ?? ''} onChange={(e) => update('swiftBic', e.target.value)} />
              <Input label="Account number" value={editing.accountNumber ?? ''} onChange={(e) => update('accountNumber', e.target.value)} />
              <Input label="Routing / sort code" value={editing.routingOrSortCode ?? ''} onChange={(e) => update('routingOrSortCode', e.target.value)} />
            </div>
            <Textarea
              value={editing.instructionsMarkdown ?? ''}
              onChange={(e) => update('instructionsMarkdown', e.target.value)}
              placeholder="Instructions (Markdown, shown to learners)"
            />
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
