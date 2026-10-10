'use client';

import Link from 'next/link';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { RotateCcw, Save, Search } from 'lucide-react';
import { Badge, statusToTone } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Input } from '@/components/admin/ui/input';
import { LinesTextarea } from '@/components/admin/ui/lines-textarea';
import { NativeSelect } from '@/components/admin/ui/native-select';
import { Textarea } from '@/components/admin/ui/textarea';
import { InlineAlert } from '@/components/ui/alert';
import {
  fetchAdminBillingAddOns,
  fetchAdminCatalogPresentation,
  isApiError,
  saveAdminWebsitePackages,
} from '@/lib/api';
import { describeAiPackageIncludes, parseBillingPrice as parsePrice } from '@/lib/api/admin-users';
import type {
  AdminCatalogAddOnSummary,
  AdminCatalogPlanSummary,
  CatalogPresentation,
  CatalogPresentationRevisions,
  PackageCommercialUpdate,
  WebsitePackageOverlay,
  WebsiteSectionOverlay,
} from '@/lib/catalog-presentation';
import {
  WEBSITE_PACKAGES,
  WEBSITE_SECTIONS,
  applyWebsitePackageOverlay,
  defaultSectionForCategory,
  isWebsiteSectionKey,
  resolveWebsitePackageByCode,
  type WebsitePackage,
  type WebsiteSectionKey,
} from '@/lib/catalog-website-packages';
import { BillingConfirmDialog } from './confirm-dialog';

const sectionTitle = 'text-base font-bold text-admin-fg-default';
const fieldLabel = 'mb-1 block text-xs font-semibold uppercase tracking-wide text-admin-fg-muted';
const touchBtn = 'min-h-11 sm:min-h-0';
const BILLING_PRICING_HREF = '/admin/billing/pricing';
const DEFAULT_TEXT_HINT = 'Leave empty to use the default text.';
const LIST_HINT = 'One per line. Clear the box to show none; use Reset this package to restore the default.';

// Server limits (CatalogPresentationDocument). The server rejects over-limit input, so the
// editor checks them first and keeps Save disabled instead of failing the whole request.
const MAX_NAME = 128;
const MAX_CATEGORY = 120;
const MAX_FORMAT_LINE = 240;
const MAX_DESCRIPTION = 1024;
const MAX_BEST_FOR = 500;
const MAX_SECTION_TITLE = 120;
const MAX_SECTION_DESCRIPTION = 600;

type OverlayMap = Record<string, WebsitePackageOverlay>;
type SectionMap = Record<string, WebsiteSectionOverlay>;
type SelectOption = { value: string; label: string };

interface LiveRow {
  kind: 'plan' | 'addon';
  code: string;
  name: string;
  price: number;
  currency: string;
  interval: string;
  status: string;
  isVisible: boolean;
  isDraft: boolean;
  activeSubscribers: number;
  /** Read-only entitlement and quota facts of the billing record; empty when the backend sent none. */
  access: AccessFact[];
}

interface AccessFact {
  label: string;
  value: string;
}

interface PackageEntry {
  /** Overlay key: the static website code, or the lower-cased plan code for custom packages. */
  code: string;
  base: WebsitePackage;
  isCustom: boolean;
  defaultSection: WebsiteSectionKey;
  live: LiveRow | null;
}

/** Raw (string) pricing form state for one billing row; kept as typed, parsed only on save. */
interface CommercialDraft {
  price: string;
  currency: string;
  interval: string;
  status: string;
  isVisible: boolean;
}

interface CommercialDiff {
  price?: number | string;
  currency?: string;
  interval?: string;
  status?: string;
  isVisible?: boolean;
}

type OverlayErrorField =
  | 'name'
  | 'packageNo'
  | 'category'
  | 'formatLine'
  | 'description'
  | 'bestFor'
  | 'metaChips'
  | 'badges'
  | 'features';
type OverlayErrors = Partial<Record<OverlayErrorField, string>>;
type CommercialErrors = Partial<Record<'price' | 'currency', string>>;

const INTERVAL_LABELS: Record<string, string> = {
  month: 'Monthly',
  year: 'Yearly',
  one_time: 'One-time',
};
const PLAN_INTERVAL_OPTIONS: SelectOption[] = ['month', 'year', 'one_time'].map((value) => ({ value, label: INTERVAL_LABELS[value] }));
const ADDON_INTERVAL_OPTIONS: SelectOption[] = ['one_time', 'month', 'year'].map((value) => ({ value, label: INTERVAL_LABELS[value] }));
const STATUS_OPTIONS: SelectOption[] = [
  { value: 'active', label: 'Active (visible)' },
  { value: 'inactive', label: 'Inactive' },
  { value: 'draft', label: 'Draft' },
  { value: 'archived', label: 'Archived' },
];

function Toggle({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <label className="flex min-h-11 items-center gap-3 text-sm text-admin-fg-default sm:min-h-0 sm:gap-2">
      <input
        type="checkbox"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
        className="h-5 w-5 rounded border-admin-border sm:h-4 sm:w-4"
      />
      {label}
    </label>
  );
}

// ---------------------------------------------------------------------------
// Pure helpers
// ---------------------------------------------------------------------------

const nonBlank = (value: unknown): string | undefined =>
  typeof value === 'string' && value.trim() !== '' ? value : undefined;

function humaniseCategory(category: string | null | undefined): string {
  const text = (category ?? '').replace(/[_-]+/g, ' ').trim();
  return text ? text.charAt(0).toUpperCase() + text.slice(1) : '';
}

function withCurrent(options: SelectOption[], current: string): SelectOption[] {
  return current && !options.some((option) => option.value === current)
    ? [...options, { value: current, label: current }]
    : options;
}

/** JSON with recursively sorted keys, so two equal documents always compare equal. */
function stable(value: unknown): string {
  return JSON.stringify(value, (_key, v: unknown) => {
    if (v && typeof v === 'object' && !Array.isArray(v)) {
      const source = v as Record<string, unknown>;
      const sorted: Record<string, unknown> = {};
      for (const key of Object.keys(source).sort()) sorted[key] = source[key];
      return sorted;
    }
    return v;
  });
}

