'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Plus, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Input, Select, Textarea, Checkbox } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';
import { BillingConfirmDialog } from './confirm-dialog';
import {
  createAdminBillingPlan,
  deleteAdminBillingPlan,
  updateAdminBillingPlan,
  fetchAdminBillingPlans,
} from '@/lib/api';

interface AdminPlanRow {
  id: string;
  code: string;
  name: string;
  description?: string;
  price: number;
  currency: string;
  interval?: string;
  durationMonths?: number;
  includedCredits?: number;
  trialDays?: number;
  displayOrder?: number;
  isVisible?: boolean;
  isRenewable?: boolean;
  status?: string;
  activeSubscribers?: number;
  includedSubtests?: string[];
  dashboardModules?: string[];
  entitlements?: Record<string, unknown> | null;
}

const SUBTESTS = ['listening', 'reading', 'writing', 'speaking'] as const;

// Admin-togglable "student subscription modules". Keys are the canonical PascalCase strings stored
// in DashboardModulesJson (see backend ModuleKeys / hooks/use-enabled-modules MODULE_KEYS). Toggling
// these now gates BOTH the learner nav/tiles and real backend access.
const MODULE_TOGGLES = [
  { key: 'Recalls', label: 'Recalls' },
  { key: 'MaterialsLibrary', label: 'Materials' },
  { key: 'VideoLibrary', label: 'Videos' },
  { key: 'Mocks', label: 'Mocks' },
] as const;
const MODULE_STATUS_OPTIONS = [
  { value: 'enabled', label: 'Enabled' },
  { value: 'disabled', label: 'Disabled' },
];
const DEFAULT_NEW_PLAN_MODULES = MODULE_TOGGLES.map((m) => m.key);

function planHasModule(modules: string[], key: string): boolean {
  return modules.some((m) => m.toLowerCase() === key.toLowerCase());
}
const INTERVAL_OPTIONS = [
  { value: 'month', label: 'Monthly' },
  { value: 'year', label: 'Yearly' },
  { value: 'one_time', label: 'One-time' },
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
  durationMonths: string;
  includedCredits: string;
  trialDays: string;
  displayOrder: string;
  isVisible: boolean;
  isRenewable: boolean;
  status: string;
  subtests: string[];
  modules: string[];
  invoiceDownloads: boolean;
  entitlements: Record<string, unknown>;
}

function emptyForm(): FormState {
  return {
    id: null, code: '', name: '', description: '', price: '', currency: 'GBP',
    interval: 'month', durationMonths: '1', includedCredits: '0', trialDays: '0',
    displayOrder: '0', isVisible: true, isRenewable: true, status: 'active',
    subtests: [], modules: [...DEFAULT_NEW_PLAN_MODULES], invoiceDownloads: false, entitlements: {},
  };
}

function toForm(row: AdminPlanRow): FormState {
  const ent = (row.entitlements ?? {}) as Record<string, unknown>;
  return {
    id: row.id,
    code: row.code ?? '',
    name: row.name ?? '',
    description: row.description ?? '',
    price: row.price != null ? String(row.price) : '',
    currency: row.currency || 'GBP',
    interval: row.interval || 'month',
    durationMonths: row.durationMonths != null ? String(row.durationMonths) : '1',
    includedCredits: row.includedCredits != null ? String(row.includedCredits) : '0',
    trialDays: row.trialDays != null ? String(row.trialDays) : '0',
    displayOrder: row.displayOrder != null ? String(row.displayOrder) : '0',
    isVisible: row.isVisible ?? true,
    isRenewable: row.isRenewable ?? true,
    status: (row.status || 'active').toLowerCase(),
    subtests: Array.isArray(row.includedSubtests) ? row.includedSubtests : [],
    modules: Array.isArray(row.dashboardModules) ? row.dashboardModules : [],
    invoiceDownloads: ent.invoiceDownloadsAvailable === true,
    entitlements: ent,
  };
}

function intOr(v: string, fallback = 0): number {
  const n = Number(v.trim());
  return Number.isFinite(n) ? Math.max(0, Math.round(n)) : fallback;
}

export interface PlanCatalogEditorProps {
  canWrite?: boolean;
}

