'use client';

import { Suspense, useCallback, useEffect, useMemo, useState } from 'react';
import {
  Award,
  ArrowRight,
  BarChart3,
  Check,
  CheckCircle2,
  Clock,
  FileText,
  Headphones,
  Layers,
  Lock,
  Mic,
  PenTool,
  PlayCircle,
  RefreshCw,
  Star,
  Hourglass,
} from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { CardLink } from '@/components/ui/card-link';
import { EmptyState } from '@/components/ui/empty-error';
import { InlineAlert } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';
import { cn } from '@/lib/utils';
import type { MockReport } from '@/lib/mock-data';
import { fetchMocksHome } from '@/lib/api';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { LearnerSkillSwitcher } from '@/components/domain/learner-skill-switcher';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
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

/** Sub-test identity colours (DESIGN.md §2 skill tokens), never status. */
const SUBTEST_COLOR: Record<SubtestCode, { fg: string; bg: string; label: string }> = {
  listening: { fg: 'text-skill-listening', bg: 'bg-skill-listening/10', label: 'Listening' },
  reading: { fg: 'text-skill-reading', bg: 'bg-skill-reading/10', label: 'Reading' },
  writing: { fg: 'text-skill-writing', bg: 'bg-skill-writing/10', label: 'Writing' },
  speaking: { fg: 'text-skill-speaking', bg: 'bg-skill-speaking/10', label: 'Speaking' },
};

const FULL_MOCK_ORDER: readonly SubtestCode[] = ['listening', 'reading', 'writing', 'speaking'] as const;

const VALID_SUBTESTS: readonly SubtestCode[] = ['listening', 'reading', 'writing', 'speaking'] as const;

/** Per-subtest learner hub for the "practise part-by-part" escape hatch on scoped empty states. */
const SUBTEST_HUB: Record<SubtestCode, string> = {
  listening: '/listening',
  reading: '/reading',
  writing: '/writing',
  speaking: '/speaking',
};

/**
 * Scope derived from the `?subtest=` / `?type=full` deep-links. Every "Full <Skill> Exam" surface on the
 * Listening / Reading / Writing hubs funnels here pre-filtered; this is what makes that promise real.
 */
type MockScope =
  | { kind: 'subtest'; subtest: SubtestCode; label: string }
  | { kind: 'full'; label: string }
  | null;

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
 * State is a status tint plus a shape cue (a check when completed, a ring while in progress), so it
 * never relies on colour alone. Purely visual; no interaction.
 */
function SectionProgressDots({ mock }: { mock: FullMockCard }) {
  if (mock.mockType !== 'full') return null;
  const included = mock.includedSubtests ?? FULL_MOCK_ORDER;
  const progress = mock.sectionProgress ?? {};
  return (
    <div className="flex items-center gap-1.5" role="group" aria-label="Per-sub-test progress">
      {FULL_MOCK_ORDER.map((subtest) => {
        const active = included.includes(subtest);
        const state = progress[subtest];
        const palette = !active
          ? 'bg-background-light text-muted/40'
          : state === 'completed'
            ? 'bg-success/10 text-success-strong'
            : state === 'in-progress'
              ? 'bg-warning/10 text-warning-strong ring-1 ring-inset ring-warning/60'
              : state === 'locked'
                ? 'bg-background-light text-disabled'
                : 'bg-background-light text-muted/60';
        const Icon = SUBTEST_ICON[subtest];
        const label = `${SUBTEST_COLOR[subtest].label}: ${active ? (state ?? 'not started') : 'not included'}`;
        return (
          <span
            key={subtest}
            role="img"
            title={label}
            aria-label={label}
            className={`relative flex h-6 w-6 items-center justify-center rounded-full ${palette}`}
          >
            <Icon className="h-3 w-3" aria-hidden="true" />
            {active && state === 'completed' ? (
              <Check
                className="absolute -end-1 -top-1 h-3 w-3 rounded-full bg-surface text-success-strong"
                strokeWidth={3}
                aria-hidden="true"
              />
            ) : null}
          </span>
        );
      })}
    </div>
  );
}

