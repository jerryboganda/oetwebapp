'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Input, Select, Textarea, Checkbox } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';
import {
  createAdminBillingAddOn,
  updateAdminBillingAddOn,
  fetchAdminBillingAddOns,
} from '@/lib/api';

interface AdminAddOnRow {
  id: string;
  code: string;
  name: string;
  description?: string;
  price: number;
  currency: string;
  interval?: string;
  durationDays?: number;
  grantCredits?: number;
  displayOrder?: number;
  isRecurring?: boolean;
  appliesToAllPlans?: boolean;
  isStackable?: boolean;
  maxQuantity?: number | null;
  status?: string;
  addonKind?: string;
  compatiblePlanCodes?: string[];
}

const INTERVAL_OPTIONS = [
  { value: 'one_time', label: 'One-time' },
  { value: 'month', label: 'Monthly' },
  { value: 'year', label: 'Yearly' },
];
const STATUS_OPTIONS = [
  { value: 'active', label: 'Active (visible)' },
  { value: 'inactive', label: 'Inactive' },
  { value: 'draft', label: 'Draft' },
  { value: 'archived', label: 'Archived' },
];

interface FormState {
  id: string | null;
  code: string;
  name: string;
  description: string;
  price: string;
  currency: string;
  interval: string;
  durationDays: string;
  grantCredits: string;
  displayOrder: string;
  isRecurring: boolean;
  appliesToAllPlans: boolean;
  isStackable: boolean;
  maxQuantity: string;
  status: string;
  addonKind: string;
  compatiblePlanCodes: string;
}

function emptyForm(): FormState {
  return {
    id: null, code: '', name: '', description: '', price: '', currency: 'GBP',
    interval: 'one_time', durationDays: '0', grantCredits: '0', displayOrder: '0',
    isRecurring: false, appliesToAllPlans: true, isStackable: true, maxQuantity: '',
    status: 'active', addonKind: 'review_credits', compatiblePlanCodes: '',
  };
}

function toForm(row: AdminAddOnRow): FormState {
  return {
    id: row.id,
    code: row.code ?? '',
    name: row.name ?? '',
    description: row.description ?? '',
    price: row.price != null ? String(row.price) : '',
    currency: row.currency || 'GBP',
    interval: row.interval || 'one_time',
    durationDays: row.durationDays != null ? String(row.durationDays) : '0',
    grantCredits: row.grantCredits != null ? String(row.grantCredits) : '0',
    displayOrder: row.displayOrder != null ? String(row.displayOrder) : '0',
    isRecurring: row.isRecurring ?? false,
    appliesToAllPlans: row.appliesToAllPlans ?? true,
    isStackable: row.isStackable ?? true,
    maxQuantity: row.maxQuantity != null ? String(row.maxQuantity) : '',
    status: (row.status || 'active').toLowerCase(),
    addonKind: row.addonKind || 'review_credits',
    compatiblePlanCodes: Array.isArray(row.compatiblePlanCodes) ? row.compatiblePlanCodes.join(', ') : '',
  };
}

function intOr(v: string, fallback = 0): number {
  const n = Number(v.trim());
  return Number.isFinite(n) ? Math.max(0, Math.round(n)) : fallback;
}

export interface AddOnCatalogEditorProps {
  canWrite?: boolean;
}