/** Drop blank strings and empty entries; an explicit `[]` is kept (it means "show none"). */
function cleanOverlay(overlay: WebsitePackageOverlay | undefined): WebsitePackageOverlay | null {
  if (!overlay || typeof overlay !== 'object') return null;
  const out: WebsitePackageOverlay = {};
  const name = nonBlank(overlay.name);
  if (name !== undefined) out.name = name;
  if (typeof overlay.packageNo === 'number') out.packageNo = overlay.packageNo;
  const category = nonBlank(overlay.category);
  if (category !== undefined) out.category = category;
  if (isWebsiteSectionKey(overlay.section)) out.section = overlay.section;
  const formatLine = nonBlank(overlay.formatLine);
  if (formatLine !== undefined) out.formatLine = formatLine;
  const description = nonBlank(overlay.description);
  if (description !== undefined) out.description = description;
  if (Array.isArray(overlay.metaChips)) out.metaChips = overlay.metaChips;
  if (Array.isArray(overlay.badges)) out.badges = overlay.badges;
  if (Array.isArray(overlay.features)) out.features = overlay.features;
  const bestFor = nonBlank(overlay.bestFor);
  if (bestFor !== undefined) out.bestFor = bestFor;
  if (typeof overlay.featured === 'boolean') out.featured = overlay.featured;
  return Object.keys(out).length > 0 ? out : null;
}

function cleanByCode(map: OverlayMap): OverlayMap {
  const out: OverlayMap = Object.create(null);
  for (const code of Object.keys(map).sort()) {
    const cleaned = cleanOverlay(map[code]);
    if (cleaned) out[code] = cleaned;
  }
  return out;
}

function cleanSections(map: SectionMap): SectionMap {
  const out: SectionMap = Object.create(null);
  for (const key of Object.keys(map).sort()) {
    const title = nonBlank(map[key]?.title);
    const description = nonBlank(map[key]?.description);
    if (title === undefined && description === undefined) continue;
    const next: WebsiteSectionOverlay = {};
    if (title !== undefined) next.title = title;
    if (description !== undefined) next.description = description;
    out[key] = next;
  }
  return out;
}

/** Stored overlays are keyed by static code; fold any legacy-code key onto its canonical code (canonical wins). */
function normaliseByCode(raw: OverlayMap | undefined): OverlayMap {
  const out: OverlayMap = Object.create(null);
  if (!raw) return out;
  const pairs: Array<{ key: string; canonical: string; value: WebsitePackageOverlay }> = [];
  for (const [rawKey, value] of Object.entries(raw)) {
    if (!value || typeof value !== 'object') continue;
    const key = rawKey.trim().toLowerCase();
    pairs.push({ key, canonical: resolveWebsitePackageByCode(key)?.code ?? key, value });
  }
  for (const pair of pairs) if (pair.key !== pair.canonical) out[pair.canonical] = pair.value;
  for (const pair of pairs) if (pair.key === pair.canonical) out[pair.canonical] = pair.value;
  return out;
}

function lengthError(label: string, value: unknown, max: number): string | undefined {
  return typeof value === 'string' && value.trim().length > max
    ? `${label} must be ${max} characters or fewer (currently ${value.trim().length}).`
    : undefined;
}

function listError(label: string, value: unknown, maxItems: number, maxLength: number): string | undefined {
  if (!Array.isArray(value)) return undefined;
  if (value.length > maxItems) return `${label}: at most ${maxItems} lines (currently ${value.length}).`;
  if (value.some((line) => typeof line === 'string' && line.length > maxLength)) {
    return `${label}: each line must be ${maxLength} characters or fewer.`;
  }
  return undefined;
}

function validateOverlay(overlay: WebsitePackageOverlay): OverlayErrors {
  const errors: OverlayErrors = {};
  if (typeof overlay.name === 'string') {
    errors.name = overlay.name.trim() === '' ? 'Name is required.' : lengthError('Name', overlay.name, MAX_NAME);
  }
  errors.category = lengthError('Category', overlay.category, MAX_CATEGORY);
  errors.formatLine = lengthError('Format line', overlay.formatLine, MAX_FORMAT_LINE);
  errors.description = lengthError('Description', overlay.description, MAX_DESCRIPTION);
  errors.bestFor = lengthError('Best for', overlay.bestFor, MAX_BEST_FOR);
  errors.metaChips = listError('Meta chips', overlay.metaChips, 8, 80);
  errors.badges = listError('Badges', overlay.badges, 8, 60);
  errors.features = listError('Feature bullets', overlay.features, 30, 300);
  for (const field of Object.keys(errors) as OverlayErrorField[]) if (!errors[field]) delete errors[field];
  return errors;
}

function validatePackageNo(raw: string): string | undefined {
  const text = raw.trim();
  if (text === '') return undefined;
  return /^\d{1,3}$/.test(text) && Number(text) >= 1 ? undefined : 'Use a whole number from 1 to 999, or leave empty.';
}

/** True when an edit patch value would change what learners see: blank text and values equal to the current ones (ignoring stray outer spaces in text) do not. */
function isMeaningfulEdit(current: unknown, next: unknown): boolean {
  if (next === undefined) return false;
  if (typeof next === 'string') {
    const text = next.trim();
    return text !== '' && text !== current;
  }
  if (Array.isArray(next) && Array.isArray(current)) {
    return next.length !== current.length || next.some((item, index) => item !== current[index]);
  }
  return next !== current;
}

/** The wanted package from a deep link (static code, legacy code or custom plan code), or '' when it matches no entry. */
function preselectCode(raw: string | null, plans: AdminCatalogPlanSummary[] | undefined): string {
  const wanted = raw?.trim().toLowerCase() ?? '';
  if (!wanted) return '';
  const code = resolveWebsitePackageByCode(wanted)?.code ?? wanted;
  const known =
    WEBSITE_PACKAGES.some((pkg) => pkg.code === code) ||
    (plans ?? []).some((plan) => plan.code.trim().toLowerCase() === code && !resolveWebsitePackageByCode(plan.code));
  return known ? code : '';
}

// An older backend omits the access summary fields, so each helper returns null for a missing value.
function amount(value: unknown, unit: string, zero = 'None'): string | null {
  if (typeof value !== 'number' || !Number.isFinite(value)) return null;
  if (value === 0) return zero;
  return unit ? `${value} ${unit}${value === 1 ? '' : 's'}` : String(value);
}

function included(value: unknown): string | null {
  return typeof value === 'boolean' ? (value ? 'Included' : 'Not included') : null;
}

function itemList(value: unknown): string | null {
  if (!Array.isArray(value)) return null;
  const items = value.filter((item): item is string => typeof item === 'string' && item.trim() !== '');
  return items.length > 0 ? items.join(', ') : 'None listed';
}

