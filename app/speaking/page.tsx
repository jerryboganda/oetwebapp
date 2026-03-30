'use client';

import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { ArrowRight, Clock, Heart, Mic, Star, Volume2 } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { InlineAlert } from '@/components/ui/alert';
import { fetchSpeakingHome, fetchSubmissions } from '@/lib/api';
import type { Submission } from '@/lib/mock-data';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain';
import { createLearnerMetaLabel, type LearnerSurfaceCardModel } from '@/lib/learner-surface';

const evidenceDateFormatter = new Intl.DateTimeFormat('en-US', {
  month: 'short',
  day: 'numeric',
  year: 'numeric',
});

const primaryLinkClasses = 'inline-flex items-center justify-center gap-2 rounded-lg bg-primary px-4 py-2 text-sm font-medium text-white shadow-sm transition-all duration-200 hover:bg-primary/90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 active:scale-[0.98]';

export default function SpeakingHome() {
  const [home, setHome] = useState<Record<string, any> | null>(null);
  const [submissions, setSubmissions] = useState<Submission[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('module_entry', { module: 'speaking' });
    Promise.all([fetchSpeakingHome(), fetchSubmissions()])
      .then(([speakingHome, allSubmissions]) => {
        setHome(speakingHome);
        setSubmissions(allSubmissions.filter((sub) => sub.subTest === 'Speaking'));
      })
      .catch(() => setError('Failed to load speaking tasks. Please try again.'))
      .finally(() => setLoading(false));
  }, []);

  if (error) {
    return (
      <LearnerDashboardShell pageTitle="Speaking">
        <InlineAlert variant="error">{error}</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Speaking">
        <div className="space-y-6">
          <Skeleton className="h-48 w-full rounded-[24px]" />
          <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
            <Skeleton className="h-72 w-full rounded-[24px]" />
            <Skeleton className="h-72 w-full rounded-[24px]" />
          </div>
          <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
            <div className="space-y-6 lg:col-span-8">
              <Skeleton className="h-24 w-full rounded-2xl" />
              <Skeleton className="h-24 w-full rounded-2xl" />
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <Skeleton className="h-60 w-full rounded-[24px]" />
                <Skeleton className="h-60 w-full rounded-[24px]" />
              </div>
            </div>
            <Skeleton className="h-[420px] w-full rounded-[24px] lg:col-span-4" />
          </div>
        </div>
      </LearnerDashboardShell>
    );
  }

  const recommended = home?.recommendedRolePlay;
  const featuredTasks = (home?.featuredTasks ?? []) as Array<Record<string, any>>;
  const drillGroups = (home?.drillGroups ?? []) as Array<Record<string, any>>;
  const credits = home?.reviewCredits?.available ?? 0;
  const recommendedId = recommended?.id ?? recommended?.contentId;
  const scenarioTasks = featuredTasks
    .filter((task) => (task.id ?? task.contentId) !== recommendedId)
    .slice(0, 3);
  const evidenceItems = submissions.slice(0, 3);
  const primaryDrillRoute = drillGroups[0]?.items?.[0]?.route ?? '/speaking/selection';
  const recommendedRoleLabel = createLearnerMetaLabel(
    typeof recommended?.profession === 'string'
      ? recommended.profession
      : typeof recommended?.scenarioType === 'string'
        ? recommended.scenarioType
        : undefined,
    'Clinical role play',
  );
  const recommendedDurationLabel = createLearnerMetaLabel(
    typeof recommended?.duration === 'string'
      ? recommended.duration
      : typeof recommended?.estimatedDurationMinutes === 'number'
        ? `${recommended.estimatedDurationMinutes} mins`
        : undefined,
    '20 mins',
  );

  const recommendedCard = recommended ? ({
    kind: 'task',
    sourceType: 'backend_summary',
    accent: 'purple',
    eyebrow: 'Recommended Next',
    eyebrowIcon: Star,
    title: recommended.title,
    description: `Start with the role play that best matches ${recommended.criteriaFocus ?? 'your current speaking priorities'}, then use the result to decide whether drills or review matter next.`,
    metaItems: [
      { label: recommendedRoleLabel },
      { icon: Clock, label: recommendedDurationLabel },
    ],
    primaryAction: {
      label: 'Start Role Play',
      href: `/speaking/check?taskId=${recommended.id ?? recommended.contentId}`,
    },
  } satisfies LearnerSurfaceCardModel) : null;

  const drillFocusCard: LearnerSurfaceCardModel = {
    kind: 'insight',
    sourceType: 'backend_summary',
    accent: 'amber',
    eyebrow: 'Drill Support',
    eyebrowIcon: Volume2,
    title: 'Use drill support before the next role play',
    description: 'Open one drill path to tighten the issues most likely to weaken your next speaking attempt, then return to a full scenario.',
    metaItems: [
      { icon: Mic, label: 'Role-play support' },
      { label: `${drillGroups.length || 0} groups ready` },
    ],
    primaryAction: {
      label: 'Open Drill Group',
      href: primaryDrillRoute,
    },
    secondaryAction: {
      label: 'View Role-Play Library',
      href: '/speaking/selection',
      variant: 'outline',
    },
  };

  return (
    <LearnerDashboardShell pageTitle="Speaking">
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Current Focus"
          icon={Mic}
          accent="purple"
          title="Keep the next speaking move and recent evidence in view"
          description="Use this workspace to choose the right role play, open drill support when it helps, and keep recent speaking evidence visible before you spend review credits."
          highlights={[
            { icon: Star, label: 'Review credits', value: `${credits} available` },
            { icon: Mic, label: 'Role plays', value: featuredTasks.length > 0 ? `${featuredTasks.length} ready` : 'Browse library' },
            { icon: Volume2, label: 'Drill groups', value: drillGroups.length > 0 ? `${drillGroups.length} available` : 'No drills yet' },
          ]}
        />

        {recommendedCard ? (
          <section className="grid grid-cols-1 gap-6 lg:grid-cols-2">
            <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }}>
              <LearnerSurfaceCard card={recommendedCard} />
            </motion.div>
            <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.05 }}>
              <LearnerSurfaceCard card={drillFocusCard}>
                <div className="space-y-2.5">
                  {(home?.commonIssuesToImprove ?? ['Build smoother openings for role plays.', 'Keep the professional tone consistent.']).slice(0, 3).map((issue: string) => (
                    <div key={issue} className="rounded-xl border border-amber-100 bg-amber-50/60 px-3 py-2 text-sm text-navy/80">
                      {issue}
                    </div>
                  ))}
                </div>
              </LearnerSurfaceCard>
            </motion.div>
          </section>
        ) : null}

        <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
          <div className="space-y-6 lg:col-span-8">
            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="More Role Plays"
                title="Keep role plays visible after the next recommendation"
                description="These scenarios keep the profession, timing, and start action visible so the page stays easy to scan."
                action={<Link href="/speaking/selection" className="text-sm font-bold text-primary hover:underline">View Full Library</Link>}
                className="mb-4"
              />

              {scenarioTasks.length === 0 ? (
                <Card className="p-6">
                  <p className="text-sm font-semibold text-navy">No extra role plays yet.</p>
                  <p className="mt-1 text-sm text-muted">Use the library to browse more speaking scenarios once they are available.</p>
                </Card>
              ) : (
                <div className="flex flex-col gap-3">
                  {scenarioTasks.map((task, index) => {
                    const taskId = task.id ?? task.contentId;
                    const durationLabel = createLearnerMetaLabel(
                      typeof task.duration === 'string'
                        ? task.duration
                        : typeof task.estimatedDurationMinutes === 'number'
                          ? `${task.estimatedDurationMinutes} mins`
                          : undefined,
                      '20 mins',
                    );
                    const scenarioLabel = createLearnerMetaLabel(
                      typeof task.scenarioType === 'string'
                        ? task.scenarioType
                        : typeof task.profession === 'string'
                          ? task.profession
                          : undefined,
                      'Speaking scenario',
                    );
                    const focusLabel = createLearnerMetaLabel(
                      typeof task.criteriaFocus === 'string' ? task.criteriaFocus : undefined,
                      'speaking control',
                    );

                    return (
                      <motion.div
                        key={taskId}
                        initial={{ opacity: 0, y: 16 }}
                        animate={{ opacity: 1, y: 0 }}
                        transition={{ delay: 0.08 + index * 0.05 }}
                      >
                        <Card className="border-gray-200/70">
                          <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
                            <div className="flex items-start gap-4">
                              <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-lg bg-purple-50 text-purple-600">
                                <Mic className="h-5 w-5" />
                              </div>
                              <div className="min-w-0">
                                <div className="flex flex-wrap items-center gap-2">
                                  <Badge variant="default" size="sm">Speaking</Badge>
                                  {task.difficulty ? (
                                    <Badge
                                      variant={String(task.difficulty).toLowerCase() === 'hard' ? 'danger' : String(task.difficulty).toLowerCase() === 'medium' ? 'warning' : 'success'}
                                      size="sm"
                                    >
                                      {task.difficulty}
                                    </Badge>
                                  ) : null}
                                </div>
                                <h3 className="mt-2 text-base font-semibold text-navy">{task.title}</h3>
                                <p className="mt-1 text-sm text-muted">Focus: {focusLabel}</p>
                                <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs font-semibold text-muted">
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
                      </motion.div>
                    );
                  })}
                </div>
              )}
            </section>

            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="Drill Groups"
                title="Fix one speaking behavior before you return to a full scenario"
                description="Each drill group narrows the problem so the learner knows exactly why the drill is worth opening."
                className="mb-4"
              />
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                {drillGroups.slice(0, 2).map((group, index) => {
                  const drillCard: LearnerSurfaceCardModel = {
                    kind: 'navigation',
                    sourceType: 'backend_summary',
                    accent: index === 0 ? 'amber' : 'rose',
                    eyebrow: 'Drill Group',
                    eyebrowIcon: index === 0 ? Volume2 : Heart,
                    title: group.title,
                    description: 'Open a focused support path, then return to the next role play with one weaker behavior already tightened.',
                    metaItems: [
                      { label: `${(group.items ?? []).length || 1} exercises` },
                      { label: 'Speaking support' },
                    ],
                    primaryAction: {
                      label: 'Open Drill Group',
                      href: group.items?.[0]?.route ?? '/speaking/selection',
                      variant: 'outline',
                    },
                  };

                  return <LearnerSurfaceCard key={group.id ?? group.title} card={drillCard} />;
                })}
              </div>
            </section>
          </div>

          <div className="space-y-5 lg:col-span-4">
            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="Evidence"
                title="Recent Speaking Evidence"
                description="Check the latest attempts before you decide between another role play, a drill, or a review follow-up."
                action={<Link href="/submissions" className="text-sm font-bold text-primary hover:underline">View Full History</Link>}
                className="mb-4"
              />

              {evidenceItems.length === 0 ? (
                <Card className="p-6 text-center">
                  <Mic className="w-8 h-8 text-muted mx-auto mb-2" />
                  <p className="text-sm font-semibold text-navy">No speaking attempts yet.</p>
                  <p className="mt-1 text-sm text-muted">Start a role play to build the first piece of speaking evidence.</p>
                  <Link href="/speaking/selection" className="text-sm font-bold text-primary mt-3 inline-block">Open Speaking Library</Link>
                </Card>
              ) : (
                <Card className="p-5">
                  <div className="space-y-3">
                    {evidenceItems.map((sub) => {
                      const parsedAttemptDate = new Date(sub.attemptDate);
                      const attemptDateLabel = Number.isNaN(parsedAttemptDate.getTime())
                        ? sub.attemptDate
                        : evidenceDateFormatter.format(parsedAttemptDate);
                      const evidenceLabel = sub.scoreEstimate?.trim() || (sub.reviewStatus === 'reviewed' ? 'Reviewed' : 'In progress');

                      return (
                        <Link
                          key={sub.id}
                          href={`/speaking/results/${sub.evaluationId ?? sub.id}`}
                          className="block rounded-xl border border-gray-200 px-4 py-3 transition-all duration-200 hover:border-gray-300 hover:bg-gray-50"
                        >
                          <div className="flex items-start justify-between gap-3">
                            <div className="min-w-0">
                              <h3 className="truncate text-sm font-semibold text-navy">{sub.taskName}</h3>
                              <p className="mt-1 text-xs text-muted">{attemptDateLabel}</p>
                            </div>
                            <Badge variant="muted" size="sm" className="shrink-0">
                              {evidenceLabel}
                            </Badge>
                          </div>
                        </Link>
                      );
                    })}
                  </div>
                </Card>
              )}
            </section>
          </div>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
