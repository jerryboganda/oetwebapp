'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Award,
  ArrowRight,
  BarChart3,
  CheckCircle2,
  Clock,
  FileText,
  Headphones,
  Layers,
  Mic,
  PenTool,
  PlayCircle,
  RefreshCw,
  Star,
  Hourglass,
  Sparkles,
} from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';
import type { MockReport } from '@/lib/mock-data';
import { fetchMocksHome } from '@/lib/api';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain';
import type { LearnerSurfaceCardModel } from '@/lib/learner-surface';

type SubtestCode = 'listening' | 'reading' | 'writing' | 'speaking';

type MockHomePayload = {
  reports?: MockReport[];
  resumableAttempts?: ResumableAttempt[];
  recommendedNextMock?: {
    id?: string;
    title?: string;
    rationale?: string;
    route?: string;
    latestOverallScore?: string | null;
    latestOverallGrade?: string | null;
    trend?: 'up' | 'down' | 'flat' | string | null;
    readiness?: {
      tier?: 'strong' | 'passing' | 'developing' | 'foundation' | string;
      message?: string;
      passThreshold?: number;
      overallScore?: number;
    } | null;
  } | null;
  purchasedMockReviews?: MockReviewSummary;
  collections?: { fullMocks?: FullMockCard[]; subTestMocks?: SubTestMockCard[] };
  emptyState?: { title?: string; description?: string; route?: string } | null;
  learnerProfession?: string | null;
  availableProfessions?: { id: string; label: string }[];
  scoreGuarantee?: {
    status?: string;
    isActive?: boolean;
    baselineScore?: number;
    guaranteedScore?: number;
    guaranteedImprovement?: number;
    latestOverallScore?: number | null;
    onTrack?: boolean;
    daysRemaining?: number;
    expiresAt?: string;
    message?: string;
    route?: string;
  } | null;
  cohortPercentile?: {
    percentile?: number;
    cohortSize?: number;
    windowDays?: number;
    learnerScore?: number;
    label?: string;
  } | null;
};

interface ResumableAttempt {
  mockAttemptId: string;
  bundleId: string;
  state: string;
  mockType: 'full' | 'sub';
  subtest?: SubtestCode | null;
  startedAt?: string;
  resumeRoute: string;
  reportRoute?: string | null;
}

interface MockReviewSummary {
  availableCredits?: number;
  reservedCredits?: number;
  consumedCredits?: number;
  pendingReviews?: number;
  completedReviews?: number;
  route?: string;
  reviewTurnaroundHours?: number;
  reviewSlaLabel?: string;
}

interface FullMockCard {
  id: string;
  title: string;
  mockType?: 'full' | 'sub';
  status?: 'completed' | 'locked' | 'in-progress' | 'available' | string;
  isRecommended?: boolean;
  date?: string;
  score?: string | number;
  reason?: string;
  duration?: string;
  route?: string;
  sectionCount?: number;
  professionId?: string | null;
  appliesToAllProfessions?: boolean;
  /** Per-subtest states surfaced by backend for the latest attempt. */
  sectionProgress?: Partial<Record<SubtestCode, 'completed' | 'in-progress' | 'not-started' | 'locked'>>;
  /** Subtests included in this bundle (defaults to the full OET order for full mocks). */
  includedSubtests?: SubtestCode[];
}

interface SubTestMockCard {
  id: string;
  title: string;
  subtest?: SubtestCode;
  sectionCount?: number;
  route?: string;
  professionId?: string | null;
  appliesToAllProfessions?: boolean;
}

const SUBTEST_ICON: Record<SubtestCode, LucideIcon> = {
  listening: Headphones,
  reading: FileText,
  writing: PenTool,
  speaking: Mic,
};

const SUBTEST_COLOR: Record<SubtestCode, { fg: string; bg: string; label: string }> = {
  listening: { fg: 'text-indigo-600', bg: 'bg-indigo-100', label: 'Listening' },
  reading: { fg: 'text-blue-600', bg: 'bg-blue-100', label: 'Reading' },
  writing: { fg: 'text-rose-600', bg: 'bg-rose-100', label: 'Writing' },
  speaking: { fg: 'text-purple-600', bg: 'bg-purple-100', label: 'Speaking' },
};

