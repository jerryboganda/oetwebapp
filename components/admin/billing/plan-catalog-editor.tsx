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
import {
  fetchProfessionCatalog,
  professionCatalogOptions,
  PROFESSION_CATALOG_FALLBACK,
  type ProfessionCatalogEntry,
} from '@/lib/api/professions';
import {
  PlanVideoOverrides,
  EMPTY_PLAN_VIDEO_OVERRIDES,
  type PlanVideoOverridesValue,
} from './plan-video-overrides';

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
  profession?: string;
  accessDurationDays?: number;
  deliveryMethod?: string;
  telegramInviteUrl?: string | null;
  deliveryInstructions?: string | null;
  contentOverridesJson?: string | null;
  entitlements?: Record<string, unknown> | null;
}

const SUBTESTS = ['listening', 'reading', 'writing', 'speaking'] as const;

// Mirrors backend DeliveryMethods (Domain/Enums.cs). Anything but automatic_web parks the
// buyer's subscription at Pending + FulfilmentStatus=pending_manual until an admin marks it
// fulfilled — so switching a live plan away from automatic_web stops new orders self-activating.
const DELIVERY_METHOD_OPTIONS = [
  { value: 'automatic_web', label: 'Automatic Web Access' },
  { value: 'manual_web', label: 'Manual Web Access' },
  { value: 'telegram', label: 'Telegram Access' },
  { value: 'manual_material', label: 'Manual Material Delivery' },
];
const MANUAL_DELIVERY_METHODS = ['manual_web', 'telegram', 'manual_material'];
const DEFAULT_ACCESS_DURATION_DAYS = 180;
const MATERIAL_OVERRIDES_PLACEHOLDER = '{\n  "include": [],\n  "exclude": []\n}';

// 'all' is a billing-only pseudo-profession (every discipline tab); the rest come from the
// canonical SignupProfessionCatalog so this list can never drift from registration again.
const ALL_PROFESSIONS_OPTION = { value: 'all', label: 'All disciplines' };

function professionOptionsFor(catalog: ProfessionCatalogEntry[], current: string) {
  const options = [ALL_PROFESSIONS_OPTION, ...professionCatalogOptions(catalog)];
  // Keep an unknown stored value selectable, so opening a legacy plan and pressing Save
  // never silently reallocates it to "All disciplines".
  if (current && !options.some((option) => option.value === current)) {
    options.push({ value: current, label: `${current} — not in catalog` });
  }
  return options;
}

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
  profession: string;
  accessDurationDays: string;
  deliveryMethod: string;
  telegramInviteUrl: string;
  deliveryInstructions: string;
  videoOverrides: PlanVideoOverridesValue;
  materialOverridesJson: string;
  invoiceDownloads: boolean;
  entitlements: Record<string, unknown>;
}

function emptyForm(): FormState {
  return {
    id: null, code: '', name: '', description: '', price: '', currency: 'GBP',
    interval: 'month', durationMonths: '1', includedCredits: '0', trialDays: '0',
    displayOrder: '0', isVisible: true, isRenewable: true, status: 'active',
    subtests: [], modules: [...DEFAULT_NEW_PLAN_MODULES], profession: 'all',
    accessDurationDays: String(DEFAULT_ACCESS_DURATION_DAYS), deliveryMethod: 'automatic_web',
    telegramInviteUrl: '', deliveryInstructions: '',
    videoOverrides: { ...EMPTY_PLAN_VIDEO_OVERRIDES }, materialOverridesJson: '',
    invoiceDownloads: false, entitlements: {},
  };
}

function toForm(row: AdminPlanRow): FormState {
  const ent = (row.entitlements ?? {}) as Record<string, unknown>;
  const { videoOverrides, materialOverridesJson } = parseContentOverrides(row.contentOverridesJson);
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
    profession: (row.profession && row.profession.trim()) || 'all',
    accessDurationDays: String(row.accessDurationDays ?? DEFAULT_ACCESS_DURATION_DAYS),
    deliveryMethod: (row.deliveryMethod && row.deliveryMethod.trim()) || 'automatic_web',
    telegramInviteUrl: row.telegramInviteUrl ?? '',
    deliveryInstructions: row.deliveryInstructions ?? '',
    videoOverrides,
    materialOverridesJson,
    invoiceDownloads: ent.invoiceDownloadsAvailable === true,
    entitlements: ent,
  };
}

