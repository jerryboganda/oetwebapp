'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { ArrowRight, Clock, Mic, RefreshCw, Star, Users, Video } from 'lucide-react';
import Link from 'next/link';
import { useAuth } from '@/contexts/auth-context';
import { LearnerDashboardShell } from '@/components/layout';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { trackSpeaking } from '@/lib/analytics/speaking-events';
import { analytics } from '@/lib/analytics';
import { InlineAlert } from '@/components/ui/alert';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { fetchSpeakingHome, type SpeakingHome, type SpeakingTask } from '@/lib/api';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { createLearnerMetaLabel, type LearnerSurfaceCardModel } from '@/lib/learner-surface';

const primaryLinkClasses = 'pressable inline-flex items-center justify-center gap-2 rounded-lg bg-primary px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-primary/90 active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2';

export default function SpeakingHome() {
  const router = useRouter();
  const { user, loading: authLoading } = useAuth();
  const needsProfession = !authLoading && user?.role === 'learner' && !user?.activeProfessionId;
  const [home, setHome] = useState<SpeakingHome | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (needsProfession) {
      router.replace('/speaking/select-profession');
    }
  }, [needsProfession, router]);

  useEffect(() => {
    if (authLoading || needsProfession) return;
    trackSpeaking('module_entry', { from: 'speaking_home' });
    fetchSpeakingHome()
      .then((speakingHome) => setHome(speakingHome))
      .catch(() => setError('Failed to load speaking tasks. Please try again.'))
      .finally(() => setLoading(false));
  }, [authLoading, needsProfession]);

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Speaking">
        <LearnerSkeleton variant="dashboard" />
      </LearnerDashboardShell>
    );
  }

  const credits = home?.reviewCredits?.available ?? 0;
  const featuredTasks = home?.featuredTasks ?? [];
  const recommended = home?.recommendedRolePlay ?? null;

  // Resume an in-progress role play — kept because it's necessary for progress
  // (backend surfaces pastAttempts[].state='in_progress'/'draft').
  const resumeAttempt = (home?.pastAttempts ?? []).find((attempt) => {
    const state = attempt?.state?.toLowerCase() ?? '';
    return state === 'in_progress' || state === 'in-progress' || state === 'draft';
  }) ?? null;

  // Practice speaking cards available on the platform (AI Assessment). The
  // recommended role play leads, then the rest of the featured library —
  // deduped by id so nothing shows twice.
  const practiceCards: SpeakingTask[] = (() => {
    const seen = new Set<string>();
    const list: SpeakingTask[] = [];
    if (recommended?.id) {
      list.push(recommended);
      seen.add(recommended.id);
    }
    for (const task of featuredTasks) {
      if (task?.id && !seen.has(task.id)) {
        list.push(task);
        seen.add(task.id);
      }
    }
    return list;
  })();

  const examCard: LearnerSurfaceCardModel = {
    kind: 'task',
    sourceType: 'backend_task',
    accent: 'indigo',
    eyebrow: 'AI Assessment',
    eyebrowIcon: Mic,
    title: 'Take a full two-card Speaking exam',
    description: 'A short unscored intro, then Card A and Card B — 3 minutes to prepare and 5 minutes to speak on each. The AI plays the patient and marks your result.',
    metaItems: [
      { icon: Mic, label: 'Card A + Card B' },
      { icon: Star, label: '2 AI credits' },
    ],
    primaryAction: { label: 'Start Speaking Exam', href: '/speaking/exam' },
  };

  const tutorCard: LearnerSurfaceCardModel = {
    kind: 'task',
    sourceType: 'frontend_navigation',
    accent: 'emerald',
    eyebrow: 'Live Tutor',
    eyebrowIcon: Video,
    title: 'Book a tutor as your patient',
    description: 'Prefer a human examiner? Book a 1-on-1 live speaking session with an OET tutor who plays the patient and gives you personalised feedback.',
    metaItems: [
      { icon: Users, label: 'Live 1-on-1' },
      { icon: Clock, label: 'Scheduled session' },
    ],
    primaryAction: { label: 'Book a Tutor', href: '/private-speaking' },
  };

  return (
    <LearnerDashboardShell pageTitle="Speaking">
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Speaking"
          icon={Mic}
          accent="purple"
          title="Get assessed by AI or book a live tutor"
          description="Take a full two-card Speaking exam marked by AI, practise any role-play card on the platform, or book a tutor to play your patient."
          highlights={[
            { icon: Star, label: 'AI credits', value: `${credits} available` },
            { icon: Mic, label: 'Practice cards', value: practiceCards.length > 0 ? `${practiceCards.length} ready` : 'Browse library' },
            { icon: Video, label: 'Live tutoring', value: '1-on-1 booking' },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {/* AI Speaking exam + live tutor — the two ways to get assessed. */}
        <section className="grid grid-cols-1 gap-6 lg:grid-cols-2">
          <MotionSection>
            <LearnerSurfaceCard card={examCard} />
          </MotionSection>
          <MotionSection delayIndex={1}>
            <LearnerSurfaceCard card={tutorCard} />
          </MotionSection>
        </section>

        {/* Resume in-progress role play — necessary for progress; backend
            surfaces pastAttempts[].state='in_progress'. */}
        {resumeAttempt ? (
          <MotionSection>
            <LearnerSurfaceCard card={{
              kind: 'task',
              sourceType: 'backend_task',
              accent: 'indigo',
              eyebrow: 'Resume Attempt',
              eyebrowIcon: RefreshCw,
              title: 'Continue your in-progress role play',
              description: 'Your speaking attempt is saved. Pick up exactly where you stopped. No credits are spent until you submit for review.',
              metaItems: [
                { icon: Clock, label: 'Paused' },
                { icon: RefreshCw, label: 'In progress' },
              ],
              primaryAction: { label: 'Resume Role Play', href: resumeAttempt.route },
              secondaryAction: { label: 'Pick a Different Scenario', href: '/speaking/selection', variant: 'secondary' },
            }} />
          </MotionSection>
        ) : null}

        {/* Practice speaking cards available on the platform (AI Assessment). */}
        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="AI Assessment"
            title="Practise any speaking card on the platform"
            description="Each role play is marked by AI against the OET Speaking criteria. Pick a card to start, or open the full library."
            action={<Link href="/speaking/selection" className="text-sm font-bold text-primary hover:underline">View Full Library</Link>}
            className="mb-4"
          />

          {practiceCards.length === 0 ? (
            <LearnerEmptyState
              compact
              icon={Mic}
              title="No practice cards yet"
              description="Browse the full library to find speaking role plays once they are available on the platform."
              primaryAction={{ label: 'Open Speaking Library', href: '/speaking/selection' }}
            />
          ) : (
            <div className="flex flex-col gap-3">
              {practiceCards.map((task, index) => {
                const taskId = task.id;
                const durationLabel = createLearnerMetaLabel(task.duration, '20 mins');
                const scenarioLabel = createLearnerMetaLabel(task.scenarioType || task.profession, 'Speaking scenario');
                const focusLabel = createLearnerMetaLabel(task.criteriaFocus, 'speaking control');
                const isRecommended = index === 0 && recommended?.id === taskId;

                return (
                  <MotionItem key={taskId} delayIndex={index}>
                    <Card className="border-border/70">
                      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
                        <div className="flex items-start gap-3">
                          <Mic className="h-5 w-5 shrink-0 text-primary mt-0.5" aria-hidden />
                          <div className="min-w-0">
                            <div className="flex flex-wrap items-center gap-2">
                              <Badge variant="muted" size="sm">Speaking</Badge>
                              {isRecommended ? (
                                <Badge variant="success" size="sm">Recommended</Badge>
                              ) : null}
                              {task.difficulty ? (
                                <Badge
                                  variant={String(task.difficulty).toLowerCase() === 'hard' ? 'danger' : String(task.difficulty).toLowerCase() === 'medium' ? 'warning' : 'success'}
                                  size="sm"
                                >
                                  {task.difficulty}
                                </Badge>
                              ) : null}
                            </div>
                            <h3 className="mt-2 text-base font-bold text-navy">{task.title}</h3>
                            <p className="mt-1 text-sm text-muted">Focus: {focusLabel}</p>
                            <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted">
                              <span>{scenarioLabel}</span>
                              <span>{durationLabel}</span>
                            </div>
                          </div>
                        </div>
                        <Link
                          href={`/speaking/check?taskId=${taskId}`}
                          className={primaryLinkClasses}
                          onClick={() => {
                            analytics.track('task_started', { taskId, subtest: 'speaking' });
                          }}
                        >
                          Start Role Play
                          <ArrowRight className="h-4 w-4" />
                        </Link>
                      </div>
                    </Card>
                  </MotionItem>
                );
              })}
            </div>
          )}
        </section>
      </div>
    </LearnerDashboardShell>
  );
}
