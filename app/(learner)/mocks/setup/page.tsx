'use client';

import { useEffect, useMemo, useState } from 'react';
import type { ElementType } from 'react';
import {
  ArrowRight,
  Award,
  Check,
  Clock,
  FileText,
  Headphones,
  Info,
  Layers,
  Lock,
  Mic,
  PenTool,
  ShieldCheck,
  Sparkles,
  Stethoscope,
  Trophy,
  Zap,
} from 'lucide-react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { ProgressBar } from '@/components/ui/progress';
import { Skeleton } from '@/components/ui/skeleton';
import { Switch } from '@/components/ui/switch';
import { MotionCollapse, MotionPresence, MotionSection } from '@/components/ui/motion-primitives';
import { cn } from '@/lib/utils';
import {
  createMockBooking,
  createMockSession,
  fetchMockEntitlementsSummary,
  fetchMockOptions,
} from '@/lib/api';
import type { MockEntitlementSummary } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { MockBundleOption, MockConfig, MockDeliveryMode, MockOptions, MockStrictness, MockTypeToken } from '@/lib/mock-data';
import {
  MOCK_EXAM_FLOW_STAGES,
  getMockModePolicy,
  getMockSectionPolicy,
  isTeacherMarkedSubtest,
} from '@/lib/mocks/workflow';

type ReviewSelection = MockConfig['reviewSelection'];
type MockType = MockTypeToken;
type MockSubType = 'reading' | 'listening' | 'writing' | 'speaking';

const MOCK_TYPE_TOKENS: ReadonlySet<MockTypeToken> = new Set<MockTypeToken>([
  'full', 'lrw', 'sub', 'part', 'final_readiness', 'remedial',
]);
const FULL_SHAPE_TOKENS: ReadonlySet<MockTypeToken> = new Set<MockTypeToken>([
  'full', 'lrw', 'final_readiness',
]);
const SUB_SHAPE_TOKENS: ReadonlySet<MockTypeToken> = new Set<MockTypeToken>([
  'sub', 'part', 'remedial',
]);
function isMockTypeToken(value: unknown): value is MockTypeToken {
  return typeof value === 'string' && MOCK_TYPE_TOKENS.has(value as MockTypeToken);
}
function isFullShape(t: MockType) { return FULL_SHAPE_TOKENS.has(t); }
function isSubShape(t: MockType) { return SUB_SHAPE_TOKENS.has(t); }

// Mocks Phase 8b — frontend MockTypeToken to backend MockEntitlementKeys ledger
// token. Each canonical mock-type maps to the ledger key that grants it.
// `sub` resolves dynamically against the chosen sub-test (writing/speaking
// have dedicated ledger keys, reading/listening fall back to `mock_sub`).
const MOCK_TYPE_LEDGER_KEYS: Record<MockTypeToken, string> = {
  full: 'mock_full',
  lrw: 'mock_lrw',
  sub: 'mock_sub',
  part: 'mock_part',
  diagnostic: 'mock_diagnostic',
  final_readiness: 'mock_final_readiness',
  remedial: 'mock_remedial',
};

function ledgerKeyForSelection(mockType: MockType, subType: MockSubType): string {
  if (mockType !== 'sub') return MOCK_TYPE_LEDGER_KEYS[mockType];
  if (subType === 'writing') return 'mock_writing';
  if (subType === 'speaking') return 'mock_speaking_session';
  return 'mock_sub';
}

// Phase 8b — diagnostic uses its own one-per-lifetime entitlement, not the
// generic ledger. Free-tier learners always get exactly one diagnostic, so the
// type-selector must never disable it on a "0 remaining" signal.
const ENTITLEMENT_GATED_TYPES: ReadonlySet<MockTypeToken> = new Set<MockTypeToken>([
  'full', 'lrw', 'sub', 'part', 'final_readiness', 'remedial',
]);

/** Sub-test identity (DESIGN.md §2 skill tokens): the icon at rest, the whole tile when chosen. */
const SUBTEST_META: Record<MockSubType, { label: string; icon: ElementType; active: string; iconTone: string }> = {
  listening: { label: 'Listening', icon: Headphones, active: 'border-skill-listening bg-skill-listening/10 text-skill-listening', iconTone: 'text-skill-listening' },
  reading: { label: 'Reading', icon: FileText, active: 'border-skill-reading bg-skill-reading/10 text-skill-reading', iconTone: 'text-skill-reading' },
  writing: { label: 'Writing', icon: PenTool, active: 'border-skill-writing bg-skill-writing/10 text-skill-writing', iconTone: 'text-skill-writing' },
  speaking: { label: 'Speaking', icon: Mic, active: 'border-skill-speaking bg-skill-speaking/10 text-skill-speaking', iconTone: 'text-skill-speaking' },
};

function isSubtest(value: string | null): value is MockSubType {
  return value === 'reading' || value === 'listening' || value === 'writing' || value === 'speaking';
}

function defaultsForMockType(type: MockType): { mode: 'practice' | 'exam'; strictness: MockStrictness; strictTimer: boolean; deliveryMode?: MockDeliveryMode } {
  if (type === 'diagnostic' || type === 'remedial') {
    return { mode: 'practice', strictness: 'learning', strictTimer: false };
  }
  if (type === 'final_readiness') {
    return { mode: 'exam', strictness: 'final_readiness', strictTimer: true, deliveryMode: 'oet_home' };
  }
  return { mode: 'exam', strictness: 'exam', strictTimer: true };
}

