'use client';

import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { motion } from 'motion/react';
import {
  ArrowRight,
  CheckCircle2,
  TrendingUp,
  FilePenLine,
  BookOpen,
  Mic,
  Headphones,
  Sparkles,
  Calendar,
  Flag,
  Star,
} from 'lucide-react';
import { Button, Card, CardContent, CardHeader, CardTitle, ProgressBar } from '@/components/ui';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader, ReadinessMeter, WeakestLinkCard } from '@/components/domain';
import { AsyncStateWrapper } from '@/components/state';
import { useAnalytics } from '@/hooks/use-analytics';
import { fetchDashboardHome, fetchReadiness, fetchStudyPlan, fetchUserProfile } from '@/lib/api';
import type { StudyPlanTask, ReadinessData, UserProfile, SubTest } from '@/lib/mock-data';
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
  const { track } = useAnalytics();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [tasks, setTasks] = useState<StudyPlanTask[]>([]);
  const [readiness, setReadiness] = useState<ReadinessData | null>(null);
  const [profile, setProfile] = useState<UserProfile | null>(null);
  const [home, setHome] = useState<Record<string, any> | null>(null);

  useEffect(() => {
    loadData();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function loadData() {
    setLoading(true);
    setError(null);
    try {
      const [t, r, p, dashboardHome] = await Promise.all([
        fetchStudyPlan(),
        fetchReadiness(),
        fetchUserProfile(),
        fetchDashboardHome(),
      ]);
      setTasks(t);
      setReadiness(r);
      setProfile(p);
      setHome(dashboardHome);
      track('readiness_viewed');
    } catch (e: any) {
      setError(e.userMessage ?? e.message ?? 'Something went wrong. Please try again.');
    } finally {
      setLoading(false);
    }
  }

  const todayTasks = tasks.filter((t) => t.section === 'today');
  const upcomingTasks = tasks.filter((t) => t.section === 'thisWeek');
  const completedToday = todayTasks.filter((t) => t.status === 'completed').length;
  const nextAction = todayTasks.find((t) => t.status !== 'completed');
  const asyncStatus = loading ? 'loading' : error ? 'error' : !profile ? 'empty' : 'success' as const;
  const dashboardHeroHighlights = [
    { icon: Calendar, label: 'Exam target', value: home?.cards?.examDate?.value ?? profile?.examDate ?? 'Set your exam date' },
    { icon: Star, label: 'Pending reviews', value: `${home?.cards?.pendingExpertReviews?.count ?? 0} in progress` },
    { icon: CheckCircle2, label: "Today's plan", value: todayTasks.length > 0 ? `${completedToday}/${todayTasks.length} done` : 'No tasks scheduled' },
  ];

  const nextActionCard = nextAction ? ({
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
  } satisfies LearnerSurfaceCardModel) : null;

  const nextMockCard: LearnerSurfaceCardModel = {
    kind: 'navigation',
    sourceType: 'backend_summary',
    accent: 'navy',
    eyebrow: 'Mock Progression',
    eyebrowIcon: Flag,
    title: home?.cards?.nextMockRecommendation?.title ?? 'Full OET Mock Test',
    description: home?.cards?.nextMockRecommendation?.rationale ?? 'Use a full mock to check whether your recent practice work is transferring under pressure.',
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
        onRetry={loadData}
        errorMessage={error ?? undefined}
        emptyContent={
          <div className="py-12 text-center space-y-3">
            <p className="text-sm font-bold text-navy">Welcome to OET Prep</p>
            <p className="text-xs text-muted">Complete onboarding to get started.</p>
            <Button size="sm" onClick={() => router.push('/onboarding')}>Start Onboarding</Button>
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

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
            {nextActionCard ? (
              <motion.div initial={{ opacity: 0, y: 16 }} animate={{ opacity: 1, y: 0 }}>
                <LearnerSurfaceCard card={nextActionCard} />
              </motion.div>
            ) : null}
            <motion.div initial={{ opacity: 0, y: 16 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.05 }}>
              <LearnerSurfaceCard card={nextMockCard} />
            </motion.div>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
            <div className="lg:col-span-8 space-y-6">
              <section>
                <LearnerSurfaceSectionHeader
                  title="Today&apos;s Study Plan"
                  description={`${completedToday} of ${todayTasks.length} scheduled tasks completed.`}
                  action={(
                    <Button variant="ghost" size="sm" onClick={() => router.push('/study-plan')}>
                      View Full Plan <ArrowRight className="w-4 h-4 ml-1" />
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
                        whileHover={isComplete ? {} : { scale: 1.01 }}
                        whileTap={isComplete ? {} : { scale: 0.98 }}
                        className={`group flex flex-col sm:flex-row items-start sm:items-center justify-between p-4 bg-surface border rounded-xl shadow-sm transition-all duration-200 ${isComplete ? 'opacity-60 border-gray-200' : 'border-gray-200/60 hover:border-gray-300 hover:shadow-md cursor-pointer'}`}
                      >
                        <div className="flex items-center gap-4 mb-3 sm:mb-0">
                          <div className={`w-11 h-11 rounded-lg flex items-center justify-center ${isComplete ? 'bg-success/10 text-success' : colorClass}`}>
                            {isComplete ? <CheckCircle2 className="w-5 h-5" /> : <Icon className="w-5 h-5" />}
                          </div>
                          <div>
                            <h3 className={`font-semibold text-sm text-navy ${isComplete ? 'line-through' : ''}`}>{task.title}</h3>
                            <p className="text-xs text-muted">{task.duration} · {task.subTest}</p>
                          </div>
                        </div>
                        {!isComplete ? (
                          <Button
                            variant="primary"
                            size="sm"
                            onClick={() => router.push(`/${task.subTest.toLowerCase()}`)}
                          >
                            Start <ArrowRight className="w-3.5 h-3.5 ml-1" />
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
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                    {upcomingTasks.map((task) => {
                      const Icon = SUBTEST_ICONS[task.subTest];
                      return (
                        <Card key={task.id} hoverable className="cursor-pointer" onClick={() => router.push(`/${task.subTest.toLowerCase()}`)}>
                          <CardContent className="flex items-start gap-3">
                            <Icon className="w-5 h-5 text-muted mt-0.5 shrink-0" />
                            <div>
                              <h4 className="font-semibold text-sm text-navy">{task.title}</h4>
                              <p className="text-xs text-muted mt-0.5">{task.dueDate} · {task.duration}</p>
                            </div>
                          </CardContent>
                        </Card>
                      );
                    })}
                  </div>
                </section>
              ) : null}
            </div>

            <div className="lg:col-span-4 space-y-5">
              {readiness ? (
                <Card>
                  <CardHeader>
                    <CardTitle>Test Readiness</CardTitle>
                  </CardHeader>
                  <CardContent className="flex flex-col items-center text-center space-y-3">
                    <ReadinessMeter
                      value={Math.round(readiness.subTests.reduce((sum, s) => sum + s.readiness, 0) / readiness.subTests.length)}
                      size={120}
                    />
                    <p className="text-sm font-semibold text-emerald-700 flex items-center gap-1">
                      <TrendingUp className="w-4 h-4" />
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
                    {readiness.subTests.map((st) => (
                      <div key={st.id}>
                        <div className="flex justify-between text-sm mb-1">
                          <span className="font-semibold text-navy">{st.name}</span>
                          <span className="text-muted">{st.readiness}%</span>
                        </div>
                        <ProgressBar
                          value={st.readiness}
                          ariaLabel={`${st.name} readiness ${st.readiness}%`}
                          color={st.readiness >= st.target ? 'success' : st.readiness >= st.target - 15 ? 'primary' : 'danger'}
                        />
                      </div>
                    ))}
                  </CardContent>
                </Card>
              ) : null}

              {readiness && readiness.blockers.length > 0 ? (
                <WeakestLinkCard
                  criterion={readiness.weakestLink}
                  subtest={readiness.subTests.find((s) => s.isWeakest)?.name ?? 'General'}
                  description={readiness.blockers[0].description}
                />
              ) : null}
            </div>
          </div>
        </div>
      </AsyncStateWrapper>
    </LearnerDashboardShell>
  );
}