export function AddOnCatalogEditor({ canWrite = true }: AddOnCatalogEditorProps) {
  const [rows, setRows] = useState<AdminAddOnRow[]>([]);
  const [status, setStatus] = useState<'loading' | 'success' | 'error'>('loading');
  const [loadError, setLoadError] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'error'; message: string } | null>(null);
  const [modalOpen, setModalOpen] = useState(false);
  const [form, setForm] = useState<FormState>(emptyForm);
  const [saving, setSaving] = useState(false);

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      const result = (await fetchAdminBillingAddOns()) as unknown as AdminAddOnRow[];
      // AI packages have their own tab — exclude them here.
      const addOns = (Array.isArray(result) ? result : []).filter((r) => (r.addonKind ?? '').toLowerCase() !== 'ai_package');
      setRows(addOns);
      setStatus('success');
      setLoadError(null);
    } catch (error) {
      setLoadError(error instanceof Error ? error.message : 'Failed to load add-ons.');
      setStatus('error');
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const sortedRows = useMemo(
    () => [...rows].sort((a, b) => (a.displayOrder ?? 0) - (b.displayOrder ?? 0) || a.price - b.price),
    [rows],
  );

  const setField = useCallback(<K extends keyof FormState>(key: K, value: FormState[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }));
  }, []);

  const openCreate = useCallback(() => { setForm(emptyForm()); setFeedback(null); setModalOpen(true); }, []);
  const openEdit = useCallback((row: AdminAddOnRow) => { setForm(toForm(row)); setFeedback(null); setModalOpen(true); }, []);

  const handleSave = useCallback(async () => {
    if (!canWrite) { setFeedback({ tone: 'error', message: 'You have read-only billing access.' }); return; }
    const name = form.name.trim();
    if (!name) { setFeedback({ tone: 'error', message: 'Name is required.' }); return; }
    const price = Number(form.price);
    if (!Number.isFinite(price) || price < 0) { setFeedback({ tone: 'error', message: 'Price must be a non-negative number.' }); return; }

    const compatibleCodes = form.compatiblePlanCodes.split(',').map((c) => c.trim()).filter(Boolean);
    const maxQ = form.maxQuantity.trim() === '' ? null : intOr(form.maxQuantity);

    const payload = {
      name,
      description: form.description.trim(),
      price,
      currency: form.currency.trim().toUpperCase() || 'GBP',
      interval: form.interval,
      durationDays: intOr(form.durationDays),
      grantCredits: intOr(form.grantCredits),
      displayOrder: intOr(form.displayOrder),
      isRecurring: form.isRecurring,
      appliesToAllPlans: form.appliesToAllPlans,
      isStackable: form.isStackable,
      maxQuantity: maxQ,
      status: form.status,
      compatiblePlanCodesJson: JSON.stringify(compatibleCodes),
      addonKind: form.addonKind.trim() || undefined,
    };

    setSaving(true);
    setFeedback(null);
    try {
      if (form.id) {
        await updateAdminBillingAddOn(form.id, { code: form.code, ...payload });
      } else {
        await createAdminBillingAddOn({ code: form.code.trim() || undefined, ...payload });
      }
      setModalOpen(false);
      setFeedback({ tone: 'success', message: `Add-on ${form.id ? 'updated' : 'created'}.` });
      await load();
    } catch (error) {
      setFeedback({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to save add-on.' });
    } finally {
      setSaving(false);
    }
  }, [canWrite, form, load]);

  return (
    <div className="space-y-4" data-testid="addon-catalog-editor">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-muted">Add-ons shown on the learner <strong>Credits &amp; Add-ons</strong> tab (review credits, extras). AI packages have their own tab.</p>
        <Button variant="primary" onClick={openCreate} disabled={!canWrite}><Plus className="mr-1 h-4 w-4" /> New add-on</Button>
      </div>

      {!canWrite ? (
        <InlineAlert variant="info" title="Read-only access">Saving changes requires Billing catalog write permission.</InlineAlert>
      ) : null}
      {feedback ? (
        <InlineAlert variant={feedback.tone === 'success' ? 'success' : 'error'} title={feedback.tone === 'success' ? 'Saved' : 'Error'}>{feedback.message}</InlineAlert>
      ) : null}
      {status === 'error' ? (
        <InlineAlert variant="error" title="Couldn’t load add-ons">{loadError ?? 'An unexpected error occurred.'}</InlineAlert>
      ) : null}

      <Card className="overflow-hidden p-0">
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-border text-sm">
            <thead className="bg-admin-bg-subtle">
              <tr className="text-left text-xs font-semibold uppercase tracking-[0.12em] text-muted">
                <th className="px-3 py-3">Name</th>
                <th className="px-3 py-3">Kind</th>
                <th className="px-3 py-3">Price</th>
                <th className="px-3 py-3">Credits</th>
                <th className="px-3 py-3">Status</th>
                <th className="px-3 py-3 sr-only">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border bg-surface">
              {status === 'loading' ? (
                <tr><td colSpan={6} className="px-4 py-8 text-center text-muted">Loading…</td></tr>
              ) : sortedRows.length === 0 ? (
                <tr><td colSpan={6} className="px-4 py-8 text-center text-muted">No add-ons yet.</td></tr>
              ) : (
                sortedRows.map((row) => (
                  <tr key={row.id} className="align-middle">
                    <td className="px-3 py-3"><div className="font-semibold text-navy">{row.name}</div><div className="text-xs text-muted">{row.code}</div></td>
                    <td className="px-3 py-3">{row.addonKind || '—'}</td>
                    <td className="px-3 py-3 tabular-nums">{row.currency} {row.price}</td>
                    <td className="px-3 py-3 tabular-nums">{row.grantCredits ?? 0}</td>
                    <td className="px-3 py-3"><Badge variant={(row.status ?? 'active').toLowerCase() === 'active' ? 'success' : 'default'}>{(row.status ?? 'active').toLowerCase()}</Badge></td>
                    <td className="px-3 py-3 text-right"><Button variant="secondary" size="sm" onClick={() => openEdit(row)}>Edit</Button></td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </Card>

      <Modal open={modalOpen} onClose={() => setModalOpen(false)} title={form.id ? 'Edit add-on' : 'New add-on'} size="lg">
        <div className="space-y-5">
          {feedback && feedback.tone === 'error' ? <InlineAlert variant="error" title="Error">{feedback.message}</InlineAlert> : null}

          <div className="grid gap-4 sm:grid-cols-2">
            <Input label="Name" value={form.name} onChange={(e) => setField('name', e.target.value)} placeholder="3 Review Credits" />
            <Input label="Code" value={form.code} onChange={(e) => setField('code', e.target.value)} disabled={!!form.id} hint={form.id ? 'Immutable after creation.' : 'Leave blank to auto-generate.'} />
          </div>
          <Textarea label="Description" value={form.description} onChange={(e) => setField('description', e.target.value)} placeholder="Pack of 3 tutor review credits." />

          <div className="grid gap-4 sm:grid-cols-3">
            <Input label="Price" inputMode="decimal" value={form.price} onChange={(e) => setField('price', e.target.value)} />
            <Input label="Currency" value={form.currency} maxLength={3} onChange={(e) => setField('currency', e.target.value.toUpperCase())} className="uppercase" />
            <Select label="Interval" value={form.interval} onChange={(e) => setField('interval', e.target.value)} options={INTERVAL_OPTIONS} />
            <Input label="Grant credits" inputMode="numeric" value={form.grantCredits} onChange={(e) => setField('grantCredits', e.target.value)} />
            <Input label="Duration (days)" inputMode="numeric" value={form.durationDays} onChange={(e) => setField('durationDays', e.target.value)} hint="0 = no expiry" />
            <Input label="Add-on kind" value={form.addonKind} onChange={(e) => setField('addonKind', e.target.value)} hint="e.g. review_credits" />
            <Input label="Display order" inputMode="numeric" value={form.displayOrder} onChange={(e) => setField('displayOrder', e.target.value)} />
            <Input label="Max quantity" inputMode="numeric" value={form.maxQuantity} onChange={(e) => setField('maxQuantity', e.target.value)} hint="Blank = unlimited" />
            <Select label="Status" value={form.status} onChange={(e) => setField('status', e.target.value)} options={STATUS_OPTIONS} />
          </div>

          <div className="grid gap-3 sm:grid-cols-3">
            <Checkbox label="Recurring" checked={form.isRecurring} onChange={(e) => setField('isRecurring', e.target.checked)} />
            <Checkbox label="Applies to all plans" checked={form.appliesToAllPlans} onChange={(e) => setField('appliesToAllPlans', e.target.checked)} />
            <Checkbox label="Stackable" checked={form.isStackable} onChange={(e) => setField('isStackable', e.target.checked)} />
          </div>

          {!form.appliesToAllPlans ? (
            <Input label="Compatible plan codes (comma-separated)" value={form.compatiblePlanCodes} onChange={(e) => setField('compatiblePlanCodes', e.target.value)} placeholder="premium-monthly, premium-yearly" />
          ) : null}

          <p className="text-xs text-muted">
            Advanced eligibility fields live on{' '}
            <Link href="/admin/billing" className="font-medium text-primary hover:underline">Billing Ops</Link>.
          </p>

          <div className="flex items-center justify-end gap-3 pt-2">
            <Button variant="secondary" onClick={() => setModalOpen(false)} disabled={saving}>Cancel</Button>
            <Button variant="primary" onClick={handleSave} disabled={!canWrite || saving}>{saving ? 'Saving…' : form.id ? 'Save add-on' : 'Create add-on'}</Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