function intOr(v: string, fallback = 0): number {
  const n = Number(v.trim());
  return Number.isFinite(n) ? Math.max(0, Math.round(n)) : fallback;
}

/**
 * Splits the stored ContentOverridesJson blob (shape: {videos:{include,exclude},
 * materialFolders:{include,exclude}}) into the video picker's structured arrays and a raw
 * textarea string for the materialFolders node only. Unparseable stored JSON is dumped into the
 * material textarea verbatim rather than discarded, so an admin can see and repair what is
 * actually persisted; the video picker just starts empty in that case.
 */
function parseContentOverrides(raw: string | null | undefined): {
  videoOverrides: PlanVideoOverridesValue;
  materialOverridesJson: string;
} {
  const empty = { videoOverrides: { ...EMPTY_PLAN_VIDEO_OVERRIDES }, materialOverridesJson: '' };
  if (!raw || !raw.trim()) return empty;
  try {
    const parsed = JSON.parse(raw) as unknown;
    if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) return empty;
    const root = parsed as Record<string, unknown>;

    const videoOverrides: PlanVideoOverridesValue = { include: [], exclude: [] };
    const videosNode = root.videos;
    if (videosNode && typeof videosNode === 'object' && !Array.isArray(videosNode)) {
      const node = videosNode as Record<string, unknown>;
      if (Array.isArray(node.include)) {
        videoOverrides.include = node.include.filter((v): v is string => typeof v === 'string');
      }
      if (Array.isArray(node.exclude)) {
        videoOverrides.exclude = node.exclude.filter((v): v is string => typeof v === 'string');
      }
    }

    const materialNode = root.materialFolders ?? root.material_folders;
    const materialOverridesJson = materialNode ? JSON.stringify(materialNode, null, 2) : '';

    return { videoOverrides, materialOverridesJson };
  } catch {
    return { videoOverrides: { ...EMPTY_PLAN_VIDEO_OVERRIDES }, materialOverridesJson: raw };
  }
}

/** Null = valid (empty is valid and means "no overrides"); string = the message to show. */
function materialOverridesError(raw: string): string | null {
  const trimmed = raw.trim();
  if (!trimmed) return null;
  let parsed: unknown;
  try {
    parsed = JSON.parse(trimmed);
  } catch (error) {
    return error instanceof Error ? `Invalid JSON — ${error.message}` : 'Invalid JSON.';
  }
  if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) {
    return 'Must be a JSON object, e.g. {"exclude":["<folder-id>"]}.';
  }
  for (const [listName, list] of Object.entries(parsed as Record<string, unknown>)) {
    if (listName !== 'include' && listName !== 'exclude') {
      return `"${listName}" is not read. Use "include" or "exclude".`;
    }
    if (!Array.isArray(list) || list.some((item) => typeof item !== 'string')) {
      return `"${listName}" must be an array of folder id strings.`;
    }
  }
  return null;
}

/** Recombines the video picker state + the raw materialFolders textarea into one
 * ContentOverridesJson blob. Empty string = no overrides at all. */