const FULL_MOCK_ORDER: readonly SubtestCode[] = ['listening', 'reading', 'writing', 'speaking'] as const;

function humanMockTypeLabel(mockType: 'full' | 'sub', subtest?: SubtestCode | null): string {
  if (mockType === 'full') return 'Full mock (all four sub-tests)';
  if (subtest) return `${SUBTEST_COLOR[subtest].label} sub-test mock`;
  return 'Sub-test mock';
}

function humanState(state: string): string {
  switch (state.toLowerCase()) {
    case 'in_progress':
    case 'in-progress':
    case 'inprogress':
      return 'In progress';
    case 'paused':
      return 'Paused';
    case 'evaluating':
      return 'Awaiting results';
    default:
      return state.replace(/_/g, ' ');
  }
}

/**
 * Compact 4-dot strip showing Listening / Reading / Writing / Speaking progress on a Full Mock row.
 * Colour tokens follow DESIGN.md (emerald/amber/slate/gray). Purely visual; no interaction.
 */
function SectionProgressDots({ mock }: { mock: FullMockCard }) {
  if (mock.mockType !== 'full') return null;
  const included = mock.includedSubtests ?? FULL_MOCK_ORDER;
  const progress = mock.sectionProgress ?? {};
  return (
    <div className="flex items-center gap-1.5" aria-label="Per-sub-test progress">
      {FULL_MOCK_ORDER.map((subtest) => {
        const active = included.includes(subtest);
        const state = progress[subtest];
        const palette = !active
          ? 'bg-background-light text-muted/40'
          : state === 'completed'
            ? 'bg-emerald-100 text-emerald-600'
            : state === 'in-progress'
              ? 'bg-amber-100 text-amber-600'
              : state === 'locked'
                ? 'bg-slate-100 text-slate-400'
                : 'bg-background-light text-muted/60';
        const Icon = SUBTEST_ICON[subtest];
        const label = `${SUBTEST_COLOR[subtest].label}: ${active ? (state ?? 'not started') : 'not included'}`;
        return (
          <span
            key={subtest}
            title={label}
            aria-label={label}
            className={`flex h-6 w-6 items-center justify-center rounded-full ${palette}`}
          >
            <Icon className="h-3 w-3" />
          </span>
        );
      })}
    </div>
  );
}

