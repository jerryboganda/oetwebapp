'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Plus, Sparkles, Trash2 } from 'lucide-react';
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

// The admin add-on read projection is loosely typed; AI packages are add-ons with
// addonKind === 'ai_package' plus the new aiPackageGroup / aiFeatures fields.
interface AdminAddOnRow {
  id: string;
  code: string;
  name: string;
  description?: string;
  price: number;
  currency: string;
  durationDays?: number;
  displayOrder?: number;
  status?: string;
  addonKind?: string;
  grantEntitlements?: Record<string, unknown> | null;
  aiPackageGroup?: string | null;
  aiFeatures?: string[] | null;
}

const GROUP_OPTIONS = [
  { value: 'full', label: 'Full package (Writing or Speaking credits)' },
  { value: 'writing', label: 'Writing only' },
  { value: 'speaking', label: 'Speaking only' },
  { value: 'listening', label: 'Listening only' },
  { value: 'reading', label: 'Reading only' },
  { value: 'mock', label: 'Mock exams' },
];

const STATUS_OPTIONS = [
  { value: 'active', label: 'Active (visible to learners)' },
  { value: 'inactive', label: 'Inactive (hidden)' },
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
  displayOrder: string;
  status: string;
  validityDays: string;
  group: string;
  packageType: string;
  flexibleCredits: string;
  writingCredits: string;
  speakingCredits: string;
  mocks: string;
  listeningTests: string; // blank = unlimited
  readingTests: string; // blank = unlimited
  passGuaranteeMonths: string;
  priorityQueue: boolean;
  feedbackReports: boolean;
  personalisedStudyRecs: boolean;
  features: string[];
}

function emptyForm(): FormState {
  return {
    id: null,
    code: '',
    name: '',
    description: '',
    price: '',
    currency: 'GBP',
    displayOrder: '0',
    status: 'active',
    validityDays: '30',
    group: 'full',
    packageType: '',
    flexibleCredits: '',
    writingCredits: '',
    speakingCredits: '',
    mocks: '',
    listeningTests: '',
    readingTests: '',
    passGuaranteeMonths: '',
    priorityQueue: false,
    feedbackReports: true,
    personalisedStudyRecs: false,
    features: [],
  };
}

function numOrBlank(v: unknown): string {
  return typeof v === 'number' && Number.isFinite(v) ? String(v) : '';
}

function toForm(row: AdminAddOnRow): FormState {
  const ent = (row.grantEntitlements ?? {}) as Record<string, unknown>;
  const readInt = (k: string) => numOrBlank(ent[k]);
  return {
    id: row.id,
    code: row.code ?? '',
    name: row.name ?? '',
    description: row.description ?? '',
    price: row.price != null ? String(row.price) : '',
    currency: row.currency || 'GBP',
    displayOrder: row.displayOrder != null ? String(row.displayOrder) : '0',
    status: (row.status || 'active').toLowerCase(),
    validityDays: row.durationDays != null ? String(row.durationDays) : '0',
    group: (row.aiPackageGroup && row.aiPackageGroup.trim()) || 'full',
    packageType: typeof ent.package_type === 'string' ? (ent.package_type as string) : '',
    flexibleCredits: readInt('flexible_credits'),
    writingCredits: readInt('writing_only_credits'),
    speakingCredits: readInt('speaking_only_credits'),
    mocks: readInt('mock_exams'),
    listeningTests: ent.listening_tests == null ? '' : readInt('listening_tests'),
    readingTests: ent.reading_tests == null ? '' : readInt('reading_tests'),
    passGuaranteeMonths: readInt('pass_guarantee_extension_months'),
    priorityQueue: ent.priority_queue === true,
    feedbackReports: ent.feedback_reports !== false,
    personalisedStudyRecs: ent.personalised_study_recs === true,
    features: Array.isArray(row.aiFeatures) ? row.aiFeatures.filter((f) => typeof f === 'string') : [],
  };
}

function intOrNull(v: string): number | null {
  const t = v.trim();
  if (t === '') return null;
  const n = Number(t);
  return Number.isFinite(n) ? Math.max(0, Math.round(n)) : null;
}

