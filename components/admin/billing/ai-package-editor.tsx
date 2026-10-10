'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Plus, Sparkles, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Input, Select, Textarea, Checkbox } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';
import { BillingConfirmDialog } from './confirm-dialog';
import { resolveWebsitePackageByCode } from '@/lib/catalog-website-packages';
import {
  createAdminBillingAddOn,
  deleteAdminBillingAddOn,
  updateAdminBillingAddOn,
  fetchAdminBillingAddOns,
} from '@/lib/api';
import { describeAiPackageIncludes, parseBillingPrice } from '@/lib/api/admin-users';
import { CREDITS_PER_WRITING_OR_SPEAKING_ACTIVITY } from '@/lib/format-allowance';

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
  // Server flag: Name/Description are owned by Subscriptions & Packages.
  packageManaged?: boolean;
}

const MANAGED_COPY_HINT = 'Managed in Subscriptions & Packages.';
const READ_ONLY_FIELD_CLASS = 'cursor-not-allowed text-muted';

const GROUP_OPTIONS = [
  { value: 'full', label: 'Full package (Shared AI credits plus Listening/Reading)' },
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
  sharedCredits: string;
  flexibleCredits: string;
  writingCredits: string;
  speakingCredits: string;
  mocks: string;
  listeningTests: string;
  readingTests: string;
  passGuaranteeMonths: string;
  unlimitedListening: boolean;
  unlimitedReading: boolean;
  unlimitedGrading: boolean;
  priorityQueue: boolean;
  feedbackReports: boolean;
  personalisedStudyRecs: boolean;
  features: string[];
  packageManaged: boolean;
  /** The row's stored entitlement JSON; keys the form does not model are carried through a save. */
  entitlements: Record<string, unknown>;
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
    sharedCredits: '',
    flexibleCredits: '',
    writingCredits: '',
    speakingCredits: '',
    mocks: '',
    listeningTests: '',
    readingTests: '',
    passGuaranteeMonths: '',
    unlimitedListening: false,
    unlimitedReading: false,
    unlimitedGrading: false,
    priorityQueue: false,
    feedbackReports: true,
    personalisedStudyRecs: false,
    features: [],
    packageManaged: false,
    entitlements: {},
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
    sharedCredits: readInt('shared_credits'),
    flexibleCredits: readInt('flexible_credits'),
    writingCredits: readInt('writing_only_credits'),
    speakingCredits: readInt('speaking_only_credits'),
    // The server resolves mocks as mock_exams ?? mockFull, so a legacy row that only
    // carries the mockFull alias opens with its real count instead of a blank box.
    mocks: readInt('mock_exams') || readInt('mockFull'),
    listeningTests: ent.listening_tests == null ? '' : readInt('listening_tests'),
    readingTests: ent.reading_tests == null ? '' : readInt('reading_tests'),
    passGuaranteeMonths: readInt('pass_guarantee_extension_months'),
    unlimitedListening: ent.unlimited_listening === true || (Object.prototype.hasOwnProperty.call(ent, 'listening_tests') && ent.listening_tests == null),
    unlimitedReading: ent.unlimited_reading === true || (Object.prototype.hasOwnProperty.call(ent, 'reading_tests') && ent.reading_tests == null),
    unlimitedGrading: ent.unlimited_grading === true,
    priorityQueue: ent.priority_queue === true,
    feedbackReports: ent.feedback_reports !== false,
    personalisedStudyRecs: ent.personalised_study_recs === true,
    features: Array.isArray(row.aiFeatures) ? row.aiFeatures.filter((f) => typeof f === 'string') : [],
    packageManaged: row.packageManaged === true
      || (!!row.code && resolveWebsitePackageByCode(row.code) !== undefined),
    entitlements: { ...ent },
  };
}

function intOrNull(v: string): number | null {
  const t = v.trim();
  if (t === '') return null;
  const n = Number(t);
  return Number.isFinite(n) ? Math.max(0, Math.round(n)) : null;
}

