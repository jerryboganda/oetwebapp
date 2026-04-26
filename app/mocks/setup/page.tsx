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
  Stethoscope,
} from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { MotionCollapse, MotionPresence, MotionSection } from '@/components/ui/motion-primitives';
import { createMockSession, fetchMockOptions } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { MockBundleOption, MockConfig, MockOptions } from '@/lib/mock-data';

type ReviewSelection = MockConfig['reviewSelection'];
type MockType = 'full' | 'sub';
type MockSubType = 'reading' | 'listening' | 'writing' | 'speaking';

const SUBTEST_META: Record<MockSubType, { label: string; icon: ElementType; active: string }> = {
  listening: { label: 'Listening', icon: Headphones, active: 'border-primary bg-primary/10 text-primary' },
  reading: { label: 'Reading', icon: FileText, active: 'border-info bg-info/10 text-info' },
  writing: { label: 'Writing', icon: PenTool, active: 'border-danger bg-danger/10 text-danger' },
  speaking: { label: 'Speaking', icon: Mic, active: 'border-primary bg-primary/10 text-primary' },
};

function isSubtest(value: string | null): value is MockSubType {
  return value === 'reading' || value === 'listening' || value === 'writing' || value === 'speaking';
}

function reviewCost(selection: ReviewSelection) {
  if (selection === 'writing_and_speaking') return 2;
  if (selection === 'writing' || selection === 'speaking' || selection === 'current_subtest') return 1;
  return 0;
}

function buildReviewOptions(mockType: MockType, subType: MockSubType) {
  if (mockType === 'full') {
    return [
      { id: 'none' as const, label: 'No Review', description: 'Run the mock without expert review.', cost: 0 },
      { id: 'writing' as const, label: 'Writing Only', description: 'Reserve one credit for Writing expert review.', cost: 1 },
      { id: 'speaking' as const, label: 'Speaking Only', description: 'Reserve one credit for Speaking expert review.', cost: 1 },
      { id: 'writing_and_speaking' as const, label: 'Writing + Speaking', description: 'Reserve two credits for both productive sections.', cost: 2 },
    ];
  }

  if (subType === 'writing' || subType === 'speaking') {
    return [
      { id: 'none' as const, label: 'No Review', description: 'Keep this sub-test mock platform-evaluated only.', cost: 0 },
      { id: 'current_subtest' as const, label: 'Review Current Sub-test', description: 'Reserve one credit for expert review of this Writing or Speaking mock.', cost: 1 },
    ];
  }

  return [
    { id: 'none' as const, label: 'No Review', description: 'Expert review is not offered for Reading or Listening mocks.', cost: 0 },
  ];
}

function normalizeReviewSelection(mockType: MockType, subType: MockSubType, selection: ReviewSelection): ReviewSelection {
  return buildReviewOptions(mockType, subType).some((option) => option.id === selection) ? selection : 'none';
}