function reviewCost(selection: ReviewSelection) {
  if (selection === 'writing_and_speaking') return 2;
  if (selection === 'writing' || selection === 'speaking' || selection === 'current_subtest') return 1;
  return 0;
}

function buildReviewOptions(mockType: MockType, subType: MockSubType) {
  if (isFullShape(mockType)) {
    // LRW excludes Speaking; offer writing-only review.
    if (mockType === 'lrw') {
      return [
        { id: 'none' as const, label: 'No Review', description: 'Run the mock without tutor review.', cost: 0 },
        { id: 'writing' as const, label: 'Writing Only', description: 'Reserve one credit for Writing tutor review.', cost: 1 },
      ];
    }
    return [
      { id: 'none' as const, label: 'No Review', description: 'Run the mock without tutor review.', cost: 0 },
      { id: 'writing' as const, label: 'Writing Only', description: 'Reserve one credit for Writing tutor review.', cost: 1 },
      { id: 'speaking' as const, label: 'Speaking Only', description: 'Reserve one credit for Speaking tutor review.', cost: 1 },
      { id: 'writing_and_speaking' as const, label: 'Writing + Speaking', description: 'Reserve two credits for both productive sections.', cost: 2 },
    ];
  }

  if (subType === 'writing' || subType === 'speaking') {
    return [
      { id: 'none' as const, label: 'No Review', description: 'Keep this sub-test mock platform-evaluated only.', cost: 0 },
      { id: 'current_subtest' as const, label: 'Review Current Sub-test', description: 'Reserve one credit for tutor review of this Writing or Speaking mock.', cost: 1 },
    ];
  }

  return [
    { id: 'none' as const, label: 'No Review', description: 'Tutor review is not offered for Reading or Listening mocks.', cost: 0 },
  ];
}

function normalizeReviewSelection(mockType: MockType, subType: MockSubType, selection: ReviewSelection): ReviewSelection {
  return buildReviewOptions(mockType, subType).some((option) => option.id === selection) ? selection : 'none';
}

function bundleMatches(bundle: MockBundleOption, mockType: MockType, subType: MockSubType, profession: string) {
  if (bundle.mockType !== mockType) return false;
  if (isSubShape(mockType) && bundle.subtest !== subType) return false;
  if (bundle.appliesToAllProfessions || !bundle.professionId) return true;
  return bundle.professionId === profession;
}