export function PlanCatalogEditor({ canWrite = true }: PlanCatalogEditorProps) {
  const [rows, setRows] = useState<AdminPlanRow[]>([]);
  const [status, setStatus] = useState<'loading' | 'success' | 'error'>('loading');
  const [loadError, setLoadError] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'error'; message: string } | null>(null);
  const [modalOpen, setModalOpen] = useState(false);
  const [form, setForm] = useState<FormState>(emptyForm);
  const [saving, setSaving] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<AdminPlanRow | null>(null);
  const [deleteConfirmInput, setDeleteConfirmInput] = useState('');
  const [deleting, setDeleting] = useState(false);

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      const result = (await fetchAdminBillingPlans()) as unknown as AdminPlanRow[];
      setRows(Array.isArray(result) ? result : []);
      setStatus('success');
      setLoadError(null);
    } catch (error) {
      setLoadError(error instanceof Error ? error.message : 'Failed to load plans.');
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

  const toggleSubtest = useCallback((subtest: string) => {
    setForm((prev) => ({
      ...prev,
      subtests: prev.subtests.includes(subtest)
        ? prev.subtests.filter((s) => s !== subtest)
        : [...prev.subtests, subtest],
    }));
  }, []);

  // Add/remove a single canonical module key while preserving every OTHER key already on the plan
  // (subtests, TutorBook, etc.), so saving never drops modules the dropdowns don't manage.
  const setModuleEnabled = useCallback((key: string, enabled: boolean) => {
    setForm((prev) => {
      const without = prev.modules.filter((m) => m.toLowerCase() !== key.toLowerCase());
      return { ...prev, modules: enabled ? [...without, key] : without };
    });
  }, []);

  const openCreate = useCallback(() => { setForm(emptyForm()); setFeedback(null); setModalOpen(true); }, []);
  const openEdit = useCallback((row: AdminPlanRow) => { setForm(toForm(row)); setFeedback(null); setModalOpen(true); }, []);

  const handleSave = useCallback(async () => {
    if (!canWrite) { setFeedback({ tone: 'error', message: 'You have read-only billing access.' }); return; }
    const name = form.name.trim();
    if (!name) { setFeedback({ tone: 'error', message: 'Name is required.' }); return; }
    const price = Number(form.price);
    if (!Number.isFinite(price) || price < 0) { setFeedback({ tone: 'error', message: 'Price must be a non-negative number.' }); return; }

    // Preserve unknown entitlement keys; just toggle the invoice-downloads flag.
    const entitlements = { ...form.entitlements, invoiceDownloadsAvailable: form.invoiceDownloads };

    const payload = {
      name,
      description: form.description.trim(),
      price,
      currency: form.currency.trim().toUpperCase() || 'GBP',
      interval: form.interval,
      durationMonths: intOr(form.durationMonths, 1),
      includedCredits: intOr(form.includedCredits),
      trialDays: intOr(form.trialDays),
      displayOrder: intOr(form.displayOrder),
      isVisible: form.isVisible,
      isRenewable: form.isRenewable,
      status: form.status,
      includedSubtestsJson: JSON.stringify(form.subtests),
      dashboardModulesJson: JSON.stringify(form.modules),
      entitlementsJson: JSON.stringify(entitlements),
    };

    setSaving(true);
    setFeedback(null);
    try {
      if (form.id) {
        await updateAdminBillingPlan(form.id, { code: form.code, ...payload });
      } else {
        await createAdminBillingPlan({ code: form.code.trim() || undefined, ...payload });
      }
      setModalOpen(false);
      setFeedback({ tone: 'success', message: `Plan ${form.id ? 'updated' : 'created'}.` });
      await load();
    } catch (error) {
      setFeedback({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to save plan.' });
    } finally {
      setSaving(false);
    }
  }, [canWrite, form, load]);

  const requestDelete = useCallback((row: AdminPlanRow) => {
    setDeleteTarget(row);
    setDeleteConfirmInput('');
    setFeedback(null);
  }, []);

  const handleDelete = useCallback(async () => {
    if (!canWrite) { setFeedback({ tone: 'error', message: 'You have read-only billing access.' }); return; }
    if (!deleteTarget) return;
    setDeleting(true);
    setFeedback(null);
    try {
      await deleteAdminBillingPlan(deleteTarget.id);
      setDeleteTarget(null);
      setDeleteConfirmInput('');
      setFeedback({ tone: 'success', message: `Plan "${deleteTarget.name}" hard-deleted.` });
      await load();
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Failed to delete plan.';
      setFeedback({
        tone: 'error',
        message: /in[_ ]use|archive|subscriber/i.test(message)
          ? `${message} Use Edit to archive instead (Status = Archived).`
          : message,
      });
    } finally {
      setDeleting(false);
    }
  }, [canWrite, deleteTarget, load]);

  return (
    <div className="space-y-4" data-testid="plan-catalog-editor">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-muted">Subscription plans shown on the learner <strong>Plans</strong> tab.</p>
        <Button variant="primary" onClick={openCreate} disabled={!canWrite}><Plus className="mr-1 h-4 w-4" /> New plan</Button>
      </div>

      {!canWrite ? (
        <InlineAlert variant="info" title="Read-only access">Saving changes requires Billing catalog write permission.</InlineAlert>
      ) : null}
      {feedback ? (
        <InlineAlert variant={feedback.tone === 'success' ? 'success' : 'error'} title={feedback.tone === 'success' ? 'Saved' : 'Error'}>{feedback.message}</InlineAlert>
      ) : null}
      {status === 'error' ? (
        <InlineAlert variant="error" title="Couldn’t load plans">{loadError ?? 'An unexpected error occurred.'}</InlineAlert>
      ) : null}

      <Card className="overflow-hidden p-0">
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-border text-sm">
            <thead className="bg-admin-bg-subtle">
              <tr className="text-left text-xs font-semibold uppercase tracking-[0.12em] text-muted">
                <th className="px-3 py-3">Name</th>
                <th className="px-3 py-3">Price</th>
                <th className="px-3 py-3">Interval</th>
                <th className="px-3 py-3">Credits</th>
                <th className="px-3 py-3">Status</th>
                <th className="px-3 py-3 sr-only">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border bg-surface">
              {status === 'loading' ? (
                <tr><td colSpan={6} className="px-4 py-8 text-center text-muted">Loading…</td></tr>
              ) : sortedRows.length === 0 ? (
                <tr><td colSpan={6} className="px-4 py-8 text-center text-muted">No plans yet.</td></tr>
              ) : (
                sortedRows.map((row) => (
                  <tr key={row.id} className="align-middle">
                    <td className="px-3 py-3"><div className="font-semibold text-navy">{row.name}</div><div className="text-xs text-muted">{row.code}</div></td>
                    <td className="px-3 py-3 tabular-nums">{row.currency} {row.price}</td>
                    <td className="px-3 py-3">{row.interval ?? 'month'}</td>
                    <td className="px-3 py-3 tabular-nums">{row.includedCredits ?? 0}</td>
                    <td className="px-3 py-3"><Badge variant={(row.status ?? 'active').toLowerCase() === 'active' ? 'success' : 'default'}>{(row.status ?? 'active').toLowerCase()}</Badge></td>
                    <td className="px-3 py-3 text-right">
                      <div className="flex justify-end gap-2">
                        <Button variant="secondary" size="sm" onClick={() => openEdit(row)}>Edit</Button>
                        <Button
                          variant="destructive"
                          size="sm"
                          onClick={() => requestDelete(row)}
                          disabled={!canWrite}
                          aria-label={`Hard delete plan ${row.name}`}
                        >
                          <Trash2 className="h-4 w-4" /> Delete
                        </Button>
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </Card>

      <Modal open={modalOpen} onClose={() => setModalOpen(false)} title={form.id ? 'Edit plan' : 'New plan'} size="lg">
        <div className="space-y-5">
          {feedback && feedback.tone === 'error' ? <InlineAlert variant="error" title="Error">{feedback.message}</InlineAlert> : null}

          <div className="grid gap-4 sm:grid-cols-2">
            <Input label="Name" value={form.name} onChange={(e) => setField('name', e.target.value)} placeholder="Premium Monthly" />
            <Input label="Code" value={form.code} onChange={(e) => setField('code', e.target.value)} disabled={!!form.id} hint={form.id ? 'Immutable after creation.' : 'Leave blank to auto-generate.'} />
          </div>
          <Textarea label="Description" value={form.description} onChange={(e) => setField('description', e.target.value)} />

          <div className="grid gap-4 sm:grid-cols-3">
            <Input label="Price" inputMode="decimal" value={form.price} onChange={(e) => setField('price', e.target.value)} />
            <Input label="Currency" value={form.currency} maxLength={3} onChange={(e) => setField('currency', e.target.value.toUpperCase())} className="uppercase" />
            <Select label="Interval" value={form.interval} onChange={(e) => setField('interval', e.target.value)} options={INTERVAL_OPTIONS} />
            <Input label="Duration (months)" inputMode="numeric" value={form.durationMonths} onChange={(e) => setField('durationMonths', e.target.value)} />
            <Input label="Included review credits" inputMode="numeric" value={form.includedCredits} onChange={(e) => setField('includedCredits', e.target.value)} />
            <Input label="Trial days" inputMode="numeric" value={form.trialDays} onChange={(e) => setField('trialDays', e.target.value)} />
            <Input label="Display order" inputMode="numeric" value={form.displayOrder} onChange={(e) => setField('displayOrder', e.target.value)} />
            <Select label="Status" value={form.status} onChange={(e) => setField('status', e.target.value)} options={STATUS_OPTIONS} />
          </div>

          <div className="rounded-2xl border border-border bg-background-light/50 p-4">
            <p className="mb-3 text-sm font-semibold text-navy">Tutor review subtests included</p>
            <div className="grid gap-3 sm:grid-cols-4">
              {SUBTESTS.map((subtest) => (
                <Checkbox key={subtest} label={subtest[0].toUpperCase() + subtest.slice(1)} checked={form.subtests.includes(subtest)} onChange={() => toggleSubtest(subtest)} />
              ))}
            </div>
          </div>

          <div className="rounded-2xl border border-border bg-background-light/50 p-4">
            <p className="mb-1 text-sm font-semibold text-navy">Student subscription modules</p>
            <p className="mb-3 text-xs text-muted">Enable or disable these modules for learners on this plan. Disabling hides the module from the learner’s dashboard &amp; navigation and blocks access on the server.</p>
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              {MODULE_TOGGLES.map((module) => (
                <Select
                  key={module.key}
                  label={module.label}
                  value={planHasModule(form.modules, module.key) ? 'enabled' : 'disabled'}
                  onChange={(e) => setModuleEnabled(module.key, e.target.value === 'enabled')}
                  options={MODULE_STATUS_OPTIONS}
                />
              ))}
            </div>
          </div>

          <div className="grid gap-3 sm:grid-cols-3">
            <Checkbox label="Visible to learners" checked={form.isVisible} onChange={(e) => setField('isVisible', e.target.checked)} />
            <Checkbox label="Auto-renewing" checked={form.isRenewable} onChange={(e) => setField('isRenewable', e.target.checked)} />
            <Checkbox label="Invoice downloads" checked={form.invoiceDownloads} onChange={(e) => setField('invoiceDownloads', e.target.checked)} />
          </div>

          <p className="text-xs text-muted">
            Advanced catalog fields (bundles, professions, eligibility, version history) live on{' '}
            <Link href="/admin/billing" className="font-medium text-primary hover:underline">Billing Ops</Link>.
          </p>

          <div className="flex items-center justify-end gap-3 pt-2">
            <Button variant="secondary" onClick={() => setModalOpen(false)} disabled={saving}>Cancel</Button>
            <Button variant="primary" onClick={handleSave} disabled={!canWrite || saving}>{saving ? 'Saving…' : form.id ? 'Save plan' : 'Create plan'}</Button>
          </div>
        </div>
      </Modal>

      <BillingConfirmDialog
        open={deleteTarget !== null}
        title="Hard-delete this plan?"
        description={
          deleteTarget
            ? `Permanently removes "${deleteTarget.name}" (${deleteTarget.code}): the plan row, all its version history, and the linked content package. This cannot be undone.${(deleteTarget.activeSubscribers ?? 0) > 0 ? ` The server will refuse because it has ${deleteTarget.activeSubscribers} active subscriber(s); archive instead.` : ''}`
            : ''
        }
        confirmPhrase={deleteTarget?.code ?? ''}
        confirmInput={deleteConfirmInput}
        onConfirmInputChange={setDeleteConfirmInput}
        confirmLabel="Permanently delete"
        variant="danger"
        loading={deleting}
        onConfirm={() => { void handleDelete(); }}
        onCancel={() => {
          setDeleteTarget(null);
          setDeleteConfirmInput('');
        }}
      />
    </div>
  );
}