function accessFacts(pairs: Array<[string, string | null]>): AccessFact[] {
  return pairs.flatMap(([label, value]): AccessFact[] => (value === null ? [] : [{ label, value }]));
}

const rowKey = (row: Pick<LiveRow, 'kind' | 'code'>) => `${row.kind}:${row.code}`;

function planRow(plan: AdminCatalogPlanSummary): LiveRow {
  return {
    kind: 'plan',
    code: plan.code,
    name: plan.name,
    price: Number.isFinite(plan.price) ? plan.price : 0,
    currency: (plan.currency ?? '').toUpperCase(),
    interval: plan.interval || 'one_time',
    status: (plan.status ?? '').toLowerCase(),
    isVisible: Boolean(plan.isVisible),
    isDraft: Boolean(plan.isDraft),
    activeSubscribers: plan.activeSubscribers ?? 0,
    access: accessFacts([
      ['Duration', amount(plan.durationMonths, 'month', 'Not set')],
      ['Access window', amount(plan.accessDurationDays, 'day', 'Not set')],
      ['Included credits', amount(plan.includedCredits, '')],
      ['AI credits', amount(plan.bundledAiCredits, '')],
      ['Writing assessments', amount(plan.bundledWritingAssessments, '')],
      ['Speaking sessions', amount(plan.bundledSpeakingSessions, '')],
      ['Tutor Book', included(plan.bundledTutorBook)],
      ['Basic English', included(plan.bundledBasicEnglish)],
      ['Dashboard modules', itemList(plan.dashboardModules)],
      ['Included sub-tests', itemList(plan.includedSubtests)],
    ]),
  };
}

/**
 * `grants` is the add-on's entitlement JSON (the catalog summary does not carry it). An AI package
 * is granted from those pools, never from the legacy grantCredits column, which is 0 by design.
 */
function addOnRow(addOn: AdminCatalogAddOnSummary, grants?: Record<string, unknown>): LiveRow {
  const isAiPackage = (addOn.addonKind ?? '').toLowerCase() === 'ai_package';
  const includes = isAiPackage && grants ? describeAiPackageIncludes(grants) : null;
  return {
    kind: 'addon',
    code: addOn.code,
    name: addOn.name,
    price: Number.isFinite(addOn.price) ? addOn.price : 0,
    currency: (addOn.currency ?? '').toUpperCase(),
    interval: addOn.interval || 'one_time',
    status: (addOn.status ?? '').toLowerCase(),
    isVisible: true,
    isDraft: false,
    activeSubscribers: 0,
    access: accessFacts([
      ['Duration', amount(addOn.durationDays, 'day', 'Not set')],
      isAiPackage
        ? ['Includes', includes ? (includes.length > 0 ? includes.join(' · ') : 'Nothing granted') : 'Credit pools are set in Billing > Pricing > AI packages']
        : ['Credits granted', amount(addOn.grantCredits, '')],
      ['Applies to all plans', typeof addOn.appliesToAllPlans === 'boolean' ? (addOn.appliesToAllPlans ? 'Yes' : 'No') : null],
    ]),
  };
}

function draftFromRow(row: LiveRow): CommercialDraft {
  return {
    price: String(row.price),
    currency: row.currency,
    interval: row.interval,
    status: row.status,
    isVisible: row.isVisible,
  };
}

/** Only the fields that differ from the loaded row. An unparseable price is kept raw so it still counts as a change. */
function commercialDiff(draft: CommercialDraft, row: LiveRow): CommercialDiff {
  const diff: CommercialDiff = {};
  const price = parsePrice(draft.price);
  if (price === null) diff.price = draft.price;
  else if (price !== row.price) diff.price = price;
  const currency = draft.currency.trim().toUpperCase();
  if (currency !== row.currency) diff.currency = currency;
  if (draft.interval !== row.interval) diff.interval = draft.interval;
  if (draft.status !== row.status) diff.status = draft.status;
  if (row.kind === 'plan' && draft.isVisible !== row.isVisible) diff.isVisible = draft.isVisible;
  return diff;
}

function validateCommercial(draft: CommercialDraft): CommercialErrors {
  const errors: CommercialErrors = {};
  if (parsePrice(draft.price) === null) errors.price = 'Enter a price of 0 or more, for example 49 or 49.99.';
  if (!/^[A-Za-z]{3}$/.test(draft.currency.trim())) errors.currency = 'Use a 3-letter currency code, for example GBP.';
  return errors;
}

function pickRow(list: LiveRow[], codes: string[]): LiveRow | null {
  for (const code of codes) {
    const hit = list.find((row) => row.code.trim().toLowerCase() === code);
    if (hit) return hit;
  }
  return null;
}

function findLiveRow(pkg: WebsitePackage, rows: { plans: LiveRow[]; addOns: LiveRow[] }): LiveRow | null {
  const codes = [pkg.code, ...(pkg.legacyCodes ?? [])].map((code) => code.toLowerCase());
  const plan = pickRow(rows.plans, codes);
  const addOn = pickRow(rows.addOns, codes);
  return pkg.productType === 'addon_purchase' ? (addOn ?? plan) : (plan ?? addOn);
}

/** WebsitePackage stand-in for a live plan that has no static definition; its defaults come from the plan row. */
function customPackageBase(plan: AdminCatalogPlanSummary): WebsitePackage {
  const code = plan.code.trim().toLowerCase();
  return {
    code,
    slug: code,
    packageNo: 0,
    section: defaultSectionForCategory(plan.productCategory),
    productType: 'plan_purchase',
    name: plan.name,
    metaChips: [],
    profession: plan.profession || 'all',
    category: humaniseCategory(plan.productCategory),
    access: '',
    formatLine: '',
    description: plan.description ?? '',
    badges: [],
    features: [],
    bestFor: '',
    featured: false,
  };
}

function buildEntries(
  plans: AdminCatalogPlanSummary[],
  rows: { plans: LiveRow[]; addOns: LiveRow[] },
): PackageEntry[] {
  const statics: PackageEntry[] = WEBSITE_PACKAGES.map((pkg) => ({
    code: pkg.code,
    base: pkg,
    isCustom: false,
    defaultSection: pkg.section,
    live: findLiveRow(pkg, rows),
  }));
  const customs = plans
    .map((plan, index) => ({ plan, row: rows.plans[index] ?? null }))
    .filter(({ plan }) => !resolveWebsitePackageByCode(plan.code))
    .sort((a, b) => (a.plan.displayOrder ?? 0) - (b.plan.displayOrder ?? 0) || a.plan.name.localeCompare(b.plan.name))
    .map(({ plan, row }): PackageEntry => {
      const base = customPackageBase(plan);
      return { code: base.code, base, isCustom: true, defaultSection: base.section, live: row };
    });
  return [...statics, ...customs];
}