export default function MockSetup() {
  const router = useRouter();
  const searchParams = useSearchParams();

  const [options, setOptions] = useState<MockOptions | null>(null);
  const [loading, setLoading] = useState(true);
  const [mockType, setMockType] = useState<MockType>('full');
  const [subType, setSubType] = useState<MockSubType>('reading');
  const [mode, setMode] = useState<'practice' | 'exam'>('exam');
  const [deliveryMode, setDeliveryMode] = useState<MockDeliveryMode>('computer');
  const [strictness, setStrictness] = useState<MockStrictness>('exam');
  const [profession, setProfession] = useState('medicine');
  const [strictTimer, setStrictTimer] = useState(true);
  const [reviewSelection, setReviewSelection] = useState<ReviewSelection>('none');
  const [selectedBundleId, setSelectedBundleId] = useState<string | null>(null);
  const [entitlementSummary, setEntitlementSummary] = useState<MockEntitlementSummary | null>(null);
  const [entitlementSummaryLoading, setEntitlementSummaryLoading] = useState(true);
  const [starting, setStarting] = useState(false);
  // Two places, two meanings: a failed load shows at the top, a failed start or booking beside the Start bar.
  const [loadError, setLoadError] = useState<string | null>(null);
  const [startError, setStartError] = useState<string | null>(null);
  const [bookingAt, setBookingAt] = useState('');
  const [booking, setBooking] = useState(false);

  useEffect(() => {
    let cancelled = false;
    fetchMockOptions()
      .then((result) => {
        if (cancelled) return;
        setOptions(result);
        const queryBundleId = searchParams?.get('bundleId');
        const querySubtest = searchParams?.get('subtest') ?? null;
        const queryType = searchParams?.get('type') ?? null;
        const bundle = queryBundleId ? result.availableBundles.find((item) => item.bundleId === queryBundleId || item.id === queryBundleId) : null;
        if (bundle) {
          setSelectedBundleId(bundle.bundleId);
          setMockType(bundle.mockType);
          const defaults = defaultsForMockType(bundle.mockType);
          setMode(defaults.mode);
          setStrictness(defaults.strictness);
          setStrictTimer(defaults.strictTimer);
          if (defaults.deliveryMode) setDeliveryMode(defaults.deliveryMode);
          const bundleSubtest = bundle.subtest ?? null;
          if (isSubtest(bundleSubtest)) setSubType(bundleSubtest);
        } else if (isSubtest(querySubtest)) {
          setMockType('sub');
          setSubType(querySubtest);
        } else if (isMockTypeToken(queryType)) {
          setMockType(queryType);
          const defaults = defaultsForMockType(queryType);
          setMode(defaults.mode);
          setStrictness(defaults.strictness);
          setStrictTimer(defaults.strictTimer);
          if (defaults.deliveryMode) setDeliveryMode(defaults.deliveryMode);
          if (isSubShape(queryType) && isSubtest(querySubtest)) setSubType(querySubtest);
        }
        // Preselect the learner's own profession; only fall back to the
        // catalog's first entry when the profile has none set.
        const learnerProfession =
          typeof (result as { learnerProfession?: unknown }).learnerProfession === 'string'
            ? ((result as { learnerProfession?: string }).learnerProfession ?? null)
            : null;
        const preferred =
          (learnerProfession && result.professions.find((p) => p.id === learnerProfession)?.id)
          || result.professions[0]?.id;
        if (preferred) setProfession(preferred);
      })
      .catch(() => setLoadError('Failed to load mock setup options.'))
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [searchParams]);

  useEffect(() => {
    let cancelled = false;
    window.queueMicrotask(() => {
      if (!cancelled) setEntitlementSummaryLoading(true);
    });
    fetchMockEntitlementsSummary()
      .then((summary) => {
        if (cancelled) return;
        setEntitlementSummary(summary);
      })
      .catch(() => {
        if (cancelled) return;
        setEntitlementSummary({ items: [], anyExhausted: false });
      })
      .finally(() => {
        if (!cancelled) setEntitlementSummaryLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const reviewOptions = useMemo(() => buildReviewOptions(mockType, subType), [mockType, subType]);
  const selectedReviewSelection = normalizeReviewSelection(mockType, subType, reviewSelection);
  const availableCredits = options?.wallet.availableCredits ?? 0;
  const selectedReviewCost = reviewCost(selectedReviewSelection);
  const insufficientCredits = selectedReviewCost > availableCredits;

  // Phase 8b — entitlement gating. Maps each backend ledger token to its
  // remaining-credit count so the type selector and Start button can disable
  // options the learner does not own. Subscribers are unlimited and bypass
  // the gate via the `hasEligibleSubscription` shortcut.
  const remainingByLedgerKey = useMemo(() => {
    const map: Record<string, number> = {};
    for (const item of entitlementSummary?.items ?? []) {
      map[item.mockType] = item.remaining;
    }
    return map;
  }, [entitlementSummary]);
  const isUnlimitedEntitlement = useMemo(
    () => (entitlementSummary?.items ?? []).some((item) => item.granted >= Number.MAX_SAFE_INTEGER / 2),
    [entitlementSummary],
  );
  const remainingForMockType = (id: MockTypeToken): number => {
    if (!ENTITLEMENT_GATED_TYPES.has(id)) return Number.POSITIVE_INFINITY;
    if (isUnlimitedEntitlement) return Number.POSITIVE_INFINITY;
    const key = ledgerKeyForSelection(id, subType);
    return remainingByLedgerKey[key] ?? 0;
  };
  const selectedMockTypeRemaining = remainingForMockType(mockType);
  const entitlementBlocked = entitlementSummary !== null
    && !isUnlimitedEntitlement
    && ENTITLEMENT_GATED_TYPES.has(mockType)
    && selectedMockTypeRemaining <= 0;

  // Build a short plain-English balance line: "You have 2 Full mock, 1 Speaking
  // mock, 0 Writing mock attempts available." Hidden when the user has an
  // unlimited subscription or no granted credits to summarise.
  const balanceSentence = useMemo(() => {
    if (!entitlementSummary || isUnlimitedEntitlement) return null;
    const visible = entitlementSummary.items.filter((item) => item.granted > 0);
    if (visible.length === 0) return null;
    const parts = visible.map((item) => `${item.remaining} ${item.label}`);
    return `You have ${parts.join(', ')} attempts available.`;
  }, [entitlementSummary, isUnlimitedEntitlement]);

  const availableBundles = useMemo(() => {
    return (options?.availableBundles ?? []).filter((bundle) => bundleMatches(bundle, mockType, subType, profession));
  }, [mockType, options?.availableBundles, profession, subType]);

  const selectedBundle = availableBundles.find((bundle) => bundle.bundleId === selectedBundleId)
    ?? availableBundles[0]
    ?? null;
  const modePolicy = getMockModePolicy(mode);
  const selectedBundlePolicies = useMemo(
    () => selectedBundle?.sections.map((section) => getMockSectionPolicy(section.subtest, mode)) ?? [],
    [mode, selectedBundle],
  );
  const teacherMarkedSectionCount = selectedBundle?.sections.filter((section) => isTeacherMarkedSubtest(section.subtest)).length ?? 0;
  const selectedBundleIncludesSpeaking = selectedBundle?.sections.some((section) => section.subtest === 'speaking') ?? false;

  const handleModeChange = (newMode: 'practice' | 'exam') => {
    if (mockType === 'final_readiness' && newMode === 'practice') return;
    setMode(newMode);
    if (newMode === 'practice') {
      setStrictness('learning');
      setStrictTimer(false);
    } else {
      setStrictness(mockType === 'final_readiness' ? 'final_readiness' : 'exam');
      setStrictTimer(true);
    }
  };

  const handleMockTypeChange = (nextType: MockType) => {
    const defaults = defaultsForMockType(nextType);
    setMockType(nextType);
    setSelectedBundleId(null);
    setStrictness(defaults.strictness);
    setMode(defaults.mode);
    setStrictTimer(defaults.strictTimer);
    if (defaults.deliveryMode) setDeliveryMode(defaults.deliveryMode);
    setReviewSelection((current) => normalizeReviewSelection(nextType, subType, current));
  };

  const handleSubTypeChange = (nextSubType: MockSubType) => {
    setSubType(nextSubType);
    setSelectedBundleId(null);
    setReviewSelection((current) => normalizeReviewSelection(mockType, nextSubType, current));
  };

  const handleStart = async () => {
    if (!selectedBundle || insufficientCredits || entitlementBlocked) return;
    setStarting(true);
    setStartError(null);
    try {
      const result = await createMockSession({
        type: mockType,
        subType: isSubShape(mockType) ? subType : undefined,
        mode,
        profession,
        strictTimer,
        reviewSelection: selectedReviewSelection,
        bundleId: selectedBundle.bundleId,
        deliveryMode,
        strictness,
      });
      analytics.track('mock_started', { mockType, mode, deliveryMode, strictness, reviewSelection: selectedReviewSelection, bundleId: selectedBundle.bundleId });
      router.push(`/mocks/player/${result.sessionId}`);
    } catch (err) {
      setStartError(err instanceof Error ? err.message : 'Failed to start mock session. Please try again.');
      setStarting(false);
    }
  };

  const handleBook = async () => {
    if (!selectedBundle || !bookingAt) return;
    setBooking(true);
    setStartError(null);
    try {
      await createMockBooking({
        mockBundleId: selectedBundle.bundleId,
        scheduledStartAt: new Date(bookingAt).toISOString(),
        timezoneIana: Intl.DateTimeFormat().resolvedOptions().timeZone,
        deliveryMode,
        consentToRecording: mockType === 'final_readiness' || subType === 'speaking' || selectedBundleIncludesSpeaking,
        learnerNotes: `${mockType} booking from learner mock setup.`,
      });
      analytics.track('mock_booking_created', { mockType, deliveryMode, bundleId: selectedBundle.bundleId });
      router.push('/mocks/bookings');
    } catch (err) {
      setStartError(err instanceof Error ? err.message : 'Failed to book this mock.');
      setBooking(false);
    }
  };

  const showProfession = isFullShape(mockType) || subType === 'writing' || subType === 'speaking';

  // One selection recipe for every option card on the form: violet is the selection colour, a
  // check or aria-pressed carries the state, so no option group needs its own hue.
  const optionClass = (selected: boolean, extra?: string) =>
    cn(
      'border text-start transition-colors',
      selected ? 'border-primary bg-primary/5' : 'border-border bg-surface hover:border-border-hover hover:bg-background-light',
      extra,
    );

  return (
    <>
      <LearnerPageHero
        eyebrow="Mock Setup"
        icon={Layers}
        accent="navy"
        title="Start from a published mock bundle"
        description="Choose your mock paper, exam mode, timing, and whether to reserve tutor review before you start."
        highlights={[
          { icon: Award, label: 'Credits', value: `${availableCredits} available` },
          { icon: Layers, label: 'Bundles', value: `${options?.availableBundles.length ?? 0} published` },
          { icon: Clock, label: 'Timer', value: strictTimer ? 'Strict' : 'Flexible' },
        ]}
      />

      <MotionSection>
        <Card padding="lg" role="region" aria-label="Mock entitlement summary">
          <LearnerSurfaceSectionHeader
            eyebrow="Entitlements"
            title="Your mock allowance"
            description="Each mock type has its own bucket. Top up from billing when one runs out."
            icon={ShieldCheck}
            className="mb-4"
          />
          {entitlementSummaryLoading ? (
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2" role="status" aria-busy="true" aria-label="Loading your mock allowance">
              <Skeleton className="h-20 rounded-xl" />
              <Skeleton className="h-20 rounded-xl" />
            </div>
          ) : !entitlementSummary || entitlementSummary.items.length === 0 ? (
            <InlineAlert variant="info" live="polite">
              No mock entitlements found yet. Visit billing to choose a plan or top-up that unlocks mock attempts.
            </InlineAlert>
          ) : (
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              {entitlementSummary.items.map((item) => {
                const totalForPct = item.granted > 0 ? item.granted : Math.max(1, item.consumed + item.remaining);
                const usedPct = Math.min(100, Math.round((item.consumed / totalForPct) * 100));
                const exhausted = item.granted > 0 && item.remaining <= 0;
                // Only a real grant can run low; a never-granted bucket is neutral, not a warning.
                const low = item.granted > 0 && !exhausted && item.remaining <= 1;
                return (
                  <div
                    key={item.mockType}
                    className={cn(
                      'min-w-0 rounded-xl border p-4 transition-colors',
                      exhausted ? 'border-danger/30 bg-danger/5' : low ? 'border-warning/30 bg-warning/5' : 'border-border bg-background-light',
                    )}
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <p className="text-sm font-bold text-navy">{item.label}</p>
                        <p className="mt-1 text-xs tabular-nums text-muted">
                          {item.consumed} of {item.granted} used / {item.remaining} remaining
                        </p>
                      </div>
                      {exhausted ? (
                        <span className="inline-flex shrink-0 items-center gap-1 rounded-md bg-danger/10 px-2 py-1 tile-label text-danger-strong">
                          <Lock className="h-3 w-3" aria-hidden="true" /> Exhausted
                        </span>
                      ) : null}
                    </div>
                    <ProgressBar
                      value={usedPct}
                      color={exhausted ? 'danger' : low ? 'warning' : 'primary'}
                      ariaLabel={`${item.label} usage`}
                      className="mt-3"
                    />
                  </div>
                );
              })}
            </div>
          )}
          {entitlementSummary?.anyExhausted ? (
            <InlineAlert
              variant="error"
              title="You've used all available mocks in at least one bucket."
              className="mt-5"
              action={(
                <Button asChild size="sm">
                  <Link href="/billing">Top up mocks</Link>
                </Button>
              )}
            >
              Top up your mocks to keep practising. Billing add-ons unlock additional attempts immediately.
            </InlineAlert>
          ) : null}
        </Card>
      </MotionSection>

      {loading ? (
        <div className="space-y-4" role="status" aria-busy="true" aria-label="Loading mock setup options">
          <Skeleton className="h-48 rounded-2xl" />
          <Skeleton className="h-48 rounded-2xl" />
        </div>
      ) : null}

      {!loading && loadError ? <InlineAlert variant="error">{loadError}</InlineAlert> : null}

      {!loading ? (
        <>
          <MotionSection delayIndex={1}>
            <Card padding="lg">
              <LearnerSurfaceSectionHeader
                eyebrow="1. Mock Type"
                title="Choose the simulation that matches your goal"
                description="Coming from the Mocks page? Your selection is already preset."
                className="mb-4"
              />
              {balanceSentence ? (
                <InlineAlert variant="info" live="polite" title="Balance" className="mb-4">
                  {balanceSentence}
                </InlineAlert>
              ) : null}
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
                {(() => {
                  // Wave 1: render all canonical mock types. Icon mapping is local; backend
                  // descriptions take precedence when available (`options.mockTypes`).
                  const ICONS: Record<MockTypeToken, ElementType> = {
                    full: Layers,
                    lrw: Layers,
                    sub: FileText,
                    part: FileText,
                    diagnostic: Sparkles,
                    final_readiness: Trophy,
                    remedial: Zap,
                  };
                  const FALLBACK: { id: MockTypeToken; label: string; desc: string }[] = [
                    { id: 'full', label: 'Full Mock', desc: 'Listening, Reading, Writing, Speaking in OET order.' },
                    { id: 'lrw', label: 'LRW Mock', desc: 'Listening + Reading + Writing in one sitting (Speaking scheduled separately).' },
                    { id: 'sub', label: 'Single Sub-test', desc: 'One published section for targeted evidence.' },
                    { id: 'part', label: 'Part Mock', desc: 'A single part within a sub-test (e.g. Reading Part A).' },
                    { id: 'final_readiness', label: 'Final Readiness Mock', desc: 'Strict full mock taken before booking the real exam.' },
                    { id: 'remedial', label: 'Remedial Mock', desc: 'Targeted mock generated from your weak-area analysis.' },
                  ];
                  const fromApi = (options?.mockTypes ?? []).map((m) => ({ id: m.id, label: m.label, desc: m.description }));
                  // Diagnostic mock removed — filter it out even if the API still advertises it.
                  const list = (fromApi.length > 0 ? fromApi : FALLBACK).filter((t) => t.id !== 'diagnostic');
                  return list.map(({ id, label, desc }) => {
                    const Icon = ICONS[id] ?? FileText;
                    const isSelected = mockType === id;
                    // Phase 8b — disable types whose ledger bucket is empty.
                    // `entitlementSummary === null` means we haven't loaded yet;
                    // don't gate until we have ground truth so the UI doesn't
                    // flicker.
                    const remaining = remainingForMockType(id);
                    const isGated = ENTITLEMENT_GATED_TYPES.has(id)
                      && entitlementSummary !== null
                      && !isUnlimitedEntitlement;
                    const disabled = isGated && remaining <= 0;
                    return (
                      // The "Buy more" link sits beside the option button, never inside it.
                      <div key={id} className="flex flex-col gap-2">
                        <button
                          type="button"
                          onClick={() => !disabled && handleMockTypeChange(id)}
                          disabled={disabled}
                          aria-disabled={disabled}
                          aria-pressed={isSelected}
                          className={cn(
                            'relative flex-1 rounded-2xl border-2 p-5 text-start transition-[color,background-color,border-color,box-shadow] duration-200',
                            isSelected ? 'border-primary bg-primary/5 ring-4 ring-primary/10' : 'border-border hover:border-border-hover hover:bg-background-light',
                            disabled && 'cursor-not-allowed opacity-60 hover:border-border hover:bg-transparent',
                          )}
                        >
                          <div className="mb-3 flex items-center justify-between">
                            <span className={`flex h-10 w-10 items-center justify-center rounded-full ${isSelected ? 'bg-primary text-white dark:bg-primary-700' : 'bg-background-light text-muted'}`}>
                              <Icon className="h-5 w-5" aria-hidden="true" />
                            </span>
                            {isSelected ? <Check className="h-5 w-5 text-primary" aria-hidden="true" /> : null}
                          </div>
                          <h3 className={`text-lg font-bold ${isSelected ? 'text-primary' : 'text-navy'}`}>{label}</h3>
                          <p className="mt-1 text-sm text-muted">{desc}</p>
                          {isGated ? (
                            <div className="mt-3 flex flex-wrap items-center gap-2">
                              {disabled ? (
                                <Badge variant="danger">0 remaining</Badge>
                              ) : remaining <= 1 ? (
                                <Badge variant="warning" className="tabular-nums">{remaining} remaining</Badge>
                              ) : (
                                <Badge variant="muted" className="tabular-nums">{remaining} remaining</Badge>
                              )}
                            </div>
                          ) : null}
                        </button>
                        {disabled ? (
                          <Link
                            href="/billing"
                            className="hover-primary inline-flex min-h-11 items-center gap-1 self-start rounded-control px-2 text-xs font-bold text-primary transition-colors"
                          >
                            Buy more <ArrowRight className="h-3.5 w-3.5 rtl:rotate-180" aria-hidden="true" />
                          </Link>
                        ) : null}
                      </div>
                    );
                  });
                })()}
              </div>
              {entitlementBlocked ? (
                <InlineAlert variant="warning" className="mt-4">
                  You have no remaining attempts for this mock type. Top up from billing to unlock more.
                </InlineAlert>
              ) : null}
              <MotionCollapse open={isSubShape(mockType)}>
                <div className="pt-5">
                  <p className="mb-3 eyebrow text-muted">Which sub-test?</p>
                  <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
                    {(Object.keys(SUBTEST_META) as MockSubType[]).map((id) => {
                      const meta = SUBTEST_META[id];
                      const Icon = meta.icon;
                      return (
                        <button
                          key={id}
                          type="button"
                          aria-pressed={subType === id}
                          onClick={() => handleSubTypeChange(id)}
                          className={`rounded-xl border px-4 py-3 transition-colors ${subType === id ? meta.active : 'border-border bg-surface text-muted hover:bg-background-light'}`}
                        >
                          <Icon className={`mx-auto mb-2 h-5 w-5 ${subType === id ? '' : meta.iconTone}`} aria-hidden="true" />
                          <span className="text-sm font-bold">{meta.label}</span>
                        </button>
                      );
                    })}
                  </div>
                </div>
              </MotionCollapse>
            </Card>
          </MotionSection>

          <MotionPresence>
            {showProfession ? (
              <MotionSection delayIndex={2}>
                <Card padding="lg">
                  <LearnerSurfaceSectionHeader
                    eyebrow="2. Profession"
                    title="Match your profession"
                    description="Full mocks include Writing and Speaking tailored to your profession. Listening and Reading are shared across all professions."
                    icon={Stethoscope}
                    className="mb-4"
                  />
                  <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
                    {(options?.professions ?? []).map((item) => (
                      <button
                        key={item.id}
                        type="button"
                        aria-pressed={profession === item.id}
                        onClick={() => {
                          setProfession(item.id);
                          setSelectedBundleId(null);
                        }}
                        className={optionClass(
                          profession === item.id,
                          cn('rounded-xl px-4 py-3 text-sm font-bold', profession === item.id ? 'text-primary' : 'text-navy'),
                        )}
                      >
                        {item.label}
                      </button>
                    ))}
                  </div>
                </Card>
              </MotionSection>
            ) : null}
          </MotionPresence>

          <MotionSection delayIndex={3}>
            <Card padding="lg">
              <LearnerSurfaceSectionHeader
                eyebrow={showProfession ? '3. Bundle' : '2. Bundle'}
                title="Pick the authored mock route"
                description="Every mock here is officially published and ready to start."
                className="mb-4"
              />
              {availableBundles.length === 0 ? (
                <InlineAlert variant="info" live="polite">
                  No published bundle matches this selection yet. Ask an admin to publish one from Content Mock Bundles.
                </InlineAlert>
              ) : (
                <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
                  {availableBundles.map((bundle) => {
                    const isSelected = selectedBundle?.bundleId === bundle.bundleId;
                    return (
                      <button
                        key={bundle.bundleId}
                        type="button"
                        aria-pressed={isSelected}
                        onClick={() => setSelectedBundleId(bundle.bundleId)}
                        className={optionClass(isSelected, 'rounded-2xl p-5')}
                      >
                        <div className="flex items-start justify-between gap-3">
                          <div className="min-w-0">
                            <p className="text-base font-bold text-navy">{bundle.title}</p>
                            <p className="mt-1 text-sm tabular-nums text-muted">
                              {bundle.sections.length} section{bundle.sections.length === 1 ? '' : 's'} / {bundle.estimatedDurationMinutes} min
                            </p>
                          </div>
                          {isSelected ? <Check className="h-5 w-5 shrink-0 text-primary" aria-hidden="true" /> : null}
                        </div>
                        <div className="mt-4 flex flex-wrap gap-2">
                          {bundle.releasePolicy ? (
                            <span className="rounded-md bg-warning/10 px-2 py-1 tile-label text-warning-strong">
                              {bundle.releasePolicy.replace(/_/g, ' ')}
                            </span>
                          ) : null}
                          {bundle.sourceStatus ? (
                            <span className="rounded-md bg-background-light px-2 py-1 tile-label text-muted">
                              {bundle.sourceStatus.replace(/_/g, ' ')}
                            </span>
                          ) : null}
                          {bundle.sections.map((section) => (
                            <span key={section.id} className="rounded-md bg-background-light px-2 py-1 tile-label tabular-nums text-muted">
                              {section.subtest} / {section.timeLimitMinutes}m
                            </span>
                          ))}
                        </div>
                      </button>
                    );
                  })}
                </div>
              )}
            </Card>
          </MotionSection>

          <MotionSection delayIndex={4}>
            <Card padding="lg">
              <LearnerSurfaceSectionHeader
                eyebrow={showProfession ? '4. Environment' : '3. Environment'}
                title="Set timing and exam behavior"
                description="Exam mode enforces strict timing. Practice mode lets you pause and take breaks."
                className="mb-4"
              />
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <button
                  type="button"
                  aria-pressed={mode === 'exam'}
                  onClick={() => handleModeChange('exam')}
                  className={optionClass(mode === 'exam', 'rounded-2xl p-4')}
                >
                  <ShieldCheck className={`mb-3 h-5 w-5 ${mode === 'exam' ? 'text-primary' : 'text-muted'}`} aria-hidden="true" />
                  <p className="text-sm font-bold text-navy">Exam Mode</p>
                  <p className="mt-1 text-xs text-muted">Strict timing and full simulation behavior.</p>
                </button>
                <button
                  type="button"
                  aria-pressed={mode === 'practice'}
                  onClick={() => handleModeChange('practice')}
                  disabled={mockType === 'final_readiness'}
                  className={optionClass(mode === 'practice', cn('rounded-2xl p-4', mockType === 'final_readiness' && 'cursor-not-allowed opacity-60'))}
                >
                  <Award className={`mb-3 h-5 w-5 ${mode === 'practice' ? 'text-primary' : 'text-muted'}`} aria-hidden="true" />
                  <p className="text-sm font-bold text-navy">Practice Mode</p>
                  <p className="mt-1 text-xs text-muted">Flexible timing for targeted practice.</p>
                </button>
              </div>
              <div className="mt-6">
                <p className="text-sm font-bold text-navy">{modePolicy.label}</p>
                <p className="mt-1 text-sm leading-6 text-muted">{modePolicy.description}</p>
                <div className="mt-4 grid grid-cols-2 gap-2 lg:grid-cols-4">
                  {[
                    ['Listening replay', modePolicy.listeningReplayAllowed ? 'Allowed' : 'Locked'],
                    ['Pause timer', modePolicy.pauseAllowed ? 'Allowed' : 'Locked'],
                    ['Writing assistant', modePolicy.writingAssistantAllowed ? 'Allowed' : 'Locked'],
                    ['Review after submit', modePolicy.reviewAfterSubmission ? 'Released' : 'Hidden'],
                  ].map(([label, value]) => (
                    <div key={label} className="min-w-0 rounded-xl border border-border bg-background-light px-3 py-2">
                      <p className="tile-label text-muted">{label}</p>
                      <p className="mt-1 text-sm font-bold text-navy">{value}</p>
                    </div>
                  ))}
                </div>
              </div>
              <div className="mt-6 grid grid-cols-1 gap-6 lg:grid-cols-2">
                <div>
                  <p className="eyebrow text-muted">Delivery mode</p>
                  <div className="mt-3 grid grid-cols-1 gap-2">
                    {(options?.deliveryModes?.length ? options.deliveryModes : [
                      { id: 'computer' as const, label: 'On-screen (computer)' },
                      { id: 'oet_home' as const, label: 'OET@Home (remote)' },
                      { id: 'paper' as const, label: 'Paper-based' },
                    ]).map((item) => (
                      <button
                        key={item.id}
                        type="button"
                        aria-pressed={deliveryMode === item.id}
                        onClick={() => setDeliveryMode(item.id)}
                        className={optionClass(
                          deliveryMode === item.id,
                          cn('min-h-11 rounded-xl px-3 py-2 text-sm font-bold', deliveryMode === item.id ? 'text-primary' : 'text-navy'),
                        )}
                      >
                        {item.label}
                      </button>
                    ))}
                  </div>
                </div>
                <div>
                  <p className="eyebrow text-muted">Strictness preset</p>
                  <div className="mt-3 grid grid-cols-1 gap-2">
                    {(options?.strictnessOptions?.length ? options.strictnessOptions : [
                      { id: 'learning' as const, label: 'Learning', description: 'Pause, replay, and hints allowed.' },
                      { id: 'exam' as const, label: 'Exam', description: 'Strict timers, one-play audio, no hints.' },
                      { id: 'final_readiness' as const, label: 'Final readiness', description: 'Strictest OET@Home-style readiness preset.' },
                    ]).map((item) => (
                      <button
                        key={item.id}
                        type="button"
                        aria-pressed={strictness === item.id}
                        onClick={() => {
                          setStrictness(item.id);
                          if (item.id === 'learning') setMode('practice');
                          else {
                            setMode('exam');
                            setStrictTimer(true);
                          }
                        }}
                        className={optionClass(
                          strictness === item.id,
                          cn('min-h-11 rounded-xl px-3 py-2', strictness === item.id ? 'text-primary' : 'text-navy'),
                        )}
                      >
                        <span className="text-sm font-bold">{item.label}</span>
                        {item.description ? <span className="mt-1 block text-xs font-normal leading-5 text-muted">{item.description}</span> : null}
                      </button>
                    ))}
                  </div>
                </div>
              </div>
              <div className="mt-6 flex items-center justify-between gap-4 border-t border-border pt-6">
                <div className="min-w-0">
                  <p className="flex items-center gap-2 text-sm font-bold text-navy"><Clock className="h-4 w-4 shrink-0 text-muted" aria-hidden="true" /> Strict Timer</p>
                  <p className="mt-1 text-xs text-muted">Use the official timing for each section automatically.</p>
                  {mode === 'exam' ? (
                    <p className="mt-2 flex items-center gap-1 tile-label text-danger-strong">
                      <Info className="h-3 w-3 shrink-0" aria-hidden="true" /> Required in exam mode
                    </p>
                  ) : null}
                </div>
                <Switch
                  checked={strictTimer}
                  onChange={() => mode !== 'exam' && setStrictTimer(!strictTimer)}
                  disabled={mode === 'exam'}
                  label="Use strict timer"
                />
              </div>
              {mockType === 'final_readiness' ? (
                <p className="mt-3 text-xs font-semibold text-danger-strong">
                  Final-readiness mocks always run in exam mode with the strict OET@Home-style preset.
                </p>
              ) : null}
            </Card>
          </MotionSection>

          <MotionSection delayIndex={5}>
            <Card padding="lg">
              <LearnerSurfaceSectionHeader
                eyebrow={showProfession ? '5. Workflow Contract' : '4. Workflow Contract'}
                title="Attempt → Review → Remediation"
                description="Practice mode teaches. Mock mode tests. Review mode improves."
                className="mb-4"
              />
              <div className="grid grid-cols-1 gap-6 lg:grid-cols-[minmax(0,1.15fr)_minmax(0,0.85fr)]">
                <div>
                  <p className="eyebrow text-muted">Official-style route</p>
                  <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2">
                    {MOCK_EXAM_FLOW_STAGES.map((stage) => (
                      <div key={stage.id} className="min-w-0 rounded-xl border border-border bg-background-light p-3">
                        <div className="flex items-start justify-between gap-3">
                          <p className="text-sm font-bold text-navy">{stage.label}</p>
                          <span className="shrink-0 rounded-full bg-surface px-2 py-0.5 tile-label tabular-nums text-muted">
                            {stage.duration}
                          </span>
                        </div>
                        <p className="mt-1 text-xs leading-5 text-muted">{stage.description}</p>
                      </div>
                    ))}
                  </div>
                </div>

                <div>
                  <p className="eyebrow text-muted">Selected bundle policy</p>
                  {selectedBundle ? (
                    <div className="mt-3 space-y-3">
                      {selectedBundlePolicies.map((policy) => (
                        <div key={policy.subtest} className="rounded-xl border border-border bg-background-light p-3">
                          <p className="text-sm font-bold text-navy">{policy.label} · {policy.timing}</p>
                          <p className="mt-1 text-xs leading-5 text-muted">{policy.examRule}</p>
                          <p className="mt-1 text-xs leading-5 text-muted">{policy.reviewRule}</p>
                        </div>
                      ))}
                      <InlineAlert variant={teacherMarkedSectionCount > 0 && selectedReviewSelection === 'none' ? 'warning' : 'info'} live="polite">
                        {teacherMarkedSectionCount > 0
                          ? selectedReviewSelection === 'none'
                            ? 'This bundle includes Writing/Speaking. Add tutor review for a final readiness-grade report.'
                            : 'Tutor review is reserved for productive skills and will gate the final readiness report.'
                          : 'This bundle is auto-scored, so results can be released immediately after submission.'}
                      </InlineAlert>
                    </div>
                  ) : (
                    <p className="mt-3 text-sm text-muted">Select a published bundle to see its timing, review, and release policy.</p>
                  )}
                </div>
              </div>
            </Card>
          </MotionSection>

          <MotionSection delayIndex={5}>
            <Card padding="lg">
              <LearnerSurfaceSectionHeader
                eyebrow={showProfession ? '6. Review Credits' : '5. Review Credits'}
                title="Reserve tutor review at mock start"
                description="Credits are reserved when you start, used when you submit Writing or Speaking, and refunded if you cancel."
                className="mb-4"
              />
              <div className="mb-4 inline-flex rounded-md bg-warning/10 px-2 py-1 tile-label tabular-nums text-warning-strong">
                {availableCredits} credits available
              </div>
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                {reviewOptions.map((option) => {
                  const disabled = option.cost > availableCredits;
                  const isSelected = selectedReviewSelection === option.id;
                  return (
                    <button
                      key={option.id}
                      type="button"
                      disabled={disabled}
                      aria-pressed={isSelected}
                      onClick={() => setReviewSelection(option.id)}
                      className={cn(
                        'rounded-2xl border p-4 text-start transition-colors',
                        isSelected
                          ? 'border-primary bg-primary/5'
                          : disabled
                            ? 'cursor-not-allowed border-border bg-background-light opacity-60'
                            : 'border-border bg-surface hover:border-border-hover',
                      )}
                    >
                      <p className="text-sm font-bold text-navy">{option.label}</p>
                      <p className="mt-2 text-xs leading-5 text-muted">{option.description}</p>
                      <p className="mt-3 tile-label tabular-nums text-muted">
                        {option.cost} credit{option.cost === 1 ? '' : 's'}
                      </p>
                    </button>
                  );
                })}
              </div>
              {insufficientCredits ? (
                <InlineAlert variant="warning" className="mt-4">This review selection needs more credits before the mock can start.</InlineAlert>
              ) : null}
            </Card>
          </MotionSection>

          <MotionSection delayIndex={5}>
            <Card padding="lg">
              <LearnerSurfaceSectionHeader
                eyebrow={showProfession ? '7. Scheduling' : '6. Scheduling'}
                title="Book final-readiness or live Speaking mocks"
                description="Scheduled mocks use the OET@Home-style pre-check flow and release results by the selected bundle policy."
                className="mb-4"
              />
              <div className="grid grid-cols-1 gap-4 lg:grid-cols-[minmax(0,1fr)_auto]">
                <Input
                  label="Scheduled start"
                  type="datetime-local"
                  value={bookingAt}
                  onChange={(event) => setBookingAt(event.target.value)}
                />
                <Button
                  variant="secondary"
                  onClick={handleBook}
                  loading={booking}
                  disabled={!selectedBundle || !bookingAt}
                  className="self-end"
                >
                  Book this mock
                </Button>
              </div>
              <p className="mt-3 text-xs leading-5 text-muted">
                Speaking bookings hide interlocutor cards from learners and expose them only to tutors.
              </p>
            </Card>
          </MotionSection>

          {startError ? <InlineAlert variant="error">{startError}</InlineAlert> : null}

          <div className="sticky bottom-4 z-10 rounded-2xl border border-border bg-surface/95 p-3 shadow-lg backdrop-blur">
            <Button
              onClick={handleStart}
              disabled={starting || !selectedBundle || insufficientCredits || entitlementBlocked}
              size="lg"
              fullWidth
            >
              {starting ? 'Starting...' : 'Start Mock Test'}
            </Button>
            <p className="mt-3 text-center text-xs text-muted">
              {entitlementBlocked
                ? 'No remaining attempts for this mock type. Top up from billing to continue.'
                : selectedBundle
                  ? `${selectedBundle.title} / ${selectedBundle.estimatedDurationMinutes} minutes`
                  : 'Select a published bundle to continue.'}
            </p>
          </div>
        </>
      ) : null}
    </>
  );
}