function buildContentOverridesJson(videoOverrides: PlanVideoOverridesValue, materialOverridesRaw: string): string {
  const overrides: Record<string, unknown> = {};
  if (videoOverrides.include.length > 0 || videoOverrides.exclude.length > 0) {
    const videos: Record<string, string[]> = {};
    if (videoOverrides.include.length > 0) videos.include = videoOverrides.include;
    if (videoOverrides.exclude.length > 0) videos.exclude = videoOverrides.exclude;
    overrides.videos = videos;
  }
  const trimmedMaterial = materialOverridesRaw.trim();
  if (trimmedMaterial) {
    overrides.materialFolders = JSON.parse(trimmedMaterial);
  }
  return Object.keys(overrides).length > 0 ? JSON.stringify(overrides) : '';
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
  const [professionCatalog, setProfessionCatalog] = useState<ProfessionCatalogEntry[]>(PROFESSION_CATALOG_FALLBACK);

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

  // fetchProfessionCatalog never rejects — it resolves to the static fallback when the API is
  // unreachable, so the dropdown always has something selectable.
  useEffect(() => {
    let cancelled = false;
    void fetchProfessionCatalog().then((entries) => {
      if (!cancelled) setProfessionCatalog(entries);
    });
    return () => { cancelled = true; };
  }, []);

  const sortedRows = useMemo(
    () => [...rows].sort((a, b) => (a.displayOrder ?? 0) - (b.displayOrder ?? 0) || a.price - b.price),
    [rows],
  );

  const professionOptions = useMemo(
    () => professionOptionsFor(professionCatalog, form.profession),
    [professionCatalog, form.profession],
  );

  const isManualDelivery = MANUAL_DELIVERY_METHODS.includes(form.deliveryMethod);

  // Live parse feedback while typing; handleSave re-checks so an invalid blob can never be sent.
  const materialsOverridesError = useMemo(
    () => materialOverridesError(form.materialOverridesJson),
    [form.materialOverridesJson],
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

    // Invalid JSON must never reach the API: the backend silently ignores overrides it cannot
    // parse, so a typo here would look saved but grant nothing.
    const overridesProblem = materialOverridesError(form.materialOverridesJson);
    if (overridesProblem) { setFeedback({ tone: 'error', message: `Material folder overrides: ${overridesProblem}` }); return; }

    // Validated rather than defaulted: intOr('') is 0, and a silent 0 would expire every new
    // buyer's access the instant they paid.
    const accessDurationDays = Number(form.accessDurationDays.trim());
    if (!Number.isInteger(accessDurationDays) || accessDurationDays < 1) {
      setFeedback({ tone: 'error', message: 'Access duration must be a whole number of days, 1 or more.' });
      return;
    }

    const telegramInviteUrl = form.telegramInviteUrl.trim();
    if (form.deliveryMethod === 'telegram' && !telegramInviteUrl) {
      setFeedback({ tone: 'error', message: 'Telegram Access needs an invite link — the learner has no other way in.' });
      return;
    }
    if (telegramInviteUrl && !/^https:\/\//i.test(telegramInviteUrl)) {
      setFeedback({ tone: 'error', message: 'Telegram invite link must start with https://.' });
      return;
    }

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
      profession: form.profession,
      accessDurationDays,
      deliveryMethod: form.deliveryMethod,
      // Empty string, not undefined: clearing a stale invite link has to be expressible.
      telegramInviteUrl,
      deliveryInstructions: form.deliveryInstructions.trim(),
      contentOverridesJson: buildContentOverridesJson(form.videoOverrides, form.materialOverridesJson),
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
            <Select label="Profession" value={form.profession} onChange={(e) => setField('profession', e.target.value)} options={professionOptions} hint="Discipline tab this plan appears under, and the profession a buyer must be registered as. “All disciplines” shows it under every tab." />
            <Input
              label="Access duration (days)"
              inputMode="numeric"
              value={form.accessDurationDays}
              onChange={(e) => setField('accessDurationDays', e.target.value)}
              hint="How long access lasts from the start date. 180 = 6 months."
            />
          </div>

          <div className="rounded-2xl border border-border bg-background-light/50 p-4">
            <p className="mb-1 text-sm font-semibold text-navy">Included subtests</p>
            <p className="mb-3 text-xs text-muted">
              The subtest half of the content model: a buyer sees content for these subtests, in their own
              registered profession. Leave every box unticked for <strong>all four subtests</strong> — that is how a
              generic “Speaking Crash Course” auto-maps to whichever profession the buyer registered under.
            </p>
            <div className="grid gap-3 sm:grid-cols-4">
              {SUBTESTS.map((subtest) => (
                <Checkbox key={subtest} label={subtest[0].toUpperCase() + subtest.slice(1)} checked={form.subtests.includes(subtest)} onChange={() => toggleSubtest(subtest)} />
              ))}
            </div>
            <p className="mt-3 text-xs font-medium text-navy">
              {form.subtests.length === 0
                ? 'Nothing ticked → all subtests included (Listening, Reading, Writing, Speaking).'
                : `Only ${form.subtests.map((s) => s[0].toUpperCase() + s.slice(1)).join(', ')} included.`}
            </p>
          </div>

          <div className="rounded-2xl border border-border bg-background-light/50 p-4">
            <p className="mb-1 text-sm font-semibold text-navy">Delivery</p>
            <p className="mb-3 text-xs text-muted">
              How the buyer receives this package. Anything other than Automatic Web Access holds the order at
              <strong> Pending manual fulfilment</strong> after payment — access and any invite link stay hidden until an
              admin marks it fulfilled.
            </p>
            <div className="grid gap-4 sm:grid-cols-2">
              <Select
                label="Delivery method"
                value={form.deliveryMethod}
                onChange={(e) => setField('deliveryMethod', e.target.value)}
                options={DELIVERY_METHOD_OPTIONS}
              />
            </div>
            {isManualDelivery ? (
              <div className="mt-4 space-y-4">
                {form.deliveryMethod === 'telegram' ? (
                  <Input
                    label="Telegram invite link"
                    value={form.telegramInviteUrl}
                    onChange={(e) => setField('telegramInviteUrl', e.target.value)}
                    placeholder="https://t.me/+…"
                    hint="Revealed on the learner’s order page only after an admin marks the order fulfilled."
                  />
                ) : null}
                <Textarea
                  label="Delivery instructions"
                  value={form.deliveryInstructions}
                  onChange={(e) => setField('deliveryInstructions', e.target.value)}
                  rows={3}
                  maxLength={2000}
                  hint="Shown to the learner once the order is fulfilled — e.g. what was sent, or what to do next."
                />
              </div>
            ) : null}
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

          <div className="rounded-2xl border border-border bg-background-light/50 p-4">
            <p className="mb-1 text-sm font-semibold text-navy">Video overrides</p>
            <p className="mb-3 text-xs text-muted">
              The subtests above and the buyer’s profession already decide which videos this plan reaches; use this to
              add or remove specific videos on top — e.g. give this plan a genuinely different video set from another
              plan that shares the same subtests. Never overrides the Videos module toggle above: if that’s disabled,
              nothing here unlocks anything.
            </p>
            <PlanVideoOverrides
              value={form.videoOverrides}
              onChange={(videoOverrides) => setField('videoOverrides', videoOverrides)}
              disabled={!canWrite}
            />
          </div>

          <div className="rounded-2xl border border-border bg-background-light/50 p-4">
            <p className="mb-1 text-sm font-semibold text-navy">Material folder overrides (advanced)</p>
            <p className="mb-3 text-xs text-muted">
              Escape hatch only. Leave blank for no overrides. An <code>include</code> wins over both the
              subtest/profession scope and an <code>exclude</code>, but never over a module disabled above.
            </p>
            <Textarea
              label="Overrides JSON"
              value={form.materialOverridesJson}
              onChange={(e) => setField('materialOverridesJson', e.target.value)}
              rows={5}
              spellCheck={false}
              className="font-mono text-xs"
              placeholder={MATERIAL_OVERRIDES_PLACEHOLDER}
              error={materialsOverridesError ?? undefined}
              hint={materialsOverridesError ? undefined : 'Valid. Ids are material folder ids.'}
            />
          </div>

          <div className="grid gap-3 sm:grid-cols-3">
            <Checkbox label="Visible to learners" checked={form.isVisible} onChange={(e) => setField('isVisible', e.target.checked)} />
            <Checkbox label="Auto-renewing" checked={form.isRenewable} onChange={(e) => setField('isRenewable', e.target.checked)} />
            <Checkbox label="Invoice downloads" checked={form.invoiceDownloads} onChange={(e) => setField('invoiceDownloads', e.target.checked)} />
          </div>

          <p className="text-xs text-muted">
            Advanced catalog fields (bundles, eligibility, version history) live on{' '}
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
