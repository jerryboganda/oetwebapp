'use client';

import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { motion } from 'motion/react';
import {
  ArrowRight,
  CheckCircle2,
  TrendingUp,
  AlertTriangle,
  FilePenLine,
  BookOpen,
  Mic,
  Headphones,
  Sparkles,
  Calendar,
} from 'lucide-react';
import { Button, Card, CardContent, CardHeader, CardTitle, ProgressBar } from '@/components/ui';
import { AppShell } from '@/components/layout';
import { ReadinessMeter, WeakestLinkCard } from '@/components/domain';
import { AsyncStateWrapper } from '@/components/state';
import { useAnalytics } from '@/hooks/use-analytics';
import { fetchStudyPlan, fetchReadiness, fetchUserProfile } from '@/lib/api';
import type { StudyPlanTask, ReadinessData, UserProfile, SubTest } from '@/lib/mock-data';

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

  useEffect(() => {
    loadData();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function loadData() {
    setLoading(true);
    setError(null);
    try {
      const [t, r, p] = await Promise.all([
        fetchStudyPlan(),
        fetchReadiness(),
        fetchUserProfile(),
      ]);
      setTasks(t);
      setReadiness(r);
      setProfile(p);
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

  // Next recommended action
  const nextAction = todayTasks.find((t) => t.status !== 'completed');

  const asyncStatus = loading ? 'loading' : error ? 'error' : !profile ? 'empty' : 'success' as const;

  return (
    <AppShell pageTitle="Dashboard">
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
        <div className="w-full max-w-[1200px] mx-auto px-4 sm:px-6 lg:px-8 py-6 grid grid-cols-1 lg:grid-cols-12 gap-6">
          {/* ── Left Column (8/12) ── */}
          <div className="lg:col-span-8 space-y-6">
            {/* Next Recommended Action */}
            {nextAction && (
              <motion.div
                initial={{ opacity: 0, y: -10 }}
                animate={{ opacity: 1, y: 0 }}
                className="bg-primary text-white rounded-xl p-6 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-5 shadow-md shadow-primary/20"
              >
                <div className="flex items-center gap-4">
                  <div className="w-12 h-12 rounded-lg bg-white/20 flex items-center justify-center">
                    <Sparkles className="w-6 h-6" />
                  </div>
                  <div>
                    <p className="text-sm text-white/80">Recommended next</p>
                    <h3 className="font-bold text-lg">{nextAction.title}</h3>
                    <p className="text-sm text-white/70">{nextAction.duration} · {nextAction.subTest}</p>
                  </div>
                </div>
                <Button
                  variant="secondary"
                  className="bg-white text-primary hover:bg-white/90 shrink-0"
                  onClick={() => router.push(`/${nextAction.subTest.toLowerCase()}`)}
                >
                  Start Now <ArrowRight className="w-4 h-4 ml-2" />
                </Button>
              </motion.div>
            )}

            {/* Today's Tasks */}
            <div>
              <div className="flex items-center justify-between mb-4">
                <div>
                  <h2 className="text-xl font-bold text-navy">Today&apos;s Study Plan</h2>
                  <p className="text-sm text-muted">{completedToday} of {todayTasks.length} completed</p>
                </div>
                <Button variant="ghost" size="sm" onClick={() => router.push('/study-plan')}>
                  View Full Plan <ArrowRight className="w-4 h-4 ml-1" />
                </Button>
              </div>

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
                      {!isComplete && (
                        <Button
                          variant="primary"
                          size="sm"
                          onClick={() => router.push(`/${task.subTest.toLowerCase()}`)}
                        >
                          Start <ArrowRight className="w-3.5 h-3.5 ml-1" />
                        </Button>
                      )}
                    </motion.div>
                  );
                })}
              </div>
            </div>

            {/* Upcoming This Week */}
            {upcomingTasks.length > 0 && (
              <div>
                <h3 className="text-lg font-bold text-navy mb-3 flex items-center gap-2">
                  <Calendar className="w-5 h-5 text-muted" />
                  Coming Up This Week
                </h3>
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
              </div>
            )}
          </div>

          {/* ── Right Column (4/12) ── */}
          <div className="lg:col-span-4 space-y-5">
            {/* Readiness Meter */}
            {readiness && (
              <Card>
                <CardHeader>
                  <CardTitle>Test Readiness</CardTitle>
                </CardHeader>
                <CardContent className="flex flex-col items-center text-center space-y-3">
                  <ReadinessMeter
                    value={Math.round(readiness.subTests.reduce((sum, s) => sum + s.readiness, 0) / readiness.subTests.length)}
                    size={120}
                  />
                  <p className="text-sm font-semibold text-success flex items-center gap-1">
                    <TrendingUp className="w-4 h-4" />
                    {readiness.evidence.recentTrend}
                  </p>
                  <p className="text-xs text-muted">
                    {readiness.weeksRemaining} weeks to exam · {readiness.overallRisk} risk
                  </p>
                </CardContent>
              </Card>
            )}

            {/* Skill Breakdown */}
            {readiness && (
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
                        color={st.readiness >= st.target ? 'success' : st.readiness >= st.target - 15 ? 'primary' : 'danger'}
                      />
                    </div>
                  ))}
                </CardContent>
              </Card>
            )}

            {/* Weakest Link */}
            {readiness && readiness.blockers.length > 0 && (
              <WeakestLinkCard
                criterion={readiness.weakestLink}
                subtest={readiness.subTests.find((s) => s.isWeakest)?.name ?? 'General'}
                description={readiness.blockers[0].description}
              />
            )}

            {/* Expert Review Promo */}
            <motion.div
              whileHover={{ scale: 1.02 }}
              whileTap={{ scale: 0.98 }}
              className="bg-navy rounded-xl p-6 text-white relative overflow-hidden cursor-pointer shadow-lg shadow-navy/20"
              onClick={() => router.push('/writing/expert-request')}
            >
              <div className="absolute -right-4 -top-4 w-32 h-32 bg-primary/40 rounded-full blur-2xl pointer-events-none" />
              <h4 className="font-bold text-lg mb-2 relative z-10">Need expert feedback?</h4>
              <p className="text-sm text-gray-300 mb-5 relative z-10">
                Get your writing or speaking graded by an OET expert.
              </p>
              <Button variant="secondary" size="sm" className="bg-white text-navy hover:bg-gray-100 relative z-10 w-full sm:w-auto">
                Request Review
              </Button>
            </motion.div>
          </div>
        </div>
      </AsyncStateWrapper>
    </AppShell>
  );
}
