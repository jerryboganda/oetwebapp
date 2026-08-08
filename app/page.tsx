'use client';

import dynamic from 'next/dynamic';
import { useContext, useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { MotionItem } from '@/components/ui/motion-primitives';
import {
  ArrowRight,
  BookOpen,
  Calendar,
  CheckCircle2,
  CreditCard,
  FilePenLine,
  Flag,
  Flame,
  Headphones,
  Mic,
  Sparkles,
  Shield,
  Star,
  Timer,
  TrendingUp,
  Trophy,
  Wallet,
} from 'lucide-react';
import { Button, Card, CardContent, CardHeader, CardTitle } from '@/components/ui';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerDashboardShell } from '@/components/layout';
import {
  LearnerPageHero,
  LearnerSurfaceCard,
} from '@/components/domain';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { LearnerSkillSwitcher } from '@/components/domain/learner-skill-switcher';
import { AsyncStateWrapper } from '@/components/state';
import { OnboardingChecklist } from '@/components/onboarding/onboarding-checklist';
import { AppDownloadPromo, PostLoginAppModal } from '@/components/marketing/app-download-promo';
import { AuthContext } from '@/contexts/auth-context';
import { useDashboardHome } from '@/lib/hooks/use-dashboard-home';
import {
  learnerGetScoringPolicy,
  fetchMyAiPackageCredits,
  fetchMyEntitlementSnapshot,
  fetchSubscriptionMe,
  type MyEntitlementSnapshot,
  type SubscriptionMe,
} from '@/lib/api';
import { queryKeys } from '@/lib/query/hooks';
import { formatMoney } from '@/lib/money';
import type { SubTest } from '@/lib/mock-data';
import type { LearnerSurfaceCardModel } from '@/lib/learner-surface';

const LearnerDashboardDetails = dynamic(
  () => import('@/components/learner/learner-dashboard-details').then((module) => module.LearnerDashboardDetails),
  {
    // The lower dashboard contains readiness, pronunciation, scoring, and
    // activity widgets. Keep that tree out of the mobile hydration critical
    // path; it is still rendered as soon as the deferred chunk is ready.
    ssr: false,
    loading: () => null,
  },
);

const SUBTEST_ICONS: Record<SubTest, React.ElementType> = {
  Writing: FilePenLine,
  Speaking: Mic,
  Reading: BookOpen,
  Listening: Headphones,
};

const SUBTEST_COLORS: Record<SubTest, string> = {
  Writing: 'text-rose-500 bg-rose-50',
  Speaking: 'text-purple-600 bg-purple-50',
  Reading: 'text-blue-600 bg-blue-50',
  Listening: 'text-indigo-600 bg-indigo-50',
};

// Left-edge accent used to make each task instantly scannable by skill.
const SUBTEST_SPINE: Record<SubTest, string> = {
  Writing: 'bg-rose-400',
  Speaking: 'bg-purple-400',
  Reading: 'bg-blue-400',
  Listening: 'bg-indigo-400',
};

function hasLiveReadinessEvidence(readiness: ReturnType<typeof useDashboardHome>['data']['readiness']) {
  if (!readiness) return false;
  const source = readiness.evidence?.source;
  if (source === 'bootstrap' || source === 'no_evidence') return false;
  return readiness.subTests.length > 0;
}
function routeForTask(task: { route?: string; subTest: SubTest }) {
  return task.route ?? `/${task.subTest.toLowerCase()}`;
}

function taskAllowedByEntitlement(task: { subTest: SubTest }, entitlement: MyEntitlementSnapshot | null) {
  if (!entitlement) return true;
  const modules = new Set(entitlement.enabledModules.map((module) => module.toLowerCase()));
  if (modules.size === 0) return false;
  const subTest = task.subTest.toLowerCase();
  if (modules.has(subTest)) return true;
  return subTest === 'speaking' && modules.has('speakingsession');
}

function calculateDaysLeft(value: string | null | undefined): string {
  if (!value) return 'Not scheduled';
  const target = new Date(value);
  if (Number.isNaN(target.getTime())) return 'Not scheduled';
  const today = new Date();
  const todayUtc = Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate());
  const targetUtc = Date.UTC(target.getUTCFullYear(), target.getUTCMonth(), target.getUTCDate());
  const days = Math.max(0, Math.ceil((targetUtc - todayUtc) / 86_400_000));
  return days === 1 ? '1 day left' : `${days} days left`;
}

