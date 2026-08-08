'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
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
  Timer,
  TrendingUp,
  Trophy,
} from 'lucide-react';
import { Button, Card, CardContent, CardHeader, CardLink, CardTitle, ProgressBar } from '@/components/ui';
import { AiUsageWidget } from '@/components/domain/AiUsageWidget';
import { LearnerSurfaceSectionHeader, ReadinessMeter, WeakestLinkCard } from '@/components/domain';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { LearnerFreshnessIndicator } from '@/components/domain/learner-freshness-indicator';
import { PronunciationDashboardTile } from '@/components/domain/pronunciation';
import { SafeRichText } from '@/components/domain/grammar/grammar-content-renderer';
import { DashboardAddonsWidget } from '@/components/learner/dashboard-addons-widget';
import { ExtendAccessCta } from '@/components/learner/extend-access-cta';
import { MotionList } from '@/components/ui/motion-primitives';
import type { learnerGetScoringPolicy, MyEntitlementSnapshot } from '@/lib/api';
import type { EngagementData } from '@/lib/hooks/use-dashboard-home';
import type { ReadinessData, StudyPlanTask, SubTest } from '@/lib/mock-data';

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

const SUBTEST_SPINE: Record<SubTest, string> = {
  Writing: 'bg-rose-400',
  Speaking: 'bg-purple-400',
  Reading: 'bg-blue-400',
  Listening: 'bg-indigo-400',
};

function routeForTask(task: { route?: string; subTest: SubTest }) {
  return task.route ?? `/${task.subTest.toLowerCase()}`;
}

type LearnerDashboardDetailsProps = {
  liveReadiness: ReadinessData | null;
  readinessAverage: number;
  readinessRecentTrend: string;
  readinessUpdatedAt: string | null | undefined;
  todayTasks: StudyPlanTask[];
  upcomingTasks: StudyPlanTask[];
  completedToday: number;
  loadedAt: string | null;
  engagement: EngagementData | null;
  entitlement: MyEntitlementSnapshot | null;
  scoringPolicy: Awaited<ReturnType<typeof learnerGetScoringPolicy>> | null;
};

/**
 * Below-the-fold learner content is intentionally code-split from the initial
 * dashboard route. The hero, onboarding state, and action cards can hydrate
 * without parsing the full readiness, pronunciation, scoring, and activity
 * widget tree on mobile CPUs. This component still renders the complete
 * dashboard immediately after its small deferred chunk is available.
 */
