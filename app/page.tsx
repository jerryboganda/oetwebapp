'use client';

import { useRouter } from 'next/navigation';
import { motion, useReducedMotion } from 'motion/react';
import { MotionItem } from '@/components/ui/motion-primitives';
import {
  ArrowRight,
  BookOpen,
  Calendar,
  CheckCircle2,
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
} from 'lucide-react';
import { Button, Card, CardContent, CardHeader, CardLink, CardTitle, ProgressBar } from '@/components/ui';
import { LearnerDashboardShell } from '@/components/layout';
import {
  LearnerPageHero,
  LearnerSurfaceCard,
  LearnerSurfaceSectionHeader,
  ReadinessMeter,
  WeakestLinkCard,
} from '@/components/domain';
import { AiUsageWidget } from '@/components/domain/AiUsageWidget';
import { PronunciationDashboardTile } from '@/components/domain/pronunciation';
import { AsyncStateWrapper } from '@/components/state';
import { useDashboardHome } from '@/lib/hooks/use-dashboard-home';
import type { SubTest } from '@/lib/mock-data';
import type { LearnerSurfaceCardModel } from '@/lib/learner-surface';

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

export default function Dashboard() {
  const router = useRouter();
  const prefersReducedMotion = useReducedMotion();
  const { data, error, reload, status } = useDashboardHome();
  const { home, profile, readiness, tasks, engagement } = data;
  const freeze = home?.freeze?.currentFreeze ?? null;

  const todayTasks = tasks.filter((task) => task.section === 'today');
  const upcomingTasks = tasks.filter((task) => task.section === 'thisWeek');
  const completedToday = todayTasks.filter((task) => task.status === 'completed').length;
  const nextAction = todayTasks.find((task) => task.status !== 'completed');
  const asyncStatus = status === 'loading' ? 'loading' : status === 'error' ? 'error' : !profile ? 'empty' : 'success' as const;
  const readinessSubTests = readiness?.subTests ?? [];
  const readinessAverage = readinessSubTests.length > 0
    ? Math.round(readinessSubTests.reduce((sum, subTest) => sum + subTest.readiness, 0) / readinessSubTests.length)
    : 0;
  const readinessRecentTrend = readiness?.evidence?.recentTrend ?? 'Trend data will appear after more practice.';

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
        description: 'This is your clearest next move inside the current study plan, based on what is due now and still incomplete.',
        metaItems: [
          { icon: Timer, label: nextAction.duration },
          { icon: nextActionSubTestIcon, label: nextAction.subTest },
        ],
        primaryAction: {
          label: 'Start Now',
          href: `/${nextAction.subTest.toLowerCase()}`,
        },
      } satisfies LearnerSurfaceCardModel)
    : null;

  const nextMockCard: LearnerSurfaceCardModel = {
    kind: 'navigation',
    sourceType: 'backend_summary',
    accent: 'navy',
    eyebrow: 'Mock Progression',
    eyebrowIcon: Flag,
    title: home?.cards?.nextMockRecommendation?.title ?? 'Full OET Mock Test',
    description:
      home?.cards?.nextMockRecommendation?.rationale ??
      'Use a full mock to check whether your recent practice work is transferring under pressure.',
    metaItems: [
      { icon: Calendar, label: home?.cards?.examDate?.value ?? 'Exam date not set' },
      { icon: Star, label: `${home?.cards?.pendingExpertReviews?.count ?? 0} pending reviews` },
    ],
    primaryAction: {
      label: 'Open Mock Center',
      href: home?.cards?.nextMockRecommendation?.route ?? '/mocks',
    },
    secondaryAction: {
      label: 'View Study Plan',
      href: '/study-plan',
      variant: 'outline',
    },
  };

  return (
    <LearnerDashboardShell pageTitle="Dashboard">
      <AsyncStateWrapper
        status={asyncStatus}
        onRetry={reload}
        errorMessage={error ?? undefined}
        emptyContent={
          <div className="space-y-3 py-12 text-center">
            <p className="text-sm font-bold text-navy">Welcome to OET Prep</p>
            <p className="text-xs text-muted">Complete onboarding to get started.</p>
            <Button size="sm" onClick={() => router.push('/onboarding')}>
              Start Onboarding
            </Button>
          </div>
        }
      >
        <div className="space-y-6">
          <LearnerPageHero
            eyebrow="Current Focus"
            icon={Sparkles}
            accent="primary"
            title="Keep today's priorities and exam signals in view"
              description="Decide your next action, check your readiness, and move forward with confidence."
            highlights={dashboardHeroHighlights}
          />

          {freeze ? (
            <Card className="border-amber-200 bg-amber-50/70 shadow-sm">
              <CardHeader className="pb-3">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div>
                    <p className="text-xs font-bold uppercase tracking-[0.18em] text-amber-700">Read-only mode</p>
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
                  <p className="text-xs font-semibold uppercase tracking-[0.16em] text-amber-700/70">Status</p>
                  <p className="mt-1 text-sm font-semibold text-amber-950">{String(freeze.status ?? 'active')}</p>
                </div>
                <div>
                  <p className="text-xs font-semibold uppercase tracking-[0.16em] text-amber-700/70">Started</p>
                  <p className="mt-1 text-sm font-semibold text-amber-950">
                    {freeze.startedAt ? new Date(freeze.startedAt).toLocaleString() : 'Pending'}
                  </p>
                </div>
                <div>
                  <p className="text-xs font-semibold uppercase tracking-[0.16em] text-amber-700/70">Ends</p>
                  <p className="mt-1 text-sm font-semibold text-amber-950">
                    {freeze.endedAt ? new Date(freeze.endedAt).toLocaleString() : 'Not set'}
                  </p>
                </div>
              </CardContent>
            </Card>
          ) : null}

          <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
            {nextActionCard ? (
              <MotionItem delayIndex={0}>
                <LearnerSurfaceCard card={nextActionCard} />
              </MotionItem>
            ) : null}
            <MotionItem delayIndex={1}>
              <LearnerSurfaceCard card={nextMockCard} />
            </MotionItem>
          </div>

          <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
            <div className="space-y-6 lg:col-span-8">
              <section>
                <LearnerSurfaceSectionHeader
                  title="Today&apos;s Study Plan"
                  description={`${completedToday} of ${todayTasks.length} scheduled tasks completed.`}
                  action={(
                    <Button variant="ghost" size="sm" onClick={() => router.push('/study-plan')}>
                      View Full Plan <ArrowRight className="ml-1 h-4 w-4" />
                    </Button>
                  )}
                  className="mb-4"
                />

                <div className="flex flex-col gap-3">
                  {todayTasks.map((task) => {
                    const Icon = SUBTEST_ICONS[task.subTest];
                    const colorClass = SUBTEST_COLORS[task.subTest];
                    const isComplete = task.status === 'completed';

                    return (
                      <motion.div
                        key={task.id}
                        whileHover={prefersReducedMotion || isComplete ? {} : { scale: 1.01 }}
                        whileTap={prefersReducedMotion || isComplete ? {} : { scale: 0.98 }}
                        className={`group flex flex-col items-start justify-between rounded-xl border bg-surface p-4 shadow-sm transition-[border-color,box-shadow,opacity,transform] duration-200 sm:flex-row sm:items-center ${
                          isComplete
                            ? 'border-border opacity-60'
                            : 'border-border/60 hover:border-border hover:shadow-md'
                        }`}
                      >
                        <div className="mb-3 flex items-center gap-4 sm:mb-0">
                          <div className={`flex h-11 w-11 items-center justify-center rounded-lg ${isComplete ? 'bg-success/10 text-success' : colorClass}`}>
                            {isComplete ? <CheckCircle2 className="h-5 w-5" /> : <Icon className="h-5 w-5" />}
                          </div>
                          <div>
                            <h3 className={`text-sm font-semibold text-navy ${isComplete ? 'line-through' : ''}`}>{task.title}</h3>
                            <p className="text-xs text-muted">{task.duration} · {task.subTest}</p>
                          </div>
                        </div>
                        {!isComplete ? (
                          <Button
                            variant="primary"
                            size="sm"
                            onClick={() => router.push(`/${task.subTest.toLowerCase()}`)}
                          >
                            Start <ArrowRight className="ml-1 h-3.5 w-3.5" />
                          </Button>
                        ) : null}
                      </motion.div>
                    );
                  })}
                </div>
              </section>

              {upcomingTasks.length > 0 ? (
                <section>
                  <LearnerSurfaceSectionHeader
                    eyebrow="This Week"
                    title="What's coming up"
                    description="See the work scheduled after today so you can plan ahead."
                    className="mb-3"
                  />
                  <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                    {upcomingTasks.map((task) => {
                      const Icon = SUBTEST_ICONS[task.subTest];
                      return (
                        <CardLink key={task.id} href={`/${task.subTest.toLowerCase()}`}>
                          <CardContent className="flex items-start gap-3 p-0">
                            <Icon className="mt-0.5 h-5 w-5 shrink-0 text-muted" />
                            <div>
                              <h4 className="text-sm font-semibold text-navy">{task.title}</h4>
                              <p className="mt-0.5 text-xs text-muted">{task.dueDate} · {task.duration}</p>
                            </div>
                          </CardContent>
                        </CardLink>
                      );
                    })}
                  </div>
                </section>
              ) : null}
            </div>

            <div className="space-y-5 lg:col-span-4">
              {readiness ? (
                <Card>
                  <CardHeader>
                    <CardTitle>Test Readiness</CardTitle>
                  </CardHeader>
                  <CardContent className="flex flex-col items-center space-y-3 text-center">
                    <ReadinessMeter
                      value={readinessAverage}
                      size={120}
                    />
                    <p className="flex items-center gap-1 text-sm font-semibold text-emerald-700">
                      <TrendingUp className="h-4 w-4" />
                      {readinessRecentTrend}
                    </p>
                    <p className="text-xs text-muted">
                      {readiness.weeksRemaining} weeks to exam · {readiness.overallRisk} risk
                    </p>
                  </CardContent>
                </Card>
              ) : null}

              {readiness ? (
                <Card>
                  <CardHeader>
                    <CardTitle>Skill Breakdown</CardTitle>
                  </CardHeader>
                  <CardContent className="space-y-4">
                    {readiness.subTests.map((subTest) => (
                      <div key={subTest.id}>
                        <div className="mb-1 flex justify-between text-sm">
                          <span className="font-semibold text-navy">{subTest.name}</span>
                          <span className="text-muted">{subTest.readiness}%</span>
                        </div>
                        <ProgressBar
                          value={subTest.readiness}
                          ariaLabel={`${subTest.name} readiness ${subTest.readiness}%`}
                          color={subTest.readiness >= subTest.target ? 'success' : subTest.readiness >= subTest.target - 15 ? 'primary' : 'danger'}
                        />
                      </div>
                    ))}
                  </CardContent>
                </Card>
              ) : null}

              {readiness && readiness.blockers.length > 0 ? (
                <WeakestLinkCard
                  criterion={readiness.weakestLink}
                  subtest={readiness.subTests.find((subTest) => subTest.isWeakest)?.name ?? 'General'}
                  description={readiness.blockers[0].description}
                />
              ) : null}

              <AiUsageWidget />

              <PronunciationDashboardTile />

              {engagement ? (
                <Card>
                  <CardHeader>
                    <CardTitle>
                      <Flame className="mr-1.5 inline-block h-4.5 w-4.5 text-amber-700" />
                      Practice Streak
                    </CardTitle>
                  </CardHeader>
                  <CardContent className="space-y-4">
                    <div className="flex items-center justify-between">
                      <div className="text-center">
                        <div className="text-3xl font-bold text-amber-700">{engagement.currentStreak}</div>
                        <div className="text-xs text-muted">Day Streak</div>
                      </div>
                      <div className="text-center">
                        <div className="flex items-center gap-1 text-lg font-bold text-amber-800">
                          <Trophy className="h-4 w-4" />
                          {engagement.longestStreak}
                        </div>
                        <div className="text-xs text-muted">Longest</div>
                      </div>
                    </div>

                    <div>
                      <p className="mb-2 text-xs font-medium text-muted">This Week</p>
                      <div className="flex justify-between gap-1">
                        {engagement.weeklyActivity.map((day) => (
                          <div key={day.day} className="flex flex-col items-center gap-1">
                            <div
                              className={`h-7 w-7 rounded-md transition-colors ${
                                day.active
                                  ? 'bg-amber-600 shadow-sm shadow-amber-200'
                                  : 'bg-background-light border border-border'
                              }`}
                            />
                            <span className="text-[10px] text-muted">{day.day}</span>
                          </div>
                        ))}
                      </div>
                    </div>

                    <div className="grid grid-cols-2 gap-3">
                      <div className="rounded-lg border border-border bg-background-light p-2.5 text-center">
                        <div className="flex items-center justify-center gap-1 text-sm font-bold text-navy">
                          <Timer className="h-3.5 w-3.5" />
                          {Math.round(engagement.totalPracticeMinutes / 60)}h
                        </div>
                        <div className="text-[10px] text-muted">Total Practice</div>
                      </div>
                      <div className="rounded-lg border border-border bg-background-light p-2.5 text-center">
                        <div className="text-sm font-bold text-navy">{engagement.totalPracticeSessions}</div>
                        <div className="text-[10px] text-muted">Sessions</div>
                      </div>
                    </div>
                  </CardContent>
                </Card>
              ) : null}
            </div>
          </div>
        </div>
      </AsyncStateWrapper>
    </LearnerDashboardShell>
  );
}
