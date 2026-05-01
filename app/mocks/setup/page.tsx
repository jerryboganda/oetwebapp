'use client';

import { useEffect, useMemo, useState } from 'react';
import type { ElementType } from 'react';
import {
  Award,
  Check,
  Clock,
  FileText,
  Headphones,
  Info,
  Layers,
  Mic,
  PenTool,
  ShieldCheck,
  Sparkles,
  Stethoscope,
  Trophy,
  Zap,
} from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { MotionCollapse, MotionPresence, MotionSection } from '@/components/ui/motion-primitives';
import { createMockBooking, createMockSession, fetchMockDiagnosticEntitlement, fetchMockOptions } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { MockBundleOption, MockConfig, MockDeliveryMode, MockDiagnosticEntitlement, MockOptions, MockStrictness, MockTypeToken } from '@/lib/mock-data';
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
  'full', 'lrw', 'sub', 'part', 'diagnostic', 'final_readiness', 'remedial',
]);
const FULL_SHAPE_TOKENS: ReadonlySet<MockTypeToken> = new Set<MockTypeToken>([
  'full', 'lrw', 'diagnostic', 'final_readiness',
]);
const SUB_SHAPE_TOKENS: ReadonlySet<MockTypeToken> = new Set<MockTypeToken>([
  'sub', 'part', 'remedial',
]);
function isMockTypeToken(value: unknown): value is MockTypeToken {
  return typeof value === 'string' && MOCK_TYPE_TOKENS.has(value as MockTypeToken);
}
function isFullShape(t: MockType) { return FULL_SHAPE_TOKENS.has(t); }
function isSubShape(t: MockType) { return SUB_SHAPE_TOKENS.has(t); }