function subscriptionStatusLabel(subscription: SubscriptionMe | null, entitlement: MyEntitlementSnapshot | null) {
  if (subscription?.pausedUntil || entitlement?.isFrozen) return 'Paused';
  if (!subscription) return 'No active subscription';
  if (subscription.status === 'trialing') return 'Trial';
  if (subscription.status === 'past_due') return 'Past due';
  if (subscription.status === 'cancelled' || subscription.status === 'canceled') return 'Cancelled';
  if (subscription.status === 'active') return 'Active';
  if (subscription.status === 'expired') return 'Expired';
  return subscription.status ? subscription.status.replace(/_/g, ' ') : 'Active';
}

function subscriptionStatusClass(subscription: SubscriptionMe | null, entitlement: MyEntitlementSnapshot | null) {
  const status = subscriptionStatusLabel(subscription, entitlement).toLowerCase();
  if (status === 'active' || status === 'trial') return 'bg-success/10 text-success';
  if (status === 'past due') return 'bg-warning/10 text-warning';
  if (status === 'paused') return 'bg-amber-100 text-amber-800';
  if (status === 'cancelled' || status === 'expired') return 'bg-danger/10 text-danger';
  return 'bg-background-light text-muted';
}

function DashboardSubscriptionStrip({
  subscription,
  entitlement,
  isLoading,
  hasError,
}: {
  subscription: SubscriptionMe | null;
  entitlement: MyEntitlementSnapshot | null;
  isLoading: boolean;
  hasError: boolean;
}) {
  const expiryDate = entitlement?.expiresAt ?? subscription?.nextRenewalAt ?? null;
  const planName = subscription?.planName ?? 'No active subscription';
  const statusLabel = subscriptionStatusLabel(subscription, entitlement);
  const priceLabel = subscription
    ? `${formatMoney(subscription.price, { currency: subscription.currency })} / ${subscription.interval}`
    : null;
  const facts = [
    !isLoading && !hasError ? { icon: Timer, label: calculateDaysLeft(expiryDate) } : null,
    priceLabel ? { icon: Wallet, label: priceLabel } : null,
    entitlement?.writingAssessmentsRemaining && entitlement.writingAssessmentsRemaining > 0
      ? { icon: FilePenLine, label: `${entitlement.writingAssessmentsRemaining} writing` }
      : null,
    entitlement?.speakingSessionsRemaining && entitlement.speakingSessionsRemaining > 0
      ? { icon: Mic, label: `${entitlement.speakingSessionsRemaining} speaking` }
      : null,
    entitlement?.aiCreditsRemaining && entitlement.aiCreditsRemaining > 0
      ? { icon: Sparkles, label: `${entitlement.aiCreditsRemaining} AI credits` }
      : null,
    entitlement?.tutorBookUnlocked ? { icon: BookOpen, label: 'Tutor Book' } : null,
  ].filter(Boolean) as { icon: typeof Timer; label: string }[];

  return (
    <div
      // The loaded facts row is taller than the initial subscription label.
      // Reserve the common mobile footprint so live entitlement data cannot
      // push the action surface down and accumulate CLS during hydration.
      className="flex min-h-[104px] flex-col gap-2.5"
    >
      <div className="flex items-center gap-2">
        <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
          <CreditCard className="h-4 w-4" aria-hidden="true" />
        </span>
        <span className="min-w-0 flex-1 truncate text-[13px] font-bold text-navy">
          {isLoading ? 'Loading subscriptionâ€¦' : hasError ? 'Subscription details unavailable' : planName}
        </span>
        {!isLoading && !hasError ? (
          <span className={`shrink-0 rounded-full px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide ${subscriptionStatusClass(subscription, entitlement)}`}>
            {statusLabel}
          </span>
        ) : null}
      </div>

      {facts.length > 0 ? (
        <div className="grid grid-cols-2 gap-1.5 sm:grid-cols-3">
          {facts.map(({ icon: Icon, label }) => (
            <span
              key={label}
              className="flex min-w-0 items-center gap-1.5 rounded-lg bg-background-light px-2.5 py-1.5 text-[11px] font-semibold text-navy ring-1 ring-border/70 transition-colors hoverable:bg-lavender/50 hoverable:ring-primary/25"
            >
              <Icon className="h-3.5 w-3.5 shrink-0 text-primary" aria-hidden="true" />
              <span className="truncate">{label}</span>
            </span>
          ))}
        </div>
      ) : null}

      <Link
        href="/catalog"
        className="-mx-2 inline-flex items-center gap-1 self-start rounded-lg px-2 py-1 text-[12px] font-semibold text-primary transition-colors hover:bg-primary/10"
      >
        See all catalog <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
      </Link>
    </div>
  );
}