function buildEntitlementsJson(f: FormState): string {
  const writingCredits = intOrNull(f.writingCredits) ?? 0;
  const speakingCredits = intOrNull(f.speakingCredits) ?? 0;
  const obj: Record<string, unknown> = {
    // Keys the form does not model (ai_credits, ...) survive a save. A mockFull alias is
    // kept too but is shadowed by the mock_exams written below (server: mock_exams ?? mockFull).
    ...f.entitlements,
    package_type: f.packageType.trim() || f.group,
    listening_tests: f.unlimitedListening ? null : (intOrNull(f.listeningTests) ?? 0),
    reading_tests: f.unlimitedReading ? null : (intOrNull(f.readingTests) ?? 0),
    mock_exams: intOrNull(f.mocks) ?? 0,
    unlimited_grading: f.unlimitedGrading,
    unlimited_listening: f.unlimitedListening,
    unlimited_reading: f.unlimitedReading,
    priority_queue: f.priorityQueue,
    feedback_reports: f.feedbackReports,
    personalised_study_recs: f.personalisedStudyRecs,
  };
  // A credit pool is written when it holds credits or the row already stated it. A new explicit
  // `shared_credits: 0` would otherwise win the storefront's shared ?? flexible fallback and hide
  // the Flexible pool of a package that never had a Shared pool.
  const pools: Array<[string, number]> = [
    ['shared_credits', intOrNull(f.sharedCredits) ?? 0],
    ['flexible_credits', intOrNull(f.flexibleCredits) ?? 0],
    ['writing_only_credits', writingCredits],
    ['speaking_only_credits', speakingCredits],
  ];
  for (const [key, value] of pools) {
    if (value > 0 || Object.prototype.hasOwnProperty.call(f.entitlements, key)) obj[key] = value;
  }
  // writing_items / speaking_items are the letter / card counts the learner storefront shows,
  // so they follow the credit pools (2 credits per letter or card) instead of going stale.
  if (writingCredits > 0) obj.writing_items = Math.floor(writingCredits / CREDITS_PER_WRITING_OR_SPEAKING_ACTIVITY);
  else delete obj.writing_items;
  if (speakingCredits > 0) obj.speaking_items = Math.floor(speakingCredits / CREDITS_PER_WRITING_OR_SPEAKING_ACTIVITY);
  else delete obj.speaking_items;
  const pg = intOrNull(f.passGuaranteeMonths);
  if (pg != null && pg > 0) obj.pass_guarantee_extension_months = pg;
  else delete obj.pass_guarantee_extension_months;
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
  const [deleteTarget, setDeleteTarget] = useState<AdminAddOnRow | null>(null);
  const [deleteConfirmInput, setDeleteConfirmInput] = useState('');
  const [deleting, setDeleting] = useState(false);

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

  // Edit mode only: a new package's code is not known yet, so its fields stay editable.
  const copyManaged = !!form.id && form.packageManaged;

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
    // parseBillingPrice rejects a blank box: Number('') is 0 and would publish a free package.
    const price = parseBillingPrice(form.price);
    if (price === null) {
      setFeedback({ tone: 'error', message: 'Enter a price of 0 or more, for example 49 or 49.99.' });
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
      displayOrder: intOrNull(form.displayOrder) ?? 0,
      isRecurring: false,
      appliesToAllPlans: true,
      isStackable: true,
      status: form.status,
      grantEntitlementsJson: buildEntitlementsJson(form),
      addonKind: 'ai_package',
      // Credits reach the learner only through the AI package wallet, from the entitlement JSON.
      // These legacy columns feed the tutor-review / private-speaking counters on every purchase
      // (SubscriptionBundleInitializer.ApplyAddOnEntitlements), so they must stay 0 for an AI package.
      grantCredits: 0,
      lettersGranted: 0,
      sessionsGranted: 0,
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

  const requestDelete = useCallback((row: AdminAddOnRow) => {
    setDeleteTarget(row);
    setDeleteConfirmInput('');
    setFeedback(null);
  }, []);

  const handleDelete = useCallback(async () => {
    if (!canWrite) {
      setFeedback({ tone: 'error', message: 'You have read-only billing access.' });
      return;
    }
    if (!deleteTarget) return;
    setDeleting(true);
    setFeedback(null);
    try {
      await deleteAdminBillingAddOn(deleteTarget.id);
      setDeleteTarget(null);
      setDeleteConfirmInput('');
      setFeedback({ tone: 'success', message: `AI package "${deleteTarget.name}" hard-deleted.` });
      await load();
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Failed to delete AI package.';
      setFeedback({
        tone: 'error',
        message: /in[_ ]use|archive|subscription|quote/i.test(message)
          ? `${message} Use Edit to archive instead (Status = Archived).`
          : message,
      });
    } finally {
      setDeleting(false);
    }
  }, [canWrite, deleteTarget, load]);

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
                <th scope="col" className="px-3 py-3">Includes</th>
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
                  const includes = describeAiPackageIncludes(row.grantEntitlements);
                  return (
                    <tr key={row.id} className="align-middle">
                      <td className="px-3 py-3">
                        <div className="font-semibold text-navy">{row.name}</div>
                        <div className="text-xs text-muted">{row.code}</div>
                      </td>
                      <td className="px-3 py-3"><Badge variant="info">{row.aiPackageGroup || 'full'}</Badge></td>
                      <td className="px-3 py-3 tabular-nums">{row.currency} {row.price}</td>
                      <td className="px-3 py-3 text-xs" data-testid={`ai-package-includes-${row.code}`}>
                        {includes.length > 0
                          ? includes.map((line) => <div key={line}>{line}</div>)
                          : <span className="text-muted">Nothing granted</span>}
                      </td>
                      <td className="px-3 py-3">{validityLabel(row.durationDays ?? 0)}</td>
                      <td className="px-3 py-3">
                        <Badge variant={(row.status ?? 'active').toLowerCase() === 'active' ? 'success' : 'default'}>
                          {(row.status ?? 'active').toLowerCase()}
                        </Badge>
                      </td>
                      <td className="px-3 py-3 text-right">
                        <div className="flex justify-end gap-2">
                          <Button variant="secondary" size="sm" onClick={() => openEdit(row)}>Edit</Button>
                          <Button
                            variant="destructive"
                            size="sm"
                            onClick={() => requestDelete(row)}
                            disabled={!canWrite}
                            aria-label={`Hard delete AI package ${row.name}`}
                          >
                            <Trash2 className="h-4 w-4" /> Delete
                          </Button>
                        </div>
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
            <Input
              label="Name"
              value={form.name}
              onChange={(e) => setField('name', e.target.value)}
              placeholder="Quick Check"
              readOnly={copyManaged}
              className={copyManaged ? READ_ONLY_FIELD_CLASS : undefined}
              hint={copyManaged ? MANAGED_COPY_HINT : undefined}
            />
            <Input label="Code" value={form.code} onChange={(e) => setField('code', e.target.value)} placeholder="pkg_quick_check" hint={form.id ? 'Code is immutable after creation.' : 'Leave blank to auto-generate from the name.'} disabled={!!form.id} />
          </div>

          <Textarea
            label="Description"
            value={form.description}
            onChange={(e) => setField('description', e.target.value)}
            placeholder="5 Shared AI credits plus Listening and Reading practice, valid for 30 days."
            readOnly={copyManaged}
            className={copyManaged ? READ_ONLY_FIELD_CLASS : undefined}
            hint={copyManaged ? MANAGED_COPY_HINT : undefined}
          />
          {copyManaged ? (
            <p className="text-xs leading-5 text-muted" data-testid="ai-package-managed-note">
              The learner-facing name and description are derived from the package record.{' '}
              <Link
                href={`/admin/billing/subscriptions-packages?code=${encodeURIComponent(form.code)}`}
                className="font-medium text-primary hover:underline"
              >
                Edit them in Subscriptions &amp; Packages
              </Link>
              . Price, currency, validity, status and entitlements stay editable here.
            </p>
          ) : null}

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
              <Input label="Shared credits" inputMode="numeric" value={form.sharedCredits} onChange={(e) => setField('sharedCredits', e.target.value)} hint="Any sub-test. Writing/Speaking cost 2 Shared." />
              <Input label="Flexible W/S credits" inputMode="numeric" value={form.flexibleCredits} onChange={(e) => setField('flexibleCredits', e.target.value)} hint="Writing or Speaking only. 1 letter/card = 2 credits. Cannot fund Listening/Reading." />
              <Input label="Writing credits" inputMode="numeric" value={form.writingCredits} onChange={(e) => setField('writingCredits', e.target.value)} hint="1 letter = 2 credits (e.g. 6 credits = 3 letters)" />
              <Input label="AI Speaking Credits (practice)" inputMode="numeric" value={form.speakingCredits} onChange={(e) => setField('speakingCredits', e.target.value)} hint="Self-practice cards + exam fallback; 1 card = 2 credits, full two-card exam = 4" />
              <Input label="Full Mock Exam Credits" inputMode="numeric" value={form.mocks} onChange={(e) => setField('mocks', e.target.value)} hint="For Speaking: 1 credit = 1 whole two-card exam (Card A + B), separate from AI Speaking Credits above" />
              <Input label="Listening tests" inputMode="numeric" value={form.listeningTests} onChange={(e) => setField('listeningTests', e.target.value)} hint="Blank = none. Use Unlimited Listening instead of a blank count." disabled={form.unlimitedListening} />
              <Input label="Reading tests" inputMode="numeric" value={form.readingTests} onChange={(e) => setField('readingTests', e.target.value)} hint="Blank = none. Use Unlimited Reading instead of a blank count." disabled={form.unlimitedReading} />
              <Input label="Pass-guarantee (months)" inputMode="numeric" value={form.passGuaranteeMonths} onChange={(e) => setField('passGuaranteeMonths', e.target.value)} />
              <Input label="Package type (advanced)" value={form.packageType} onChange={(e) => setField('packageType', e.target.value)} hint="Defaults to the group." />
            </div>
            <div className="mt-4 grid gap-3 sm:grid-cols-3">
              <Checkbox label="Unlimited Listening" checked={form.unlimitedListening} onChange={(e) => setField('unlimitedListening', e.target.checked)} />
              <Checkbox label="Unlimited Reading" checked={form.unlimitedReading} onChange={(e) => setField('unlimitedReading', e.target.checked)} />
              <Checkbox label="Unlimited Writing/Speaking grading" checked={form.unlimitedGrading} onChange={(e) => setField('unlimitedGrading', e.target.checked)} />
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

      <BillingConfirmDialog
        open={deleteTarget !== null}
        title="Hard-delete this AI package?"
        description={
          deleteTarget
            ? `Permanently removes "${deleteTarget.name}" (${deleteTarget.code}): the AI package add-on row, all its version history, and the linked content package. This cannot be undone. If the package has any historical subscription items or quotes, the server will refuse and ask you to archive instead.`
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