const SUBTEST_META: Record<MockSubType, { label: string; icon: ElementType; active: string }> = {
  listening: { label: 'Listening', icon: Headphones, active: 'border-primary bg-primary/10 text-primary' },
  reading: { label: 'Reading', icon: FileText, active: 'border-info bg-info/10 text-info' },
  writing: { label: 'Writing', icon: PenTool, active: 'border-danger bg-danger/10 text-danger' },
  speaking: { label: 'Speaking', icon: Mic, active: 'border-primary bg-primary/10 text-primary' },
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
  const [diagnosticEntitlement, setDiagnosticEntitlement] = useState<MockDiagnosticEntitlement | null>(null);
  const [starting, setStarting] = useState(false);
  const [startError, setStartError] = useState<string | null>(null);
  const [bookingAt, setBookingAt] = useState('');
  const [booking, setBooking] = useState(false);

  useEffect(() => {
    let cancelled = false;
    Promise.all([
      fetchMockOptions(),
      fetchMockDiagnosticEntitlement().catch(() => null),
    ])
      .then(([result, entitlement]) => {
        if (cancelled) return;
        setOptions(result);
        setDiagnosticEntitlement(entitlement);
        const queryBundleId = searchParams?.get('bundleId');
        const querySubtest = searchParams?.get('subtest');
        const queryType = searchParams?.get('type');
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
        const firstProfession = result.professions[0]?.id;
        if (firstProfession) setProfession(firstProfession);
      })
      .catch(() => setStartError('Failed to load mock setup options.'))
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [searchParams]);

  const reviewOptions = useMemo(() => buildReviewOptions(mockType, subType), [mockType, subType]);
  const selectedReviewSelection = normalizeReviewSelection(mockType, subType, reviewSelection);
  const availableCredits = options?.wallet.availableCredits ?? 0;
  const selectedReviewCost = reviewCost(selectedReviewSelection);
  const insufficientCredits = selectedReviewCost > availableCredits;

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
  const diagnosticBlocked = mockType === 'diagnostic' && diagnosticEntitlement?.allowed === false;

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
    if (!selectedBundle || insufficientCredits || diagnosticBlocked) return;
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

  return (
    <LearnerDashboardShell pageTitle="Configure Mock" subtitle="Set up your practice environment" backHref="/mocks">
      <div className="space-y-8 pb-24">
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

        {loading ? (
          <div className="space-y-4">
            <Skeleton className="h-48 rounded-2xl" />
            <Skeleton className="h-48 rounded-2xl" />
          </div>
        ) : null}

        {!loading && startError ? <InlineAlert variant="error">{startError}</InlineAlert> : null}

        {!loading ? (
          <>
            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm sm:p-8">
              <LearnerSurfaceSectionHeader
                eyebrow="1. Mock Type"
                title="Choose the simulation that matches your goal"
                description="Coming from the Mocks page? Your selection is already preset."
                className="mb-4"
              />
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
                    { id: 'diagnostic', label: 'Diagnostic Mock', desc: 'Establish your baseline and unlock a personalised study path.' },
                    { id: 'final_readiness', label: 'Final Readiness Mock', desc: 'Strict full mock taken before booking the real exam.' },
                    { id: 'remedial', label: 'Remedial Mock', desc: 'Targeted mock generated from your weak-area analysis.' },
                  ];
                  const fromApi = (options?.mockTypes ?? []).map((m) => ({ id: m.id, label: m.label, desc: m.description }));
                  const list = fromApi.length > 0 ? fromApi : FALLBACK;
                  return list.map(({ id, label, desc }) => {
                    const Icon = ICONS[id] ?? FileText;
                    const isSelected = mockType === id;
                    return (
                      <button
                        key={id}
                        type="button"
                        onClick={() => handleMockTypeChange(id)}
                        className={`rounded-2xl border-2 p-5 text-left transition-all ${
                          isSelected ? 'border-primary bg-primary/5 ring-4 ring-primary/10' : 'border-border hover:border-border-hover hover:bg-background-light'
                        }`}
                      >
                        <div className="mb-3 flex items-center justify-between">
                          <span className={`flex h-10 w-10 items-center justify-center rounded-full ${isSelected ? 'bg-primary text-white' : 'bg-background-light text-muted'}`}>
                            <Icon className="h-5 w-5" />
                          </span>
                          {isSelected ? <Check className="h-5 w-5 text-primary" /> : null}
                        </div>
                        <h3 className={`text-lg font-bold ${isSelected ? 'text-primary' : 'text-navy'}`}>{label}</h3>
                        <p className="mt-1 text-sm text-muted">{desc}</p>
                      </button>
                    );
                  });
                })()}
              </div>
              {diagnosticBlocked ? (
                <div className="mt-4">
                  <InlineAlert variant="warning">
                    {diagnosticEntitlement?.message ?? 'Your billing plan does not currently allow another diagnostic mock.'}
                  </InlineAlert>
                </div>
              ) : null}
              <MotionCollapse open={isSubShape(mockType)}>
                <div className="pt-5">
                  <p className="mb-3 text-xs font-black uppercase tracking-widest text-muted">Which sub-test?</p>
                  <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
                    {(Object.keys(SUBTEST_META) as MockSubType[]).map((id) => {
                      const meta = SUBTEST_META[id];
                      const Icon = meta.icon;
                      return (
                        <button
                          key={id}
                          type="button"
                          onClick={() => handleSubTypeChange(id)}
                          className={`rounded-xl border px-4 py-3 transition-colors ${subType === id ? meta.active : 'border-border bg-surface text-muted hover:bg-background-light'}`}
                        >
                          <Icon className="mx-auto mb-2 h-5 w-5" />
                          <span className="text-sm font-bold">{meta.label}</span>
                        </button>
                      );
                    })}
                  </div>
                </div>
              </MotionCollapse>
            </section>

            <MotionPresence>
              {showProfession ? (
                <MotionSection className="rounded-2xl border border-border bg-surface p-6 shadow-sm sm:p-8">
                  <LearnerSurfaceSectionHeader
                    eyebrow="2. Profession"
                    title="Match your profession"
                    description="Full mocks include Writing and Speaking tailored to your profession. Listening and Reading are shared across all professions."
                    icon={Stethoscope}
                    className="mb-4"
                  />
                  <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
                    {(options?.professions ?? []).map((item) => (
                      <button
                        key={item.id}
                        type="button"
                        onClick={() => {
                          setProfession(item.id);
                          setSelectedBundleId(null);
                        }}
                        className={`rounded-xl border px-4 py-3 text-left text-sm font-bold transition-colors ${
                          profession === item.id ? 'border-primary bg-primary/5 text-primary' : 'border-border bg-surface text-navy hover:bg-background-light'
                        }`}
                      >
                        {item.label}
                      </button>
                    ))}
                  </div>
                </MotionSection>
              ) : null}
            </MotionPresence>

            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm sm:p-8">
              <LearnerSurfaceSectionHeader
                eyebrow={showProfession ? '3. Bundle' : '2. Bundle'}
                title="Pick the authored mock route"
                description="Every mock here is officially published and ready to start."
                className="mb-4"
              />
              {availableBundles.length === 0 ? (
                <InlineAlert variant="info">
                  No published bundle matches this selection yet. Ask an admin to publish one from Content Mock Bundles.
                </InlineAlert>
              ) : (
                <div className="grid gap-4 lg:grid-cols-2">
                  {availableBundles.map((bundle) => (
                    <button
                      key={bundle.bundleId}
                      type="button"
                      onClick={() => setSelectedBundleId(bundle.bundleId)}
                      className={`rounded-2xl border p-5 text-left transition-colors ${
                        selectedBundle?.bundleId === bundle.bundleId ? 'border-primary bg-primary/5' : 'border-border bg-surface hover:border-border-hover'
                      }`}
                    >
                      <div className="flex items-start justify-between gap-3">
                        <div>
                          <p className="text-base font-black text-navy">{bundle.title}</p>
                          <p className="mt-1 text-sm text-muted">
                            {bundle.sections.length} section{bundle.sections.length === 1 ? '' : 's'} / {bundle.estimatedDurationMinutes} min
                          </p>
                        </div>
                        {selectedBundle?.bundleId === bundle.bundleId ? <Check className="h-5 w-5 text-primary" /> : null}
                      </div>
                      <div className="mt-4 flex flex-wrap gap-2">
                        {bundle.releasePolicy ? (
                          <span className="rounded-md bg-warning/10 px-2 py-1 text-[11px] font-black uppercase tracking-widest text-warning">
                            {bundle.releasePolicy.replace(/_/g, ' ')}
                          </span>
                        ) : null}
                        {bundle.sourceStatus ? (
                          <span className="rounded-md bg-background-light px-2 py-1 text-[11px] font-black uppercase tracking-widest text-muted">
                            {bundle.sourceStatus.replace(/_/g, ' ')}
                          </span>
                        ) : null}
                        {bundle.sections.map((section) => (
                          <span key={section.id} className="rounded-md bg-background-light px-2 py-1 text-[11px] font-black uppercase tracking-widest text-muted">
                            {section.subtest} / {section.timeLimitMinutes}m
                          </span>
                        ))}
                      </div>
                    </button>
                  ))}
                </div>
              )}
            </section>

            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm sm:p-8">
              <LearnerSurfaceSectionHeader
                eyebrow={showProfession ? '4. Environment' : '3. Environment'}
                title="Set timing and exam behavior"
                description="Exam mode enforces strict timing. Practice mode lets you pause and take breaks."
                className="mb-4"
              />
              <div className="grid gap-4 sm:grid-cols-2">
                <button
                  type="button"
                  onClick={() => handleModeChange('exam')}
                  className={`rounded-2xl border-2 p-4 text-left transition-colors ${mode === 'exam' ? 'border-danger bg-danger/10' : 'border-border hover:bg-background-light'}`}
                >
                  <ShieldCheck className={`mb-3 h-5 w-5 ${mode === 'exam' ? 'text-danger' : 'text-muted'}`} />
                  <p className="text-sm font-bold text-navy">Exam Mode</p>
                  <p className="mt-1 text-xs text-muted">Strict timing and full simulation behavior.</p>
                </button>
                <button
                  type="button"
                  onClick={() => handleModeChange('practice')}
                  disabled={mockType === 'final_readiness'}
                  className={`rounded-2xl border-2 p-4 text-left transition-colors ${mode === 'practice' ? 'border-info bg-info/10' : 'border-border hover:bg-background-light'} ${mockType === 'final_readiness' ? 'cursor-not-allowed opacity-60' : ''}`}
                >
                  <Award className={`mb-3 h-5 w-5 ${mode === 'practice' ? 'text-info' : 'text-muted'}`} />
                  <p className="text-sm font-bold text-navy">Practice Mode</p>
                  <p className="mt-1 text-xs text-muted">Flexible timing for targeted practice.</p>
                </button>
              </div>
              <div className="mt-6 rounded-2xl border border-border bg-background-light p-4">
                <p className="text-sm font-black text-navy">{modePolicy.label}</p>
                <p className="mt-1 text-sm leading-6 text-muted">{modePolicy.description}</p>
                <div className="mt-4 grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
                  {[
                    ['Listening replay', modePolicy.listeningReplayAllowed ? 'Allowed' : 'Locked'],
                    ['Pause timer', modePolicy.pauseAllowed ? 'Allowed' : 'Locked'],
                    ['Writing assistant', modePolicy.writingAssistantAllowed ? 'Allowed' : 'Locked'],
                    ['Review after submit', modePolicy.reviewAfterSubmission ? 'Released' : 'Hidden'],
                  ].map(([label, value]) => (
                    <div key={label} className="rounded-xl bg-surface px-3 py-2">
                      <p className="text-[10px] font-black uppercase tracking-widest text-muted">{label}</p>
                      <p className="mt-1 text-sm font-bold text-navy">{value}</p>
                    </div>
                  ))}
                </div>
              </div>
              <div className="mt-6 grid gap-4 lg:grid-cols-2">
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <p className="text-xs font-black uppercase tracking-widest text-muted">Delivery mode</p>
                  <div className="mt-3 grid gap-2">
                    {(options?.deliveryModes?.length ? options.deliveryModes : [
                      { id: 'computer' as const, label: 'On-screen (computer)' },
                      { id: 'oet_home' as const, label: 'OET@Home (remote)' },
                      { id: 'paper' as const, label: 'Paper-based' },
                    ]).map((item) => (
                      <button
                        key={item.id}
                        type="button"
                        onClick={() => setDeliveryMode(item.id)}
                        className={`rounded-xl border px-3 py-2 text-left text-sm font-bold transition-colors ${
                          deliveryMode === item.id ? 'border-primary bg-primary/5 text-primary' : 'border-border bg-surface text-navy hover:bg-background-light'
                        }`}
                      >
                        {item.label}
                      </button>
                    ))}
                  </div>
                </div>
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <p className="text-xs font-black uppercase tracking-widest text-muted">Strictness preset</p>
                  <div className="mt-3 grid gap-2">
                    {(options?.strictnessOptions?.length ? options.strictnessOptions : [
                      { id: 'learning' as const, label: 'Learning', description: 'Pause, replay, and hints allowed.' },
                      { id: 'exam' as const, label: 'Exam', description: 'Strict timers, one-play audio, no hints.' },
                      { id: 'final_readiness' as const, label: 'Final readiness', description: 'Strictest OET@Home-style readiness preset.' },
                    ]).map((item) => (
                      <button
                        key={item.id}
                        type="button"
                        onClick={() => {
                          setStrictness(item.id);
                          if (item.id === 'learning') setMode('practice');
                          else {
                            setMode('exam');
                            setStrictTimer(true);
                          }
                        }}
                        className={`rounded-xl border px-3 py-2 text-left transition-colors ${
                          strictness === item.id ? 'border-danger bg-danger/10 text-danger' : 'border-border bg-surface text-navy hover:bg-background-light'
                        }`}
                      >
                        <span className="text-sm font-bold">{item.label}</span>
                        {item.description ? <span className="mt-1 block text-xs font-normal leading-5 text-muted">{item.description}</span> : null}
                      </button>
                    ))}
                  </div>
                </div>
              </div>
              <div className="mt-6 flex items-center justify-between border-t border-border pt-6">
                <div className="pr-4">
                  <p className="flex items-center gap-2 text-sm font-bold text-navy"><Clock className="h-4 w-4 text-muted" /> Strict Timer</p>
                  <p className="mt-1 text-xs text-muted">Use the official timing for each section automatically.</p>
                  {mode === 'exam' ? (
                    <p className="mt-2 flex items-center gap-1 text-[10px] font-black uppercase tracking-widest text-danger">
                      <Info className="h-3 w-3" /> Required in exam mode
                    </p>
                  ) : null}
                </div>
                <button
                  type="button"
                  onClick={() => mode !== 'exam' && setStrictTimer(!strictTimer)}
                  disabled={mode === 'exam'}
                  className={`relative inline-flex h-8 w-14 shrink-0 items-center rounded-full transition-colors ${strictTimer ? 'bg-primary' : 'bg-border'} ${mode === 'exam' ? 'cursor-not-allowed opacity-50' : ''}`}
                  role="switch"
                  aria-checked={strictTimer}
                >
                  <span className="sr-only">Use strict timer</span>
                  <span className={`inline-block h-6 w-6 rounded-full bg-white shadow transition-transform ${strictTimer ? 'translate-x-7' : 'translate-x-1'}`} />
                </button>
              </div>
              {mockType === 'final_readiness' ? (
                <p className="mt-3 text-xs font-semibold text-danger">
                  Final-readiness mocks always run in exam mode with the strict OET@Home-style preset.
                </p>
              ) : null}
            </section>

            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm sm:p-8">
              <LearnerSurfaceSectionHeader
                eyebrow={showProfession ? '5. Workflow Contract' : '4. Workflow Contract'}
                title="Attempt → Review → Remediation"
                description="Practice mode teaches. Mock mode tests. Review mode improves."
                className="mb-4"
              />
              <div className="grid gap-4 lg:grid-cols-[1.15fr_0.85fr]">
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <p className="text-xs font-black uppercase tracking-widest text-muted">Official-style route</p>
                  <div className="mt-4 grid gap-3 sm:grid-cols-2">
                    {MOCK_EXAM_FLOW_STAGES.map((stage) => (
                      <div key={stage.id} className="rounded-xl bg-surface p-3">
                        <div className="flex items-start justify-between gap-3">
                          <p className="text-sm font-bold text-navy">{stage.label}</p>
                          <span className="shrink-0 rounded-full bg-background-light px-2 py-0.5 text-[10px] font-black uppercase tracking-widest text-muted">
                            {stage.duration}
                          </span>
                        </div>
                        <p className="mt-1 text-xs leading-5 text-muted">{stage.description}</p>
                      </div>
                    ))}
                  </div>
                </div>

                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <p className="text-xs font-black uppercase tracking-widest text-muted">Selected bundle policy</p>
                  {selectedBundle ? (
                    <div className="mt-4 space-y-3">
                      {selectedBundlePolicies.map((policy) => (
                        <div key={policy.subtest} className="rounded-xl bg-surface p-3">
                          <p className="text-sm font-bold text-navy">{policy.label} · {policy.timing}</p>
                          <p className="mt-1 text-xs leading-5 text-muted">{policy.examRule}</p>
                          <p className="mt-1 text-xs leading-5 text-muted">{policy.reviewRule}</p>
                        </div>
                      ))}
                      <InlineAlert variant={teacherMarkedSectionCount > 0 && selectedReviewSelection === 'none' ? 'warning' : 'info'}>
                        {teacherMarkedSectionCount > 0
                          ? selectedReviewSelection === 'none'
                            ? 'This bundle includes Writing/Speaking. Add tutor review for a final readiness-grade report.'
                            : 'Tutor review is reserved for productive skills and will gate the final readiness report.'
                          : 'This bundle is auto-scored, so results can be released immediately after submission.'}
                      </InlineAlert>
                    </div>
                  ) : (
                    <p className="mt-4 text-sm text-muted">Select a published bundle to see its timing, review, and release policy.</p>
                  )}
                </div>
              </div>
            </section>

            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm sm:p-8">
              <LearnerSurfaceSectionHeader
                eyebrow={showProfession ? '6. Review Credits' : '5. Review Credits'}
                title="Reserve tutor review at mock start"
                description="Credits are reserved when you start, used when you submit Writing or Speaking, and refunded if you cancel."
                className="mb-4"
              />
              <div className="mb-4 inline-flex rounded-md bg-warning/10 px-2 py-1 text-[10px] font-black uppercase tracking-widest text-warning">
                {availableCredits} credits available
              </div>
              <div className="grid gap-3 sm:grid-cols-2">
                {reviewOptions.map((option) => {
                  const disabled = option.cost > availableCredits;
                  return (
                    <button
                      key={option.id}
                      type="button"
                      disabled={disabled}
                      onClick={() => setReviewSelection(option.id)}
                      className={`rounded-2xl border p-4 text-left transition-colors ${
                        selectedReviewSelection === option.id
                          ? 'border-warning bg-warning/10'
                          : disabled
                            ? 'cursor-not-allowed border-border bg-background-light opacity-60'
                            : 'border-border bg-surface hover:border-border-hover'
                      }`}
                    >
                      <p className="text-sm font-bold text-navy">{option.label}</p>
                      <p className="mt-2 text-xs leading-5 text-muted">{option.description}</p>
                      <p className="mt-3 text-[11px] font-black uppercase tracking-widest text-muted">
                        {option.cost} credit{option.cost === 1 ? '' : 's'}
                      </p>
                    </button>
                  );
                })}
              </div>
              {insufficientCredits ? (
                <div className="mt-4">
                  <InlineAlert variant="warning">This review selection needs more credits before the mock can start.</InlineAlert>
                </div>
              ) : null}
            </section>

            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm sm:p-8">
              <LearnerSurfaceSectionHeader
                eyebrow={showProfession ? '7. Scheduling' : '6. Scheduling'}
                title="Book final-readiness or live Speaking mocks"
                description="Scheduled mocks use the OET@Home-style pre-check flow and release results by the selected bundle policy."
                className="mb-4"
              />
              <div className="grid gap-4 lg:grid-cols-[1fr_auto]">
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
                Diagnostic access remains billing-configurable. Speaking bookings hide interlocutor cards from learners and expose them only to tutors.
              </p>
            </section>

            {startError ? <InlineAlert variant="error">{startError}</InlineAlert> : null}

            <div className="sticky bottom-4 z-10 rounded-2xl border border-border bg-surface/95 p-3 shadow-lg backdrop-blur">
              <Button
                onClick={handleStart}
                disabled={starting || !selectedBundle || insufficientCredits || diagnosticBlocked}
                size="lg"
                className="w-full gap-2 py-5 text-base font-black"
              >
                {starting ? 'Starting...' : 'Start Mock Test'}
              </Button>
              <p className="mt-3 text-center text-xs text-muted">
                {selectedBundle ? `${selectedBundle.title} / ${selectedBundle.estimatedDurationMinutes} minutes` : 'Select a published bundle to continue.'}
              </p>
            </div>
          </>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