function LearnerDashboardLoadingCard() {
  return (
    <Card
      className="min-h-[260px]"
      role="status"
      aria-busy="true"
      aria-label="Preparing your next study step"
    >
      <div className="flex h-full flex-col justify-between gap-4 sm:gap-6">
        <div>
          <div className="inline-flex items-center gap-1.5 rounded-full border border-primary/20 bg-primary/10 px-2.5 py-1 text-[11px] font-bold uppercase tracking-wider text-primary sm:text-xs">
            <Sparkles className="h-3.5 w-3.5" aria-hidden="true" />
            Next action
          </div>
          <h3 className="mt-2.5 text-base font-bold text-navy sm:mt-4 sm:text-xl">
            Preparing your next study step
          </h3>
          <p className="mt-1.5 max-w-2xl text-[13px] leading-relaxed text-muted sm:mt-2 sm:text-sm">
            We&apos;re loading your live study plan so you can start the right practice without losing your place.
          </p>
        </div>
        <div aria-hidden="true" className="space-y-2">
          <div className="h-10 w-full rounded-lg bg-border/60 motion-safe:animate-pulse" />
          <div className="h-10 w-full rounded-lg bg-border/60 motion-safe:animate-pulse" />
        </div>
      </div>
    </Card>
  );
}

export default function Dashboard() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const authContext = useContext(AuthContext);
  const queryClient = useQueryClient();
  const { data, error, reload, status } = useDashboardHome();
  const [scoringExpanded, setScoringExpanded] = useState(false);
  const purchaseSuccess = searchParams?.get('purchase') === 'success';
  const queryUserId = authContext?.user?.userId ?? 'current';
  const supplementalQueriesEnabled = authContext
    ? !authContext.loading && authContext.isAuthenticated
    : true;
  const scoringPolicyQuery = useQuery({
    queryKey: queryKeys.dashboard.scoringPolicy(queryUserId),
    queryFn: learnerGetScoringPolicy,
    staleTime: 60_000,
    enabled: supplementalQueriesEnabled,
  });
  const entitlementQuery = useQuery({
    queryKey: queryKeys.dashboard.entitlement(queryUserId),
    queryFn: fetchMyEntitlementSnapshot,
    staleTime: 30_000,
    enabled: supplementalQueriesEnabled,
  });
  const subscriptionQuery = useQuery({
    queryKey: queryKeys.dashboard.subscription(queryUserId),
    queryFn: fetchSubscriptionMe,
    staleTime: 30_000,
    enabled: supplementalQueriesEnabled,
  });
  const aiPackageCreditsQuery = useQuery({
    queryKey: queryKeys.dashboard.aiPackageCredits(queryUserId),
    queryFn: fetchMyAiPackageCredits,
    staleTime: 30_000,
    enabled: supplementalQueriesEnabled && purchaseSuccess,
  });
  const scoringPolicy = scoringPolicyQuery.data ?? null;
  const entitlement = entitlementQuery.data ?? null;
  const subscription = subscriptionQuery.data ?? null;
  const aiPackageCredits = aiPackageCreditsQuery.data ?? null;
  const subscriptionLoading = subscriptionQuery.isPending;
  const subscriptionError = Boolean(subscriptionQuery.error);
  const { home, profile, readiness, tasks, engagement, loadedAt } = data;
  const freeze = home?.freeze?.currentFreeze ?? null;

  const entitledTasks = tasks.filter((task) => taskAllowedByEntitlement(task, entitlement));
  const todayTasks = entitledTasks.filter((task) => task.section === 'today');
  const upcomingTasks = entitledTasks.filter((task) => task.section === 'thisWeek');
  const completedToday = todayTasks.filter((task) => task.status === 'completed').length;
  const nextAction = todayTasks.find((task) => task.status !== 'completed');
  const asyncStatus = status === 'loading' ? 'loading' : status === 'error' ? 'error' : status === 'partial' ? 'partial' : !profile ? 'empty' : 'success' as const;
  const liveReadiness = hasLiveReadinessEvidence(readiness) ? readiness : null;
  const readinessSubTests = liveReadiness?.subTests ?? [];
  const readinessAverage = readinessSubTests.length > 0
    ? Math.round(readinessSubTests.reduce((sum, subTest) => sum + subTest.readiness, 0) / readinessSubTests.length)
    : 0;
  const readinessRecentTrend = liveReadiness?.evidence?.recentTrend ?? 'Trend data will appear after more practice.';
  const readinessUpdatedAt = liveReadiness?.evidence?.lastUpdated ?? loadedAt;

  useEffect(() => {
    if (!purchaseSuccess || !supplementalQueriesEnabled) return;
    void Promise.all([
      queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.entitlement(queryUserId) }),
      queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.subscription(queryUserId) }),
      queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.aiPackageCredits(queryUserId) }),
    ]);
  }, [purchaseSuccess, queryClient, queryUserId, supplementalQueriesEnabled]);

  const dashboardHeroHighlights = [
    {
      icon: Calendar,
      label: 'Exam target',
      value: home?.cards?.examDate?.value ?? profile?.examDate ?? 'Set your exam date',
    },
    {
      icon: Star,
      label: 'Pending reviews',
      value: `${home?.cards?.pendingExpertReviews?.count ?? 0} in progress`,
    },
    {
      icon: CheckCircle2,
      label: "Today's plan",
      value: todayTasks.length > 0 ? `${completedToday}/${todayTasks.length} done` : 'No tasks scheduled',
    },
  ];

  const subTestIconMap: Record<string, typeof Sparkles> = {
    speaking: Mic,
    writing: FilePenLine,
    reading: BookOpen,
    listening: Headphones,
  };
  const nextActionSubTestIcon = nextAction
    ? subTestIconMap[nextAction.subTest.toLowerCase()] ?? BookOpen
    : BookOpen;

  const nextActionCard = nextAction
    ? ({
        kind: 'task',
        sourceType: 'backend_task',
        accent: 'primary',
        eyebrow: 'Recommended Next',
        eyebrowIcon: Sparkles,
        title: nextAction.title,
        description: nextAction.rationale || 'This is the next scheduled item from your live study plan.',
        metaItems: [
          { icon: Timer, label: nextAction.duration },
          { icon: nextActionSubTestIcon, label: nextAction.subTest },
        ],
        primaryAction: {
          label: 'Start Now',
          href: routeForTask(nextAction),
        },
      } satisfies LearnerSurfaceCardModel)
    : null;

  const nextMockRecommendation = home?.cards?.nextMockRecommendation;
  const nextMockCard: LearnerSurfaceCardModel | null = nextMockRecommendation
    ? {
    kind: 'navigation',
    sourceType: 'backend_summary',
    accent: 'navy',
    eyebrow: 'Mock Progression',
    eyebrowIcon: Flag,
    title: nextMockRecommendation.title,
    description: nextMockRecommendation.rationale,
    metaItems: [
      { icon: Calendar, label: home?.cards?.examDate?.value ?? 'Exam date not set' },
      { icon: Star, label: `${home?.cards?.pendingExpertReviews?.count ?? 0} pending reviews` },
    ],
    primaryAction: {
      label: 'Open Mock Center',
      href: nextMockRecommendation.route ?? '/mocks',
    },
    secondaryAction: {
      label: 'View Study Plan',
      href: '/study-plan',
      variant: 'secondary',
    },
  }
    : null;

  return (
    <LearnerDashboardShell pageTitle="Dashboard">
      <div className="space-y-6">
        <PostLoginAppModal />
        {/* Keep the stable, useful dashboard context outside the authenticated
            data boundary. Slow critical API responses can fill the action
            cards below without leaving a learner staring at a full-page
            skeleton on mobile WebKit or a slower connection. */}
        <div data-testid="learner-dashboard-hero">
          <LearnerPageHero
            eyebrow="Current Focus"
            icon={Sparkles}
            accent="primary"
            title="Keep today's priorities and exam signals in view"
            description="Decide your next action, check your readiness, and move forward with confidence."
            highlights={dashboardHeroHighlights}
            footer={(
              <DashboardSubscriptionStrip
                subscription={subscription}
                entitlement={entitlement}
                isLoading={subscriptionLoading}
                hasError={subscriptionError}
              />
            )}
          />
        </div>

        <AsyncStateWrapper
          status={asyncStatus}
          onRetry={reload}
          errorMessage={error ?? undefined}
          initial={false}
          partialMessage={error ?? 'Some dashboard data could not be loaded right now. The rest of your workspace is still available.'}
          loadingContent={(
            <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
              <LearnerDashboardLoadingCard />
            </div>
          )}
          emptyContent={
            <LearnerEmptyState
              icon={Sparkles}
              title="Welcome to your OET workspace"
              description="Complete onboarding to personalize your dashboard, or set goals first if you already know your exam target."
              primaryAction={{ label: 'Start Onboarding', href: '/onboarding' }}
              secondaryAction={{ label: 'Set Goals', href: '/goals' }}
            />
          }
        >
          <div className="space-y-6">
          {purchaseSuccess ? (
            <InlineAlert variant="success">
              AI package purchase received. Current balances: {aiPackageCredits
                ? `${aiPackageCredits.flexibleCredits} flexible, ${aiPackageCredits.writingOnlyCredits} writing, ${aiPackageCredits.speakingOnlyCredits} speaking, ${aiPackageCredits.mockExamsRemaining} mocks.`
                : 'refreshing your package balance.'}
            </InlineAlert>
          ) : null}

          <OnboardingChecklist />

          {freeze ? (
            <Card className="border-amber-200 bg-amber-50/70 shadow-sm">
              <CardHeader className="pb-3">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div>
                    <p className="text-[10px] font-medium uppercase tracking-wide text-amber-700">Read-only mode</p>
                    <CardTitle className="mt-2 flex items-center gap-2 text-xl text-amber-950">
                      <Shield className="h-5 w-5" />
                      Your account is currently frozen
                    </CardTitle>
                  </div>
                  <Button variant="outline" onClick={() => router.push('/freeze')}>
                    Open Freeze Center
                  </Button>
                </div>
              </CardHeader>
              <CardContent className="grid gap-3 sm:grid-cols-3">
                <div>
                  <p className="text-[10px] font-medium uppercase tracking-wide text-amber-700/70">Status</p>
                  <p className="mt-1 text-sm font-normal text-amber-950">{String(freeze.status ?? 'active')}</p>
                </div>
                <div>
                  <p className="text-[10px] font-medium uppercase tracking-wide text-amber-700/70">Started</p>
                  <p className="mt-1 text-sm font-normal text-amber-950">
                    {freeze.startedAt ? new Date(freeze.startedAt).toLocaleString() : 'Pending'}
                  </p>
                </div>
                <div>
                  <p className="text-[10px] font-medium uppercase tracking-wide text-amber-700/70">Ends</p>
                  <p className="mt-1 text-sm font-normal text-amber-950">
                    {freeze.endedAt ? new Date(freeze.endedAt).toLocaleString() : 'Not set'}
                  </p>
                </div>
              </CardContent>
            </Card>
          ) : null}

          <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
            {nextActionCard ? (
              <MotionItem initial={false} layout={false}>
                <div data-tour="learner-dashboard-next-action">
                  <LearnerSurfaceCard card={nextActionCard} className="min-h-[260px]" />
                </div>
              </MotionItem>
            ) : null}
            {nextMockCard ? (
              <MotionItem initial={false} layout={false}>
                <LearnerSurfaceCard card={nextMockCard} className="min-h-[260px]" />
              </MotionItem>
            ) : null}
            {!nextActionCard && !nextMockCard ? (
              <LearnerEmptyState
                className="lg:col-span-2"
                icon={Sparkles}
                title="No live dashboard priorities yet"
                description="Complete onboarding or open your study plan to create the evidence that powers next actions."
                primaryAction={{ label: 'Open Study Plan', href: '/study-plan' }}
              />
            ) : null}
          </div>

          <div data-tour="learner-dashboard-skills">
            <LearnerSkillSwitcher compact />
          </div>

          <LearnerDashboardDetails
            liveReadiness={liveReadiness}
            readinessAverage={readinessAverage}
            readinessRecentTrend={readinessRecentTrend}
            readinessUpdatedAt={readinessUpdatedAt}
            todayTasks={todayTasks}
            upcomingTasks={upcomingTasks}
            completedToday={completedToday}
            loadedAt={loadedAt}
            engagement={engagement}
            entitlement={entitlement}
            scoringPolicy={scoringPolicy}
          />

          {/* Keep this optional marketing promotion below the complete
              dashboard content so it cannot compete with the first viewport
              for LCP or introduce a visible mobile layout shift. */}
          <AppDownloadPromo variant="banner" />
        </div>
        </AsyncStateWrapper>
      </div>
    </LearnerDashboardShell>
  );
}
