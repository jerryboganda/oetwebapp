'use client';

import { useEffect, useState } from 'react';
import { MotionItem } from '@/components/ui/motion-primitives';
import { ArrowRight, Clock, FileText, Headphones, Sparkles, Target, Volume2 } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { useAuth } from '@/contexts/auth-context';
import { analytics } from '@/lib/analytics';
import { fetchListeningHome, fetchMockReports } from '@/lib/api';
import type { MockReport } from '@/lib/mock-data';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain';
import type { LearnerSurfaceCardModel } from '@/lib/learner-surface';

interface ListeningHomeTask {
  contentId: string;
  title: string;
  estimatedDurationMinutes: number;
  difficulty: string;
  scenarioType?: string;
}

export default function ListeningHome() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const [home, setHome] = useState<Record<string, any> | null>(null);
  const [tasks, setTasks] = useState<ListeningHomeTask[]>([]);
  const [mockReports, setMockReports] = useState<MockReport[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (authLoading) {
      // Auth state not yet known — keep showing skeletons without fetching.
      return;
    }
    if (!isAuthenticated) {
      // Middleware should have redirected, but defend against stale renders:
      // stop showing a skeleton forever and let the surface render empty.
      setLoading(false);
      return;
    }

    let cancelled = false;
    analytics.track('module_entry', { module: 'listening' });

    (async () => {
      try {
        setLoading(true);
        setError(null);
        const [listeningHome, reports] = await Promise.all([fetchListeningHome(), fetchMockReports()]);
        if (cancelled) return;
        setHome(listeningHome);
        setTasks((listeningHome.featuredTasks ?? []) as ListeningHomeTask[]);
        setMockReports(reports);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Failed to load listening practice.');
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
  const transcriptRoute = home?.transcriptBackedReview?.route ?? '/listening';
  const drillRoute = home?.distractorDrills?.[0]?.route ?? '/listening';

  const distractorCard: LearnerSurfaceCardModel = {
    kind: 'insight',
    sourceType: 'frontend_insight',
    accent: 'rose',
    eyebrow: 'Distractor Focus',
    eyebrowIcon: Target,
    title: 'Build more reliable listening discrimination',
    description: 'Train yourself to capture exact changes in plan, frequencies, and clinical detail instead of approximate impressions.',
    metaItems: [
      { icon: Headphones, label: 'Consultation audio' },
      { icon: Clock, label: 'Post-attempt review' },
    ],
  };

  const transcriptCard: LearnerSurfaceCardModel = {
    kind: 'navigation',
    sourceType: 'backend_summary',
    accent: 'amber',
    eyebrow: 'Recommended Next Step',
    eyebrowIcon: Sparkles,
    title: home?.transcriptBackedReview?.title ?? 'Review transcript-backed evidence',
    description: home?.accessPolicyHints?.rationale ?? 'Use transcript-backed review after an attempt so you can diagnose distractor patterns with real evidence instead of replaying blindly.',
    metaItems: [
      { icon: Clock, label: 'Timed flow' },
      { icon: FileText, label: 'Report included' },
    ],
    primaryAction: {
      label: 'Open Transcript Review',
      href: transcriptRoute,
    },
    secondaryAction: {
      label: 'Open Mock Center',
      href: mockRoute,
      variant: 'outline',
    },
  };

  return (
    <LearnerDashboardShell pageTitle="Listening" subtitle="Strengthen audio comprehension, exact detail capture, and distractor control.">
      <div className="space-y-10">
        <LearnerPageHero
          eyebrow="Module Focus"
          icon={Headphones}
          accent="indigo"
          title="Train listening accuracy before you test it under pressure"
          description={home?.intro ?? 'Use this workspace to tighten detail capture, review distractors with evidence, and move into transcript-backed follow-up when it helps.'}
          highlights={[
            { icon: Volume2, label: 'Featured tasks', value: loading ? 'Loading...' : tasks.length ? `${tasks.length} ready now` : 'None yet' },
            { icon: Target, label: 'Drill paths', value: `${home?.distractorDrills?.length ?? 0} available` },
            { icon: FileText, label: 'Transcript review', value: home?.transcriptBackedReview ? 'Available after attempts' : 'Open after a task' },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="Practice Tasks"
            title="Open a listening task with a clear outcome"
            description="Each card tells you what you are practising, how long it should take, and what type of audio scenario you are entering."
            className="mb-5"
          />

          {loading ? (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
              {[1, 2].map((i) => <Skeleton key={i} className="h-56 rounded-[24px]" />)}
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
              {tasks.map((task, index) => {
                const taskCard: LearnerSurfaceCardModel = {
                  kind: 'task',
                  sourceType: 'backend_task',
                  accent: 'indigo',
                  eyebrow: task.scenarioType ? task.scenarioType.replace(/_/g, ' ') : 'Listening Task',
                  eyebrowIcon: Volume2,
                  title: task.title,
                  description: 'Use this task to practise exact detail capture before switching into mock or transcript-backed review.',
                  metaItems: [
                    { icon: Clock, label: `${task.estimatedDurationMinutes} mins` },
                    { label: task.difficulty },
                  ],
                  primaryAction: {
                    label: 'Start Task',
                    href: `/listening/player/${task.contentId}`,
                  },
                };

                return (
                  <MotionItem
                    key={task.contentId}
                    delayIndex={index}
                  >
                    <LearnerSurfaceCard card={taskCard} />
                  </MotionItem>
                );
              })}
            </div>
          )}
        </section>

        <section className="grid grid-cols-1 lg:grid-cols-2 gap-8">
          <LearnerSurfaceCard card={distractorCard}>
            <ul className="space-y-3 text-sm text-navy/80">
              <li>Listen for exact frequencies, not approximate impressions.</li>
              <li>Separate the patient&apos;s concern from the clinician&apos;s recommendation.</li>
              <li>Use transcript-backed review after errors instead of replaying blindly.</li>
            </ul>
            <div className="mt-6">
              <Link href={drillRoute} className="inline-flex items-center gap-2 text-sm font-bold text-primary hover:underline">
                Open distractor drill <ArrowRight className="h-4 w-4" />
              </Link>
            </div>
          </LearnerSurfaceCard>

          <LearnerSurfaceCard card={transcriptCard} />
        </section>

        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="Recent Mock Reports"
            title="Track listening impact inside full mocks"
            description="These reports help you see whether distractor control is improving when listening sits inside the full exam load."
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
                <MotionItem
                  key={report.id}
                  delayIndex={index}
                >
                  <LearnerSurfaceCard card={reportCard} />
                </MotionItem>
              );
            })}
          </div>
        </section>
      </div>
    </LearnerDashboardShell>
  );
}
