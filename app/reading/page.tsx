'use client';

import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { ArrowRight, BookOpen, Clock, FileText, Lightbulb, Target, TrendingUp } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useAuth } from '@/contexts/auth-context';
import { analytics } from '@/lib/analytics';
import { fetchMockReports, fetchReadingHome } from '@/lib/api';
import type { MockReport } from '@/lib/mock-data';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain';
import type { LearnerSurfaceCardModel } from '@/lib/learner-surface';

interface ReadingHomeTask {
  contentId: string;
  title: string;
  difficulty: string;
  estimatedDurationMinutes: number;
  scenarioType?: string;
}

export default function ReadingHome() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const [home, setHome] = useState<Record<string, any> | null>(null);
  const [tasks, setTasks] = useState<ReadingHomeTask[]>([]);
  const [mockReports, setMockReports] = useState<MockReport[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (authLoading || !isAuthenticated) {
      return;
    }

    let cancelled = false;
    analytics.track('module_entry', { module: 'reading' });

    (async () => {
      try {
        setLoading(true);
        setError(null);
        const [readingHome, reports] = await Promise.all([fetchReadingHome(), fetchMockReports()]);
        if (cancelled) return;
        setHome(readingHome);
        setTasks((readingHome.featuredTasks ?? []) as ReadingHomeTask[]);
        setMockReports(reports);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Failed to load reading practice.');
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [authLoading, isAuthenticated]);

  const mockRoute = home?.mockSets?.[0]?.route ?? '/mocks';

  const strategyCard: LearnerSurfaceCardModel = {
    kind: 'insight',
    sourceType: 'frontend_insight',
    accent: 'amber',
    eyebrow: 'Strategy Focus',
    eyebrowIcon: Lightbulb,
    title: 'Build reliable detail extraction',
    description: 'Use one focused reading block to improve exact scanning, then validate the gain under timed mock pressure.',
    metaItems: [
      { icon: Target, label: 'Inference control' },
      { icon: Clock, label: 'Timed reading' },
    ],
  };

  const nextStepCard: LearnerSurfaceCardModel = {
    kind: 'navigation',
    sourceType: 'backend_summary',
    accent: 'indigo',
    eyebrow: 'Recommended Next Step',
    eyebrowIcon: TrendingUp,
    title: 'Move from practice to timed evidence',
    description: 'After one focused reading task, enter mock setup to confirm whether your detail extraction holds up under exam pressure.',
    metaItems: [
      { icon: Clock, label: 'Timed flow' },
      { icon: FileText, label: 'Report included' },
    ],
    primaryAction: {
      label: 'Enter Mock Setup',
      href: mockRoute,
    },
  };

  return (
    <LearnerDashboardShell pageTitle="Reading">
      <main className="space-y-10">
        <LearnerPageHero
          eyebrow="Module Focus"
          icon={BookOpen}
          accent="blue"
          title="Build reading accuracy before you validate it in mocks"
          description={home?.intro ?? 'Use this workspace to sharpen detail extraction, keep timing visible, and check whether gains hold up in mock conditions.'}
          highlights={[
            { icon: Target, label: 'Featured tasks', value: tasks.length ? `${tasks.length} ready now` : 'Loading...' },
            { icon: FileText, label: 'Mock reports', value: mockReports.length ? `${mockReports.length} available` : 'No reports yet' },
            { icon: TrendingUp, label: 'Mock routes', value: home?.mockSets?.length ? `${home.mockSets.length} ready` : 'Mock setup ready' },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="Practice Tasks"
            title="Start with backend-backed reading work"
            description="Each task shows the skill shape, timing signal, and direct next action."
            className="mb-5"
          />

          {loading ? (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
              {[1, 2].map((i) => <Skeleton key={i} className="h-56 rounded-[24px]" />)}
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
              {tasks.map((task, index) => {
                const practiceCard: LearnerSurfaceCardModel = {
                  kind: 'task',
                  sourceType: 'backend_task',
                  accent: 'blue',
                  eyebrow: task.scenarioType ? task.scenarioType.replace(/_/g, ' ') : 'Reading Task',
                  eyebrowIcon: Target,
                  title: task.title,
                  description: 'Open this task to practise fast detail extraction and clinically accurate answer selection.',
                  metaItems: [
                    { icon: Clock, label: `${task.estimatedDurationMinutes} mins` },
                    { label: task.difficulty },
                  ],
                  primaryAction: {
                    label: 'Start Task',
                    href: `/reading/player/${task.contentId}`,
                  },
                };

                return (
                  <motion.div
                    key={task.contentId}
                    initial={{ opacity: 0, y: 20 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ delay: index * 0.08 }}
                  >
                    <LearnerSurfaceCard card={practiceCard} />
                  </motion.div>
                );
              })}
            </div>
          )}
        </section>

        <section className="grid grid-cols-1 lg:grid-cols-2 gap-8">
          <LearnerSurfaceCard card={strategyCard}>
            <ul className="space-y-3 text-sm text-navy/80">
              <li>Prioritise exact detail extraction before making inference choices.</li>
              <li>Underline numbers, ranges, and named concepts while reading Part C.</li>
              <li>Review wrong answers immediately to identify distractor patterns.</li>
            </ul>
          </LearnerSurfaceCard>

          <LearnerSurfaceCard card={nextStepCard} />
        </section>

        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="Recent Mock Reports"
            title="Use mock evidence to validate progress"
            description="Full-mock feedback shows whether reading gains are transferring under cross-subtest pressure."
            action={<Link href="/mocks" className="text-sm font-bold text-primary hover:underline">Open Mock Center</Link>}
            className="mb-5"
          />

          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            {mockReports.slice(0, 2).map((report, index) => {
              const reportCard: LearnerSurfaceCardModel = {
                kind: 'evidence',
                sourceType: 'backend_summary',
                accent: 'slate',
                eyebrow: 'Mock Evidence',
                eyebrowIcon: FileText,
                title: report.title,
                description: report.summary,
                metaItems: [
                  { label: report.date },
                  { label: report.overallScore },
                ],
                primaryAction: {
                  label: 'View Report',
                  href: `/mocks/report/${report.id}`,
                  variant: 'outline',
                },
              };

              return (
                <motion.div
                  key={report.id}
                  initial={{ opacity: 0, y: 20 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ delay: 0.15 + index * 0.08 }}
                >
                  <LearnerSurfaceCard card={reportCard} />
                </motion.div>
              );
            })}
          </div>
        </section>
      </main>
    </LearnerDashboardShell>
  );
}