function MockCenterInner() {
  const searchParams = useSearchParams();
  const scope: MockScope = useMemo(() => {
    const subtestParam = searchParams.get('subtest');
    const typeParam = searchParams.get('type');
    if (subtestParam && (VALID_SUBTESTS as readonly string[]).includes(subtestParam)) {
      const st = subtestParam as SubtestCode;
      return { kind: 'subtest', subtest: st, label: `Full ${SUBTEST_COLOR[st].label} Mock` };
    }
    if (typeParam === 'full') {
      return { kind: 'full', label: 'Full Combined Mock' };
    }
    return null;
  }, [searchParams]);

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

  // Apply the deep-link scope on top of the profession filter (composed with AND). A `subtest` scope keeps
  // the matching sub-test mocks plus full mocks that *include* that sub-test; a `full` scope shows full mocks
  // only and hides the sub-test section entirely.
  const scopedSubTestMocks = useMemo(() => {
    if (!scope) return filteredSubTestMocks;
    if (scope.kind === 'full') return [];
    return filteredSubTestMocks.filter((mock) => mock.subtest === scope.subtest);
  }, [filteredSubTestMocks, scope]);

  const scopedFullMocks = useMemo(() => {
    if (!scope) return filteredFullMocks;
    if (scope.kind === 'full') return filteredFullMocks;
    return filteredFullMocks.filter((mock) =>
      (mock.includedSubtests ?? FULL_MOCK_ORDER).includes(scope.subtest),
    );
  }, [filteredFullMocks, scope]);

  // Does any listening/reading/... sub-test bundle exist at all (ignoring profession)? Used to phrase the
  // scoped empty state honestly: "none published yet" vs "none for this profession".
  const scopedSubtestHasAny = useMemo(() => {
    if (scope?.kind !== 'subtest') return false;
    return subTestMocks.some((mock) => mock.subtest === scope.subtest);
  }, [subTestMocks, scope]);

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
    // The card shows three meta items: the learner's real trend leads, so it is never the one cut.
    metaItems: [
      ...(trendLabel ? [{ icon: BarChart3, label: trendLabel }] : []),
      { icon: Clock, label: '~3 hours' },
      { icon: Award, label: 'Full mock flow' },
      { icon: FileText, label: 'Report included' },
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
      variant: 'primary',
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
          variant: 'primary',
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
        description: `${
          typeof cohortPercentile.percentile === 'number' && typeof cohortPercentile.cohortSize === 'number'
            ? `Your latest mock is in the ${cohortPercentile.percentile}th percentile of ${cohortPercentile.cohortSize} learners who completed a mock in the last ${cohortPercentile.windowDays ?? 90} days.`
            : 'Complete a full mock to compare your overall score against the recent cohort.'
        } All comparisons are private.`,
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
        { icon: Layers, label: 'Mock routes', value: `${scopedFullMocks.length} full mocks` },
        { icon: BarChart3, label: 'Recent reports', value: `${reports.length} available` },
      ];

  const noBundles = fullMocks.length === 0 && subTestMocks.length === 0;
  // The backend's no-bundles copy addresses admins and links /admin/* (DEF-005: a 403 trap for
  // learners), so its message only replaces the generic one when it points at a learner route.
  const learnerEmptyRoute = emptyState?.route && !emptyState.route.startsWith('/admin/') ? emptyState.route : null;

  const categories = [
    { href: '/mocks?subtest=listening', label: 'Full Listening Mock', icon: SUBTEST_ICON.listening, palette: SUBTEST_COLOR.listening, testid: 'mocks-cat-listening' },
    { href: '/mocks?subtest=reading', label: 'Full Reading Mock', icon: SUBTEST_ICON.reading, palette: SUBTEST_COLOR.reading, testid: 'mocks-cat-reading' },
    { href: '/mocks?subtest=writing', label: 'Full Writing Mock', icon: SUBTEST_ICON.writing, palette: SUBTEST_COLOR.writing, testid: 'mocks-cat-writing' },
    // All four sub-tests: the module's own navy (the hero's Layers mark), not a status colour.
    { href: '/mocks?type=full', label: 'Full Combined Mock', icon: Layers, palette: { fg: 'text-navy', bg: 'bg-navy/10', label: 'Combined' }, testid: 'mocks-cat-combined' },
  ] as const;

  const professionChip = (selected: boolean) =>
    cn(
      'pressable touch-target rounded-full border px-4 py-1.5 text-xs font-semibold transition-colors',
      selected
        ? 'border-primary bg-primary text-white dark:bg-primary-700'
        : 'border-border bg-surface text-navy hover:border-border-hover',
    );

  return (
    <>
      <LearnerPageHero
        eyebrow="Module Focus"
        icon={Layers}
        accent="navy"
        title="Choose the mock that proves whether practice is transferring"
        description="Pick the right mock depth, track your progress, and let your results guide your next move."
        highlights={heroHighlights}
        footer={(
          <p
            className="flex items-start gap-2 text-xs font-semibold leading-5 text-muted"
            data-testid="mocks-integrity-reminder"
            role="note"
          >
            <Lock className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
            OET test content is confidential. Do not redistribute or share questions outside this practice context.
          </p>
        )}
      />

      {/* Per the 2026-05-27 OET sample-test alignment, the Mocks tab is the
          single canonical home for full mocks. We surface the four owner-
          required categories — Full Listening Mock, Full Reading Mock, Full
          Writing Mock, Full Combined Mock — as a clear landing matrix so
          first-time visitors immediately see the four routes without having
          to scroll past Resume / Recommended / Profession-filter blocks. */}
      <MotionSection data-testid="mocks-categories">
        <LearnerSurfaceSectionHeader
          eyebrow="Choose your mock type"
          title="The four canonical OET mock categories"
          className="mb-4"
        />
        <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
          {categories.map((category, index) => {
            const Icon = category.icon;
            const isActive =
              (scope?.kind === 'subtest' && category.href === `/mocks?subtest=${scope.subtest}`) ||
              (scope?.kind === 'full' && category.href === '/mocks?type=full');
            return (
              <li key={category.href}>
                <MotionItem delayIndex={index} className="h-full">
                  <CardLink
                    href={category.href}
                    data-testid={category.testid}
                    aria-current={isActive ? 'true' : undefined}
                    className={cn('group flex h-full items-start gap-3', isActive && 'border-primary ring-1 ring-primary/40')}
                  >
                    <div className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl ${category.palette.bg}`}>
                      <Icon className={`h-5 w-5 ${category.palette.fg}`} aria-hidden="true" />
                    </div>
                    <div className="min-w-0 flex-1">
                      <h3 className="text-sm font-bold text-navy">{category.label}</h3>
                      <p className="mt-0.5 text-xs text-muted">
                        Jump straight to {category.palette.label.toLowerCase()} bundles
                      </p>
                    </div>
                    <ArrowRight
                      className="h-4 w-4 self-center text-muted/40 transition-colors group-hover:text-navy rtl:rotate-180"
                      aria-hidden="true"
                    />
                  </CardLink>
                </MotionItem>
              </li>
            );
          })}
        </ul>
      </MotionSection>

      {scope ? (
        <div
          data-testid="mocks-scope-banner"
          className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-primary/30 bg-primary/5 px-4 py-3"
        >
          <p className="text-sm font-semibold text-navy">
            Showing: <span className="text-primary">{scope.label}</span>
          </p>
          <Button asChild variant="outline" size="sm">
            <Link href="/mocks" data-testid="mocks-scope-clear">
              <Layers className="h-3.5 w-3.5" aria-hidden="true" />
              View all mocks
            </Link>
          </Button>
        </div>
      ) : null}

      <LearnerSkillSwitcher compact />

      {!loading && !error && noBundles ? (
        learnerEmptyRoute ? (
          <LearnerEmptyState
            icon={Layers}
            title={emptyState?.title ?? 'No mock bundles are published yet'}
            description={
              emptyState?.description ??
              'Once an admin publishes a bundle it will appear here with its real section order.'
            }
            primaryAction={{ label: 'Go to dashboard', href: learnerEmptyRoute }}
          />
        ) : (
          <LearnerEmptyState
            icon={Layers}
            title="No mock bundles are published yet"
            description="The four mock categories above will populate as bundles are published: Full Listening Mock, Full Reading Mock, Full Writing Mock, and Full Combined Mock (all four sub-tests). Meanwhile, you can still practise part-by-part inside Listening or Reading."
            primaryAction={{ label: 'Practise Listening', href: '/listening' }}
            secondaryAction={{ label: 'Track Progress', href: '/progress' }}
          />
        )
      ) : null}

      {loading ? (
        <LearnerSkeleton variant="list" />
      ) : error ? (
        <MotionSection>
          <InlineAlert
            variant="error"
            title="We couldn't load the Mock Center right now"
            action={(
              <div className="flex flex-wrap gap-2">
                <Button size="sm" onClick={() => setReloadKey((k) => k + 1)}>
                  <RefreshCw className="h-4 w-4" aria-hidden="true" />
                  Retry
                </Button>
                <Button asChild variant="ghost" size="sm">
                  <a href="mailto:support@oetwithdrhesham.co.uk">Contact support</a>
                </Button>
              </div>
            )}
          >
            {error} If this keeps happening, the mock service may be warming up &mdash; try again
            in a moment.
          </InlineAlert>
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
                  <MotionItem key={attempt.mockAttemptId} delayIndex={Math.min(idx, 5)}>
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
                          ? { label: 'View report so far', href: attempt.reportRoute, variant: 'secondary' as const }
                          : undefined,
                      }}
                    />
                  </MotionItem>
                ))}
              </div>
            </MotionSection>
          )}

          <MotionSection delayIndex={1}>
            <LearnerSurfaceCard card={recommendedCard} />
          </MotionSection>

          {noBundles ? null : (
            <MotionSection delayIndex={2} className="grid grid-cols-1 gap-8 lg:grid-cols-3">
              <div className="space-y-6 sm:space-y-8 lg:col-span-2">
                {availableProfessions.length > 0 ? (
                  <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Filter mocks by profession">
                    <span className="eyebrow text-muted">Profession</span>
                    <button
                      type="button"
                      aria-pressed={professionFilter === null}
                      onClick={() => setProfessionFilter(null)}
                      className={professionChip(professionFilter === null)}
                    >
                      All professions
                    </button>
                    {availableProfessions.map((p) => (
                      <button
                        key={p.id}
                        type="button"
                        aria-pressed={professionFilter === p.id}
                        onClick={() => setProfessionFilter(p.id)}
                        className={professionChip(professionFilter === p.id)}
                      >
                        {p.label}
                      </button>
                    ))}
                  </div>
                ) : null}

                {scope?.kind === 'full' ? null : (
                  <section>
                    <LearnerSurfaceSectionHeader
                      eyebrow="Sub-test Mocks"
                      title={
                        scope?.kind === 'subtest'
                          ? `Full ${SUBTEST_COLOR[scope.subtest].label} Mocks`
                          : 'Choose the simulation scope you need'
                      }
                      description={
                        scope?.kind === 'subtest'
                          ? `Single ${SUBTEST_COLOR[scope.subtest].label.toLowerCase()} sub-test mocks, timed like the real exam.`
                          : "Each entry tells you whether it's a single sub-test or a full mock."
                      }
                      className="mb-4"
                    />
                    {scopedSubTestMocks.length === 0 ? (
                      scope?.kind === 'subtest' && !scopedSubtestHasAny ? (
                        <LearnerEmptyState
                          compact
                          icon={SUBTEST_ICON[scope.subtest]}
                          title={`No ${scope.label} bundles are published yet`}
                          description={`You can still practise ${SUBTEST_COLOR[scope.subtest].label.toLowerCase()} part-by-part in the meantime.`}
                          primaryAction={{
                            label: `Practise ${SUBTEST_COLOR[scope.subtest].label} part-by-part`,
                            href: SUBTEST_HUB[scope.subtest],
                          }}
                          secondaryAction={{ label: 'View all mocks', href: '/mocks' }}
                        />
                      ) : (
                        <LearnerEmptyState
                          compact
                          icon={Layers}
                          title={
                            scope?.kind === 'subtest'
                              ? `No ${SUBTEST_COLOR[scope.subtest].label} sub-test mocks match the selected profession`
                              : subTestMocks.length === 0
                                ? 'No published sub-test mock bundles are available yet'
                                : 'No sub-test mocks match the selected profession'
                          }
                          description={
                            scope?.kind !== 'subtest' && subTestMocks.length === 0
                              ? 'Once an admin publishes a bundle it will appear here with its real section order.'
                              : 'Try “All professions” to widen your view.'
                          }
                        />
                      )
                    ) : (
                      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                        {scopedSubTestMocks.map((mock, idx) => {
                          const subtest = (mock.subtest ?? 'reading') as SubtestCode;
                          const palette = SUBTEST_COLOR[subtest];
                          const Icon = SUBTEST_ICON[subtest];
                          const href = mock.route ?? '/mocks/setup';
                          const count = mock.sectionCount ?? 1;
                          return (
                            <MotionItem key={mock.id} delayIndex={Math.min(idx, 5)} className="h-full">
                              <CardLink href={href} className="group flex h-full items-center gap-4">
                                <div className={`flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl ${palette.bg}`}>
                                  <Icon className={`h-6 w-6 ${palette.fg}`} aria-hidden="true" />
                                </div>
                                <div className="min-w-0 flex-1">
                                  <h3 className="text-base font-bold text-navy">{mock.title}</h3>
                                  <p className="text-sm text-muted">
                                    <span className="tabular-nums">{count}</span> section{count === 1 ? '' : 's'} available
                                  </p>
                                </div>
                                <ArrowRight
                                  className="h-5 w-5 shrink-0 text-muted/40 transition-colors group-hover:text-navy rtl:rotate-180"
                                  aria-hidden="true"
                                />
                              </CardLink>
                            </MotionItem>
                          );
                        })}
                      </div>
                    )}
                  </section>
                )}

                <section>
                  <LearnerSurfaceSectionHeader
                    eyebrow="Full Mocks"
                    title={
                      scope?.kind === 'subtest'
                        ? `Full mocks that include ${SUBTEST_COLOR[scope.subtest].label}`
                        : 'Keep full-exam progression visible'
                    }
                    description="Progress dots show which sub-tests you've completed on the latest attempt for each bundle."
                    className="mb-4"
                  />
                  {scopedFullMocks.length === 0 ? (
                    <LearnerEmptyState
                      compact
                      icon={Layers}
                      title={
                        fullMocks.length === 0
                          ? 'No full mock bundles are published yet'
                          : scope?.kind === 'subtest'
                            ? `No full mocks include a ${SUBTEST_COLOR[scope.subtest].label} section yet`
                            : 'No full mocks match the selected profession'
                      }
                      description={
                        fullMocks.length > 0 && scope?.kind !== 'subtest'
                          ? 'Try “All professions” to widen your view.'
                          : 'Once an admin publishes a bundle, it will appear here with its real section order.'
                      }
                    />
                  ) : (
                    <Card padding="none" className="overflow-hidden">
                      <ul className="divide-y divide-border">
                        {scopedFullMocks.map((mock, idx) => {
                          const locked = mock.status === 'locked';
                          return (
                            <li key={mock.id}>
                              <MotionItem delayIndex={Math.min(idx, 5)}>
                                <Link
                                  href={locked ? '/mocks' : mock.route ?? '/mocks/setup'}
                                  aria-disabled={locked}
                                  tabIndex={locked ? -1 : undefined}
                                  className={cn(
                                    'flex items-center justify-between gap-4 p-4 transition-colors focus-visible:-outline-offset-2 sm:p-5',
                                    locked ? 'pointer-events-none bg-background-light opacity-75' : 'hover:bg-background-light',
                                  )}
                                >
                                  <div className="flex min-w-0 items-center gap-4">
                                    <div className="shrink-0" aria-hidden="true">
                                      {mock.status === 'completed' ? (
                                        <CheckCircle2 className="h-6 w-6 text-success-strong" />
                                      ) : locked ? (
                                        <div className="flex h-6 w-6 items-center justify-center rounded-full border-2 border-border">
                                          <div className="h-2 w-2 rounded-full bg-border" />
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
                                          <span className="shrink-0 rounded-full bg-warning/10 px-2 py-0.5 tile-label text-warning-strong">
                                            Recommended
                                          </span>
                                        ) : null}
                                      </h3>
                                      <div className="mt-0.5 text-sm text-muted">
                                        {mock.status === 'completed' ? (
                                          <span>
                                            Completed {mock.date} · Score:{' '}
                                            <span className="font-bold tabular-nums text-navy">{mock.score}</span>
                                          </span>
                                        ) : locked ? (
                                          <span className="flex items-center gap-1">
                                            <Clock className="h-3.5 w-3.5 shrink-0" aria-hidden="true" /> {mock.reason}
                                          </span>
                                        ) : (
                                          <span className="flex items-center gap-1">
                                            <Clock className="h-3.5 w-3.5 shrink-0" aria-hidden="true" /> {mock.duration}
                                          </span>
                                        )}
                                      </div>
                                      <div className="mt-2">
                                        <SectionProgressDots mock={mock} />
                                      </div>
                                    </div>
                                  </div>
                                  {!locked ? (
                                    <ArrowRight className="hidden h-5 w-5 shrink-0 text-muted/40 sm:block rtl:rotate-180" aria-hidden="true" />
                                  ) : null}
                                </Link>
                              </MotionItem>
                            </li>
                          );
                        })}
                      </ul>
                    </Card>
                  )}
                </section>
              </div>

              {/* Each rail card carries its own eyebrow, title and description. */}
              <div className="space-y-6 sm:space-y-8">
                <LearnerSurfaceCard card={reviewCard} />
                {scoreGuaranteeCard ? <LearnerSurfaceCard card={scoreGuaranteeCard} /> : null}
                {cohortCard ? <LearnerSurfaceCard card={cohortCard} /> : null}

                <section>
                  <LearnerSurfaceSectionHeader
                    eyebrow="Previous Reports"
                    title="Keep the latest evidence visible"
                    description="Detailed reports for every completed mock attempt."
                    className="mb-4"
                  />
                  {reports.length === 0 ? (
                    <EmptyState
                      icon={<BarChart3 className="h-7 w-7" aria-hidden="true" />}
                      title="No reports yet"
                      description="Complete a mock to see your results here."
                      className="py-8"
                    />
                  ) : (
                    <Card padding="none" className="overflow-hidden">
                      <ul className="divide-y divide-border">
                        {reports.slice(0, 4).map((report, idx) => (
                          <li key={report.id}>
                            <MotionItem delayIndex={Math.min(idx, 5)}>
                              <Link
                                href={`/mocks/report/${report.id}`}
                                className="group flex items-center justify-between gap-3 p-4 transition-colors hover:bg-background-light focus-visible:-outline-offset-2"
                              >
                                <div className="flex min-w-0 items-center gap-3">
                                  <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-background-light">
                                    <BarChart3 className="h-4 w-4 text-muted" aria-hidden="true" />
                                  </div>
                                  <div className="min-w-0">
                                    <h3 className="text-sm font-bold text-navy transition-colors group-hover:text-primary">
                                      {report.title}
                                    </h3>
                                    <p className="text-xs text-muted">{report.date}</p>
                                  </div>
                                </div>
                                <span className="shrink-0 text-sm font-bold tabular-nums text-navy">{report.overallScore}</span>
                              </Link>
                            </MotionItem>
                          </li>
                        ))}
                      </ul>
                    </Card>
                  )}
                </section>
              </div>
            </MotionSection>
          )}
        </>
      )}
    </>
  );
}

export default function MockCenter() {
  // `useSearchParams()` triggers a client-side-rendering bailout, so the reader must sit inside a Suspense
  // boundary (mirrors the pattern in app/reading/paper/[paperId]/page.tsx).
  return (
    <Suspense
      fallback={
        <>
          <LearnerSkeleton variant="dashboard" />
        </>
      }
    >
      <MockCenterInner />
    </Suspense>
  );
}