function entryMatches(entry: PackageEntry, needle: string): boolean {
  const section = WEBSITE_SECTIONS.find((candidate) => candidate.key === entry.base.section);
  return (
    entry.base.name.toLowerCase().includes(needle) ||
    entry.code.includes(needle) ||
    (entry.base.legacyCodes ?? []).some((code) => code.includes(needle)) ||
    entry.base.section.toLowerCase().includes(needle) ||
    (section?.title.toLowerCase().includes(needle) ?? false)
  );
}

function clockTime(): string {
  return new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

// ---------------------------------------------------------------------------
// Editor
// ---------------------------------------------------------------------------

export function SubscriptionsPackagesEditor({ canWrite = true }: { canWrite?: boolean }) {
  const [loading, setLoading] = useState(true);
  const [loaded, setLoaded] = useState(false);
  const [reloading, setReloading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const [saving, setSaving] = useState(false);
  const [savedAt, setSavedAt] = useState<string | null>(null);
  const [droppedCodes, setDroppedCodes] = useState<string[]>([]);
  const [mirroredCodes, setMirroredCodes] = useState<string[]>([]);
  const [byCode, setByCode] = useState<OverlayMap>({});
  const [sections, setSections] = useState<SectionMap>({});
  const [plans, setPlans] = useState<AdminCatalogPlanSummary[]>([]);
  const [addOns, setAddOns] = useState<AdminCatalogAddOnSummary[]>([]);
  // Entitlement pools by lower-cased add-on code; the catalog summary does not carry them.
  const [grantsByCode, setGrantsByCode] = useState<Record<string, Record<string, unknown>>>({});
  const [revision, setRevision] = useState('');
  const [commercial, setCommercial] = useState<Record<string, CommercialDraft>>({});
  const [packageNoDrafts, setPackageNoDrafts] = useState<Record<string, string>>({});
  const [savedSnapshot, setSavedSnapshot] = useState('');
  const [resetNonce, setResetNonce] = useState(0);
  const [selectedCode, setSelectedCode] = useState('');
  const [query, setQuery] = useState('');
  const [archiveOpen, setArchiveOpen] = useState(false);
  const aliveRef = useRef(true);

  const hydrate = useCallback(
    (
      presentation: CatalogPresentation | null | undefined,
      revisions: CatalogPresentationRevisions | undefined,
      nextPlans: AdminCatalogPlanSummary[] | undefined,
      nextAddOns: AdminCatalogAddOnSummary[] | undefined,
    ) => {
      const stored = presentation?.websitePackages;
      const nextByCode = normaliseByCode(stored?.byCode);
      const nextSections: SectionMap = { ...(stored?.sections ?? {}) };
      setByCode(nextByCode);
      setSections(nextSections);
      setPlans(nextPlans ?? []);
      setAddOns(nextAddOns ?? []);
      setRevision(revisions?.websitePackages ?? '');
      setCommercial({});
      setPackageNoDrafts({});
      setSavedSnapshot(stable({ byCode: cleanByCode(nextByCode), sections: cleanSections(nextSections), commercial: {} }));
      setResetNonce((n) => n + 1);
      setLoaded(true);
    },
    [],
  );

  const loadLatest = useCallback(
    async (initial: boolean) => {
      if (!initial) setReloading(true);
      try {
        const res = await fetchAdminCatalogPresentation();
        if (!aliveRef.current) return;
        hydrate(res.presentation, res.revisions, res.plans, res.addOns);
        // Deep link (?code=): only used while nothing is selected yet, so Reload latest never moves the selection.
        const wanted = preselectCode(
          typeof window === 'undefined' ? null : new URLSearchParams(window.location.search).get('code'),
          res.plans,
        );
        setSelectedCode((current) => current || wanted || WEBSITE_PACKAGES[0]?.code || '');
        setError(null);
        setConflict(false);
        setDroppedCodes([]);
        setMirroredCodes([]);
        // Best effort: without it an AI package row shows a pointer instead of its credit pools.
        void fetchAdminBillingAddOns()
          .then((rows) => {
            if (!aliveRef.current || !Array.isArray(rows)) return;
            const next: Record<string, Record<string, unknown>> = {};
            for (const row of rows as Array<{ code?: string; grantEntitlements?: Record<string, unknown> | null }>) {
              if (row.code && row.grantEntitlements) next[row.code.toLowerCase()] = row.grantEntitlements;
            }
            setGrantsByCode(next);
          })
          .catch(() => undefined);
      } catch (err) {
        if (!aliveRef.current) return;
        setError(err instanceof Error ? err.message : 'Failed to load package settings.');
      } finally {
        if (aliveRef.current) {
          setLoading(false);
          setReloading(false);
        }
      }
    },
    [hydrate],
  );

  useEffect(() => {
    aliveRef.current = true;
    return () => {
      aliveRef.current = false;
    };
  }, []);

  useEffect(() => {
    void loadLatest(true);
  }, [loadLatest]);

  const liveRows = useMemo(
    () => ({
      plans: plans.map(planRow),
      addOns: addOns.map((addOn) => addOnRow(addOn, grantsByCode[addOn.code.toLowerCase()])),
    }),
    [plans, addOns, grantsByCode],
  );
  const entries = useMemo(() => buildEntries(plans, liveRows), [plans, liveRows]);
  const rowIndex = useMemo(
    () => new Map([...liveRows.plans, ...liveRows.addOns].map((row) => [rowKey(row), row] as const)),
    [liveRows],
  );
  const selectedEntry = useMemo(
    () => entries.find((entry) => entry.code === selectedCode) ?? entries[0],
    [entries, selectedCode],
  );
  const selectedOverlay = selectedEntry ? byCode[selectedEntry.code] : undefined;
  const resolved = useMemo(
    () => (selectedEntry ? applyWebsitePackageOverlay(selectedEntry.base, selectedOverlay) : null),
    [selectedEntry, selectedOverlay],
  );

  const filteredEntries = useMemo(() => {
    const needle = query.trim().toLowerCase();
    if (!needle) return entries;
    return entries.filter((entry) => entry.code === selectedEntry?.code || entryMatches(entry, needle));
  }, [entries, query, selectedEntry]);

  const payload = useMemo(
    () => ({ byCode: cleanByCode(byCode), sections: cleanSections(sections) }),
    [byCode, sections],
  );

  const commercialChanges = useMemo(() => {
    const out: Record<string, CommercialDiff> = {};
    for (const [key, draft] of Object.entries(commercial)) {
      const row = rowIndex.get(key);
      if (!row) continue;
      const diff = commercialDiff(draft, row);
      if (Object.keys(diff).length > 0) out[key] = diff;
    }
    return out;
  }, [commercial, rowIndex]);

  const commercialUpdates = useMemo(() => {
    const out: PackageCommercialUpdate[] = [];
    for (const [key, diff] of Object.entries(commercialChanges)) {
      const row = rowIndex.get(key);
      if (!row) continue;
      const update: PackageCommercialUpdate = { kind: row.kind, code: row.code };
      if (typeof diff.price === 'number') update.price = diff.price;
      if (diff.currency !== undefined) update.currency = diff.currency;
      if (diff.interval !== undefined) update.interval = diff.interval;
      if (diff.status !== undefined) update.status = diff.status;
      if (diff.isVisible !== undefined) update.isVisible = diff.isVisible;
      out.push(update);
    }
    return out;
  }, [commercialChanges, rowIndex]);

  const dirty = useMemo(
    () => stable({ byCode: payload.byCode, sections: payload.sections, commercial: commercialChanges }) !== savedSnapshot,
    [payload, commercialChanges, savedSnapshot],
  );

  const overlayErrors = useMemo(() => {
    const out: Record<string, OverlayErrors> = {};
    const known = new Set(entries.map((entry) => entry.code));
    for (const [code, overlay] of Object.entries(byCode)) {
      if (!known.has(code)) continue;
      const errors = validateOverlay(overlay);
      if (Object.keys(errors).length > 0) out[code] = errors;
    }
    for (const [code, raw] of Object.entries(packageNoDrafts)) {
      const message = validatePackageNo(raw);
      if (message) out[code] = { ...(out[code] ?? {}), packageNo: message };
    }
    return out;
  }, [byCode, entries, packageNoDrafts]);

  const commercialErrors = useMemo(() => {
    const out: Record<string, CommercialErrors> = {};
    for (const [key, draft] of Object.entries(commercial)) {
      const errors = validateCommercial(draft);
      if (Object.keys(errors).length > 0) out[key] = errors;
    }
    return out;
  }, [commercial]);

  const errorEntries = useMemo(
    () =>
      entries.filter(
        (entry) => Boolean(overlayErrors[entry.code]) || (entry.live ? Boolean(commercialErrors[rowKey(entry.live)]) : false),
      ),
    [entries, overlayErrors, commercialErrors],
  );
  const hasErrors = Object.keys(commercialErrors).length > 0 || Object.keys(overlayErrors).length > 0;

  const pendingArchives = useMemo(() => {
    const out: Array<{ name: string; code: string; subscribers: number }> = [];
    for (const [key, diff] of Object.entries(commercialChanges)) {
      const row = rowIndex.get(key);
      if (row && diff.status === 'archived') out.push({ name: row.name, code: row.code, subscribers: row.activeSubscribers });
    }
    return out;
  }, [commercialChanges, rowIndex]);

  const canSave = canWrite && dirty && !saving && !hasErrors && revision !== '' && !loading;

  const updateOverlay = (entry: PackageEntry, patch: Partial<WebsitePackageOverlay>) => {
    // Only a real change seeds the section: clearing a box or retyping the current value must not publish a custom plan.
    const currentValues = (resolved ?? {}) as unknown as Record<string, unknown>;
    const publishes = Object.entries(patch).some(([key, value]) => isMeaningfulEdit(currentValues[key], value));
    setByCode((current) => {
      const previous = current[entry.code] ?? {};
      // A key the overlay does not hold yet is written only for a real change, so a no-op edit (the same
      // list, a toggle back to the shown value) never stores e.g. `features: []` ("show none"). Text is
      // always written: it feeds a controlled input; cleanOverlay drops fully blank text and the server trims the rest.
      const effective: Record<string, unknown> = {};
      for (const [key, value] of Object.entries(patch)) {
        if (
          Object.prototype.hasOwnProperty.call(previous, key) ||
          typeof value === 'string' ||
          isMeaningfulEdit(currentValues[key], value)
        ) {
          effective[key] = value;
        }
      }
      if (Object.keys(effective).length === 0) return current;
      // A custom package is shown to learners only once it has a section, so the first edit seeds it.
      const seed: Partial<WebsitePackageOverlay> =
        entry.isCustom && publishes && !isWebsiteSectionKey(previous.section) ? { section: entry.defaultSection } : {};
      return { ...current, [entry.code]: { ...seed, ...previous, ...(effective as Partial<WebsitePackageOverlay>) } };
    });
  };

  const updatePackageNo = (entry: PackageEntry, raw: string) => {
    setPackageNoDrafts((current) => ({ ...current, [entry.code]: raw }));
    const text = raw.trim();
    if (text === '') updateOverlay(entry, { packageNo: undefined });
    else if (/^\d{1,3}$/.test(text) && Number(text) >= 1) updateOverlay(entry, { packageNo: Number(text) });
  };

  const updateSection = (key: string, patch: WebsiteSectionOverlay) => {
    setSections((current) => ({ ...current, [key]: { ...(current[key] ?? {}), ...patch } }));
  };

  const updateCommercial = (row: LiveRow, patch: Partial<CommercialDraft>) => {
    const key = rowKey(row);
    setCommercial((current) => ({ ...current, [key]: { ...(current[key] ?? draftFromRow(row)), ...patch } }));
  };

  const resetPackage = () => {
    if (!selectedEntry) return;
    const code = selectedEntry.code;
    setByCode((current) => {
      const next = { ...current };
      delete next[code];
      return next;
    });
    setPackageNoDrafts((current) => {
      const next = { ...current };
      delete next[code];
      return next;
    });
    if (selectedEntry.live) {
      const key = rowKey(selectedEntry.live);
      setCommercial((current) => {
        const next = { ...current };
        delete next[key];
        return next;
      });
    }
    setResetNonce((n) => n + 1);
  };

  const resetAll = () => {
    const confirmed = window.confirm(
      'Discard the overrides for ALL packages and section headings? Custom plans also stop being listed for learners. Nothing is saved until you press Save changes.',
    );
    if (!confirmed) return;
    setByCode({});
    setSections({});
    setPackageNoDrafts({});
    setResetNonce((n) => n + 1);
  };

  const doSave = async () => {
    setSaving(true);
    setError(null);
    setConflict(false);
    setDroppedCodes([]);
    setMirroredCodes([]);
    try {
      const res = await saveAdminWebsitePackages({
        expectedRevision: revision,
        websitePackages: payload,
        ...(commercialUpdates.length > 0 ? { commercialUpdates } : {}),
      });
      hydrate(res.presentation, res.revisions, res.plans, res.addOns);
      setDroppedCodes(res.droppedCodes ?? []);
      setMirroredCodes(Array.isArray(res.mirroredCodes) ? res.mirroredCodes : []);
      setSavedAt(clockTime());
    } catch (err) {
      if (isApiError(err) && (err.status === 409 || err.code === 'catalog_presentation_conflict')) {
        setConflict(true);
        setError(err.message);
      } else if (isApiError(err) && err.status === 400 && err.fieldErrors.length > 0) {
        setError(err.fieldErrors[0].message);
      } else {
        setError(err instanceof Error ? err.message : 'Failed to save package settings.');
      }
    } finally {
      setSaving(false);
    }
  };

  const requestSave = () => {
    if (!canSave) return;
    if (pendingArchives.length > 0) {
      setArchiveOpen(true);
      return;
    }
    void doSave();
  };

  if (loading) {
    return <div className="h-64 animate-pulse rounded-2xl border border-admin-border bg-admin-bg-page" />;
  }

  if (!loaded) {
    return (
      <InlineAlert
        variant="error"
        action={
          <Button size="sm" variant="secondary" className={touchBtn} loading={reloading} onClick={() => void loadLatest(false)}>
            Retry
          </Button>
        }
      >
        {error ?? 'Failed to load package settings.'}
      </InlineAlert>
    );
  }

  if (!selectedEntry || !resolved) {
    return <InlineAlert variant="error">No website packages found.</InlineAlert>;
  }

  const entry = selectedEntry;
  const live = entry.live;
  const overlay = selectedOverlay;
  const fieldErrors = overlayErrors[entry.code] ?? {};
  const overlaySection = overlay?.section;
  const effectiveSection: WebsiteSectionKey = isWebsiteSectionKey(overlaySection) ? overlaySection : entry.defaultSection;
  const staticSection = WEBSITE_SECTIONS.find((section) => section.key === entry.base.section);
  const hasOverrides = cleanOverlay(overlay) !== null;
  const packageNoValue =
    packageNoDrafts[entry.code] ?? (typeof overlay?.packageNo === 'number' ? String(overlay.packageNo) : '');
  const draft = live ? (commercial[rowKey(live)] ?? draftFromRow(live)) : null;
  const draftErrors = live ? (commercialErrors[rowKey(live)] ?? {}) : {};
  const otherErrorEntries = errorEntries.filter((candidate) => candidate.code !== entry.code);
  const customGroup = filteredEntries.filter((candidate) => candidate.isCustom);

  const optionLabel = (candidate: PackageEntry) => {
    const stored = byCode[candidate.code];
    const name = nonBlank(stored?.name)?.trim() ?? candidate.base.name;
    const number =
      typeof stored?.packageNo === 'number' ? stored.packageNo : candidate.base.packageNo > 0 ? candidate.base.packageNo : null;
    const status = candidate.live && candidate.live.status !== 'active' ? ` [${candidate.live.status}]` : '';
    return `${number !== null ? `Package ${number} — ` : ''}${name}${status}`;
  };

  const errorCount = Math.max(errorEntries.length, 1);
  const statusText = !canWrite
    ? 'Read-only access'
    : hasErrors
      ? `Fix the errors in ${errorCount} package${errorCount === 1 ? '' : 's'} to save`
      : dirty
        ? 'Unsaved changes'
        : savedAt
          ? `Saved at ${savedAt}`
          : 'No unsaved changes';

  const archiveDescription =
    `Archiving takes ${pendingArchives
      .map((item) => `${item.name} (${item.code}${item.subscribers > 0 ? `, ${item.subscribers} active subscriber${item.subscribers === 1 ? '' : 's'}` : ''})`)
      .join('; ')} off sale. Existing purchases are kept and you can set the status back to Active later. ` +
    'The server may refuse to archive a plan that still has active subscribers.';

  return (
    <div className="space-y-6">
      <div className="sticky top-0 z-20 flex items-center justify-between gap-2 rounded-2xl border border-admin-border bg-admin-bg-surface/95 px-3 py-2 shadow-sm backdrop-blur sm:px-4 sm:py-3">
        <div className="min-w-0">
          <p aria-live="polite" className="truncate text-sm font-semibold text-admin-fg-default">
            {statusText}
          </p>
          <p className="hidden text-xs text-admin-fg-muted sm:block">
            Changes apply to every learner surface and to Billing/Pricing when saved.
          </p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <Button
            variant="ghost"
            size="sm"
            className={touchBtn}
            onClick={resetAll}
            disabled={!canWrite || saving}
            startIcon={<RotateCcw className="h-4 w-4" />}
          >
            <span className="sr-only sm:not-sr-only">Reset all package text</span>
          </Button>
          <Button
            size="sm"
            className={touchBtn}
            onClick={requestSave}
            disabled={!canSave}
            loading={saving}
            loadingText="Saving..."
            startIcon={<Save className="h-4 w-4" />}
          >
            <span className="hidden sm:inline">Save changes</span>
            <span className="sm:hidden">Save</span>
          </Button>
        </div>
      </div>

      <p className="text-sm text-admin-fg-muted">
        Saving updates the same package everywhere it appears: learner Subscriptions, Catalog, Marketplace packages and AI
        packages, checkout and plan names, and Billing &amp; Pricing, where the plan name and description become read-only
        copies of this text. Price, currency, interval and status are saved to the billing record that checkout reads. The
        mobile apps pick the changes up the next time they refresh.
      </p>

      {!canWrite ? (
        <InlineAlert variant="warning">
          You have read-only billing access. You can review package copy, but saving is disabled.
        </InlineAlert>
      ) : null}

      {error ? (
        <InlineAlert
          variant={conflict ? 'warning' : 'error'}
          action={
            conflict ? (
              <Button size="sm" variant="secondary" className={touchBtn} loading={reloading} onClick={() => void loadLatest(false)}>
                Reload latest
              </Button>
            ) : undefined
          }
        >
          {error}
          {conflict ? ' Your edits are still on screen; reloading replaces them with the latest saved version.' : ''}
        </InlineAlert>
      ) : null}

      {droppedCodes.length > 0 ? (
        <InlineAlert variant="info" live="polite">
          Saved. These package codes were not stored (no matching billing record, or the code format is not supported for package copy): {droppedCodes.join(', ')}.
        </InlineAlert>
      ) : null}

      {mirroredCodes.length > 0 ? (
        <InlineAlert variant="info" live="polite">
          Billing records updated for {mirroredCodes.length} package{mirroredCodes.length === 1 ? '' : 's'}:{' '}
          {mirroredCodes.slice(0, 6).join(', ')}
          {mirroredCodes.length > 6 ? ` and ${mirroredCodes.length - 6} more` : ''}.
        </InlineAlert>
      ) : null}

      {otherErrorEntries.length > 0 ? (
        <InlineAlert variant="warning" live="polite">
          Fix the errors in:{' '}
          {otherErrorEntries.map((candidate) => (
            <button
              key={candidate.code}
              type="button"
              onClick={() => setSelectedCode(candidate.code)}
              className="mr-3 min-h-11 font-semibold underline underline-offset-2 sm:min-h-0"
            >
              {candidate.base.name}
            </button>
          ))}
        </InlineAlert>
      ) : null}

      <Card>
        <CardContent className="space-y-4 pt-6">
          <h2 className={sectionTitle}>Select a package</h2>
          <Input
            label="Search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Search packages by name, code, or section..."
            startIcon={<Search className="h-4 w-4" />}
          />
          <NativeSelect label="Package" value={entry.code} onChange={(e) => setSelectedCode(e.target.value)}>
            {WEBSITE_SECTIONS.map((section) => {
              const group = filteredEntries.filter((candidate) => !candidate.isCustom && candidate.base.section === section.key);
              if (group.length === 0) return null;
              return (
                <optgroup key={section.key} label={section.title}>
                  {group.map((candidate) => (
                    <option key={candidate.code} value={candidate.code}>
                      {optionLabel(candidate)}
                    </option>
                  ))}
                </optgroup>
              );
            })}
            {customGroup.length > 0 ? (
              <optgroup label="Other plans">
                {customGroup.map((candidate) => (
                  <option key={candidate.code} value={candidate.code}>
                    {optionLabel(candidate)}
                  </option>
                ))}
              </optgroup>
            ) : null}
          </NativeSelect>
        </CardContent>
      </Card>

      <Card>
        <CardContent className="space-y-4 pt-6">
          <div className="flex flex-wrap items-start justify-between gap-2">
            <div className="min-w-0">
              <h2 className={sectionTitle}>{resolved.name}</h2>
              <p className="mt-1 flex flex-wrap items-center gap-2 text-xs text-admin-fg-muted">
                <span className="font-medium">{entry.code}</span>
                {entry.isCustom ? <Badge variant="info" size="sm">Custom plan</Badge> : null}
                {hasOverrides ? <Badge variant="primary" size="sm">Customised</Badge> : null}
                {live ? <Badge variant={statusToTone(live.status)} size="sm">{live.status || 'no status'}</Badge> : null}
              </p>
            </div>
            <Button
              variant="ghost"
              size="sm"
              className={touchBtn}
              onClick={resetPackage}
              disabled={!canWrite || saving}
              startIcon={<RotateCcw className="h-4 w-4" />}
            >
              Reset this package
            </Button>
          </div>

          <div className="grid gap-4 md:grid-cols-2">
            <Input
              label="Name"
              required
              value={overlay?.name ?? resolved.name}
              onChange={(e) => updateOverlay(entry, { name: e.target.value })}
              error={fieldErrors.name}
            />
            <Input
              label="Package number"
              inputMode="numeric"
              value={packageNoValue}
              placeholder={entry.base.packageNo > 0 ? String(entry.base.packageNo) : 'None'}
              onChange={(e) => updatePackageNo(entry, e.target.value)}
              error={fieldErrors.packageNo}
              hint="Display label only. Leave empty to use the default."
            />
          </div>

          <div className="grid gap-4 md:grid-cols-2">
            <Input
              label="Category"
              value={overlay?.category ?? resolved.category}
              onChange={(e) => updateOverlay(entry, { category: e.target.value })}
              error={fieldErrors.category}
              hint={DEFAULT_TEXT_HINT}
            />
            {entry.isCustom ? (
              <NativeSelect
                label="Section"
                value={effectiveSection}
                onChange={(e) => updateOverlay(entry, { section: e.target.value as WebsiteSectionKey })}
                hint={
                  isWebsiteSectionKey(overlaySection)
                    ? 'Learners see this plan under this heading.'
                    : 'Not shown to learners yet: saving any edit to this package lists it under this section.'
                }
              >
                {WEBSITE_SECTIONS.map((section) => (
                  <option key={section.key} value={section.key}>
                    {section.title}
                  </option>
                ))}
              </NativeSelect>
            ) : (
              <div>
                <span className={fieldLabel}>Section</span>
                <p className="text-sm text-admin-fg-default">{staticSection?.title ?? entry.base.section}</p>
              </div>
            )}
          </div>

          <Input
            label="Format line"
            value={overlay?.formatLine ?? resolved.formatLine}
            onChange={(e) => updateOverlay(entry, { formatLine: e.target.value })}
            error={fieldErrors.formatLine}
            hint={DEFAULT_TEXT_HINT}
          />
          <Textarea
            label="Description"
            value={overlay?.description ?? resolved.description}
            onChange={(e) => updateOverlay(entry, { description: e.target.value })}
            rows={4}
            maxLength={MAX_DESCRIPTION}
            enterKeyHint="enter"
            className="scroll-my-24"
            error={fieldErrors.description}
            hint={DEFAULT_TEXT_HINT}
          />
          <LinesTextarea
            key={`${entry.code}:${resetNonce}:chips`}
            label="Meta chips (one per line)"
            lines={overlay?.metaChips ?? resolved.metaChips}
            onLinesChange={(metaChips) => updateOverlay(entry, { metaChips })}
            rows={3}
            className="scroll-my-24"
            error={fieldErrors.metaChips}
            hint={LIST_HINT}
          />
          <LinesTextarea
            key={`${entry.code}:${resetNonce}:badges`}
            label="Badges (one per line)"
            lines={overlay?.badges ?? resolved.badges}
            onLinesChange={(badges) => updateOverlay(entry, { badges })}
            rows={2}
            className="scroll-my-24"
            error={fieldErrors.badges}
            hint={LIST_HINT}
          />
          <LinesTextarea
            key={`${entry.code}:${resetNonce}:features`}
            label="Feature bullets / ticks (one per line)"
            lines={overlay?.features ?? resolved.features}
            onLinesChange={(features) => updateOverlay(entry, { features })}
            rows={6}
            className="scroll-my-24"
            error={fieldErrors.features}
            hint={LIST_HINT}
          />
          <Textarea
            label="Best for"
            value={overlay?.bestFor ?? resolved.bestFor}
            onChange={(e) => updateOverlay(entry, { bestFor: e.target.value })}
            rows={3}
            maxLength={MAX_BEST_FOR}
            enterKeyHint="enter"
            className="scroll-my-24"
            error={fieldErrors.bestFor}
            hint={DEFAULT_TEXT_HINT}
          />
          <Toggle
            label="Featured (highlighted card)"
            checked={overlay?.featured ?? resolved.featured}
            onChange={(value) => updateOverlay(entry, { featured: value })}
          />
        </CardContent>
      </Card>

      <Card>
        <CardContent className="space-y-4 pt-6">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h2 className={sectionTitle}>Pricing &amp; status</h2>
            <Link
              href={BILLING_PRICING_HREF}
              className="inline-flex min-h-11 items-center text-sm font-medium text-admin-primary underline-offset-4 hover:underline sm:min-h-0"
            >
              Open Billing &amp; Pricing
            </Link>
          </div>
          {live && draft ? (
            <>
              <p className="text-sm text-admin-fg-muted">
                Linked billing record: {live.kind === 'plan' ? 'plan' : 'add-on'} code{' '}
                <code className="rounded bg-admin-bg-subtle px-1 py-0.5 font-mono text-xs text-admin-fg-default">{live.code}</code>.
                Price, currency, interval and status are saved to this same record, the one checkout reads.
              </p>
              <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
                <Input
                  label="Price"
                  inputMode="decimal"
                  value={draft.price}
                  onChange={(e) => updateCommercial(live, { price: e.target.value })}
                  error={draftErrors.price}
                />
                <Input
                  label="Currency"
                  maxLength={3}
                  autoCapitalize="characters"
                  className="uppercase"
                  value={draft.currency}
                  onChange={(e) => updateCommercial(live, { currency: e.target.value.toUpperCase() })}
                  error={draftErrors.currency}
                />
                <NativeSelect
                  label="Interval"
                  value={draft.interval}
                  onChange={(e) => updateCommercial(live, { interval: e.target.value })}
                  options={withCurrent(live.kind === 'plan' ? PLAN_INTERVAL_OPTIONS : ADDON_INTERVAL_OPTIONS, draft.interval)}
                />
                <NativeSelect
                  label="Status"
                  value={draft.status}
                  onChange={(e) => updateCommercial(live, { status: e.target.value })}
                  options={withCurrent(STATUS_OPTIONS, draft.status)}
                />
              </div>
              {live.kind === 'plan' ? (
                <Toggle
                  label="Visible to learners"
                  checked={draft.isVisible}
                  onChange={(value) => updateCommercial(live, { isVisible: value })}
                />
              ) : null}
              {live.isDraft ? (
                <p className="text-xs text-admin-fg-muted">
                  This plan carries the Draft flag in Billing &amp; Pricing, so learners cannot see it until that flag is cleared there.
                </p>
              ) : null}
              {live.activeSubscribers > 0 ? (
                <p className="text-xs text-admin-fg-muted">
                  {live.activeSubscribers} active subscriber{live.activeSubscribers === 1 ? '' : 's'} on this plan.
                </p>
              ) : null}
              {live.access.length > 0 ? (
                <div className="space-y-3 border-t border-admin-border pt-4">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <h3 className="text-sm font-semibold text-admin-fg-default">
                      Access &amp; quotas (from the linked billing record)
                    </h3>
                    <Link
                      href={BILLING_PRICING_HREF}
                      className="inline-flex min-h-11 items-center text-sm font-medium text-admin-primary underline-offset-4 hover:underline sm:min-h-0"
                    >
                      Open in Pricing
                    </Link>
                  </div>
                  <dl className="grid gap-x-4 gap-y-3 text-sm sm:grid-cols-2 lg:grid-cols-3">
                    {live.access.map((fact) => (
                      <div key={fact.label} className="min-w-0">
                        <dt className={fieldLabel}>{fact.label}</dt>
                        <dd className="break-words text-admin-fg-default">{fact.value}</dd>
                      </div>
                    ))}
                  </dl>
                  <p className="text-xs text-admin-fg-muted">
                    Read-only here. Change entitlements, quotas and included sub-tests in Billing &amp; Pricing.
                  </p>
                </div>
              ) : null}
            </>
          ) : (
            <p className="text-sm text-admin-fg-muted">
              No billing record found for this package, so it has no price or status to edit here. Create the plan or add-on in
              Billing &amp; Pricing first.
            </p>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardContent className="space-y-4 pt-6">
          <h2 className={sectionTitle}>Section headings</h2>
          <p className="text-sm text-admin-fg-muted">
            Override the title and description shown above each group of packages. {DEFAULT_TEXT_HINT}
          </p>
          <div className="space-y-5">
            {WEBSITE_SECTIONS.map((section) => (
              <fieldset key={section.key} className="min-w-0">
                <legend className="mb-2 text-sm font-semibold text-admin-fg-default">{section.title}</legend>
                <div className="grid gap-3 md:grid-cols-2">
                  <Input
                    label="Heading"
                    maxLength={MAX_SECTION_TITLE}
                    value={sections[section.key]?.title ?? section.title}
                    onChange={(e) => updateSection(section.key, { title: e.target.value })}
                  />
                  <Textarea
                    label="Description"
                    rows={2}
                    maxLength={MAX_SECTION_DESCRIPTION}
                    enterKeyHint="enter"
                    className="scroll-my-24"
                    value={sections[section.key]?.description ?? section.description}
                    onChange={(e) => updateSection(section.key, { description: e.target.value })}
                  />
                </div>
              </fieldset>
            ))}
          </div>
        </CardContent>
      </Card>

      <BillingConfirmDialog
        open={archiveOpen}
        title="Archive billing record?"
        description={archiveDescription}
        variant="warning"
        confirmLabel="Archive and save"
        loading={saving}
        onConfirm={() => {
          setArchiveOpen(false);
          void doSave();
        }}
        onCancel={() => setArchiveOpen(false)}
      />
    </div>
  );
}