function bundleMatches(bundle: MockBundleOption, mockType: MockType, subType: MockSubType, profession: string) {
  if (bundle.mockType !== mockType) return false;
  if (mockType === 'sub' && bundle.subtest !== subType) return false;
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
  const [profession, setProfession] = useState('medicine');
  const [strictTimer, setStrictTimer] = useState(true);
  const [reviewSelection, setReviewSelection] = useState<ReviewSelection>('none');
  const [selectedBundleId, setSelectedBundleId] = useState<string | null>(null);
  const [starting, setStarting] = useState(false);
  const [startError, setStartError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    fetchMockOptions()
      .then((result) => {
        if (cancelled) return;
        setOptions(result);
        const queryBundleId = searchParams?.get('bundleId');
        const querySubtest = searchParams?.get('subtest');
        const queryType = searchParams?.get('type');
        const bundle = queryBundleId ? result.availableBundles.find((item) => item.bundleId === queryBundleId || item.id === queryBundleId) : null;
        if (bundle) {
          setSelectedBundleId(bundle.bundleId);
          setMockType(bundle.mockType);
          const bundleSubtest = bundle.subtest ?? null;
          if (isSubtest(bundleSubtest)) setSubType(bundleSubtest);
        } else if (isSubtest(querySubtest)) {
          setMockType('sub');
          setSubType(querySubtest);
        } else if (queryType === 'sub' || queryType === 'full') {
          setMockType(queryType);
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

  const handleModeChange = (newMode: 'practice' | 'exam') => {
    setMode(newMode);
    if (newMode === 'exam') setStrictTimer(true);
  };

  const handleMockTypeChange = (nextType: MockType) => {
    setMockType(nextType);
    setSelectedBundleId(null);
    setReviewSelection((current) => normalizeReviewSelection(nextType, subType, current));
  };

  const handleSubTypeChange = (nextSubType: MockSubType) => {
    setSubType(nextSubType);
    setSelectedBundleId(null);
    setReviewSelection((current) => normalizeReviewSelection(mockType, nextSubType, current));
  };

  const handleStart = async () => {
    if (!selectedBundle || insufficientCredits) return;
    setStarting(true);
    setStartError(null);
    try {
      const result = await createMockSession({
        type: mockType,
        subType: mockType === 'sub' ? subType : undefined,
        mode,
        profession,
        strictTimer,
        reviewSelection: selectedReviewSelection,
        bundleId: selectedBundle.bundleId,
      });
      analytics.track('mock_started', { mockType, mode, reviewSelection: selectedReviewSelection, bundleId: selectedBundle.bundleId });
      router.push(`/mocks/player/${result.sessionId}`);
    } catch (err) {
      setStartError(err instanceof Error ? err.message : 'Failed to start mock session. Please try again.');
      setStarting(false);
    }
  };

  const showProfession = mockType === 'full' || subType === 'writing' || subType === 'speaking';

  return (
    <LearnerDashboardShell pageTitle="Configure Mock" subtitle="Set up your practice environment" backHref="/mocks">
      <div className="space-y-8 pb-24">
        <LearnerPageHero
          eyebrow="Mock Setup"
          icon={Layers}
          accent="navy"
          title="Start from a published mock bundle"
          description="Choose your mock paper, exam mode, timing, and whether to reserve expert review before you start."
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
                title="Choose full simulation or focused sub-test"
                description="Coming from the Mocks page? Your selection is already preset."
                className="mb-4"
              />
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                {[
                  { id: 'full' as const, icon: Layers, label: 'Full Mock', desc: 'Listening, Reading, Writing, Speaking in OET order.' },
                  { id: 'sub' as const, icon: FileText, label: 'Single Sub-test', desc: 'One published section for targeted evidence.' },
                ].map(({ id, icon: Icon, label, desc }) => (
                  <button
                    key={id}
                    type="button"
                    onClick={() => handleMockTypeChange(id)}
                    className={`rounded-2xl border-2 p-5 text-left transition-all ${
                      mockType === id ? 'border-primary bg-primary/5 ring-4 ring-primary/10' : 'border-border hover:border-border-hover hover:bg-background-light'
                    }`}
                  >
                    <div className="mb-3 flex items-center justify-between">
                      <span className={`flex h-10 w-10 items-center justify-center rounded-full ${mockType === id ? 'bg-primary text-white' : 'bg-background-light text-muted'}`}>
                        <Icon className="h-5 w-5" />
                      </span>
                      {mockType === id ? <Check className="h-5 w-5 text-primary" /> : null}
                    </div>
                    <h3 className={`text-lg font-bold ${mockType === id ? 'text-primary' : 'text-navy'}`}>{label}</h3>
                    <p className="mt-1 text-sm text-muted">{desc}</p>
                  </button>
                ))}
              </div>
              <MotionCollapse open={mockType === 'sub'}>
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
                  className={`rounded-2xl border-2 p-4 text-left transition-colors ${mode === 'practice' ? 'border-info bg-info/10' : 'border-border hover:bg-background-light'}`}
                >
                  <Award className={`mb-3 h-5 w-5 ${mode === 'practice' ? 'text-info' : 'text-muted'}`} />
                  <p className="text-sm font-bold text-navy">Practice Mode</p>
                  <p className="mt-1 text-xs text-muted">Flexible timing for targeted practice.</p>
                </button>
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
            </section>

            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm sm:p-8">
              <LearnerSurfaceSectionHeader
                eyebrow={showProfession ? '5. Review Credits' : '4. Review Credits'}
                title="Reserve expert review at mock start"
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

            {startError ? <InlineAlert variant="error">{startError}</InlineAlert> : null}

            <div className="sticky bottom-4 z-10 rounded-2xl border border-border bg-surface/95 p-3 shadow-lg backdrop-blur">
              <Button
                onClick={handleStart}
                disabled={starting || !selectedBundle || insufficientCredits}
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