export default function MockCenter() {
  const [home, setHome] = useState<MockHomePayload | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  const load = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setError(null);
    try {
      const data = (await fetchMocksHome()) as MockHomePayload;
      if (signal?.aborted) return;
      setHome(data);
    } catch (err) {
      if (signal?.aborted) return;
      const message = err instanceof Error && err.message ? err.message : 'Failed to load mock center.';
      setError(message);
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }, []);

  useEffect(() => {
    analytics.track('module_entry', { module: 'mocks' });
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load, reloadKey]);

  const reports: MockReport[] = home?.reports ?? [];
  const resumableAttempts: ResumableAttempt[] = home?.resumableAttempts ?? [];
  const recommended = home?.recommendedNextMock ?? null;
  const reviewSummary: MockReviewSummary = home?.purchasedMockReviews ?? {};
  const availableCredits = reviewSummary.availableCredits ?? 0;
  const reservedCredits = reviewSummary.reservedCredits ?? 0;
  const consumedCredits = reviewSummary.consumedCredits ?? 0;
  const pendingReviews = reviewSummary.pendingReviews ?? 0;
  const completedReviews = reviewSummary.completedReviews ?? 0;
  const reviewSlaLabel = reviewSummary.reviewSlaLabel ?? null;
  const fullMocks: FullMockCard[] = useMemo(() => home?.collections?.fullMocks ?? [], [home]);
  const subTestMocks: SubTestMockCard[] = useMemo(() => home?.collections?.subTestMocks ?? [], [home]);
  const emptyState = home?.emptyState ?? null;
  const learnerProfession = home?.learnerProfession ?? null;
  const availableProfessions = useMemo(() => home?.availableProfessions ?? [], [home]);
  const scoreGuarantee = home?.scoreGuarantee ?? null;
  const cohortPercentile = home?.cohortPercentile ?? null;

  // Profession filter. `null` means "all professions". Default to the learner's active profession
  // whenever a payload arrives, but only if that profession actually has bundles available (otherwise
  // showing their profession would render zero results and look broken).
  const [professionFilter, setProfessionFilter] = useState<string | null>(null);
  useEffect(() => {
    if (!learnerProfession) return;
    const matches = availableProfessions.some((p) => p.id === learnerProfession);
    if (matches) setProfessionFilter(learnerProfession);
  }, [learnerProfession, availableProfessions]);

  const filterBundleByProfession = useCallback(
    (bundle: { professionId?: string | null; appliesToAllProfessions?: boolean }) => {
      if (professionFilter === null) return true;
      if (bundle.appliesToAllProfessions) return true;
      if (!bundle.professionId) return true;
      return bundle.professionId === professionFilter;
    },
    [professionFilter],
  );

  const filteredFullMocks = useMemo(() => fullMocks.filter(filterBundleByProfession), [fullMocks, filterBundleByProfession]);
  const filteredSubTestMocks = useMemo(() => subTestMocks.filter(filterBundleByProfession), [subTestMocks, filterBundleByProfession]);

  const recommendedReadiness = recommended?.readiness ?? null;
  const recommendedTrend = recommended?.trend ?? null;
  const recommendedLatestScore = recommended?.latestOverallScore ?? null;
  const recommendedLatestGrade = recommended?.latestOverallGrade ?? null;
  const trendLabel: string | null = (() => {
    if (!recommendedLatestScore) return null;
    const gradeSuffix = recommendedLatestGrade ? ` - Grade ${recommendedLatestGrade}` : '';
    const base = `Last overall ${recommendedLatestScore}/500${gradeSuffix}`;
    if (recommendedTrend === 'up') return `${base} - trending up`;
    if (recommendedTrend === 'down') return `${base} - trending down`;
    return base;
  })();

  const recommendedCard: LearnerSurfaceCardModel = {
    kind: 'navigation',
    sourceType: recommended ? 'backend_summary' : 'frontend_navigation',
    accent: 'navy',
    eyebrow: 'Recommended Next Step',
    eyebrowIcon: Star,
    title: recommended?.title ?? 'Full Practice Mock',
    description:
      recommendedReadiness?.message ??
      recommended?.rationale ??
      'Start a full mock when you need evidence that your recent practice work is holding up under full-exam pressure.',
    metaItems: [
      { icon: Clock, label: '~3 hours' },
      { icon: Award, label: 'Full mock flow' },
      { icon: FileText, label: 'Report included' },
      ...(trendLabel ? [{ icon: BarChart3, label: trendLabel }] : []),
    ],
    primaryAction: {
      label: 'Start Mock',
      href: recommended?.route ?? emptyState?.route ?? '/mocks/setup',
    },
  };

  const reviewCard: LearnerSurfaceCardModel = {
    kind: 'status',
    sourceType: 'backend_summary',
    accent: 'amber',
    eyebrow: 'Review Capacity',
    eyebrowIcon: Star,
    title:
      availableCredits > 0
        ? 'Writing and speaking reviews are available'
        : 'Top up review credits to unlock tutor feedback',
    description:
      reviewSlaLabel
        ? `Use review credits to add tutor feedback after high-value mock attempts. Credits are reserved when you start and consumed only after the review is delivered. ${reviewSlaLabel}.`
        : 'Use review credits to add tutor feedback after high-value mock attempts. Credits are reserved when you start and consumed only after the review is delivered.',
    metaItems: [
      { icon: Star, label: `${availableCredits} available` },
      { icon: Hourglass, label: `${reservedCredits} reserved` },
      { icon: CheckCircle2, label: `${consumedCredits} consumed` },
      { icon: RefreshCw, label: `${pendingReviews} pending` },
      { icon: Award, label: `${completedReviews} completed` },
      ...(reviewSlaLabel ? [{ icon: Clock, label: reviewSlaLabel }] : []),
    ],
    primaryAction: {
      label: availableCredits > 0 ? 'Reserve a review' : 'Purchase New Review',
      href: reviewSummary.route ?? '/billing',
      variant: 'outline',
    },
  };

  // Phase C1 — Score Guarantee signal (read-only). We render the card only when the backend
  // returns a pledge; billing remains the source of truth for activation / claim flows.
  const scoreGuaranteeCard: LearnerSurfaceCardModel | null = scoreGuarantee
    ? {
        kind: 'status',
        sourceType: 'backend_summary',
        accent: scoreGuarantee.onTrack ? 'primary' : 'amber',
        eyebrow: 'Score Guarantee',
        eyebrowIcon: Award,
        title: scoreGuarantee.isActive
          ? scoreGuarantee.onTrack
            ? 'On track to meet your guarantee'
            : `Target ${scoreGuarantee.guaranteedScore ?? ''}/500 to stay on track`
          : 'Score Guarantee status',
        description: scoreGuarantee.message ?? 'Open billing to review your Score Guarantee pledge.',
        metaItems: [
          ...(typeof scoreGuarantee.baselineScore === 'number'
            ? [{ icon: BarChart3, label: `Baseline ${scoreGuarantee.baselineScore}/500` }]
            : []),
          ...(typeof scoreGuarantee.guaranteedScore === 'number'
            ? [{ icon: Award, label: `Guaranteed ${scoreGuarantee.guaranteedScore}/500` }]
            : []),
          ...(typeof scoreGuarantee.latestOverallScore === 'number'
            ? [{ icon: Star, label: `Latest ${scoreGuarantee.latestOverallScore}/500` }]
            : []),
          ...(scoreGuarantee.isActive && typeof scoreGuarantee.daysRemaining === 'number'
            ? [
                {
                  icon: Clock,
                  label:
                    scoreGuarantee.daysRemaining === 1
                      ? '1 day remaining'
                      : `${scoreGuarantee.daysRemaining} days remaining`,
                },
              ]
            : []),
        ],
        primaryAction: {
          label: 'Open billing',
          href: scoreGuarantee.route ?? '/billing/score-guarantee',
          variant: 'outline',
        },
      }
    : null;

  // Phase C2 — anonymised cohort percentile. Backend returns null (hidden card) when the
  // cohort is too small (< 10 peers) to prevent re-identification.
  const cohortCard: LearnerSurfaceCardModel | null = cohortPercentile
    ? {
        kind: 'status',
        sourceType: 'backend_summary',
        accent: 'slate',
        eyebrow: 'Cohort position',
        eyebrowIcon: BarChart3,
        title: cohortPercentile.label ?? 'Where you sit in the cohort',
        description:
          typeof cohortPercentile.percentile === 'number' && typeof cohortPercentile.cohortSize === 'number'
            ? `Your latest mock is in the ${cohortPercentile.percentile}th percentile of ${cohortPercentile.cohortSize} learners who completed a mock in the last ${cohortPercentile.windowDays ?? 90} days.`
            : 'Complete a full mock to compare your overall score against the recent cohort.',
        metaItems: [
          ...(typeof cohortPercentile.percentile === 'number'
            ? [{ icon: BarChart3, label: `${cohortPercentile.percentile}th percentile` }]
            : []),
          ...(typeof cohortPercentile.cohortSize === 'number'
            ? [{ icon: Layers, label: `Cohort of ${cohortPercentile.cohortSize}` }]
            : []),
          ...(typeof cohortPercentile.learnerScore === 'number'
            ? [{ icon: Star, label: `Your score ${cohortPercentile.learnerScore}/500` }]
            : []),
        ],
      }
    : null;

  const heroHighlights = error
    ? undefined
    : [
        { icon: Award, label: 'Review credits', value: `${availableCredits} available` },
        { icon: Layers, label: 'Mock routes', value: `${filteredFullMocks.length} full mocks` },
        { icon: BarChart3, label: 'Recent reports', value: `${reports.length} available` },
      ];

  return (
    <LearnerDashboardShell pageTitle="Mock Center" subtitle="Your hub for full exams, sub-test practice, and expert reviews.">
      <div className="space-y-10">
        <LearnerPageHero
          eyebrow="Module Focus"
          icon={Layers}
          accent="navy"
          title="Choose the mock that proves whether practice is transferring"
          description="Pick the right mock depth, track your progress, and let your results guide your next move."
          highlights={heroHighlights}
        />

        {loading ? (
          <div className="space-y-4" aria-busy="true" aria-live="polite">
            <Skeleton className="h-72 rounded-2xl" />
            <Skeleton className="h-52 rounded-2xl" />
          </div>
        ) : error ? (
          <MotionSection>
            <div
              role="alert"
              className="rounded-[24px] border border-danger/20 bg-danger/5 p-6 shadow-sm"
            >
              <InlineAlert variant="error" title="We couldn't load the Mock Center right now">
                {error} If this keeps happening, the mock service may be warming up &mdash; try again
                in a moment.
              </InlineAlert>
              <div className="mt-5 flex flex-col gap-2 sm:flex-row sm:items-center">
                <Button
                  variant="primary"
                  onClick={() => setReloadKey((k) => k + 1)}
                  className="gap-2"
                >
                  <RefreshCw className="h-4 w-4" />
                  Retry
                </Button>
                <Link
                  href="/help"
                  className="pressable inline-flex items-center justify-center gap-2 rounded-[16px] px-4 py-2 text-sm font-semibold text-navy transition-colors hover:bg-background-light"
                >
                  Contact support
                </Link>
              </div>
            </div>
          </MotionSection>
        ) : (
          <>
            {resumableAttempts.length > 0 && (
              <MotionSection>
                <LearnerSurfaceSectionHeader
                  eyebrow="Continue where you left off"
                  title="You have mocks in progress"
                  description="Jump back in so your progress is preserved exactly where you paused."
                  className="mb-4"
                />
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                  {resumableAttempts.map((attempt, idx) => (
                    <MotionItem key={attempt.mockAttemptId} delayIndex={idx}>
                      <LearnerSurfaceCard
                        card={{
                          kind: 'status',
                          sourceType: 'backend_summary',
                          accent: 'primary',
                          eyebrow: humanMockTypeLabel(attempt.mockType, attempt.subtest),
                          eyebrowIcon: Hourglass,
                          title: humanState(attempt.state),
                          description:
                            attempt.startedAt
                              ? `Started ${new Date(attempt.startedAt).toLocaleString()}`
                              : 'Resume to continue from your last saved section.',
                          metaItems: [{ icon: Layers, label: attempt.mockType === 'full' ? 'Full mock' : 'Sub-test mock' }],
                          primaryAction: {
                            label: 'Resume mock',
                            href: attempt.resumeRoute,
                          },
                          secondaryAction: attempt.reportRoute
                            ? { label: 'View report so far', href: attempt.reportRoute, variant: 'outline' }
                            : undefined,
                        }}
                      />
                    </MotionItem>
                  ))}
                </div>
              </MotionSection>
            )}

            <MotionSection>
              <LearnerSurfaceCard card={recommendedCard} />
            </MotionSection>

            {emptyState && fullMocks.length === 0 && subTestMocks.length === 0 ? (
              <MotionSection>
                <LearnerSurfaceCard
                  card={{
                    kind: 'status',
                    sourceType: 'backend_summary',
                    accent: 'slate',
                    eyebrow: 'Mock Library',
                    eyebrowIcon: Sparkles,
                    title: emptyState.title ?? 'No mock bundles are published yet',
                    description:
                      emptyState.description ??
                      'Once an admin publishes a bundle it will appear here with its real section order.',
                    primaryAction: emptyState.route
                      ? { label: 'Go to dashboard', href: emptyState.route }
                      : undefined,
                  }}
                />
              </MotionSection>
            ) : (
              <div className="grid grid-cols-1 gap-8 lg:grid-cols-3">
                <div className="space-y-10 lg:col-span-2">
                  {availableProfessions.length > 0 ? (
                    <section aria-label="Profession filter">
                      <div className="flex flex-wrap items-center gap-2" role="tablist" aria-label="Filter mocks by profession">
                        <span className="text-xs font-black uppercase tracking-widest text-muted">Profession</span>
                        <button
                          type="button"
                          role="tab"
                          aria-selected={professionFilter === null}
                          onClick={() => setProfessionFilter(null)}
                          className={`pressable touch-target rounded-full border px-4 py-1.5 text-xs font-semibold transition-colors ${
                            professionFilter === null
                              ? 'border-primary bg-primary text-white'
                              : 'border-border bg-surface text-navy hover:border-border'
                          }`}
                        >
                          All professions
                        </button>
                        {availableProfessions.map((p) => (
                          <button
                            key={p.id}
                            type="button"
                            role="tab"
                            aria-selected={professionFilter === p.id}
                            onClick={() => setProfessionFilter(p.id)}
                            className={`pressable touch-target rounded-full border px-4 py-1.5 text-xs font-semibold transition-colors ${
                              professionFilter === p.id
                                ? 'border-primary bg-primary text-white'
                                : 'border-border bg-surface text-navy hover:border-border'
                            }`}
                          >
                            {p.label}
                          </button>
                        ))}
                      </div>
                    </section>
                  ) : null}

                  <section>
                    <LearnerSurfaceSectionHeader
                      eyebrow="Sub-test Mocks"
                      title="Choose the simulation scope you need"
                      description="Each entry tells you whether it's a single sub-test or a full mock."
                      className="mb-4"
                    />
                    {filteredSubTestMocks.length === 0 ? (
                      <div className="rounded-[24px] border border-border bg-surface p-6 text-sm text-muted">
                        {subTestMocks.length === 0
                          ? 'No published sub-test mock bundles are available yet.'
                          : 'No sub-test mocks match the selected profession. Try \u201cAll professions\u201d to widen your view.'}
                      </div>
                    ) : (
                      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                        {filteredSubTestMocks.map((mock, idx) => {
                          const subtest = (mock.subtest ?? 'reading') as SubtestCode;
                          const palette = SUBTEST_COLOR[subtest];
                          const Icon = SUBTEST_ICON[subtest];
                          const href = mock.route ?? '/mocks/setup';
                          const count = mock.sectionCount ?? 1;
                          return (
                            <MotionItem key={mock.id} delayIndex={idx}>
                              <Link
                                href={href}
                                className="group flex items-center gap-4 rounded-[24px] border border-border bg-surface p-5 transition-all hover:border-border hover:shadow-md"
                              >
                                <div
                                  className={`flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl ${palette.bg} transition-transform group-hover:scale-105`}
                                >
                                  <Icon className={`h-6 w-6 ${palette.fg}`} />
                                </div>
                                <div className="flex-1">
                                  <h3 className="text-base font-bold text-navy">{mock.title}</h3>
                                  <p className="text-sm text-muted">
                                    {count} section{count === 1 ? '' : 's'} available
                                  </p>
                                </div>
                                <ArrowRight className="h-5 w-5 text-muted/40 transition-colors group-hover:text-navy" />
                              </Link>
                            </MotionItem>
                          );
                        })}
                      </div>
                    )}
                  </section>

                  <section>
                    <LearnerSurfaceSectionHeader
                      eyebrow="Full Mocks"
                      title="Keep full-exam progression visible"
                      description="Progress dots show which sub-tests you've completed on the latest attempt for each bundle."
                      className="mb-4"
                    />
                    {filteredFullMocks.length === 0 ? (
                      <div className="rounded-[24px] border border-border bg-surface p-6 text-sm text-muted">
                        {fullMocks.length === 0
                          ? 'No full mock bundles are published yet. Once an admin publishes a bundle, it will appear here with its real section order.'
                          : 'No full mocks match the selected profession. Try \u201cAll professions\u201d to widen your view.'}
                      </div>
                    ) : (
                      <div className="overflow-hidden rounded-[24px] border border-border bg-surface shadow-sm">
                        <div className="divide-y divide-border">
                          {filteredFullMocks.map((mock, idx) => {
                            const locked = mock.status === 'locked';
                            return (
                              <MotionItem key={mock.id} delayIndex={idx}>
                                <Link
                                  href={locked ? '/mocks' : mock.route ?? '/mocks/setup'}
                                  aria-disabled={locked}
                                  className={`flex items-center justify-between gap-4 p-5 transition-colors ${
                                    locked ? 'pointer-events-none bg-background-light opacity-75' : 'hover:bg-background-light'
                                  }`}
                                >
                                  <div className="flex min-w-0 items-center gap-4">
                                    <div className="shrink-0">
                                      {mock.status === 'completed' ? (
                                        <CheckCircle2 className="h-6 w-6 text-emerald-500" />
                                      ) : locked ? (
                                        <div className="flex h-6 w-6 items-center justify-center rounded-full border-2 border-border">
                                          <div className="h-2 w-2 rounded-full bg-gray-300" />
                                        </div>
                                      ) : (
                                        <div className="flex h-6 w-6 items-center justify-center rounded-full border-2 border-primary">
                                          <PlayCircle className="ml-0.5 h-4 w-4 text-primary" />
                                        </div>
                                      )}
                                    </div>
                                    <div className="min-w-0">
                                      <h3 className="flex items-center gap-2 text-base font-bold text-navy">
                                        <span className="truncate">{mock.title}</span>
                                        {mock.isRecommended ? (
                                          <span className="rounded-md bg-amber-100 px-2 py-0.5 text-[10px] font-black uppercase tracking-widest text-amber-700">
                                            Recommended
                                          </span>
                                        ) : null}
                                      </h3>
                                      <div className="mt-0.5 text-sm text-muted">
                                        {mock.status === 'completed' ? (
                                          <span>
                                            Completed {mock.date} · Score:{' '}
                                            <span className="font-bold text-navy">{mock.score}</span>
                                          </span>
                                        ) : locked ? (
                                          <span className="flex items-center gap-1">
                                            <Clock className="h-3.5 w-3.5" /> {mock.reason}
                                          </span>
                                        ) : (
                                          <span className="flex items-center gap-1">
                                            <Clock className="h-3.5 w-3.5" /> {mock.duration}
                                          </span>
                                        )}
                                      </div>
                                      <div className="mt-2">
                                        <SectionProgressDots mock={mock} />
                                      </div>
                                    </div>
                                  </div>
                                  {!locked ? (
                                    <ArrowRight className="hidden h-5 w-5 text-muted/40 sm:block" />
                                  ) : null}
                                </Link>
                              </MotionItem>
                            );
                          })}
                        </div>
                      </div>
                    )}
                  </section>
                </div>

                <div className="space-y-10">
                  <section>
                    <LearnerSurfaceSectionHeader
                      eyebrow="Expert Reviews"
                      title="Keep review readiness explicit"
                      description="Track the status of any expert reviews attached to your mocks."
                      className="mb-4"
                    />
                    <LearnerSurfaceCard card={reviewCard} />
                  </section>

                  {scoreGuaranteeCard ? (
                    <section>
                      <LearnerSurfaceSectionHeader
                        eyebrow="Your Guarantee"
                        title="Score Guarantee progress"
                        description="Read-only summary of your active pledge — manage activation and claims from billing."
                        className="mb-4"
                      />
                      <LearnerSurfaceCard card={scoreGuaranteeCard} />
                    </section>
                  ) : null}

                  {cohortCard ? (
                    <section>
                      <LearnerSurfaceSectionHeader
                        eyebrow="Benchmark"
                        title="Recent learner cohort"
                        description="See where you stand against learners who took a mock in the last 90 days. All comparisons are private."
                        className="mb-4"
                      />
                      <LearnerSurfaceCard card={cohortCard} />
                    </section>
                  ) : null}

                  <section>
                    <LearnerSurfaceSectionHeader
                      eyebrow="Previous Reports"
                      title="Keep the latest evidence visible"
                      description="Detailed reports for every completed mock attempt."
                      className="mb-4"
                    />
                    {reports.length === 0 ? (
                      <div className="py-8 text-center text-sm text-muted">
                        No reports yet. Complete a mock to see your results here.
                      </div>
                    ) : (
                      <div className="overflow-hidden rounded-[24px] border border-border bg-surface shadow-sm">
                        <div className="divide-y divide-border">
                          {reports.slice(0, 4).map((report, idx) => (
                            <MotionItem key={report.id} delayIndex={idx}>
                              <Link
                                href={`/mocks/report/${report.id}`}
                                className="group flex items-center justify-between p-4 transition-colors hover:bg-background-light"
                              >
                                <div className="flex items-center gap-3">
                                  <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-background-light">
                                    <BarChart3 className="h-4 w-4 text-muted" />
                                  </div>
                                  <div>
                                    <h3 className="text-sm font-bold text-navy transition-colors group-hover:text-primary">
                                      {report.title}
                                    </h3>
                                    <p className="text-xs text-muted">{report.date}</p>
                                  </div>
                                </div>
                                <span className="text-sm font-black text-navy">{report.overallScore}</span>
                              </Link>
                            </MotionItem>
                          ))}
                        </div>
                      </div>
                    )}
                  </section>
                </div>
              </div>
            )}
          </>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
