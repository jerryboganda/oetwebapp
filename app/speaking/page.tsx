'use client';

import { useEffect, useMemo, useState } from 'react';
import { ArrowRight, Award, BookOpen, Clock, FileText, Heart, Mic, RefreshCw, Star, Volume2 } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { analytics } from '@/lib/analytics';
import { InlineAlert } from '@/components/ui/alert';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { fetchSpeakingHome, fetchSubmissions, fetchMockReports, type SpeakingHome } from '@/lib/api';
import type { Submission, MockReport } from '@/lib/mock-data';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { LearnerSkillSwitcher } from '@/components/domain/learner-skill-switcher';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { createLearnerMetaLabel, type LearnerSurfaceCardModel } from '@/lib/learner-surface';

interface SpeakingLatestEvaluationDto {
  evaluationId?: string | null;
  scoreRange?: string | null;
  confidenceLabel?: string | null;
  strengths?: string[];
  issues?: string[];
}

const evidenceDateFormatter = new Intl.DateTimeFormat('en-US', {
  month: 'short',
  day: 'numeric',
  year: 'numeric',
});

const primaryLinkClasses = 'pressable inline-flex items-center justify-center gap-2 rounded-lg bg-primary px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-primary/90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2';

export default function SpeakingHome() {
  const [home, setHome] = useState<SpeakingHome | null>(null);
  const [submissions, setSubmissions] = useState<Submission[]>([]);
  const [mockReports, setMockReports] = useState<MockReport[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('module_entry', { module: 'speaking' });
    Promise.all([fetchSpeakingHome(), fetchSubmissions(), fetchMockReports()])
      .then(([speakingHome, allSubmissions, reports]) => {
        setHome(speakingHome);
        setSubmissions(allSubmissions.filter((sub) => sub.subTest === 'Speaking'));
        setMockReports(Array.isArray(reports) ? reports : []);
      })
      .catch(() => setError('Failed to load speaking tasks. Please try again.'))
      .finally(() => setLoading(false));
  }, []);

  const resumeAttempt = useMemo(() => {
    return (home?.pastAttempts ?? []).find((attempt) => {
      const state = attempt?.state?.toLowerCase() ?? '';
      return state === 'in_progress' || state === 'in-progress' || state === 'draft';
    }) ?? null;
  }, [home]);

  const latestEvaluation = useMemo<SpeakingLatestEvaluationDto | null>(() => {
    const raw = home?.latestEvaluation;
    if (!raw || typeof raw !== 'object') return null;
    const record = raw as Record<string, unknown>;
    return {
      evaluationId: typeof record.evaluationId === 'string' ? record.evaluationId : null,
      scoreRange: typeof record.scoreRange === 'string' ? record.scoreRange : null,
      confidenceLabel: typeof record.confidenceLabel === 'string' ? record.confidenceLabel : null,
      strengths: Array.isArray(record.strengths) ? record.strengths.filter((s): s is string => typeof s === 'string') : [],
      issues: Array.isArray(record.issues) ? record.issues.filter((s): s is string => typeof s === 'string') : [],
    };
  }, [home]);

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Speaking">
        <LearnerSkeleton variant="dashboard" />
      </LearnerDashboardShell>
    );
  }

  const recommended = home?.recommendedRolePlay;
  const featuredTasks = home?.featuredTasks ?? [];
  const drillGroups = home?.drillGroups ?? [];
  const supportEntries = home?.supportEntries ?? [];
  const credits = home?.reviewCredits?.available ?? 0;
  const recommendedId = recommended?.id;
  const scenarioTasks = featuredTasks
    .filter((task) => task.id !== recommendedId)
    .slice(0, 3);
  const evidenceItems = submissions.slice(0, 3);
  const primaryDrillRoute = drillGroups[0]?.items?.[0]?.route ?? '/speaking/selection';
  const recommendedRoleLabel = createLearnerMetaLabel(
    recommended?.profession ?? recommended?.scenarioType,
    'Clinical role play',
  );
  const recommendedDurationLabel = createLearnerMetaLabel(
    recommended?.duration,
    '20 mins',
  );

  const recommendedCard = recommended ? ({
    kind: 'task',
    sourceType: 'backend_summary',
    accent: 'purple',
    eyebrow: 'Recommended Next',
    eyebrowIcon: Star,
    title: recommended.title,
    description: `Start with the role play that best matches ${recommended.criteriaFocus || 'your current speaking priorities'}, then use the result to decide whether drills or review matter next.`,
    metaItems: [
      { icon: Mic, label: recommendedRoleLabel },
      { icon: Clock, label: recommendedDurationLabel },
    ],
    primaryAction: {
      label: 'Start Role Play',
      href: `/speaking/check?taskId=${recommended.id}`,
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
      { icon: Volume2, label: `${drillGroups.length || 0} groups ready` },
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
          description="Choose your next role play, drill targeted weaknesses, and review recent recordings before requesting tutor feedback."
          highlights={[
            { icon: Star, label: 'Review credits', value: `${credits} available` },
            { icon: Mic, label: 'Role plays', value: featuredTasks.length > 0 ? `${featuredTasks.length} ready` : 'Browse library' },
            { icon: Volume2, label: 'Drill groups', value: drillGroups.length > 0 ? `${drillGroups.length} available` : 'No drills yet' },
          ]}
        />

        <LearnerSkillSwitcher compact />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {/* Resume in-progress speaking attempt — backend surfaces pastAttempts[].state='in_progress'. */}
        {resumeAttempt ? (
          <MotionSection>
            <LearnerSurfaceCard card={{
              kind: 'task',
              sourceType: 'backend_task',
              accent: 'indigo',
              eyebrow: 'Resume Attempt',
              eyebrowIcon: RefreshCw,
              title: 'Continue your in-progress role play',
              description: 'Your speaking attempt is saved — pick up exactly where you stopped. No credits are spent until you submit for review.',
              metaItems: [
                { icon: Clock, label: 'Paused' },
                { icon: RefreshCw, label: 'In progress' },
              ],
              primaryAction: { label: 'Resume Role Play', href: resumeAttempt.route },
              secondaryAction: { label: 'Pick a Different Scenario', href: '/speaking/selection', variant: 'outline' },
            }} />
          </MotionSection>
        ) : null}

        {/* Rulebook entry point — parity with Writing / Reading / Selection. */}
        <MotionSection delayIndex={1}>
          <LearnerSurfaceCard
            card={{
              kind: 'navigation',
              sourceType: 'frontend_navigation',
              accent: 'indigo',
              eyebrow: 'Rulebook',
              eyebrowIcon: BookOpen,
              title: 'Know exactly how your speaking is judged',
              description: 'See the criteria your role-play feedback is built on — from conversational flow to the protocol for breaking bad news.',
              metaItems: [
                { icon: FileText, label: 'Speaking criteria' },
                { icon: Heart, label: 'Breaking bad news' },
              ],
              primaryAction: {
                label: 'Open Speaking Rules',
                href: '/speaking/rulebook',
              },
              secondaryAction: {
                label: 'Breaking Bad News',
                href: '/speaking/rulebook/RULE_44',
                variant: 'outline',
              },
            }}
          />
        </MotionSection>

        {recommendedCard ? (
          <section className="grid grid-cols-1 gap-6 lg:grid-cols-2">
            <MotionSection>
              <LearnerSurfaceCard card={recommendedCard} />
            </MotionSection>
            <MotionSection delayIndex={1}>
              <LearnerSurfaceCard card={drillFocusCard}>
                <div className="space-y-2.5">
                  {(home?.commonIssuesToImprove ?? ['Build smoother openings for role plays.', 'Keep the professional tone consistent.']).slice(0, 3).map((issue) => (
                    <div
                      key={issue}
                      className="rounded-xl border !border-amber-300 !bg-white px-3 py-2 text-sm font-black !text-slate-950 shadow-sm shadow-amber-950/5 dark:!border-amber-300/30 dark:!bg-slate-950 dark:!text-amber-50 dark:shadow-none"
                    >
                      {issue}
                    </div>
                  ))}
                </div>
              </LearnerSurfaceCard>
            </MotionSection>
          </section>
        ) : null}

        {/* Latest Result — surfaces home.latestEvaluation rubric summary from the backend. */}
        {latestEvaluation && latestEvaluation.scoreRange ? (
          <MotionSection delayIndex={2}>
            <LearnerSurfaceSectionHeader
              eyebrow="Latest Result"
              title="Read your most recent rubric-graded speaking evaluation"
              description="Open the full card to see strengths, issues, and the detailed feedback breakdown."
              className="mb-4"
            />
            <LearnerSurfaceCard card={{
              kind: 'evidence',
              sourceType: 'backend_summary',
              accent: 'emerald',
              eyebrow: 'Rubric Summary',
              eyebrowIcon: Award,
              title: latestEvaluation.scoreRange ?? 'Rubric-graded',
              description: 'Your role play was graded against the OET Speaking criteria. Open the result to see the full breakdown and rulebook audit.',
              metaItems: [
                ...(latestEvaluation.confidenceLabel ? [{ label: latestEvaluation.confidenceLabel }] : []),
                ...((latestEvaluation.strengths ?? []).slice(0, 1).map((s) => ({ label: `Strength: ${s}` }))),
                ...((latestEvaluation.issues ?? []).slice(0, 1).map((s) => ({ label: `Issue: ${s}` }))),
              ],
              primaryAction: latestEvaluation.evaluationId
                ? { label: 'Open Result', href: `/speaking/results/${latestEvaluation.evaluationId}` }
                : { label: 'View Submissions', href: '/submissions', variant: 'outline' },
            }} />
          </MotionSection>
        ) : null}

        <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
          <div className="space-y-6 lg:col-span-8">
            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="More Role Plays"
                title="Keep role plays visible after the next recommendation"
                description="Each scenario shows the profession, timing, and a quick-start link."
                action={<Link href="/speaking/selection" className="text-sm font-bold text-primary hover:underline">View Full Library</Link>}
                className="mb-4"
              />

              {scenarioTasks.length === 0 ? (
                <LearnerEmptyState
                  compact
                  icon={Mic}
                  title="No extra role plays yet"
                  description="Use the library to browse more speaking scenarios once they are available, or start with the recommended speaking path above."
                  primaryAction={{ label: 'Open Speaking Library', href: '/speaking/selection' }}
                  secondaryAction={{ label: 'Track Progress', href: '/progress' }}
                />
              ) : (
                <div className="flex flex-col gap-3">
                  {scenarioTasks.map((task, index) => {
                    const taskId = task.id;
                    const durationLabel = createLearnerMetaLabel(task.duration, '20 mins');
                    const scenarioLabel = createLearnerMetaLabel(task.scenarioType || task.profession, 'Speaking scenario');
                    const focusLabel = createLearnerMetaLabel(task.criteriaFocus, 'speaking control');

                    return (
                      <MotionItem
                        key={taskId}
                        delayIndex={index}
                      >
                        <Card className="border-border/70">
                          <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
                            <div className="flex items-start gap-4">
                              <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
                                <Mic className="h-5 w-5" />
                              </div>
                              <div className="min-w-0">
                                <div className="flex flex-wrap items-center gap-2">
                                  <Badge variant="muted" size="sm">Speaking</Badge>
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
                      </MotionItem>
                    );
                  })}
                </div>
              )}
            </section>

            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="Drill Groups"
                title="Fix one speaking behavior before you return to a full scenario"
                description="Each drill targets a specific speaking weakness so you know exactly why it's worth your time."
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
                      { icon: FileText, label: (() => { const count = (group.items ?? []).length || 1; return `${count} ${count === 1 ? 'exercise' : 'exercises'}`; })() },
                      { icon: Volume2, label: 'Speaking support' },
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

            {supportEntries.length > 0 ? (
              <section>
                <LearnerSurfaceSectionHeader
                  eyebrow="Support Paths"
                  title="Use the right speaking support for the job"
                  description="Interactive AI practice, pronunciation training, and private tutoring are available in their own dedicated sections."
                  className="mb-4"
                />
                <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
                  {supportEntries.map((entry) => (
                    <Link
                      key={entry.id}
                      href={entry.route}
                      className="rounded-2xl border border-border/80 bg-surface p-4 shadow-sm transition-all duration-200 hover:border-primary/30 hover:bg-primary/5"
                    >
                      <p className="text-sm font-bold text-navy">{entry.title}</p>
                      <p className="mt-2 text-xs leading-relaxed text-muted">{entry.description}</p>
                    </Link>
                  ))}
                </div>
              </section>
            ) : null}
          </div>

          <div className="space-y-5 lg:col-span-4">
            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="Evidence"
                title="Recent Speaking Evidence"
                description="Review your latest attempts to decide your next step — another role play, a focused drill, or tutor feedback."
                action={<Link href="/submissions" className="text-sm font-bold text-primary hover:underline">View Full History</Link>}
                className="mb-4"
              />

              {evidenceItems.length === 0 ? (
                <LearnerEmptyState
                  compact
                  icon={Mic}
                  title="No speaking attempts yet"
                  description="Start a role play to build the first piece of speaking evidence."
                  primaryAction={{ label: 'Open Speaking Library', href: '/speaking/selection' }}
                  secondaryAction={{ label: 'Start AI Conversation', href: '/conversation' }}
                />
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
                          className="block rounded-xl border border-border px-4 py-3 transition-all duration-200 hover:border-border-hover hover:bg-background-light"
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

        {/* Recent Mock Reports — cross-module evidence parity with Writing / Reading / Listening. */}
        <MotionSection delayIndex={3}>
          <LearnerSurfaceSectionHeader
            eyebrow="Recent Mock Reports"
            title="Track speaking impact inside full mocks"
            description="See whether your role-play and drill gains hold up under real exam pressure."
            action={<Link href="/mocks" className="text-sm font-bold text-primary hover:underline">Open Mock Center</Link>}
            className="mb-4"
          />
          {mockReports.length > 0 ? (
            <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
              {mockReports.slice(0, 2).map((report, index) => (
                <MotionItem key={report.id} delayIndex={index}>
                  <LearnerSurfaceCard card={{
                    kind: 'evidence',
                    sourceType: 'backend_summary',
                    accent: 'slate',
                    eyebrow: 'Mock Evidence',
                    eyebrowIcon: FileText,
                    title: report.title,
                    description: report.summary,
                    metaItems: [
                      { icon: Clock, label: report.date },
                      { icon: Award, label: report.overallScore },
                    ],
                    primaryAction: { label: 'View Report', href: `/mocks/report/${report.id}`, variant: 'outline' },
                  }} />
                </MotionItem>
              ))}
            </div>
          ) : (
            <LearnerEmptyState
              compact
              icon={Award}
              title="No speaking mock evidence yet"
              description="Complete a mock to see whether your speaking gains transfer under full exam pressure."
              primaryAction={{ label: 'Open Mock Center', href: '/mocks' }}
              secondaryAction={{ label: 'Track Progress', href: '/progress' }}
            />
          )}
        </MotionSection>
      </div>
    </LearnerDashboardShell>
  );
}