export function LearnerDashboardDetails({
  liveReadiness,
  readinessAverage,
  readinessRecentTrend,
  readinessUpdatedAt,
  todayTasks,
  upcomingTasks,
  completedToday,
  loadedAt,
  engagement,
  entitlement,
  scoringPolicy,
}: LearnerDashboardDetailsProps) {
  const router = useRouter();
  const prefersReducedMotion = useReducedMotion();
  const [scoringExpanded, setScoringExpanded] = useState(false);

  return (
    <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
      <div className="space-y-6 lg:col-span-8">
        <section data-tour="learner-dashboard-today">
          <LearnerSurfaceSectionHeader
            title="Today&apos;s Study Plan"
            description={`${completedToday} of ${todayTasks.length} scheduled tasks completed.`}
            action={(
              <div className="flex flex-wrap items-center justify-between gap-2">
                <LearnerFreshnessIndicator updatedAt={loadedAt} source="loaded" staleAfterMinutes={30} />
                <Button variant="primary" size="sm" className="ml-auto" onClick={() => router.push('/study-plan')}>
                  View Full Plan <ArrowRight className="ml-1 h-4 w-4" />
                </Button>
              </div>
            )}
            className="mb-4"
          />

          <div className="flex flex-col gap-3">
            {todayTasks.length > 0 ? todayTasks.map((task) => {
              const Icon = SUBTEST_ICONS[task.subTest];
              const colorClass = SUBTEST_COLORS[task.subTest];
              const spineClass = SUBTEST_SPINE[task.subTest];
              const isComplete = task.status === 'completed';

              return (
                <motion.div
                  key={task.id}
                  whileHover={prefersReducedMotion || isComplete ? {} : { scale: 1.01 }}
                  whileTap={prefersReducedMotion || isComplete ? {} : { scale: 0.98 }}
                  className={`group relative flex flex-col items-start justify-between overflow-hidden rounded-2xl border bg-surface p-4 pl-5 shadow-sm transition-[border-color,box-shadow,opacity,transform] duration-200 sm:flex-row sm:items-center ${
                    isComplete
                      ? 'border-border opacity-60'
                      : 'border-border/60 hover:border-border-hover hover:shadow-clinical'
                  }`}
                >
                  <span
                    aria-hidden="true"
                    className={`absolute inset-y-0 left-0 w-1.5 transition-colors ${isComplete ? 'bg-success/50' : spineClass}`}
                  />
                  <div className="mb-3 flex items-center gap-4 sm:mb-0">
                    <div className={`flex h-11 w-11 items-center justify-center rounded-lg ${isComplete ? 'bg-success/10 text-success' : colorClass}`}>
                      {isComplete ? <CheckCircle2 className="h-5 w-5" /> : <Icon className="h-5 w-5" />}
                    </div>
                    <div>
                      <h3 className={`text-sm font-bold text-navy ${isComplete ? 'line-through' : ''}`}>{task.title}</h3>
                      <p className="text-xs text-muted">{task.duration} · {task.subTest}</p>
                    </div>
                  </div>
                  {!isComplete ? (
                    <Button
                      variant="primary"
                      size="sm"
                      onClick={() => router.push(routeForTask(task))}
                    >
                      Start <ArrowRight className="ml-1 h-3.5 w-3.5" />
                    </Button>
                  ) : null}
                </motion.div>
              );
            }) : (
              <LearnerEmptyState
                compact
                icon={Calendar}
                title="No live tasks scheduled today"
                description="Tasks appear from your server-backed study plan after onboarding or new practice evidence."
                primaryAction={{ label: 'Build Study Plan', href: '/study-plan' }}
                secondaryAction={{ label: 'Start Practice', href: '/writing' }}
              />
            )}
          </div>
        </section>

        {upcomingTasks.length > 0 ? (
          <section>
            <LearnerSurfaceSectionHeader
              eyebrow="This Week"
              title="What&apos;s coming up"
              description="See the work scheduled after today so you can plan ahead."
              className="mb-3"
            />
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              {upcomingTasks.map((task) => {
                const Icon = SUBTEST_ICONS[task.subTest];
                const colorClass = SUBTEST_COLORS[task.subTest];
                return (
                  <CardLink key={task.id} href={routeForTask(task)}>
                    <CardContent className="flex items-start gap-3 p-0">
                      <span className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-lg ${colorClass}`}>
                        <Icon className="h-4.5 w-4.5" />
                      </span>
                      <div className="min-w-0">
                        <h4 className="truncate text-sm font-bold text-navy">{task.title}</h4>
                        <p className="mt-0.5 text-xs text-muted">{task.dueDate} · {task.duration}</p>
                      </div>
                    </CardContent>
                  </CardLink>
                );
              })}
            </div>
          </section>
        ) : null}

        <DashboardAddonsWidget
          writingAddonsEnabled={entitlement?.writingAddonsEnabled ?? false}
          speakingAddonsEnabled={entitlement?.speakingAddonsEnabled ?? false}
          tutorBookDiscountEnabled={entitlement?.tutorBookDiscountEnabled ?? false}
        />
      </div>

      <MotionList initial={false} layout={false} className="space-y-5 lg:col-span-4">
        {liveReadiness ? (
          <Card data-tour="learner-dashboard-readiness">
            <CardHeader>
              <div className="flex flex-wrap items-center justify-between gap-2">
                <CardTitle>Test Readiness</CardTitle>
                <LearnerFreshnessIndicator updatedAt={readinessUpdatedAt} staleAfterMinutes={1440} />
              </div>
            </CardHeader>
            <CardContent className="flex flex-col items-center space-y-3 text-center">
              <ReadinessMeter value={readinessAverage} size={120} />
              <p className="flex items-center gap-1.5 text-sm font-semibold text-emerald-700">
                <TrendingUp className="h-4 w-4 shrink-0" />
                {readinessRecentTrend}
              </p>
              <div className="flex flex-wrap items-center justify-center gap-1.5">
                <span className="inline-flex items-center gap-1 rounded-full bg-background-light px-2.5 py-1 text-[11px] font-semibold text-navy ring-1 ring-border/70">
                  <Timer className="h-3 w-3 text-primary" aria-hidden="true" />
                  {liveReadiness.weeksRemaining} weeks to exam
                </span>
                <span className="inline-flex items-center gap-1 rounded-full bg-background-light px-2.5 py-1 text-[11px] font-semibold capitalize text-navy ring-1 ring-border/70">
                  <Flag className="h-3 w-3 text-primary" aria-hidden="true" />
                  {liveReadiness.overallRisk} risk
                </span>
              </div>
              <Link
                href="/readiness"
                prefetch={false}
                className="inline-flex items-center gap-1.5 text-xs font-bold text-primary hover:underline"
              >
                View full readiness centre →
              </Link>
            </CardContent>
          </Card>
        ) : null}

        {liveReadiness ? (
          <Card>
            <CardHeader>
              <CardTitle>Skill Breakdown</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              {liveReadiness.subTests.map((subTest) => (
                <div key={subTest.id}>
                  <div className="mb-1 flex items-center justify-between text-sm">
                    <span className="flex items-center gap-2 font-semibold text-navy">
                      <span
                        aria-hidden="true"
                        className={`h-2 w-2 shrink-0 rounded-full ${SUBTEST_SPINE[subTest.name as SubTest] ?? 'bg-muted'}`}
                      />
                      {subTest.name}
                    </span>
                    <span className="tabular-nums text-muted">
                      {subTest.readiness}%
                      <span className="text-muted/60"> · target {subTest.target}%</span>
                    </span>
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

        {liveReadiness && liveReadiness.blockers.length > 0 ? (
          <WeakestLinkCard
            criterion={liveReadiness.weakestLink}
            subtest={liveReadiness.subTests.find((subTest) => subTest.isWeakest)?.name ?? 'General'}
            description={liveReadiness.blockers[0].description}
          />
        ) : null}

        <AiUsageWidget />
        <PronunciationDashboardTile />
        <ExtendAccessCta
          hasEligibleSubscription={entitlement?.hasEligibleSubscription ?? false}
          expiresAt={entitlement?.expiresAt}
        />

        {scoringPolicy ? (
          <Card>
            <CardHeader>
              <div className="flex items-center justify-between gap-3">
                <CardTitle>
                  <BookOpen className="mr-1.5 inline-block h-4.5 w-4.5 text-primary" />
                  How am I graded?
                </CardTitle>
                <Button variant="ghost" size="sm" onClick={() => setScoringExpanded((value) => !value)}>
                  {scoringExpanded ? 'Hide' : 'Show'}
                </Button>
              </div>
            </CardHeader>
            {scoringExpanded ? (
              <CardContent className="space-y-3 text-sm text-muted">
                <SafeRichText markdown={scoringPolicy.bodyMarkdown} className="text-muted" />
                <p className="text-xs text-muted">Updated {new Date(scoringPolicy.updatedAt).toLocaleDateString()}</p>
              </CardContent>
            ) : null}
          </Card>
        ) : null}

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
                <p className="mb-2 text-[11px] font-bold uppercase tracking-wider text-muted">This Week</p>
                <div className="grid grid-cols-7 gap-1.5">
                  {engagement.weeklyActivity.map((day, index, all) => {
                    const isToday = index === all.length - 1;
                    return (
                      <div
                        key={day.day}
                        title={day.day}
                        className={`flex h-9 items-center justify-center rounded-lg text-[11px] font-bold uppercase transition-colors duration-200 ${
                          day.active
                            ? 'bg-amber-600 text-white shadow-sm shadow-amber-200/70'
                            : `bg-background-light text-muted/70 ${isToday ? 'ring-2 ring-inset ring-amber-400/60' : 'border border-border'}`
                        }`}
                      >
                        {day.day.slice(0, 1)}
                      </div>
                    );
                  })}
                </div>
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div className="rounded-xl border border-border bg-background-light p-2.5 text-center transition-colors hoverable:border-border-hover">
                  <div className="flex items-center justify-center gap-1 text-sm font-bold text-navy">
                    <Timer className="h-3.5 w-3.5 text-amber-700" />
                    {Math.round(engagement.totalPracticeMinutes / 60)}h
                  </div>
                  <div className="text-[11px] text-muted">Total Practice</div>
                </div>
                <div className="rounded-xl border border-border bg-background-light p-2.5 text-center transition-colors hoverable:border-border-hover">
                  <div className="text-sm font-bold text-navy">{engagement.totalPracticeSessions}</div>
                  <div className="text-[11px] text-muted">Sessions</div>
                </div>
              </div>
            </CardContent>
          </Card>
        ) : null}
      </MotionList>
    </div>
  );
}