function buildEntitlementsJson(f: FormState): string {
  const obj: Record<string, unknown> = {
    package_type: f.packageType.trim() || f.group,
    flexible_credits: intOrNull(f.flexibleCredits),
    writing_only_credits: intOrNull(f.writingCredits),
    speaking_only_credits: intOrNull(f.speakingCredits),
    listening_tests: intOrNull(f.listeningTests), // null = unlimited
    reading_tests: intOrNull(f.readingTests), // null = unlimited
    mock_exams: intOrNull(f.mocks) ?? 0,
    priority_queue: f.priorityQueue,
    feedback_reports: f.feedbackReports,
    personalised_study_recs: f.personalisedStudyRecs,
  };
  const pg = intOrNull(f.passGuaranteeMonths);
  if (pg != null && pg > 0) obj.pass_guarantee_extension_months = pg;
  return JSON.stringify(obj);
}

function validityLabel(days: number): string {
  if (!Number.isFinite(days) || days <= 0) return '—';
  return days >= 180 ? '6-month' : `${days}-day`;
}

export interface AiPackageEditorProps {
  canWrite?: boolean;
}

export function AiPackageEditor({ canWrite = true }: AiPackageEditorProps) {
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
      const aiPackages = (Array.isArray(result) ? result : []).filter((r) => (r.addonKind ?? '').toLowerCase() === 'ai_package');
      setRows(aiPackages);
      setStatus('success');
      setLoadError(null);
    } catch (error) {
      setLoadError(error instanceof Error ? error.message : 'Failed to load AI packages.');
      setStatus('error');
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const sortedRows = useMemo(
    () => [...rows].sort((a, b) => (a.displayOrder ?? 0) - (b.displayOrder ?? 0) || a.price - b.price),
    [rows],
  );

  const openCreate = useCallback(() => {
    setForm(emptyForm());
    setFeedback(null);
    setModalOpen(true);
  }, []);

  const openEdit = useCallback((row: AdminAddOnRow) => {
    setForm(toForm(row));
    setFeedback(null);
    setModalOpen(true);
  }, []);

  const setField = useCallback(<K extends keyof FormState>(key: K, value: FormState[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }));
  }, []);

  const updateFeature = useCallback((index: number, value: string) => {
    setForm((prev) => ({ ...prev, features: prev.features.map((f, i) => (i === index ? value : f)) }));
  }, []);
  const addFeature = useCallback(() => setForm((prev) => ({ ...prev, features: [...prev.features, ''] })), []);
  const removeFeature = useCallback((index: number) => {
    setForm((prev) => ({ ...prev, features: prev.features.filter((_, i) => i !== index) }));
  }, []);

  const handleSave = useCallback(async () => {
    if (!canWrite) {
      setFeedback({ tone: 'error', message: 'You have read-only billing access.' });
      return;
    }
    const name = form.name.trim();
    if (!name) {
      setFeedback({ tone: 'error', message: 'Name is required.' });
      return;
    }
    const price = Number(form.price);
    if (!Number.isFinite(price) || price < 0) {
      setFeedback({ tone: 'error', message: 'Price must be a non-negative number.' });
      return;
    }

    const cleanFeatures = form.features.map((f) => f.trim()).filter(Boolean).slice(0, 12);
    const payload = {
      name,
      description: form.description.trim(),
      price,
      currency: form.currency.trim().toUpperCase() || 'GBP',
      interval: 'one_time',
      durationDays: intOrNull(form.validityDays) ?? 0,
      grantCredits: intOrNull(form.flexibleCredits) ?? 0,
      displayOrder: intOrNull(form.displayOrder) ?? 0,
      isRecurring: false,
      appliesToAllPlans: true,
      isStackable: true,
      status: form.status,
      grantEntitlementsJson: buildEntitlementsJson(form),
      addonKind: 'ai_package',
      lettersGranted: intOrNull(form.writingCredits) ?? 0,
      sessionsGranted: intOrNull(form.speakingCredits) ?? 0,
      aiPackageGroup: form.group,
      aiFeaturesJson: JSON.stringify(cleanFeatures),
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
      setFeedback({ tone: 'success', message: `AI package ${form.id ? 'updated' : 'created'}.` });
      await load();
    } catch (error) {
      setFeedback({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to save AI package.' });
    } finally {
      setSaving(false);
    }
  }, [canWrite, form, load]);

  return (
    <div className="space-y-4" data-testid="ai-package-editor">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-muted">
          AI grading packages shown on the learner <strong>AI Credits</strong> tab. Group, entitlements, and feature
          bullets are fully editable — no raw JSON or code-naming conventions.
        </p>
        <Button variant="primary" onClick={openCreate} disabled={!canWrite}>
          <Plus className="mr-1 h-4 w-4" /> New AI package
        </Button>
      </div>

      {!canWrite ? (
        <InlineAlert variant="info" title="Read-only access">
          You can review AI packages, but saving changes requires Billing catalog write permission.
        </InlineAlert>
      ) : null}

      {feedback ? (
        <InlineAlert variant={feedback.tone === 'success' ? 'success' : 'error'} title={feedback.tone === 'success' ? 'Saved' : 'Error'}>
          {feedback.message}
        </InlineAlert>
      ) : null}

      {status === 'error' ? (
        <InlineAlert variant="error" title="Couldn’t load AI packages">
          {loadError ?? 'An unexpected error occurred.'}
        </InlineAlert>
      ) : null}

      <Card className="overflow-hidden p-0">
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-border text-sm">
            <thead className="bg-admin-bg-subtle">
              <tr className="text-left text-xs font-semibold uppercase tracking-[0.12em] text-muted">
                <th scope="col" className="px-3 py-3">Name</th>
                <th scope="col" className="px-3 py-3">Group</th>
                <th scope="col" className="px-3 py-3">Price</th>
                <th scope="col" className="px-3 py-3">Credits</th>
                <th scope="col" className="px-3 py-3">Validity</th>
                <th scope="col" className="px-3 py-3">Status</th>
                <th scope="col" className="px-3 py-3 sr-only">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border bg-surface">
              {status === 'loading' ? (
                <tr><td colSpan={7} className="px-4 py-8 text-center text-muted">Loading…</td></tr>
              ) : sortedRows.length === 0 ? (
                <tr><td colSpan={7} className="px-4 py-8 text-center text-muted">No AI packages yet. Create one to populate the learner AI Credits tab.</td></tr>
              ) : (
                sortedRows.map((row) => {
                  const ent = (row.grantEntitlements ?? {}) as Record<string, unknown>;
                  const credits = numOrBlank(ent.flexible_credits) || '0';
                  return (
                    <tr key={row.id} className="align-middle">
                      <td className="px-3 py-3">
                        <div className="font-semibold text-navy">{row.name}</div>
                        <div className="text-xs text-muted">{row.code}</div>
                      </td>
                      <td className="px-3 py-3"><Badge variant="info">{row.aiPackageGroup || 'full'}</Badge></td>
                      <td className="px-3 py-3 tabular-nums">{row.currency} {row.price}</td>
                      <td className="px-3 py-3 tabular-nums">{credits}</td>
                      <td className="px-3 py-3">{validityLabel(row.durationDays ?? 0)}</td>
                      <td className="px-3 py-3">
                        <Badge variant={(row.status ?? 'active').toLowerCase() === 'active' ? 'success' : 'default'}>
                          {(row.status ?? 'active').toLowerCase()}
                        </Badge>
                      </td>
                      <td className="px-3 py-3 text-right">
                        <Button variant="secondary" size="sm" onClick={() => openEdit(row)}>Edit</Button>
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </Card>

      <Modal open={modalOpen} onClose={() => setModalOpen(false)} title={form.id ? 'Edit AI package' : 'New AI package'} size="lg">
        <div className="space-y-5">
          {feedback && feedback.tone === 'error' ? (
            <InlineAlert variant="error" title="Error">{feedback.message}</InlineAlert>
          ) : null}

          <div className="grid gap-4 sm:grid-cols-2">
            <Input label="Name" value={form.name} onChange={(e) => setField('name', e.target.value)} placeholder="Quick Check" />
            <Input label="Code" value={form.code} onChange={(e) => setField('code', e.target.value)} placeholder="pkg_quick_check" hint={form.id ? 'Code is immutable after creation.' : 'Leave blank to auto-generate from the name.'} disabled={!!form.id} />
          </div>

          <Textarea label="Description" value={form.description} onChange={(e) => setField('description', e.target.value)} placeholder="5 flexible AI grading credits for Writing or Speaking, valid for 30 days." />

          <div className="grid gap-4 sm:grid-cols-3">
            <Input label="Price" inputMode="decimal" value={form.price} onChange={(e) => setField('price', e.target.value)} placeholder="19" />
            <Input label="Currency" value={form.currency} maxLength={3} onChange={(e) => setField('currency', e.target.value.toUpperCase())} className="uppercase" />
            <Input label="Validity (days)" inputMode="numeric" value={form.validityDays} onChange={(e) => setField('validityDays', e.target.value)} hint="180+ shows as “6-month”." />
          </div>

          <div className="grid gap-4 sm:grid-cols-3">
            <Select label="Group" value={form.group} onChange={(e) => setField('group', e.target.value)} options={GROUP_OPTIONS} />
            <Select label="Status" value={form.status} onChange={(e) => setField('status', e.target.value)} options={STATUS_OPTIONS} />
            <Input label="Display order" inputMode="numeric" value={form.displayOrder} onChange={(e) => setField('displayOrder', e.target.value)} />
          </div>

          <div className="rounded-2xl border border-border bg-background-light/50 p-4">
            <p className="mb-3 text-sm font-semibold text-navy">Entitlements granted on purchase</p>
            <div className="grid gap-4 sm:grid-cols-3">
              <Input label="Flexible credits" inputMode="numeric" value={form.flexibleCredits} onChange={(e) => setField('flexibleCredits', e.target.value)} hint="Writing or Speaking" />
              <Input label="Writing credits" inputMode="numeric" value={form.writingCredits} onChange={(e) => setField('writingCredits', e.target.value)} />
              <Input label="Speaking credits" inputMode="numeric" value={form.speakingCredits} onChange={(e) => setField('speakingCredits', e.target.value)} />
              <Input label="Mock exams" inputMode="numeric" value={form.mocks} onChange={(e) => setField('mocks', e.target.value)} />
              <Input label="Listening tests" inputMode="numeric" value={form.listeningTests} onChange={(e) => setField('listeningTests', e.target.value)} hint="Blank = unlimited" />
              <Input label="Reading tests" inputMode="numeric" value={form.readingTests} onChange={(e) => setField('readingTests', e.target.value)} hint="Blank = unlimited" />
              <Input label="Pass-guarantee (months)" inputMode="numeric" value={form.passGuaranteeMonths} onChange={(e) => setField('passGuaranteeMonths', e.target.value)} />
              <Input label="Package type (advanced)" value={form.packageType} onChange={(e) => setField('packageType', e.target.value)} hint="Defaults to the group." />
            </div>
            <div className="mt-4 grid gap-3 sm:grid-cols-3">
              <Checkbox label="Priority grading queue" checked={form.priorityQueue} onChange={(e) => setField('priorityQueue', e.target.checked)} />
              <Checkbox label="AI feedback reports" checked={form.feedbackReports} onChange={(e) => setField('feedbackReports', e.target.checked)} />
              <Checkbox label="Personalised study recs" checked={form.personalisedStudyRecs} onChange={(e) => setField('personalisedStudyRecs', e.target.checked)} />
            </div>
          </div>

          <div className="rounded-2xl border border-border bg-background-light/50 p-4">
            <div className="mb-2 flex items-center justify-between gap-2">
              <p className="text-sm font-semibold text-navy">Feature bullets</p>
              <Button variant="secondary" size="sm" onClick={addFeature}><Plus className="mr-1 h-4 w-4" /> Add bullet</Button>
            </div>
            <p className="mb-3 text-xs text-muted">Shown on the package card. Leave empty to auto-generate from the entitlements above.</p>
            <div className="space-y-2">
              {form.features.length === 0 ? (
                <p className="text-xs italic text-muted">No custom bullets — features will be auto-generated.</p>
              ) : (
                form.features.map((feature, index) => (
                  <div key={index} className="flex items-center gap-2">
                    <Input aria-label={`Feature ${index + 1}`} value={feature} onChange={(e) => updateFeature(index, e.target.value)} className="flex-1" />
                    <Button variant="ghost" size="sm" aria-label={`Remove feature ${index + 1}`} onClick={() => removeFeature(index)}>
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                ))
              )}
            </div>
          </div>

          <div className="flex items-center justify-end gap-3 pt-2">
            <Button variant="secondary" onClick={() => setModalOpen(false)} disabled={saving}>Cancel</Button>
            <Button variant="primary" onClick={handleSave} disabled={!canWrite || saving}>
              <Sparkles className="mr-1 h-4 w-4" /> {saving ? 'Saving…' : form.id ? 'Save package' : 'Create package'}
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
