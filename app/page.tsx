'use client';

import { useRouter } from 'next/navigation';
import { motion, useReducedMotion } from 'motion/react';
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

  const todayTasks = tasks.filter((task) => task.section === 'today');
  const upcomingTasks = tasks.filter((task) => task.section === 'thisWeek');
  const completedToday = todayTasks.filter((task) => task.status === 'completed').length;
  const nextAction = todayTasks.find((task) => task.status !== 'completed');
  const asyncStatus = status === 'loading' ? 'loading' : status === 'error' ? 'error' : !profile ? 'empty' : 'success' as const;
  const introMotion = prefersReducedMotion ? {} : { initial: { opacity: 0, y: 16 }, animate: { opacity: 1, y: 0 } };

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
          { label: nextAction.duration },
          { label: nextAction.subTest },
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
            description="Use the dashboard to decide the next action, check readiness evidence, and move without guesswork."
            highlights={dashboardHeroHighlights}
          />

          <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
            {nextActionCard ? (
              <motion.div {...introMotion}>
                <LearnerSurfaceCard card={nextActionCard} />
              </motion.div>
            ) : null}
            <motion.div
              {...introMotion}
              transition={prefersReducedMotion ? undefined : { delay: 0.05 }}
            >
              <LearnerSurfaceCard card={nextMockCard} />
            </motion.div>
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
                            ? 'border-gray-200 opacity-60'
                            : 'border-gray-200/60 hover:border-gray-300 hover:shadow-md'
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
                    title="Upcoming work stays visible"
                    description="Learners should always be able to see what is coming next after today’s immediate actions."
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
                      value={Math.round(readiness.subTests.reduce((sum, subTest) => sum + subTest.readiness, 0) / readiness.subTests.length)}
                      size={120}
                    />
                    <p className="flex items-center gap-1 text-sm font-semibold text-emerald-700">
                      <TrendingUp className="h-4 w-4" />
                      {readiness.evidence.recentTrend}
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

              {engagement ? (
                <Card>
                  <CardHeader>
                    <CardTitle>
                      <Flame className="mr-1.5 inline-block h-4.5 w-4.5 text-orange-500" />
                      Practice Streak
                    </CardTitle>
                  </CardHeader>
                  <CardContent className="space-y-4">
                    <div className="flex items-center justify-between">
                      <div className="text-center">
                        <div className="text-3xl font-bold text-orange-500">{engagement.currentStreak}</div>
                        <div className="text-xs text-muted">Day Streak</div>
                      </div>
                      <div className="text-center">
                        <div className="flex items-center gap-1 text-lg font-bold text-amber-600">
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
                                  ? 'bg-orange-500/90 shadow-sm shadow-orange-200'
                                  : 'bg-gray-100'
                              }`}
                            />
                            <span className="text-[10px] text-muted">{day.day}</span>
                          </div>
                        ))}
                      </div>
                    </div>

                    <div className="grid grid-cols-2 gap-3">
                      <div className="rounded-lg bg-gray-50 p-2.5 text-center">
                        <div className="flex items-center justify-center gap-1 text-sm font-bold text-navy">
                          <Timer className="h-3.5 w-3.5" />
                          {Math.round(engagement.totalPracticeMinutes / 60)}h
                        </div>
                        <div className="text-[10px] text-muted">Total Practice</div>
                      </div>
                      <div className="rounded-lg bg-gray-50 p-2.5 text-center">
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
